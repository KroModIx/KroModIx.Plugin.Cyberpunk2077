using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Installiert ein Nexus-Mod-Archiv (ZIP/RAR/7z) mit Auto-Layout-
/// Erkennung ins Cyberpunk-Game-Root.
///
/// <para><b>Seit v0.16.0 über <c>IHostServices.Archives</c></b> (Host
/// v1.30.0). Das Plugin entscheidet weiter, <b>welches</b> Layout ein Archiv
/// hat und <b>wohin</b> seine Dateien gehören — das ist Cyberpunk-Wissen.
/// Das Öffnen der Formate und der Ausbruch-Schutz kommen aus dem Host.</para>
///
/// <para><b>Warum das keine Aufräumarbeit war.</b> Der eigene Schutz hier
/// prüfte <c>name.Contains("..")</c>. Am 03.10.2026 nachgemessen: ein
/// Archiv-Eintrag mit <b>absolutem</b> Namen enthält kein <c>..</c>, kommt
/// also durch, und <c>Path.Combine(installDir, "/tmp/ausserhalb.txt")</c>
/// gibt <c>/tmp/ausserhalb.txt</c> zurück — das Zielverzeichnis wird
/// verworfen. Die Datei landete außerhalb des Spiels, und der Install
/// meldete <c>Success = true</c>. Der Host-Schutz
/// (<see cref="ArchivePathSafety"/>) löst jeden Zielpfad auf und nimmt ihn
/// nur, wenn er wirklich unter dem Ziel landet.</para>
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
    private readonly IArchiveService _archives;
    private readonly InstallManifestStore? _manifests;

    public CyberpunkZipInstaller(IArchiveService archives, InstallManifestStore? manifests = null)
    {
        _archives = archives;
        _manifests = manifests;
    }

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

    /// <summary>Endungs-Vorfilter fuer den Downloads-Tab-Scan. Kommt aus dem
    /// Host-Baukasten, damit ein dort neu unterstuetztes Format nicht in
    /// neun Plugins nachgetragen werden muss.</summary>
    public IReadOnlyList<string> SupportedExtensions => _archives.SupportedExtensions;

    public bool HasSupportedExtension(string path)
        => _archives.HasSupportedExtension(path);

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
            // Am Inhalt pruefen, nicht an der Endung: ein Download mit
            // falscher Endung landete sonst unveraendert im Spiel.
            if (_archives.DetectKind(archivePath) == ArchiveKind.Unknown)
                return new ZipInstallResult(false,
                    "Das ist kein lesbares Archiv (ZIP/RAR/7z) — eventuell ein abgebrochener Download.",
                    Array.Empty<string>());

            var entries = _archives.List(archivePath);
            if (entries.Count == 0)
                return new ZipInstallResult(false, "Archiv ist leer.", Array.Empty<string>());

            // 1) Bekannter Root im Archiv? Dann direkt ins Game-Root extrahieren.
            bool knownLayout = entries.Any(e =>
                KnownRoots.Any(root => e.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase)));

            if (knownLayout)
            {
                var r = _archives.Extract(archivePath, installDir);
                if (Abgelehnt(r) is { } warnung)
                    return new ZipInstallResult(false, warnung, r.ExtractedPaths);
                WriteManifests(r.ExtractedPaths, installDir, archivePath);
                return new ZipInstallResult(true,
                    $"Direkt-Layout erkannt — {r.Count} Datei(en) ins Game-Root extrahiert.",
                    r.ExtractedPaths);
            }

            // 2) Fallback: single-.archive-Layout → archive/pc/mod/.
            var hatArchives = entries.Any(e =>
                e.Path.EndsWith(".archive", StringComparison.OrdinalIgnoreCase));
            var hatReds = entries.Any(e =>
                e.Path.EndsWith(".reds", StringComparison.OrdinalIgnoreCase));

            if (hatArchives && !hatReds)
            {
                // v0.15.0: anlegen statt annehmen — nach einer Neuinstallation
                // gibt es archive/pc/mod nicht, und der Install lief ins Leere.
                var target = ModFolderDiscovery.FindOrCreate(installDir, "archive/pc/mod")
                             ?? Path.Combine(installDir, "archive", "pc", "mod");
                Directory.CreateDirectory(target);
                var r = _archives.Extract(archivePath, target, new ArchiveExtractOptions(
                    Filter: p => p.EndsWith(".archive", StringComparison.OrdinalIgnoreCase),
                    Flatten: true));
                if (Abgelehnt(r) is { } warnung)
                    return new ZipInstallResult(false, warnung, r.ExtractedPaths);
                WriteManifests(r.ExtractedPaths, installDir, archivePath);
                return new ZipInstallResult(true,
                    $"Flat-Layout: {r.Count} .archive-Datei(en) nach archive/pc/mod/ extrahiert.",
                    r.ExtractedPaths);
            }

            return new ZipInstallResult(false,
                "Unbekanntes Archiv-Layout — bitte manuell entpacken. " +
                $"Archiv enthält {entries.Count} Dateien in Ordnern: " +
                string.Join(", ", entries.Take(5)
                    .Select(e => Path.GetDirectoryName(e.Path)).Distinct()),
                Array.Empty<string>());
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Archive-Install fehlgeschlagen: {Archive}", archivePath);
            return new ZipInstallResult(false, "Fehler: " + ex.Message, Array.Empty<string>());
        }
    }

    /// <summary>Hat der Ausbruch-Schutz Einträge abgelehnt, bricht der
    /// Install mit Meldung ab — statt still das zu installieren, was
    /// durchkam.
    ///
    /// <para>Der Abbruch trifft auch ein bloß kaputtes Archiv, bei dem ein
    /// einzelner Eintrag von zweihundert krumm ist. Das ist bewusst: ein
    /// Archiv, das aus dem Spielverzeichnis herausschreiben will, ist nicht
    /// „überwiegend in Ordnung", und die Entscheidung, es trotzdem zu
    /// nehmen, gehört dem Nutzer und nicht einer Zeile Code. Die bereits
    /// geschriebenen Dateien stehen in der Antwort, damit die Meldung sagen
    /// kann, was schon im Spiel liegt.</para></summary>
    private static string? Abgelehnt(ArchiveExtractResult r)
    {
        if (r.SkippedUnsafe.Count == 0) return null;
        Log.Warn("Ausbruchsversuch im Archiv, {Count} Eintrag/Einträge abgelehnt: {Entries}",
            r.SkippedUnsafe.Count, string.Join(", ", r.SkippedUnsafe));
        return $"Abgebrochen: {r.SkippedUnsafe.Count} Eintrag/Einträge wollten aus dem "
             + "Spielverzeichnis herausschreiben — "
             + string.Join(", ", r.SkippedUnsafe.Take(3))
             + (r.SkippedUnsafe.Count > 3 ? ", …" : "")
             + $". {r.Count} Datei(en) waren schon geschrieben, bevor das auffiel.";
    }

    /// <summary>Fuer jeden installierten Mod-Bestandteil ein Manifest im
    /// <see cref="InstallManifestStore"/> schreiben. Nexus-ModId wird aus
    /// dem Archive-Filename per <see cref="NexusFileNameParser"/> gelesen —
    /// wenn der User manuell reinkopierte Archive installiert, bleibt sie
    /// null (dann kein Enrichment im Installiert-Tab, kein Details-Button).</summary>
    private void WriteManifests(IReadOnlyList<string> installedPaths, string installDir, string archivePath)
    {
        if (_manifests is null) return;
        var archiveName = Path.GetFileName(archivePath);
        var nexusModId = NexusFileNameParser.TryExtractModId(archiveName);
        var relPaths = installedPaths
            .Select(p => Path.GetRelativePath(installDir, p).Replace('\\', '/'))
            .ToList();

        // Aus den relativen Pfaden die (Type, Name)-Paare ableiten — pro
        // Mod eine Manifest-Datei. Ein Archiv kann mehrere Mods enthalten
        // (mehrere .archive-Files oder mehrere REDmod-Ordner).
        var seenKeys = new HashSet<string>();
        foreach (var rel in relPaths)
        {
            var (type, name) = ClassifyPath(rel);
            if (type is null || string.IsNullOrEmpty(name)) continue;
            var key = InstallManifestStore.BuildKey(type.Value, name);
            if (!seenKeys.Add(key)) continue;
            _manifests.Save(key, new InstallManifest(
                NexusModId: nexusModId,
                OriginalFilename: archiveName,
                InstalledAtUtc: DateTime.UtcNow,
                InstalledPaths: relPaths));
        }
    }

    /// <summary>Aus einem relativen Install-Pfad den (Mod-Typ, Mod-Name)
    /// ableiten — deckungsgleich mit der Scan-Logik in
    /// <see cref="CyberpunkModScanner"/>.</summary>
    private static (CyberpunkModType? Type, string? Name) ClassifyPath(string relPath)
    {
        // Normalize
        var p = relPath.Replace('\\', '/').TrimStart('/');
        var segments = p.Split('/');

        // archive/pc/mod/<name>.archive
        if (segments.Length >= 4
            && segments[0].Equals("archive", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("pc", StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("mod", StringComparison.OrdinalIgnoreCase)
            && segments[3].EndsWith(".archive", StringComparison.OrdinalIgnoreCase))
        {
            return (CyberpunkModType.Archive, segments[3][..^".archive".Length]);
        }
        // mods/<name>/… (REDmod)
        if (segments.Length >= 2 && segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase))
        {
            return (CyberpunkModType.RedMod, segments[1]);
        }
        // bin/x64/plugins/cyber_engine_tweaks/mods/<name>/…
        if (segments.Length >= 6
            && segments[0].Equals("bin", StringComparison.OrdinalIgnoreCase)
            && segments[3].Equals("cyber_engine_tweaks", StringComparison.OrdinalIgnoreCase)
            && segments[4].Equals("mods", StringComparison.OrdinalIgnoreCase))
        {
            return (CyberpunkModType.CyberEngineTweaks, segments[5]);
        }
        // red4ext/plugins/<name>/…
        if (segments.Length >= 3
            && segments[0].Equals("red4ext", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("plugins", StringComparison.OrdinalIgnoreCase))
        {
            return (CyberpunkModType.Red4Ext, segments[2]);
        }
        // r6/scripts/<name>/… oder r6/scripts/<name>.reds
        if (segments.Length >= 3
            && segments[0].Equals("r6", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("scripts", StringComparison.OrdinalIgnoreCase))
        {
            var last = segments[2];
            if (last.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                return (CyberpunkModType.Redscript, last[..^".reds".Length]);
            return (CyberpunkModType.Redscript, last);
        }
        return (null, null);
    }
}

/// <summary>Ergebnis einer <see cref="CyberpunkZipInstaller.Install"/>-
/// Operation. <c>Success=false</c> mit <c>InstalledPaths=empty</c> bei
/// unbekanntem Layout.</summary>
public sealed record ZipInstallResult(bool Success, string Message, IReadOnlyList<string> InstalledPaths);
