using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Installiert eine Nexus-Mod-ZIP mit Auto-Layout-Detection ins
/// Cyberpunk-Game-Root. Cyberpunk-ZIPs enthalten typischerweise bereits
/// die Zielordner-Struktur ausgehend vom Game-Root:
/// <list type="bullet">
///   <item><c>archive/pc/mod/*.archive</c></item>
///   <item><c>mods/&lt;name&gt;/info.json + ...</c></item>
///   <item><c>bin/x64/plugins/cyber_engine_tweaks/mods/&lt;name&gt;/</c></item>
///   <item><c>red4ext/plugins/&lt;name&gt;/</c></item>
///   <item><c>r6/scripts/</c> oder <c>r6/tweaks/</c></item>
/// </list>
/// Wenn im ZIP-Root eines dieser Präfixe existiert → direktes Extract nach
/// <c>&lt;InstallDir&gt;</c> (bekanntes Layout). Sonst versuchen wir
/// Fallback-Heuristiken (single .archive → archive/pc/mod/, single .reds →
/// r6/scripts/). Wenn nichts greift: <see cref="ZipInstallResult.Success"/>=false
/// mit Fehler-Meldung.</summary>
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

    public ZipInstallResult Install(string zipPath, DetectedGame game)
    {
        if (!File.Exists(zipPath))
            return new ZipInstallResult(false, "ZIP nicht gefunden: " + zipPath, Array.Empty<string>());
        var installDir = game.InstallDir;
        if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
            return new ZipInstallResult(false, "Cyberpunk-InstallDir ungültig: " + installDir,
                Array.Empty<string>());

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries
                .Where(e => !string.IsNullOrEmpty(e.FullName) && !e.FullName.EndsWith('/'))
                .ToList();

            // 1) Bekannter Root im ZIP? Dann direkt ins Game-Root extrahieren.
            var normalized = entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            bool knownLayout = normalized.Any(p =>
                KnownRoots.Any(root => p.StartsWith(root, StringComparison.OrdinalIgnoreCase)));

            if (knownLayout)
            {
                var installed = ExtractDirect(archive, installDir);
                return new ZipInstallResult(true,
                    $"Direkt-Layout erkannt — {installed.Count} Datei(en) ins Game-Root extrahiert.",
                    installed);
            }

            // 2) Fallback-Heuristiken auf Flat-ZIP-Layout (keine bekannten Ordner).
            //    Single .archive → archive/pc/mod/
            //    Single .reds → r6/scripts/
            var archives = normalized.Where(p =>
                p.EndsWith(".archive", StringComparison.OrdinalIgnoreCase)).ToList();
            var reds = normalized.Where(p =>
                p.EndsWith(".reds", StringComparison.OrdinalIgnoreCase)).ToList();

            if (archives.Count > 0 && reds.Count == 0)
            {
                var target = Path.Combine(installDir, "archive", "pc", "mod");
                Directory.CreateDirectory(target);
                var installed = new List<string>();
                foreach (var e in entries.Where(e => e.FullName.EndsWith(".archive",
                    StringComparison.OrdinalIgnoreCase)))
                {
                    var name = Path.GetFileName(e.FullName);
                    var dst = Path.Combine(target, name);
                    e.ExtractToFile(dst, overwrite: true);
                    installed.Add(dst);
                }
                return new ZipInstallResult(true,
                    $"Flat-Layout: {installed.Count} .archive-Datei(en) nach archive/pc/mod/ extrahiert.",
                    installed);
            }

            return new ZipInstallResult(false,
                "Unbekanntes ZIP-Layout — bitte manuell entpacken. " +
                $"ZIP enthält {entries.Count} Dateien in Ordnern: " +
                string.Join(", ", entries.Take(5).Select(e => Path.GetDirectoryName(e.FullName)).Distinct()),
                Array.Empty<string>());
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "ZIP-Install fehlgeschlagen: {Zip}", zipPath);
            return new ZipInstallResult(false, "Fehler: " + ex.Message, Array.Empty<string>());
        }
    }

    private static IReadOnlyList<string> ExtractDirect(ZipArchive archive, string installDir)
    {
        var installed = new List<string>();
        foreach (var e in archive.Entries)
        {
            var name = e.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(name) || name.EndsWith('/')) continue;
            // Zip-Slip-Prevention: keine ../-Pfade
            if (name.Contains("..")) { Log.Warn("Zip-Slip-Attempt: {Name}", name); continue; }
            var dst = Path.Combine(installDir, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            e.ExtractToFile(dst, overwrite: true);
            installed.Add(dst);
        }
        return installed;
    }
}

/// <summary>Ergebnis einer <see cref="CyberpunkZipInstaller.Install"/>-
/// Operation. <c>Success=false</c> mit <c>InstalledPaths=empty</c> bei
/// unbekanntem Layout.</summary>
public sealed record ZipInstallResult(bool Success, string Message, IReadOnlyList<string> InstalledPaths);
