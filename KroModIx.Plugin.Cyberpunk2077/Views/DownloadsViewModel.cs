using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.Cyberpunk2077.Services;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>Downloads-Tab (v0.3): listet ZIPs in
/// <see cref="CyberpunkPaths.DownloadsDir"/> (Premium-Direct-Downloads UND
/// manuelle Browser-Downloads die der User dort ablegt). Pro Row: Install
/// (Auto-Layout-Detection ins Game-Root), Löschen. Bulk: Alle
/// installieren. v0.5: subscribed <see cref="DownloadEventBus.DownloadsChanged"/>
/// — Direct-Download aus dem Nexus-Tab landet sofort hier.</summary>
public sealed partial class DownloadsViewModel : ObservableObject, IDisposable
{
    private readonly DetectedGame _game;
    private readonly CyberpunkPaths _paths;
    private readonly CyberpunkZipInstaller _installer;
    private readonly DownloadEventBus _downloadBus;
    private readonly IHostServices _host;
    private readonly EventHandler<string> _downloadsChangedHandler;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DownloadRow> Rows { get; } = new();

    public DownloadsViewModel(DetectedGame game, CyberpunkPaths paths,
        CyberpunkZipInstaller installer, DownloadEventBus downloadBus,
        IHostServices host)
    {
        _game = game;
        _paths = paths;
        _installer = installer;
        _downloadBus = downloadBus;
        _host = host;
        _downloadsChangedHandler = (_, _) => Dispatcher.UIThread.Post(Refresh);
        _downloadBus.DownloadsChanged += _downloadsChangedHandler;
        Refresh();
    }

    public void Dispose()
    {
        _downloadBus.DownloadsChanged -= _downloadsChangedHandler;
    }

    [RelayCommand]
    private void Refresh()
    {
        Rows.Clear();
        if (!Directory.Exists(_paths.DownloadsDir))
        {
            StatusText = Strings.T("status.downloads_dir_missing") + _paths.DownloadsDir;
            return;
        }
        // v0.8.1: alle unterstuetzten Archiv-Formate scannen (ZIP+RAR+7z).
        var archives = Directory.EnumerateFiles(_paths.DownloadsDir, "*",
                SearchOption.TopDirectoryOnly)
            .Where(p => CyberpunkZipInstaller.SupportedExtensions
                .Any(ext => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        foreach (var p in archives.OrderByDescending(File.GetLastWriteTimeUtc))
            Rows.Add(new DownloadRow(p, new FileInfo(p)));
        StatusText = archives.Count == 0
            ? string.Format(Strings.T("status.no_zips_hint"), _paths.DownloadsDir)
            : string.Format(Strings.T("status.zips_ready"), archives.Count);
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
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.install_error_title"), ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task InstallAllAsync()
    {
        if (Rows.Count == 0) return;
        var ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("dialog.install_all_title"),
            string.Format(Strings.T("dialog.install_all_msg"), Rows.Count),
            okLabel: Strings.T("dialog.install_all_ok"));
        if (!ok) return;
        using var scope = _host.BeginProgress(string.Format(Strings.T("progress.install_zips"), Rows.Count));
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
        _host.Notifications.Notify(string.Format(Strings.T("notify.bulk_install_result"), done, failed),
            failed == 0 ? NotificationLevel.Success : NotificationLevel.Warning);
    }

    [RelayCommand]
    private async Task DeleteAsync(DownloadRow? row)
    {
        if (row is null) return;
        var ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("dialog.delete_zip_title"),
            string.Format(Strings.T("dialog.delete_zip_msg"), row.FileName),
            okLabel: Strings.T("dialog.delete_zip_ok"));
        if (!ok) return;
        try { File.Delete(row.FullPath); Refresh(); }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "ZIP-Delete: {File}", row.FileName);
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.error_title"), ex.Message);
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
