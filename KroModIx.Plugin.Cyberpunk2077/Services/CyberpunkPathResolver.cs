using System;
using System.Collections.Generic;
using System.IO;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Findet die fünf gängigen Cyberpunk-Mod-Ordner relativ zum
/// Game-Install-Verzeichnis (<see cref="DetectedGame.InstallDir"/>).
///
/// <para><b>v0.15.0:</b> die Pfade kommen aus
/// <see cref="ModFolderDiscovery"/> statt aus festen
/// <c>Path.Combine</c>-Einzeilern. Zwei Gruende, beide real aufgetreten:
/// keiner der fünf Ordner gehoert zur Vanilla-Installation — nach einer
/// Neuinstallation sind alle weg, und das Plugin stand mit leerer Liste ohne
/// Install-Ziel da. Und unter Linux entscheidet die Gross-/Kleinschreibung:
/// ein von Hand angelegtes <c>Mods/</c> fand <c>Directory.Exists(".../mods")</c>
/// nie, obwohl das Spiel es laedt.</para>
///
/// <para><c>Get*Dir</c> liefert den vorhandenen Ordner oder null (nur lesen),
/// <c>Ensure*Dir</c> legt den kanonischen Pfad an — das ist derselbe, den auch
/// REDmod, CET und RED4ext anlegen.</para></summary>
public sealed class CyberpunkPathResolver
{
    private static readonly string[] ArchiveCandidates    = { "archive/pc/mod" };
    private static readonly string[] RedModCandidates     = { "mods", "Mods" };
    private static readonly string[] CetCandidates        = { "bin/x64/plugins/cyber_engine_tweaks/mods" };
    private static readonly string[] Red4ExtCandidates    = { "red4ext/plugins" };
    private static readonly string[] RedscriptCandidates  = { "r6/scripts" };

    public string? GetArchiveDir(DetectedGame game) => Find(game, ArchiveCandidates);
    public string? GetRedModDir(DetectedGame game) => Find(game, RedModCandidates);
    public string? GetCetDir(DetectedGame game) => Find(game, CetCandidates);
    public string? GetRed4ExtDir(DetectedGame game) => Find(game, Red4ExtCandidates);
    public string? GetRedscriptDir(DetectedGame game) => Find(game, RedscriptCandidates);

    public string? EnsureArchiveDir(DetectedGame game) => Ensure(game, ArchiveCandidates);
    public string? EnsureRedModDir(DetectedGame game) => Ensure(game, RedModCandidates);
    public string? EnsureCetDir(DetectedGame game) => Ensure(game, CetCandidates);
    public string? EnsureRed4ExtDir(DetectedGame game) => Ensure(game, Red4ExtCandidates);
    public string? EnsureRedscriptDir(DetectedGame game) => Ensure(game, RedscriptCandidates);

    /// <summary>Alle fünf Mod-Orte, die wirklich existieren — fuer Scans und
    /// fuer die Ordner-Auswahl im Downloads-Tab.</summary>
    public IReadOnlyList<string> GetExistingModDirs(DetectedGame game)
    {
        if (string.IsNullOrEmpty(game.InstallDir)) return Array.Empty<string>();
        return ModFolderDiscovery.FindAll(game.InstallDir,
            "archive/pc/mod", "mods", "bin/x64/plugins/cyber_engine_tweaks/mods",
            "r6/scripts", "red4ext/plugins");
    }

    private static string? Find(DetectedGame game, string[] candidates)
        => string.IsNullOrEmpty(game.InstallDir)
            ? null
            : ModFolderDiscovery.Find(game.InstallDir, candidates);

    private static string? Ensure(DetectedGame game, string[] candidates)
        => string.IsNullOrEmpty(game.InstallDir)
            ? null
            : ModFolderDiscovery.FindOrCreate(game.InstallDir, candidates);

    /// <summary>Existiert die Standard-Cyberpunk-Verzeichnisstruktur? Wird
    /// vor jedem Scan geprüft — bei ungewöhnlichen Installations-Pfaden
    /// (z. B. GOG-Standalone in exotischem Ordner) meldet der Scanner
    /// dann sauber "kein CP-Ordner".</summary>
    public bool LooksLikeCyberpunkInstall(DetectedGame game)
    {
        if (string.IsNullOrEmpty(game.InstallDir)) return false;
        // bin/x64/ + engine/ sind Standard-Cyberpunk-Marker
        return Directory.Exists(Path.Combine(game.InstallDir, "bin", "x64"))
            && Directory.Exists(Path.Combine(game.InstallDir, "engine"));
    }
}
