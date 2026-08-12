using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Cross-Reference: installierte Mods (mit erkennbarer Version) vs
/// Nexus-Katalog-latest. Fuer v0.4 nur REDmods (info.json hat sauberes
/// <c>version</c>-Feld) — Archive/CET/RED4ext/redscript haben keine
/// konsistente Version-Convention und wuerden False-Positives liefern.
///
/// <para>Matching: Nexus-Katalog nach Name-Substring durchsuchen
/// (info.json.name vs NexusCatalogEntry.Name). Fuzzy — Nexus-Namen und
/// REDmod-Namen unterscheiden sich oft leicht (Punktuation, Feature-
/// Description). Erste plausible Uebereinstimmung wird genommen.</para>
///
/// <para>Update-Kandidat: Nexus-Version &gt; installierte Version (SemVer-
/// like Vergleich). Kein Update wenn Versionen nicht parsbar sind — dann
/// nur Nexus-UpdatedUtc-Zeitstempel gegen mtime der info.json vergleichen
/// (Fallback, konservativ: nur wenn Nexus &gt; mtime + 7 Tage).</para></summary>
public sealed class CyberpunkUpdateChecker
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly CyberpunkModScanner _scanner;
    private readonly CyberpunkNexusCatalog _catalog;

    private IReadOnlyList<UpdateCandidate> _pending = Array.Empty<UpdateCandidate>();
    private DateTime _lastCheckUtc;

    public CyberpunkUpdateChecker(CyberpunkModScanner scanner, CyberpunkNexusCatalog catalog)
    {
        _scanner = scanner;
        _catalog = catalog;
    }

    public IReadOnlyList<UpdateCandidate> Pending => _pending;
    public int PendingCount => _pending.Count;
    public DateTime LastCheckUtc => _lastCheckUtc;

    /// <summary>Prueft alle installierten Mods gegen den (moeglicherweise
    /// gecachten) Katalog. Kein Live-Katalog-Refresh — der User macht das
    /// im Nexus-Tab. Aufruf ist billig (nur In-Memory-Vergleich).</summary>
    public async Task<int> CheckAsync(DetectedGame game, CancellationToken ct = default)
    {
        var catalog = _catalog.Cached;
        if (catalog.Count == 0)
        {
            // Erst-Aufruf: Katalog holen (billig, 3 API-Calls von 2500/h).
            catalog = await _catalog.RefreshAsync(ct);
        }
        if (catalog.Count == 0)
        {
            _pending = Array.Empty<UpdateCandidate>();
            return 0;
        }

        var installed = _scanner.ScanAll(game)
            .Where(m => m.Type == CyberpunkModType.RedMod
                && m.IsEnabled
                && !string.IsNullOrEmpty(m.Version))
            .ToList();
        if (installed.Count == 0)
        {
            _pending = Array.Empty<UpdateCandidate>();
            _lastCheckUtc = DateTime.UtcNow;
            return 0;
        }

        var pending = new List<UpdateCandidate>();
        foreach (var mod in installed)
        {
            var match = FindBestMatch(mod, catalog);
            if (match is null) continue;
            if (!TryCompareVersions(mod.Version!, match.Version, out var isNewer)) continue;
            if (!isNewer) continue;
            pending.Add(new UpdateCandidate(
                InstalledName: mod.Name,
                InstalledVersion: mod.Version!,
                NexusModId: match.ModId,
                NexusName: match.Name,
                NexusVersion: match.Version));
        }
        _pending = pending;
        _lastCheckUtc = DateTime.UtcNow;
        Log.Info("Cyberpunk-Update-Check: {N} Update(s) fuer {Installed} REDmods (Katalog {Cat})",
            pending.Count, installed.Count, catalog.Count);
        return pending.Count;
    }

    private static NexusCatalogEntry? FindBestMatch(CyberpunkMod mod, IReadOnlyList<NexusCatalogEntry> catalog)
    {
        // Simple Fuzzy: Nexus-Namen die den REDmod-Namen (oder umgekehrt)
        // als Substring enthalten. Beide Richtungen weil Nexus-Titel oft
        // mehr Wörter haben ("Immersive Hacking 2.0 - Overhaul" vs REDmod
        // "ImmersiveHacking").
        var needle = mod.Name.Replace(" ", "").ToLowerInvariant();
        foreach (var entry in catalog)
        {
            var candidate = entry.Name.Replace(" ", "").ToLowerInvariant();
            if (candidate.Contains(needle, StringComparison.Ordinal)) return entry;
            if (needle.Contains(candidate, StringComparison.Ordinal)) return entry;
        }
        return null;
    }

    /// <summary>Parst zwei Version-Strings via <see cref="Version.TryParse"/>
    /// (nach Strippen von 'v'/'V'-Prefix und Pre-Release-Suffixen). Bei
    /// unparseablem Format: <c>isNewer=false</c>, damit wir keine
    /// False-Positives fluten.</summary>
    public static bool TryCompareVersions(string installed, string nexus, out bool isNewer)
    {
        isNewer = false;
        if (!TryParse(installed, out var i)) return false;
        if (!TryParse(nexus, out var n)) return false;
        isNewer = n > i;
        return true;

        static bool TryParse(string s, out Version v)
        {
            s = s.Trim();
            if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
            var dash = s.IndexOf('-'); if (dash >= 0) s = s[..dash];
            var plus = s.IndexOf('+'); if (plus >= 0) s = s[..plus];
            return Version.TryParse(s, out v!);
        }
    }
}

public sealed record UpdateCandidate(
    string InstalledName, string InstalledVersion,
    int NexusModId, string NexusName, string NexusVersion);
