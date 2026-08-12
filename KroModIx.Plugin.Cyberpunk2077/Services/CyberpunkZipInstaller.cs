using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KroModIx.Plugin.Contracts;
using NLog;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Installiert ein Nexus-Mod-Archiv (ZIP/RAR/7z) mit Auto-Layout-
/// Detection ins Cyberpunk-Game-Root. Nutzt <see cref="ArchiveFactory"/>
/// aus SharpCompress — Format wird automatisch erkannt, keine
/// Extension-Whitelist noetig.
///
/// <para>Cyberpunk-Archive enthalten typischerweise bereits die Zielordner-
/// Struktur ausgehend vom Game-Root:</para>
/// <list type="bullet">
///   <item><c>archive/pc/mod/*.archive</c></item>
///   <item><c>mods/&lt;name&gt;/info.json + ...</c></item>
///   <item><c>bin/x64/plugins/cyber_engine_tweaks/mods/&lt;name&gt;/</c></item>
///   <item><c>red4ext/plugins/&lt;name&gt;/</c></item>
///   <item><c>r6/scripts/</c> oder <c>r6/tweaks/</c></item>
/// </list>
/// Wenn im Archive-Root eines dieser Praefixe existiert → direktes Extract
/// nach <c>&lt;InstallDir&gt;</c> (bekanntes Layout). Sonst Fallback-
/// Heuristik (single .archive → archive/pc/mod/). Wenn nichts greift:
/// <see cref="ZipInstallResult.Success"/>=false mit Fehler-Meldung.</summary>
public sealed class CyberpunkZipInstaller
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static readonly string[] KnownRoots = new[]
    {
        "archive/pc/mod/",
        "mods/",
        "bin/x64/plugins/cyber_engine_tweaks/mods/",
        "bin/x64/plugins/",
        "red4ext/plugins/",
        "red4ext/",
        "r6/scripts/",
        "r6/tweaks/",
        "r6/",
        "engine/",
    };

    /// <summary>Unterstuetzte Archiv-Endungen fuer Downloads-Tab-Scan +
    /// Install. SharpCompress kann noch mehr (.tar, .gz), aber Nexus-
    /// Cyberpunk-Mods kommen fast ausschliesslich als ZIP oder RAR,
    /// selten 7z.</summary>
    public static readonly string[] SupportedExtensions = new[] { ".zip", ".rar", ".7z" };

    public ZipInstallResult Install(string archivePath, DetectedGame game)
    {
        if (!File.Exists(archivePath))
            return new ZipInstallResult(false, "Archiv nicht gefunden: " + archivePath, Array.Empty<string>());
        var installDir = game.InstallDir;
        if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
            return new ZipInstallResult(false, "Cyberpunk-InstallDir ungültig: " + installDir,
                Array.Empty<string>());

        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            var entries = archive.Entries
                .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key))
                .ToList();
            if (entries.Count == 0)
                return new ZipInstallResult(false, "Archiv ist leer.", Array.Empty<string>());

            var normalized = entries.Select(e => (e.Key ?? "").Replace('\\', '/')).ToList();

            // 1) Bekannter Root im Archiv? Dann direkt ins Game-Root extrahieren.
            bool knownLayout = normalized.Any(p =>
                KnownRoots.Any(root => p.StartsWith(root, StringComparison.OrdinalIgnoreCase)));

            if (knownLayout)
            {
                var installed = ExtractDirect(entries, installDir);
                return new ZipInstallResult(true,
                    $"Direkt-Layout erkannt — {installed.Count} Datei(en) ins Game-Root extrahiert.",
                    installed);
            }

            // 2) Fallback: single-.archive-Layout → archive/pc/mod/.
            var archives = normalized.Where(p =>
                p.EndsWith(".archive", StringComparison.OrdinalIgnoreCase)).ToList();
            var reds = normalized.Where(p =>
                p.EndsWith(".reds", StringComparison.OrdinalIgnoreCase)).ToList();

            if (archives.Count > 0 && reds.Count == 0)
            {
                var target = Path.Combine(installDir, "archive", "pc", "mod");
                Directory.CreateDirectory(target);
                var installed = new List<string>();
                foreach (var e in entries.Where(e =>
                    (e.Key ?? "").EndsWith(".archive", StringComparison.OrdinalIgnoreCase)))
                {
                    var name = Path.GetFileName((e.Key ?? "").Replace('\\', '/'));
                    var dst = Path.Combine(target, name);
                    ExtractOne(e, dst);
                    installed.Add(dst);
                }
                return new ZipInstallResult(true,
                    $"Flat-Layout: {installed.Count} .archive-Datei(en) nach archive/pc/mod/ extrahiert.",
                    installed);
            }

            return new ZipInstallResult(false,
                "Unbekanntes Archiv-Layout — bitte manuell entpacken. " +
                $"Archiv enthält {entries.Count} Dateien in Ordnern: " +
                string.Join(", ", normalized.Take(5)
                    .Select(p => Path.GetDirectoryName(p)).Distinct()),
                Array.Empty<string>());
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Archive-Install fehlgeschlagen: {Archive}", archivePath);
            return new ZipInstallResult(false, "Fehler: " + ex.Message, Array.Empty<string>());
        }
    }

    private static IReadOnlyList<string> ExtractDirect(
        IEnumerable<IArchiveEntry> entries, string installDir)
    {
        var installed = new List<string>();
        foreach (var e in entries)
        {
            var name = (e.Key ?? "").Replace('\\', '/');
            if (string.IsNullOrEmpty(name) || name.EndsWith('/')) continue;
            // Zip-Slip-Prevention: keine ../-Pfade akzeptieren.
            if (name.Contains("..")) { Log.Warn("Zip-Slip-Attempt: {Name}", name); continue; }
            var dst = Path.Combine(installDir, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            ExtractOne(e, dst);
            installed.Add(dst);
        }
        return installed;
    }

    private static void ExtractOne(IArchiveEntry entry, string destination)
    {
        using var input = entry.OpenEntryStream();
        using var output = File.Create(destination);
        input.CopyTo(output);
    }
}

/// <summary>Ergebnis einer <see cref="CyberpunkZipInstaller.Install"/>-
/// Operation. <c>Success=false</c> mit <c>InstalledPaths=empty</c> bei
/// unbekanntem Layout.</summary>
public sealed record ZipInstallResult(bool Success, string Message, IReadOnlyList<string> InstalledPaths);
