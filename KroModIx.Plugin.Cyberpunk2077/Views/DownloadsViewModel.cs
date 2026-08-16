using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.Cyberpunk2077.Services;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>Downloads-Tab (v0.8.3, Kroste-Card-Look mit Nexus-Enrichment):
/// listet Archive (ZIP/RAR/7z) im plugin-eigenen Downloads-Ordner. Pro Row:
/// Install (Auto-Layout-Detection ins Game-Root), Details (Nexus-Detail-
/// Dialog mit Screenshot-Galerie + KI-Zusammenfassung), Löschen. Auto-
/// Refresh via <see cref="DownloadEventBus.DownloadsChanged"/>. Async-
/// Enrichment: pro Row mit erkannter Nexus-ModId (Filename-Parse) wird im
/// Hintergrund das Cover + Author + Version + Summary via
/// <see cref="INexusService"/> gefetcht.</summary>
public sealed partial class DownloadsViewModel : ObservableObject, IDisposable
{
    private readonly DetectedGame _game;
    private readonly CyberpunkPaths _paths;
    private readonly CyberpunkZipInstaller _installer;
    private readonly DownloadEventBus _downloadBus;
    private readonly INexusService _nexus;
    private readonly CyberpunkDownloader _downloader;
    private readonly CoverCache _covers;
    private readonly NexusMediaScraper _mediaScraper;
    private readonly IHostServices _host;
    private readonly EventHandler<string> _downloadsChangedHandler;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DownloadRow> Rows { get; } = new();

    public DownloadsViewModel(DetectedGame game, CyberpunkPaths paths,
        CyberpunkZipInstaller installer, DownloadEventBus downloadBus,
        INexusService nexus, CyberpunkDownloader downloader, CoverCache covers,
        NexusMediaScraper mediaScraper, IHostServices host)
    {
        _game = game;
        _paths = paths;
        _installer = installer;
        _downloadBus = downloadBus;
        _nexus = nexus;
        _downloader = downloader;
        _covers = covers;
        _mediaScraper = mediaScraper;
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
            StatusText = Strings.T("status.no_zips_hint") + " " + _paths.DownloadsDir;
            return;
        }
        var archives = Directory.EnumerateFiles(_paths.DownloadsDir, "*",
                SearchOption.TopDirectoryOnly)
            .Where(p => CyberpunkZipInstaller.SupportedExtensions
                .Any(ext => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        foreach (var p in archives.OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var fi = new FileInfo(p);
            var row = new DownloadRow(p, fi)
            {
                NexusModId = NexusFileNameParser.TryExtractModId(fi.Name),
                ModName = NexusFileNameParser.TryExtractModName(fi.Name),
                Version = NexusFileNameParser.TryExtractVersion(fi.Name),
            };
            Rows.Add(row);
        }
        StatusText = archives.Count == 0
            ? string.Format(Strings.T("status.no_zips_hint"), _paths.DownloadsDir)
            : string.Format(Strings.T("status.zips_ready"), archives.Count);

        // Async: Nexus-Detail-Fetch + Cover-Load pro Row mit erkennbarer ModId.
        _ = EnrichRowsAsync(Rows.ToArray());
    }

    /// <summary>Iteriert ueber Rows mit erkannter <see cref="DownloadRow.NexusModId"/>,
    /// holt Detail via Nexus-API + Cover ueber CoverCache. Throttled: 250 ms
    /// zwischen Detail-Requests damit Rate-Limit-freundlich.</summary>
    private async Task EnrichRowsAsync(DownloadRow[] rows)
    {
        if (!_nexus.HasApiKey) return;
        foreach (var row in rows)
        {
            if (row.NexusModId is not int modId) continue;
            try
            {
                var detail = await _nexus.GetModDetailAsync(CyberpunkNexusCatalog.GameSlug, modId);
                if (detail is null) continue;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    row.ModName = detail.Name;
                    row.Author = detail.Author;
                    if (!string.IsNullOrWhiteSpace(detail.Version)) row.Version = detail.Version;
                    row.Summary = detail.Summary;
                });
                if (!string.IsNullOrEmpty(detail.PictureUrl))
                    await LoadCoverAsync(row, detail.PictureUrl);
            }
            catch (Exception ex)
            {
                _host.Logger.Debug(ex, "Downloads-Enrichment fehlgeschlagen fuer mod_id={Id}", modId);
            }
            await Task.Delay(250);
        }
    }

    private async Task LoadCoverAsync(DownloadRow row, string pictureUrl)
    {
        // v0.11.0: Bytes vom Cache holen, Decode via zentralem Host-Baukasten
        // (kein manueller new Bitmap(Stream) mehr).
        var bytes = await _covers.GetOrDownloadBytesAsync(pictureUrl);
        if (bytes is null) return;
        var bmp = await _host.Images.DecodeAsync(bytes);
        if (bmp is null)
        {
            _host.Logger.Debug("Downloads-Cover-Decode fehlgeschlagen fuer mod_id={Id}", row.NexusModId);
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(() => row.Cover = bmp);
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

    /// <summary>Oeffnet den Nexus-Mod-Detail-Dialog fuer die Row. Nur moeglich
    /// wenn der Filename dem Nexus-Muster entspricht
    /// (<see cref="DownloadRow.NexusModId"/> != null). Reuse des
    /// bestehenden <see cref="NexusModDetailViewModel"/>-Full-Ctors mit
    /// Initial-Werten aus der Row + async Full-Detail-Load im Hintergrund.</summary>
    [RelayCommand]
    private void ShowDetail(DownloadRow? row)
    {
        if (row is null) return;
        if (row.NexusModId is not int modId)
        {
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.no_nexus_id"), row.FileName),
                NotificationLevel.Info);
            return;
        }
        // Fake-NexusCatalogEntry aus den bereits gesammelten Row-Werten
        // damit NexusModDetailViewModel den Ctor(NexusRow, ...) nutzen kann.
        var fakeEntry = new NexusCatalogEntry(
            ModId: modId,
            Name: row.ModName ?? row.FileName,
            Author: row.Author ?? "",
            Summary: row.Summary ?? "",
            Category: "",
            Version: row.Version ?? "",
            PictureUrl: "",
            UpdatedUtc: DateTime.UtcNow,
            Downloads: 0,
            Endorsements: 0,
            Available: true);
        var fakeRow = new NexusRow(fakeEntry) { IsPremium = _nexus.IsPremium, Cover = row.Cover };
        var vm = new NexusModDetailViewModel(fakeRow, _nexus.IsPremium,
            _nexus, _downloader, _downloadBus, _mediaScraper, _covers,
            new Dictionary<int, string>(), _host);
        var window = new NexusModDetailWindow { DataContext = vm };
        var owner = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is not null) window.Show(owner); else window.Show();
    }
}

public sealed partial class DownloadRow : ObservableObject
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

    /// <summary>Aus dem Filename extrahiert. null wenn der Filename nicht
    /// dem Nexus-Muster entspricht — dann steht der Details-Button nicht
    /// zur Verfuegung.</summary>
    public int? NexusModId { get; init; }
    public bool HasNexusMatch => NexusModId is not null;

    /// <summary>Aus Filename-Parse (Fallback) oder aus Nexus-Detail-Fetch
    /// ueberschrieben. Detail-Fetch laeuft async im Hintergrund.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string? _modName;

    [ObservableProperty] private string? _author;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    private string? _summary;
    [ObservableProperty] private string? _version;

    /// <summary>Cover aus dem Nexus-CDN via CoverCache. null bis Enrichment
    /// laeuft — die View zeigt dann einen 📦-Emoji-Platzhalter.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private Bitmap? _cover;

    public bool HasCover => Cover is not null;
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public string DisplayName => !string.IsNullOrWhiteSpace(ModName) ? ModName! : FileName;
    public string VersionDisplay => string.IsNullOrEmpty(Version)
        ? ""
        : (char.IsDigit(Version[0]) ? "v" + Version : Version);
}
