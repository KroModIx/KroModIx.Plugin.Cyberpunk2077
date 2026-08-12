using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Scannt alle Cyberpunk-Mod-Ordner und liefert eine flache Liste
/// von <see cref="CyberpunkMod"/>. Enable/Disable-Detection via
/// <c>.disabled</c>-Suffix (unser eigenes Muster — Cyberpunk selbst kennt
/// keine „disabled"-Semantik, daher setzen wir sie durch Rename).
///
/// <para>Kein Cache, kein async — die Verzeichnisse sind flach, ein Scan
/// über 200 Mods braucht &lt; 100 ms. Wenn Perf mal Problem wird, ist der
/// erste Fix `Task.Run(scan)` im Aufrufer.</para></summary>
public sealed class CyberpunkModScanner
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly CyberpunkPathResolver _paths;
    private readonly InstallManifestStore? _manifests;

    public CyberpunkModScanner(CyberpunkPathResolver paths, InstallManifestStore? manifests = null)
    {
        _paths = paths;
        _manifests = manifests;
    }

    /// <summary>Alle Mod-Typen in einem Rutsch scannen + optional per
    /// InstallManifest-Store mit Nexus-Match-Kontext anreichern (NexusModId).
    /// Fehler pro Typ werden geloggt aber die anderen laufen weiter.</summary>
    public IReadOnlyList<CyberpunkMod> ScanAll(DetectedGame game)
    {
        var mods = new List<CyberpunkMod>();
        SafeScan("archive", () => mods.AddRange(ScanArchives(game)));
        SafeScan("redmod", () => mods.AddRange(ScanRedMods(game)));
        SafeScan("cet", () => mods.AddRange(ScanCetMods(game)));
        SafeScan("red4ext", () => mods.AddRange(ScanRed4ExtMods(game)));
        SafeScan("redscript", () => mods.AddRange(ScanRedscriptMods(game)));
        // Manifest-Enrichment: pro Mod nachschauen ob NexusModId persistiert
        // ist (beim Install via CyberpunkZipInstaller geschrieben).
        if (_manifests is not null)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                var manifest = _manifests.TryGet(m.ManifestKey);
                if (manifest?.NexusModId is int id)
                    mods[i] = m with { NexusModId = id };
            }
        }
        return mods
            .OrderBy(m => m.Type)
            .ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void SafeScan(string label, Action scan)
    {
        try { scan(); }
        catch (Exception ex) { Log.Warn(ex, "Cyberpunk-Scan {Label} fehlgeschlagen", label); }
    }

    /// <summary>*.archive im <c>archive/pc/mod/</c>. Enable/Disable via
    /// <c>.archive.disabled</c>-Suffix (weit verbreitetes Cyberpunk-Muster).</summary>
    public IReadOnlyList<CyberpunkMod> ScanArchives(DetectedGame game)
    {
        var dir = _paths.GetArchiveDir(game);
        if (!Directory.Exists(dir)) return Array.Empty<CyberpunkMod>();
        var result = new List<CyberpunkMod>();
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(file);
            bool enabled;
            string displayName;
            if (name.EndsWith(".archive", StringComparison.OrdinalIgnoreCase))
            {
                enabled = true;
                displayName = name[..^".archive".Length];
            }
            else if (name.EndsWith(".archive.disabled", StringComparison.OrdinalIgnoreCase))
            {
                enabled = false;
                displayName = name[..^".archive.disabled".Length];
            }
            else continue; // andere Dateitypen im mod-Ordner ignorieren
            long? size = null;
            try { size = new FileInfo(file).Length; } catch { }
            result.Add(new CyberpunkMod(CyberpunkModType.Archive, displayName, file, enabled,
                SizeBytes: size));
        }
        return result;
    }

    /// <summary>REDmods im <c>mods/&lt;name&gt;/</c>. Ordner mit
    /// <c>info.json</c> (offizielle REDmod-Convention). Enable/Disable via
    /// Ordner-Suffix <c>.disabled</c> (analog Archive).</summary>
    public IReadOnlyList<CyberpunkMod> ScanRedMods(DetectedGame game)
    {
        var dir = _paths.GetRedModDir(game);
        if (!Directory.Exists(dir)) return Array.Empty<CyberpunkMod>();
        var result = new List<CyberpunkMod>();
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var folder = Path.GetFileName(sub);
            bool enabled = !folder.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var displayName = enabled ? folder : folder[..^".disabled".Length];
            var info = TryReadInfoJson(Path.Combine(sub, "info.json"));
            result.Add(new CyberpunkMod(CyberpunkModType.RedMod,
                Name: info?.Name ?? displayName,
                Path: sub,
                IsEnabled: enabled,
                Version: info?.Version,
                Author: info?.Author,
                Description: info?.Description));
        }
        return result;
    }

    /// <summary>CET-Mods im <c>bin/x64/plugins/cyber_engine_tweaks/mods/&lt;name&gt;/</c>.
    /// Ordner mit <c>init.lua</c> ist Standard, aber wir listen auch Ordner
    /// ohne — der User weiß was drin ist.</summary>
    public IReadOnlyList<CyberpunkMod> ScanCetMods(DetectedGame game)
    {
        var dir = _paths.GetCetDir(game);
        if (!Directory.Exists(dir)) return Array.Empty<CyberpunkMod>();
        var result = new List<CyberpunkMod>();
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var folder = Path.GetFileName(sub);
            bool enabled = !folder.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var displayName = enabled ? folder : folder[..^".disabled".Length];
            result.Add(new CyberpunkMod(CyberpunkModType.CyberEngineTweaks,
                displayName, sub, enabled));
        }
        return result;
    }

    /// <summary>RED4ext-Plugins im <c>red4ext/plugins/&lt;name&gt;/</c>.
    /// Ordner mit .dll drin.</summary>
    public IReadOnlyList<CyberpunkMod> ScanRed4ExtMods(DetectedGame game)
    {
        var dir = _paths.GetRed4ExtDir(game);
        if (!Directory.Exists(dir)) return Array.Empty<CyberpunkMod>();
        var result = new List<CyberpunkMod>();
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var folder = Path.GetFileName(sub);
            bool enabled = !folder.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var displayName = enabled ? folder : folder[..^".disabled".Length];
            result.Add(new CyberpunkMod(CyberpunkModType.Red4Ext,
                displayName, sub, enabled));
        }
        return result;
    }

    /// <summary>Redscript-Mods im <c>r6/scripts/</c>. Zwei Layouts:
    /// (1) Unterordner pro Mod mit .reds drin, (2) einzelne .reds direkt
    /// in scripts/. Wir behandeln beide.</summary>
    public IReadOnlyList<CyberpunkMod> ScanRedscriptMods(DetectedGame game)
    {
        var dir = _paths.GetRedscriptDir(game);
        if (!Directory.Exists(dir)) return Array.Empty<CyberpunkMod>();
        var result = new List<CyberpunkMod>();

        // Unterordner-Layout
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var folder = Path.GetFileName(sub);
            bool enabled = !folder.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var displayName = enabled ? folder : folder[..^".disabled".Length];
            result.Add(new CyberpunkMod(CyberpunkModType.Redscript,
                displayName, sub, enabled));
        }
        // Einzel-.reds direkt im scripts-Root
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(file);
            bool enabled;
            string displayName;
            if (name.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
            {
                enabled = true;
                displayName = name[..^".reds".Length];
            }
            else if (name.EndsWith(".reds.disabled", StringComparison.OrdinalIgnoreCase))
            {
                enabled = false;
                displayName = name[..^".reds.disabled".Length];
            }
            else continue;
            long? size = null;
            try { size = new FileInfo(file).Length; } catch { }
            result.Add(new CyberpunkMod(CyberpunkModType.Redscript, displayName, file, enabled,
                SizeBytes: size));
        }
        return result;
    }

    private static RedModInfo? TryReadInfoJson(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RedModInfo>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "info.json unlesbar: {Path}", path);
            return null;
        }
    }

    /// <summary>REDmod-info.json Schema (Auszug — nur die Felder die wir
    /// anzeigen). Original hat mehr wie <c>customSounds</c>, aber die
    /// interessieren uns hier nicht.</summary>
    private sealed class RedModInfo
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
    }
}
