using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Persistiert pro installiertem Mod ein Manifest-File mit dem
/// Nexus-Match-Kontext (ModId + Original-Filename) damit der Installiert-
/// Tab spaeter Cover + Meta ueber <see cref="INexusService"/> nachziehen
/// kann. Ohne diesen Store gaebe es bei installierten Mods keine
/// Nexus-Enrichment-Moeglichkeit — der Nexus-Filename wird beim Extract
/// verworfen.
///
/// <para>Layout:</para>
/// <code>
/// ~/.config/KroModIx/plugin-data/kroste.cyberpunk2077/install-manifests/
/// ├── Archive_Liberty_stock.json
/// ├── RedMod_ImmersiveHacking.json
/// └── …
/// </code>
///
/// <para>Manifest-Key = <c>{Type}_{Name}</c> — stabil ueber Enable/Disable
/// hinweg (die <c>.disabled</c>-Suffixe sind nicht im Namen).</para></summary>
public sealed class InstallManifestStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _dir;

    public InstallManifestStore(IHostServices host)
    {
        _dir = Path.Combine(host.PluginDataDir, "install-manifests");
        Directory.CreateDirectory(_dir);
    }

    public static string BuildKey(CyberpunkModType type, string name)
        => $"{type}_{SanitizeFileName(name)}";

    private static string SanitizeFileName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c);
        return sb.ToString();
    }

    public InstallManifest? TryGet(string key)
    {
        var path = Path.Combine(_dir, key + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<InstallManifest>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "InstallManifest unlesbar: {Path}", path);
            return null;
        }
    }

    public void Save(string key, InstallManifest manifest)
    {
        var path = Path.Combine(_dir, key + ".json");
        try
        {
            var json = JsonSerializer.Serialize(manifest, JsonOpts);
            File.WriteAllText(path, json);
            Log.Debug("InstallManifest gespeichert: {Key} → nexus mod_id={Id}",
                key, manifest.NexusModId);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "InstallManifest-Save fehlgeschlagen: {Path}", path);
        }
    }

    public void Delete(string key)
    {
        var path = Path.Combine(_dir, key + ".json");
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Log.Debug(ex, "InstallManifest-Delete fehlgeschlagen: {Path}", path); }
    }
}

/// <summary>Manifest-Payload — persistiert pro installiertem Mod. Fields
/// bewusst minimal (weitere Meta kommt on-demand ueber Nexus-API).</summary>
public sealed record InstallManifest(
    int? NexusModId,
    string? OriginalFilename,
    DateTime InstalledAtUtc,
    IReadOnlyList<string> InstalledPaths);
