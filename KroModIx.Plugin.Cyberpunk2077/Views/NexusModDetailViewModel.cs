using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.Cyberpunk2077.Services;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>VM für den Nexus-Mod-Detail-Dialog. Lädt beim Öffnen das volle
/// Mod-Detail (<c>/mods/{id}.json</c>) im Hintergrund, dekodiert die HTML/
/// BBCode-Beschreibung, mappt Kategorie-ID auf Namen (aus der beim Katalog-
/// Refresh vorgeladenen Map), bietet Browser-Öffnen und KI-Zusammenfassung
/// über den Host-KI-Provider. Direkt aus <c>ShowDetail</c> auf dem
/// <see cref="NexusViewModel"/> instanziiert.</summary>
public sealed partial class NexusModDetailViewModel : ObservableObject
{
    private readonly int _modId;
    private readonly string _gameSlug = CyberpunkNexusCatalog.GameSlug;
    private readonly string _detailUrl;
    private readonly INexusService _nexus;
    private readonly CyberpunkDownloader _downloader;
    private readonly DownloadEventBus _downloadBus;
    private readonly NexusMediaScraper _mediaScraper;
    private readonly CoverCache _covers;
    private readonly IReadOnlyDictionary<int, string> _categoryMap;
    private readonly IHostServices _host;
    private IReadOnlyList<NexusScreenshot> _rawScreenshots = Array.Empty<NexusScreenshot>();

    public NexusModDetailViewModel(NexusRow row, bool isPremium,
        INexusService nexus, CyberpunkDownloader downloader,
        DownloadEventBus downloadBus, NexusMediaScraper mediaScraper,
        CoverCache covers,
        IReadOnlyDictionary<int, string> categoryMap,
        IHostServices host)
    {
        _modId = row.Source.ModId;
        _detailUrl = $"https://www.nexusmods.com/{_gameSlug}/mods/{_modId}";
        _nexus = nexus;
        _downloader = downloader;
        _downloadBus = downloadBus;
        _mediaScraper = mediaScraper;
        _covers = covers;
        _categoryMap = categoryMap;
        _host = host;
        IsPremium = isPremium;

        Title = row.Name;
        Author = row.Author;
        Summary = row.Source.Summary;
        Version = row.VersionDisplay;
        EndorsementsText = row.EndorsementsText;
        UpdatedText = row.UpdatedText;
        Cover = row.Cover;
        Description = "Detail-Beschreibung wird geladen …";
        StatusText = "Detail wird geladen …";

        _ = LoadDetailAsync();
        _ = LoadScreenshotsAsync();
    }

    public ObservableCollection<ScreenshotThumb> Screenshots { get; } = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScreenshots))]
    private bool _screenshotsBusy = true;
    public bool HasScreenshots => Screenshots.Count > 0;

    /// <summary>Scraped die Nexus-Media-Tab-Seite und laedt die Thumbnails
    /// sequenziell mit 150 ms Delay (Rate-Limit-freundlich). Klick auf ein
    /// Thumbnail oeffnet <see cref="ScreenshotViewerWindow"/> mit Full-Res.</summary>
    private async Task LoadScreenshotsAsync()
    {
        try
        {
            _rawScreenshots = await _mediaScraper.ScrapeAsync(_gameSlug, _modId);
            if (_rawScreenshots.Count == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ScreenshotsBusy = false;
                    OnPropertyChanged(nameof(HasScreenshots));
                });
                return;
            }
            for (int i = 0; i < _rawScreenshots.Count; i++)
            {
                var item = _rawScreenshots[i];
                var idx = i;
                var localPath = await _covers.GetOrDownloadCoverAsync(item.ThumbUrl);
                if (localPath is null) continue;
                Bitmap? bmp = null;
                try
                {
                    bmp = await Task.Run(() =>
                    {
                        using var s = File.OpenRead(localPath);
                        return new Bitmap(s);
                    });
                }
                catch (Exception ex)
                {
                    _host.Logger.Debug(ex, "Screenshot-Thumb-Load fehlgeschlagen: {Url}", item.ThumbUrl);
                    continue;
                }
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Screenshots.Add(new ScreenshotThumb(idx, bmp));
                    OnPropertyChanged(nameof(HasScreenshots));
                });
                await Task.Delay(150);
            }
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Screenshot-Load fehlgeschlagen fuer mod_id={Id}", _modId);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => ScreenshotsBusy = false);
        }
    }

    /// <summary>Oeffnet den Fullscreen-Viewer mit dem geklickten Screenshot als
    /// Startbild. Prev/Next-Navigation im Viewer arbeitet auf der gesamten
    /// Screenshot-Liste.</summary>
    [RelayCommand]
    private void OpenScreenshot(ScreenshotThumb? thumb)
    {
        if (thumb is null || _rawScreenshots.Count == 0) return;
        var window = new ScreenshotViewerWindow(_rawScreenshots, thumb.Index, _host);
        var owner = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is not null) window.Show(owner); else window.Show();
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private string _endorsementsText = "";
    [ObservableProperty] private string _updatedText = "";
    [ObservableProperty] private string _category = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _containsAdultContent;
    [ObservableProperty] private Bitmap? _cover;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    private string _aiSummary = "";
    public bool HasSummary => !string.IsNullOrWhiteSpace(AiSummary);

    [ObservableProperty] private bool _summaryBusy;
    [ObservableProperty] private bool _isPremium;
    [ObservableProperty] private bool _downloadBusy;

    private async Task LoadDetailAsync()
    {
        try
        {
            var detail = await _nexus.GetModDetailAsync(_gameSlug, _modId);
            if (detail is null)
            {
                Description = "Detail konnte nicht geladen werden (API-Fehler oder Rate-Limit).";
                StatusText = "Fehler beim Laden.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(detail.Name)) Title = detail.Name;
            if (!string.IsNullOrWhiteSpace(detail.Author)) Author = detail.Author;
            if (!string.IsNullOrWhiteSpace(detail.Summary)) Summary = detail.Summary;
            if (!string.IsNullOrWhiteSpace(detail.Version))
            {
                var v = detail.Version.TrimStart();
                Version = v.Length > 0 && char.IsDigit(v[0]) ? "v" + detail.Version : detail.Version;
            }
            EndorsementsText = detail.EndorsementCount > 0 ? $"👍 {detail.EndorsementCount}" : "";
            UpdatedText = detail.UpdatedUtc.ToLocalTime().ToString("g");
            ContainsAdultContent = detail.ContainsAdultContent;

            Description = HtmlStrip.ToPlainText(detail.DescriptionHtml);
            if (string.IsNullOrWhiteSpace(Description))
                Description = string.IsNullOrWhiteSpace(detail.Summary)
                    ? "Keine Beschreibung im Detail-Endpoint."
                    : detail.Summary;

            Category = _categoryMap.TryGetValue(detail.CategoryId, out var name)
                ? name
                : (detail.CategoryId > 0 ? $"Kategorie #{detail.CategoryId}" : "");
            StatusText = $"v{detail.Version} · {(detail.Available ? "verfügbar" : "nicht verfügbar")}";
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Nexus-Detail-Load fehlgeschlagen für mod_id={Id}", _modId);
            Description = $"Fehler: {ex.Message}";
            StatusText = "Fehler beim Laden.";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void OpenInBrowser() => _host.Shell.OpenExternalUrl(_detailUrl);

    /// <summary>Premium-Direct-Download aus dem Detail-Dialog — nutzt
    /// dieselbe MAIN-File-Auswahl-Heuristik wie der Row-Download im
    /// Katalog-Tab.</summary>
    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (!IsPremium)
        {
            _host.Notifications.Notify(
                "Direct-Download braucht Nexus-Premium. Klick \"Auf Nexus öffnen\" für den Browser-Weg.",
                NotificationLevel.Warning);
            return;
        }
        DownloadBusy = true;
        using var scope = _host.BeginProgress($"Nexus: {Title}");
        scope.Report(0, "Download läuft …");
        try
        {
            var progress = new Progress<double>(f => scope.Report(f, $"{Title} · {(int)(f * 100)}%"));
            var target = await _downloader.DownloadPrimaryAsync(_modId, progress);
            if (target is null)
            {
                _host.Notifications.Notify(
                    "Nexus verweigert Download-URL — Premium-Status prüfen (Verify im Host-Settings-Tab).",
                    NotificationLevel.Error);
                return;
            }
            _host.Notifications.Notify($"Heruntergeladen: {Path.GetFileName(target)}",
                NotificationLevel.Success);
            _downloadBus.RaiseDownloadsChanged(Path.GetFileName(target));
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Nexus-Detail-Download fehlgeschlagen für mod_id={Id}", _modId);
            _host.Notifications.Notify($"Download-Fehler: {ex.Message}", NotificationLevel.Error);
        }
        finally { DownloadBusy = false; }
    }

    [RelayCommand]
    private async Task SummarizeAsync()
    {
        if (IsLoading || string.IsNullOrWhiteSpace(Description))
        {
            _host.Notifications.Notify("Bitte warten bis Detail geladen ist.", NotificationLevel.Info);
            return;
        }
        if (!await _host.Ai.IsAvailableAsync())
        {
            _host.Notifications.Notify(
                "KI-Provider nicht erreichbar — bitte in den KroModIx-Einstellungen konfigurieren.",
                NotificationLevel.Warning);
            return;
        }
        SummaryBusy = true;
        AiSummary = $"KI-Zusammenfassung via {_host.Ai.ProviderInfo} …";
        try
        {
            var systemPrompt = "Du bist ein deutschsprachiger Cyberpunk-2077-Mod-Reviewer. " +
                "Fasse die Mod-Beschreibung in 3–5 Sätzen zusammen: " +
                "Was macht der Mod? Ist es ein Archive/REDmod/CET/RED4ext/redscript-Mod? " +
                "Welche Änderungen (Gameplay, Grafik, QoL, Bugfix, Cheat)? " +
                "Sachlich, kein Werbe-Sprech.";
            var userPrompt = $"Titel: {Title}\nAutor: {Author}\n\nBeschreibung:\n{Description}";
            var answer = await _host.Ai.CompleteAsync(systemPrompt, userPrompt);
            AiSummary = string.IsNullOrWhiteSpace(answer)
                ? "KI hat keine Antwort geliefert."
                : answer;
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Nexus-Summarize fehlgeschlagen für {Id}", _modId);
            AiSummary = $"Fehler: {ex.Message}";
        }
        finally { SummaryBusy = false; }
    }
}

/// <summary>Ein Screenshot-Thumbnail im Detail-Dialog. <see cref="Index"/>
/// ist die Position in der Screenshot-Liste — der Fullscreen-Viewer nutzt
/// den Index als Start-Position fuer die Prev/Next-Navigation.</summary>
public sealed record ScreenshotThumb(int Index, Bitmap Thumbnail);
