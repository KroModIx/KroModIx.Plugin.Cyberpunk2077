using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Katalog fuer Cyberpunk 2077 — nutzt den GraphQL-basierten
/// <see cref="INexusService.SearchModsAsync"/> (Contracts v1.15.0+) fuer
/// den vollen Nexus-Bestand (~23000 Mods) mit Pagination, Sortierung und
/// Server-side-Volltextsuche.
///
/// <para>Der Katalog fuehrt den State (aktuelle Sort-Reihenfolge,
/// Search-Query, geladene Eintraege) und bietet Methoden zum initialen
/// Laden bzw. „mehr laden" (naechste Seite anhaengen). Beim Wechsel von
/// Sort oder Search wird der State zurueckgesetzt.</para></summary>
public sealed class CyberpunkNexusCatalog
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Nexus-Domain-Name fuer Cyberpunk 2077 — hart kodiert, nur
    /// dieses eine Spiel wird vom Plugin bedient.</summary>
    public const string GameSlug = "cyberpunk2077";

    /// <summary>Pro Seiten-Fetch: 40 Eintraege. Nexus GraphQL erlaubt bis
    /// 100 aber 40 haelt den Cover-Load-Cycle (250 ms pro Cover) in einem
    /// vertretbaren 10-Sekunden-Fenster pro Load-More-Klick.</summary>
    public const int PageSize = 40;

    private readonly INexusService _nexus;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private readonly List<NexusCatalogEntry> _entries = new();
    private DateTime _cacheAt;
    private int _totalCount;
    private NexusSort _currentSort = NexusSort.LatestUpdate;
    private string _currentQuery = "";

    public CyberpunkNexusCatalog(INexusService nexus)
    {
        _nexus = nexus;
    }

    public IReadOnlyList<NexusCatalogEntry> Cached => _entries;
    public DateTime CachedAtUtc => _cacheAt;
    public int TotalCount => _totalCount;
    public NexusSort CurrentSort => _currentSort;
    public string CurrentQuery => _currentQuery;
    public bool HasMore => _entries.Count < _totalCount;

    /// <summary>Reset + erste Seite laden. Wird bei Sort-Wechsel, Query-
    /// Wechsel oder initialem Katalog-Open gerufen.</summary>
    public Task<int> LoadFirstPageAsync(
        NexusSort sort, string? query, CancellationToken ct = default)
    {
        return LoadCoreAsync(reset: true, sort, query ?? "", ct);
    }

    /// <summary>Nachste Seite anhaengen. Kein Sort/Query-Wechsel.</summary>
    public Task<int> LoadNextPageAsync(CancellationToken ct = default)
    {
        return LoadCoreAsync(reset: false, _currentSort, _currentQuery, ct);
    }

    /// <summary>Legacy-API fuer AutoUpdate-Discovery (v0.4) — laedt die
    /// erste Seite mit dem aktuellen Sort. Nicht mehr fuer den User-View
    /// gedacht.</summary>
    public Task<IReadOnlyList<NexusCatalogEntry>> RefreshAsync(CancellationToken ct = default)
    {
        return LoadCoreAsync(reset: true, _currentSort, _currentQuery, ct)
            .ContinueWith(_ => (IReadOnlyList<NexusCatalogEntry>)_entries, ct);
    }

    private async Task<int> LoadCoreAsync(bool reset, NexusSort sort, string query, CancellationToken ct)
    {
        if (!_nexus.HasApiKey)
        {
            Log.Info("Katalog-Load uebersprungen: kein Nexus-API-Key");
            return 0;
        }

        await _loadGate.WaitAsync(ct);
        try
        {
            if (reset)
            {
                _entries.Clear();
                _totalCount = 0;
                _currentSort = sort;
                _currentQuery = query;
            }
            var result = await _nexus.SearchModsAsync(
                GameSlug, offset: _entries.Count, count: PageSize,
                sort: _currentSort,
                searchQuery: string.IsNullOrWhiteSpace(_currentQuery) ? null : _currentQuery,
                ct);

            _entries.AddRange(result.Entries);
            _totalCount = result.TotalCount;
            _cacheAt = DateTime.UtcNow;
            Log.Info("Cyberpunk-Katalog {Mode}: +{Added}, {N}/{Total} (sort={Sort} query='{Q}')",
                reset ? "reset" : "append",
                result.Entries.Count, _entries.Count, _totalCount, _currentSort, _currentQuery);
            return result.Entries.Count;
        }
        finally { _loadGate.Release(); }
    }
}
