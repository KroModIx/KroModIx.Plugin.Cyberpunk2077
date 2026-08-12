using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.Cyberpunk2077.Services;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>Downloads-Tab (v0.3): listet ZIPs in
/// <see cref="CyberpunkPaths.DownloadsDir"/> (Premium-Direct-Downloads UND
/// manuelle Browser-Downloads die der User dort ablegt). Pro Row: Install
/// (Auto-Layout-Detection ins Game-Root), Löschen. Bulk: Alle
/// installieren.</summary>
public sealed partial class DownloadsViewModel : ObservableObject
{
    private readonly DetectedGame _game;
    private readonly CyberpunkPaths _paths;
    private readonly CyberpunkZipInstaller _installer;
    private readonly IHostServices _host;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DownloadRow> Rows { get; } = new();

    public DownloadsViewModel(DetectedGame game, CyberpunkPaths paths,
        CyberpunkZipInstaller installer, IHostServices host)
    {
        _game = game;
        _paths = paths;
        _installer = installer;
        _host = host;
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Rows.Clear();
        if (!Directory.Exists(_paths.DownloadsDir))
        {
            StatusText = "Downloads-Ordner existiert nicht: " + _paths.DownloadsDir;
            return;
        }
        var zips = Directory.EnumerateFiles(_paths.DownloadsDir, "*.zip",
            SearchOption.TopDirectoryOnly).ToList();
        foreach (var p in zips.OrderByDescending(File.GetLastWriteTimeUtc))
            Rows.Add(new DownloadRow(p, new FileInfo(p)));
        StatusText = zips.Count == 0
            ? $"Keine ZIPs unter {_paths.DownloadsDir} — Nexus-Downloads landen hier."
            : $"{zips.Count} ZIP(s) bereit zum Install.";
    }

    [RelayCommand]
    private void OpenDownloadsFolder() => _host.Shell.OpenDirectory(_paths.DownloadsDir);

    [RelayCommand]
    private async Task InstallAsync(DownloadRow? row)
    {
        if (row is null) return;
        try
        {
            IsBusy = true;
            var result = await Task.Run(() => _installer.Install(row.FullPath, _game));
            _host.Notifications.Notify(result.Message,
                result.Success ? NotificationLevel.Success : NotificationLevel.Warning);
            if (result.Success)
                _host.Logger.Info("Installiert: {File} → {N} Datei(en)", row.FileName, result.InstalledPaths.Count);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "ZIP-Install Ausnahme: {File}", row.FileName);
            await _host.Dialogs.ShowMessageAsync("Install-Fehler", ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task InstallAllAsync()
    {
        if (Rows.Count == 0) return;
        var ok = await _host.Dialogs.ConfirmAsync("Alle installieren?",
            $"{Rows.Count} ZIP(s) werden nacheinander ins Game-Root extrahiert. Fortfahren?",
            okLabel: "Installieren");
        if (!ok) return;
        using var scope = _host.BeginProgress($"Installiere {Rows.Count} ZIP(s) …");
        int done = 0, failed = 0;
        foreach (var row in Rows.ToList())
        {
            scope.Report((double)(done + failed) / Rows.Count,
                $"{done + failed + 1}/{Rows.Count}: {row.FileName}");
            try
            {
                var result = await Task.Run(() => _installer.Install(row.FullPath, _game));
                if (result.Success) done++; else failed++;
            }
            catch (Exception ex)
            {
                _host.Logger.Warn(ex, "Bulk-Install {File}", row.FileName);
                failed++;
            }
        }
        _host.Notifications.Notify($"{done} installiert, {failed} Fehler.",
            failed == 0 ? NotificationLevel.Success : NotificationLevel.Warning);
    }

    [RelayCommand]
    private async Task DeleteAsync(DownloadRow? row)
    {
        if (row is null) return;
        var ok = await _host.Dialogs.ConfirmAsync("ZIP löschen?",
            $"{row.FileName} wirklich löschen? (Nur die ZIP im Downloads-Ordner, " +
            $"schon installierte Dateien im Game-Root bleiben.)",
            okLabel: "Löschen");
        if (!ok) return;
        try { File.Delete(row.FullPath); Refresh(); }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "ZIP-Delete: {File}", row.FileName);
            await _host.Dialogs.ShowMessageAsync("Fehler", ex.Message);
        }
    }
}

public sealed class DownloadRow
{
    public string FullPath { get; }
    public string FileName { get; }
    public string ModifiedText { get; }
    public string SizeText { get; }

    public DownloadRow(string path, FileInfo fi)
    {
        FullPath = path;
        FileName = fi.Name;
        ModifiedText = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
        SizeText = fi.Length switch
        {
            < 1024 => $"{fi.Length} B",
            < 1024 * 1024 => $"{fi.Length / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{fi.Length / (1024.0 * 1024):F1} MB",
            _ => $"{fi.Length / (1024.0 * 1024 * 1024):F2} GB",
        };
    }
}
