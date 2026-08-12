using System.IO;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Die fünf gängigen Cyberpunk-Mod-Typen. Reihenfolge bestimmt die
/// Sortier-Prio in der UI (Archive zuerst — häufigster Fall).</summary>
public enum CyberpunkModType
{
    /// <summary><c>archive/pc/mod/*.archive</c> — Content-Assets
    /// (Meshes, Texturen, Musik).</summary>
    Archive,
    /// <summary><c>mods/&lt;name&gt;/info.json</c> — offizielles REDmod-Framework
    /// von CDPR (Assets + Skripte kombiniert).</summary>
    RedMod,
    /// <summary><c>bin/x64/plugins/cyber_engine_tweaks/mods/&lt;name&gt;/</c> —
    /// CET Lua-Mods.</summary>
    CyberEngineTweaks,
    /// <summary><c>red4ext/plugins/&lt;name&gt;/*.dll</c> — RED4ext-Plugins.</summary>
    Red4Ext,
    /// <summary><c>r6/scripts/&lt;name&gt;/*.reds</c> oder
    /// <c>r6/scripts/*.reds</c> — redscript-Mods.</summary>
    Redscript,
}

/// <summary>Ein installiertes Cyberpunk-Mod. Immutable — ein Rescan liefert
/// eine neue Liste. <see cref="Path"/> ist der Pfad zur Datei (Archive/
/// Redscript) oder zum Ordner (REDmod/CET/Red4Ext).</summary>
public sealed record CyberpunkMod(
    CyberpunkModType Type,
    string Name,
    string Path,
    bool IsEnabled,
    string? Version = null,
    string? Author = null,
    string? Description = null,
    long? SizeBytes = null,
    int? NexusModId = null)
{
    /// <summary>Stabiler Manifest-Key ({Type}_{Name}) fuer den
    /// <see cref="InstallManifestStore"/>-Lookup. Enthaelt nicht das
    /// <c>.disabled</c>-Suffix (Name ist bereits bereinigt).</summary>
    public string ManifestKey => InstallManifestStore.BuildKey(Type, Name);

    public bool HasNexusMatch => NexusModId is not null;

    /// <summary>Human-friendly Type-Label für die UI (z. B. „Archive", „REDmod").</summary>
    public string TypeLabel => Type switch
    {
        CyberpunkModType.Archive => "Archive",
        CyberpunkModType.RedMod => "REDmod",
        CyberpunkModType.CyberEngineTweaks => "CET",
        CyberpunkModType.Red4Ext => "RED4ext",
        CyberpunkModType.Redscript => "redscript",
        _ => "?",
    };

    /// <summary>Kompakter Icon-String je Mod-Typ.</summary>
    public string TypeIcon => Type switch
    {
        CyberpunkModType.Archive => "📦",
        CyberpunkModType.RedMod => "🎮",
        CyberpunkModType.CyberEngineTweaks => "🔧",
        CyberpunkModType.Red4Ext => "🧩",
        CyberpunkModType.Redscript => "📜",
        _ => "❓",
    };

    /// <summary>Existiert der Mod-Pfad noch auf der Platte? Für Sanity-Checks
    /// bevor Enable/Disable/Uninstall aufgerufen werden.</summary>
    public bool StillExists() => File.Exists(Path) || Directory.Exists(Path);
}
