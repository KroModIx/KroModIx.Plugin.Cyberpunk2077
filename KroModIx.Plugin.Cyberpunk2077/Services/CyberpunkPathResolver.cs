using System.IO;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Findet die fünf gängigen Cyberpunk-Mod-Ordner relativ zum
/// Game-Install-Verzeichnis (<see cref="DetectedGame.InstallDir"/>). Die
/// Ordner existieren nur wenn der User bereits Mods installiert hat — der
/// Resolver liefert IMMER Pfade zurück (auch wenn der Ordner nicht existiert),
/// damit der Scanner sie einheitlich prüfen kann.</summary>
public sealed class CyberpunkPathResolver
{
    public string GetArchiveDir(DetectedGame game) =>
        Path.Combine(game.InstallDir, "archive", "pc", "mod");

    public string GetRedModDir(DetectedGame game) =>
        Path.Combine(game.InstallDir, "mods");

    public string GetCetDir(DetectedGame game) =>
        Path.Combine(game.InstallDir, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods");

    public string GetRed4ExtDir(DetectedGame game) =>
        Path.Combine(game.InstallDir, "red4ext", "plugins");

    public string GetRedscriptDir(DetectedGame game) =>
        Path.Combine(game.InstallDir, "r6", "scripts");

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
