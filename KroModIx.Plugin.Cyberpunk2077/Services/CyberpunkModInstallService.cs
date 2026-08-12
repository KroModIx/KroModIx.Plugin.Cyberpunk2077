using System;
using System.IO;
using NLog;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Enable/Disable/Uninstall für alle Cyberpunk-Mod-Typen.
/// Enable/Disable via <c>.disabled</c>-Suffix (bei Files am Filename, bei
/// Ordnern am Ordner-Namen). Uninstall = File-/Directory-Delete rekursiv.
///
/// <para>Bewusst kein Install-Command in v0.1 — Mod-ZIPs von Nexus haben
/// unterschiedliche Layouts (manche mit <c>archive/pc/mod</c>-Struktur zum
/// Extrahieren ins Game-Root, manche brauchen manuelle Zuordnung). Kommt in
/// v0.2 mit Nexus-Katalog-Anbindung wo wir die Mod-Metadata haben.</para></summary>
public sealed class CyberpunkModInstallService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Ändert den enabled-Zustand durch Rename. Liefert den neuen
    /// Pfad zurück (mit oder ohne .disabled-Suffix). Bei bereits gewünschtem
    /// Zustand: keine Aktion, gibt den vorhandenen Pfad zurück.</summary>
    public string SetEnabled(CyberpunkMod mod, bool enabled)
    {
        if (mod.IsEnabled == enabled) return mod.Path;
        var newPath = TargetPathForState(mod, enabled);
        if (string.Equals(newPath, mod.Path, StringComparison.OrdinalIgnoreCase))
            return mod.Path;

        // File oder Directory? System.IO.File / Directory hat unterschiedliche
        // Move-Methoden, aber MoveTo mit overwrite=false ist bei Files
        // simpel, bei Directories nutzen wir Directory.Move (schlägt fehl
        // bei bestehendem Zielordner → sauber).
        try
        {
            if (File.Exists(mod.Path))
            {
                File.Move(mod.Path, newPath, overwrite: false);
            }
            else if (Directory.Exists(mod.Path))
            {
                if (Directory.Exists(newPath) || File.Exists(newPath))
                    throw new IOException($"Zielpfad existiert bereits: {newPath}");
                Directory.Move(mod.Path, newPath);
            }
            else
            {
                throw new FileNotFoundException($"Mod-Pfad nicht mehr da: {mod.Path}");
            }
            Log.Info("Mod {Name} ({Type}): {From} → {To}",
                mod.Name, mod.Type, mod.Path, newPath);
            return newPath;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "SetEnabled fehlgeschlagen: {Name}", mod.Name);
            throw;
        }
    }

    /// <summary>Löscht den Mod komplett (File → Delete, Ordner →
    /// Recursive-Delete). Wenn der Pfad nicht existiert: no-op.</summary>
    public void Uninstall(CyberpunkMod mod)
    {
        try
        {
            if (File.Exists(mod.Path))
                File.Delete(mod.Path);
            else if (Directory.Exists(mod.Path))
                Directory.Delete(mod.Path, recursive: true);
            Log.Info("Mod deinstalliert: {Name} ({Type}) - {Path}",
                mod.Name, mod.Type, mod.Path);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Uninstall fehlgeschlagen: {Name}", mod.Name);
            throw;
        }
    }

    /// <summary>Berechnet den Ziel-Pfad für den gewünschten enabled-Zustand.
    /// Bei File-Mods (Archive/Redscript-Einzeldatei) wird die Extension um
    /// <c>.disabled</c> ergänzt/gekürzt, bei Ordner-Mods der Ordner-Name.</summary>
    private static string TargetPathForState(CyberpunkMod mod, bool enabled)
    {
        var path = mod.Path;
        bool isFile = File.Exists(path);
        if (mod.Type == CyberpunkModType.Archive || (mod.Type == CyberpunkModType.Redscript && isFile))
        {
            // File-Mode: Suffix-Toggle
            if (!enabled)
            {
                return path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? path : path + ".disabled";
            }
            return path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? path[..^".disabled".Length] : path;
        }
        // Ordner-Mode: Namen-Suffix-Toggle
        var parent = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        if (!enabled)
        {
            if (!name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                name += ".disabled";
        }
        else
        {
            if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                name = name[..^".disabled".Length];
        }
        return Path.Combine(parent, name);
    }
}
