using System;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Plugin-interner Event-Bus fuer Cross-Tab-Refresh: der Nexus-Tab
/// laedt eine ZIP via Premium-Direct-Download in den Downloads-Ordner, der
/// Downloads-Tab soll sie sofort sehen ohne dass der User „Aktualisieren"
/// klickt. Konsumenten muessen den Handler auf den UI-Thread posten — der
/// Event feuert auf dem Download-Task-Thread.</summary>
public sealed class DownloadEventBus
{
    /// <summary>Datei im Downloads-Ordner erschienen. Payload = Dateiname.</summary>
    public event EventHandler<string>? DownloadsChanged;

    public void RaiseDownloadsChanged(string fileName)
        => DownloadsChanged?.Invoke(this, fileName);
}
