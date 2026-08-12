using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Aggregiert die drei Nexus-Kurzlisten (latest_added,
/// latest_updated, trending) für Cyberpunk 2077 (game-slug <c>cyberpunk2077</c>)
/// zu einem dedup'ten Katalog. Kein persistenter Cache in v0.2 — Nexus
/// liefert die Listen schnell (~200 ms) und die 3-Endpoint-Aggregation
/// verbraucht 3 von 2500 Rate-Limit-Slots pro Refresh. Persistenter Cache
/// wird in v0.4 mit Update-Discovery interessant.</summary>
public sealed class CyberpunkNexusCatalog
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Nexus-Domain-Name für Cyberpunk 2077 — hart kodiert, nur
    /// dieses eine Spiel wird vom Plugin bedient.</summary>
    public const string GameSlug = "cyberpunk2077";

    private readonly INexusService _nexus;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(30);
    private IReadOnlyList<NexusCatalogEntry> _cache = Array.Empty<NexusCatalogEntry>();
    private DateTime _cacheAt;

    public CyberpunkNexusCatalog(INexusService nexus)
    {
        _nexus = nexus;
    }

    public IReadOnlyList<NexusCatalogEntry> Cached => _cache;
    public DateTime CachedAtUtc => _cacheAt;

    /// <summary>Refresht den Katalog live. Aggregiert die drei Endpoints
    /// (latest_added → latest_updated → trending) sequenziell und dedup't
    /// per <c>ModId</c>. Fehler pro Endpoint werden geloggt aber der Rest
    /// laeuft weiter (Netz-Aussetzer nicht totaler Datenverlust).
    ///
    /// <para><b>SemaphoreSlim + Coalesce-Fenster (v0.4.1):</b> mehrere
    /// gleichzeitige Aufrufer (Plugin-Init-Auto-Check, View-Ctor,
    /// ApiKeyChanged-Handler) werden serialisiert. Wenn der Cache jünger
    /// als 30 s ist, wird der Refresh übersprungen — sonst hätten wir
    /// 3 API-Calls × N Aufrufer verschwendet (im Log gesehen: 9 parallele
    /// Refreshes = 27 API-Calls für einen einzigen Katalog-Öffnungs-Event).</para></summary>
    public async Task<IReadOnlyList<NexusCatalogEntry>> RefreshAsync(CancellationToken ct = default)
    {
        if (!_nexus.HasApiKey)
        {
            Log.Info("Katalog-Refresh uebersprungen: kein Nexus-API-Key");
            return _cache;
        }

        await _refreshGate.WaitAsync(ct);
        try
        {
            // Coalesce: nach dem Warten schauen ob ein anderer Aufrufer den
            // Refresh in der Zwischenzeit erledigt hat.
            if (_cache.Count > 0 && DateTime.UtcNow - _cacheAt < CoalesceWindow)
            {
                Log.Debug("Katalog-Refresh coalesced — Cache ist {Sec:F0} s jung",
                    (DateTime.UtcNow - _cacheAt).TotalSeconds);
                return _cache;
            }

            var seen = new Dictionary<int, NexusCatalogEntry>();
            foreach (var endpoint in new[] { "latest_added", "latest_updated", "trending" })
            {
                try
                {
                    var chunk = await _nexus.GetLatestModsAsync(GameSlug, endpoint, ct);
                    foreach (var e in chunk)
                        seen[e.ModId] = e; // dedup — letzter gewinnt
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Cyberpunk Nexus-Catalog {Endpoint} fehlgeschlagen", endpoint);
                }
            }
            _cache = seen.Values
                .OrderByDescending(e => e.UpdatedUtc)
                .ToList();
            _cacheAt = DateTime.UtcNow;
            Log.Info("Cyberpunk Nexus-Katalog aktualisiert: {N} unique Mods", _cache.Count);
            return _cache;
        }
        finally { _refreshGate.Release(); }
    }
}
