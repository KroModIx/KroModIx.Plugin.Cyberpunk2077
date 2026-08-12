using System.IO;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Plugin-lokale Pfade (Downloads-Verzeichnis pro Session).</summary>
public sealed class CyberpunkPaths
{
    public string DownloadsDir { get; }

    public CyberpunkPaths(IHostServices host)
    {
        DownloadsDir = Path.Combine(host.PluginDataDir, "downloads");
        Directory.CreateDirectory(DownloadsDir);
    }
}
