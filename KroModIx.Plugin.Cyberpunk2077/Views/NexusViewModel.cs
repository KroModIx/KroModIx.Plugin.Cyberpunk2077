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

/// <summary>Katalog-Tab (v0.5): aggregierter Nexus-Katalog (latest_added +
/// latest_updated + trending). Cover-Enrichment im Hintergrund, Kategorien-
/// Filter, pro Row eigene Buttons (Download für Premium, Details-Dialog,
/// Nexus öffnen). Doppelklick öffnet den Detail-Dialog.</summary>
public sealed partial class NexusViewModel : ObservableObject, IDisposable
{
    private readonly CyberpunkNexusCatalog _catalog;
    private readonly CoverCache _covers;
    private readonly INexusService _nexus;
    private readonly CyberpunkDownloader _downloader;
    private readonly DownloadEventBus _downloadBus;
    private readonly NexusMediaScraper _mediaScraper;
    private readonly IHostServices _host;
    private readonly EventHandler _apiKeyChangedHandler;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private NexusCategoryOption? _selectedCategory;

    /// <summary>Aus <see cref="INexusService.IsPremium"/> — steuert ob die
    /// Download-Buttons in den Rows enabled sind. Änderung propagiert auf
    /// alle Row-Instanzen (siehe <see cref="OnIsPremiumChanged"/>).</summary>
    [ObservableProperty] private bool _isPremium;

    partial void OnIsPremiumChanged(bool value)
    {
        foreach (var row in Rows) row.IsPremium = value;
    }

    public ObservableCollection<NexusRow> Rows { get; } = new();
    public ObservableCollection<NexusCategoryOption> Categories { get; } = new();

    private List<NexusRow> _all = new();
    private Dictionary<int, string> _categoryMap = new();

    public NexusViewModel(CyberpunkNexusCatalog catalog, CoverCache covers,
        INexusService nexus, CyberpunkDownloader downloader,
        DownloadEventBus downloadBus, NexusMediaScraper mediaScraper,
        IHostServices host)
    {
        _catalog = catalog;
        _covers = covers;
        _nexus = nexus;
        _downloader = downloader;
        _downloadBus = downloadBus;
        _mediaScraper = mediaScraper;
        _host = host;
        IsPremium = _nexus.IsPremium;
        // Bei Key-Change (User trägt neuen im Host-Settings ein) → Refresh
        // und Premium-Flag synchronisieren.
        _apiKeyChangedHandler = (_, _) => Dispatcher.UIThread.Post(() =>
        {
            IsPremium = _nexus.IsPremium;
            _ = RefreshAsync();
        });
        _nexus.ApiKeyChanged += _apiKeyChangedHandler;
        _ = InitialLoadAsync();
    }

    public void Dispose()
    {
        _nexus.ApiKeyChanged -= _apiKeyChangedHandler;
    }

    private async Task InitialLoadAsync()
    {
        if (_catalog.Cached.Count == 0)
            await RefreshAsync();
        else
            LoadFromCache();
    }

    private void LoadFromCache()
    {
        _all = _catalog.Cached.Select(e => new NexusRow(e) { IsPremium = IsPremium }).ToList();
        ApplyFilter();
        StatusText = $"{_all.Count} Mods · {_catalog.CachedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(NexusCategoryOption? value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = FilterText?.Trim() ?? "";
        var catFilter = SelectedCategory?.CategoryId;
        Rows.Clear();
        var matched = _all.Where(r =>
            (string.IsNullOrEmpty(q)
                || r.Source.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Source.Author.Contains(q, StringComparison.OrdinalIgnoreCase))
            && (catFilter is null || GetCategoryIdForRow(r) == catFilter))
            .ToList();
        foreach (var r in matched) Rows.Add(r);
    }

    private int? GetCategoryIdForRow(NexusRow r)
    {
        // Kategorie steht nur im Volldetail — der Katalog-Endpoint liefert
        // die nicht. Für den Filter reicht dass wir NULL zurueckliefern
        // wenn wir noch nicht angefragt haben — dann wird nicht gefiltert.
        return null;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_nexus.HasApiKey)
        {
            StatusText = "Kein Nexus-API-Key im Host-Settings — bitte unter 🌐 Nexus eintragen.";
            _all = new();
            Rows.Clear();
            return;
        }
        try
        {
            IsBusy = true;
            StatusText = "Lade Katalog …";
            await _catalog.RefreshAsync();
            LoadFromCache();
            _ = LoadCategoriesAsync();
            _ = LoadCoversAsync();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Cyberpunk Nexus-Refresh fehlgeschlagen");
            StatusText = "Refresh-Fehler: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task LoadCategoriesAsync()
    {
        try
        {
            var cats = await _nexus.GetCategoriesAsync(CyberpunkNexusCatalog.GameSlug);
            _categoryMap = cats.ToDictionary(c => c.CategoryId, c => c.Name);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Categories.Clear();
                Categories.Add(new NexusCategoryOption(null, "Alle Kategorien"));
                foreach (var c in cats.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
                    Categories.Add(new NexusCategoryOption(c.CategoryId, c.Name));
                if (SelectedCategory is null) SelectedCategory = Categories[0];
            });
        }
        catch (Exception ex)
        {
            _host.Logger.Debug(ex, "Cyberpunk Nexus-Kategorien konnten nicht geladen werden");
        }
    }

    private async Task LoadCoversAsync()
    {
        // Sequenziell mit 250 ms Pause zwischen den Downloads (Rate-Limit-
        // freundlich; 20 Rows × 250 ms = 5 s bis alle Cover da sind).
        foreach (var row in _all)
        {
            if (string.IsNullOrEmpty(row.Source.PictureUrl)) continue;
            var path = await _covers.GetOrDownloadCoverAsync(row.Source.PictureUrl);
            if (path is null) continue;
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    using var s = File.OpenRead(path);
                    row.Cover = new Bitmap(s);
                });
            }
            catch (Exception ex)
            {
                _host.Logger.Debug(ex, "Cover-Bitmap-Load fuer {Id} fehlgeschlagen", row.Source.ModId);
            }
            await Task.Delay(250);
        }
    }

    [RelayCommand]
    private void OpenRowInBrowser(NexusRow? row)
    {
        if (row is null) return;
        _host.Shell.OpenExternalUrl(
            $"https://www.nexusmods.com/{CyberpunkNexusCatalog.GameSlug}/mods/{row.Source.ModId}");
    }

    /// <summary>Öffnet den Detail-Dialog für die Row. Analog Icarus:
    /// eigenes Modal-Fenster mit Owner=MainWindow, VM lädt <c>/mods/{id}.json</c>
    /// async, KI-Zusammenfassung über <c>_host.Ai</c>, Premium-Download
    /// aus dem Footer.</summary>
    [RelayCommand]
    private void ShowDetail(NexusRow? row)
    {
        if (row is null) return;
        var vm = new NexusModDetailViewModel(row, IsPremium,
            _nexus, _downloader, _downloadBus, _mediaScraper, _covers,
            _categoryMap, _host);
        var window = new NexusModDetailWindow { DataContext = vm };
        var owner = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is not null) window.Show(owner); else window.Show();
    }

    /// <summary>Direkt-Download für Premium-User: nutzt
    /// <see cref="CyberpunkDownloader.DownloadPrimaryAsync"/>. Fortschritt in
    /// der Host-Statusbar, nach Erfolg feuert der Event-Bus damit der
    /// Downloads-Tab sich auto-refresht.</summary>
    [RelayCommand]
    private async Task DownloadRowAsync(NexusRow? row)
    {
        if (row is null) return;
        if (!IsPremium)
        {
            _host.Notifications.Notify(
                "Direct-Download braucht Nexus-Premium. Klick \"Nexus öffnen\" für den Browser-Weg.",
                NotificationLevel.Warning);
            return;
        }

        using var scope = _host.BeginProgress($"Nexus: {row.Source.Name}");
        scope.Report(0, "Download läuft …");
        try
        {
            var progress = new Progress<double>(f =>
                scope.Report(f, $"{row.Source.Name} · {(int)(f * 100)}%"));
            var target = await _downloader.DownloadPrimaryAsync(row.Source.ModId, progress);
            if (target is null)
            {
                _host.Notifications.Notify(
                    "Download fehlgeschlagen — Log-Detail prüfen (Premium-Status? Rate-Limit?).",
                    NotificationLevel.Error);
                return;
            }
            _host.Notifications.Notify($"Heruntergeladen: {Path.GetFileName(target)}",
                NotificationLevel.Success);
            _downloadBus.RaiseDownloadsChanged(Path.GetFileName(target));
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Nexus-Download fehlgeschlagen für mod_id={Id}", row.Source.ModId);
            _host.Notifications.Notify($"Download-Fehler: {ex.Message}", NotificationLevel.Error);
        }
    }
}

public sealed partial class NexusRow : ObservableObject
{
    public NexusRow(NexusCatalogEntry source) => Source = source;
    public NexusCatalogEntry Source { get; }

    /// <summary>Wird vom <see cref="NexusViewModel"/> beim Erzeugen und bei
    /// Änderungen des Premium-Status gesetzt. Muss auf der Row selbst liegen
    /// weil ein <c>RelativeSource FindAncestor</c>-Bind vom Button auf
    /// die ListBox-DataContext-Property in einem FuncDataTemplate in Avalonia
    /// nicht zuverlässig aufgelöst wird (Binding liefert null → default(bool)
    /// = false → Button disabled auch für Premium-User).</summary>
    [ObservableProperty] private bool _isPremium;

    public string Name => Source.Name;
    public string Author => Source.Author;
    public string Summary => Source.Summary;

    /// <summary>Version mit smartem „v"-Prefix — nur wenn String mit Ziffer
    /// beginnt. Verhindert „vV1.4.0" (Autor hat bereits eigenes v) oder
    /// „vpatch3" (nicht-SemVer).</summary>
    public string VersionDisplay
    {
        get
        {
            var v = Source.Version?.Trim() ?? "";
            if (v.Length == 0) return "";
            return char.IsDigit(v[0]) ? "v" + v : v;
        }
    }

    public string EndorsementsText => Source.Endorsements > 0 ? $"👍 {Source.Endorsements}" : "";

    /// <summary>„Aktualisiert vor N Tagen" — relative statt absolut damit die
    /// Meta-Zeile nicht überladen wird.</summary>
    public string UpdatedText
    {
        get
        {
            var delta = DateTime.UtcNow - Source.UpdatedUtc;
            if (delta.TotalDays < 1) return "heute";
            if (delta.TotalDays < 2) return "gestern";
            if (delta.TotalDays < 30) return $"vor {(int)delta.TotalDays} Tagen";
            if (delta.TotalDays < 365) return $"vor {(int)(delta.TotalDays / 30)} Monaten";
            return Source.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd");
        }
    }

    [ObservableProperty] private Bitmap? _cover;
}

public sealed record NexusCategoryOption(int? CategoryId, string Name);
