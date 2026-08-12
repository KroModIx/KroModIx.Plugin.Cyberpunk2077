using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.Cyberpunk2077.Services;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>Katalog-Tab (v0.7): Voll-Katalog via
/// <see cref="INexusService.SearchModsAsync"/> (Contracts v1.15.0+
/// GraphQL). Pagination mit „Mehr laden"-Button, Server-side Volltext-
/// suche und Sort-Dropdown (Neueste Updates / Neueste Adds / Endorsements
/// / Downloads). Cover-Enrichment im Hintergrund pro Seite. Detail-Dialog
/// mit Screenshot-Galerie + KI-Zusammenfassung + Premium-Direct-Download.</summary>
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
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;

    /// <summary>User-Eingabe im Suchfeld. Wird per <see cref="SearchCommand"/>
    /// (Enter/Button) an den GraphQL-Server geschickt — kein Auto-Search
    /// bei jedem Tastendruck (das wuerde N API-Calls pro Wort machen).</summary>
    [ObservableProperty] private string _searchQuery = "";

    /// <summary>Ausgewaehlte Sortier-Option. Umschalten triggert einen
    /// Reset (erste Seite mit neuem Sort laden).</summary>
    [ObservableProperty] private NexusSortOption _selectedSort = SortOptions[0];

    [ObservableProperty] private bool _isPremium;

    partial void OnIsPremiumChanged(bool value)
    {
        foreach (var row in Rows) row.IsPremium = value;
    }

    partial void OnSelectedSortChanged(NexusSortOption value)
    {
        _ = LoadFirstPageAsync();
    }

    public ObservableCollection<NexusRow> Rows { get; } = new();

    public static IReadOnlyList<NexusSortOption> SortOptions { get; } = new[]
    {
        new NexusSortOption("Neueste Updates", NexusSort.LatestUpdate),
        new NexusSortOption("Neu hinzugefuegt", NexusSort.LatestAdd),
        new NexusSortOption("Meistgeliked", NexusSort.MostEndorsed),
        new NexusSortOption("Meistgeladen", NexusSort.MostDownloaded),
    };

    public bool HasMore => _catalog.HasMore;

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
        _apiKeyChangedHandler = (_, _) => Dispatcher.UIThread.Post(() =>
        {
            IsPremium = _nexus.IsPremium;
            _ = LoadFirstPageAsync();
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
            await LoadFirstPageAsync();
        else
            RebuildRowsFromCatalog();
    }

    private void RebuildRowsFromCatalog()
    {
        Rows.Clear();
        foreach (var e in _catalog.Cached)
            Rows.Add(new NexusRow(e) { IsPremium = IsPremium });
        UpdateStatus();
        OnPropertyChanged(nameof(HasMore));
    }

    private void UpdateStatus()
    {
        var loaded = _catalog.Cached.Count;
        var total = _catalog.TotalCount;
        var qHint = string.IsNullOrWhiteSpace(_catalog.CurrentQuery)
            ? ""
            : $" — Suche '{_catalog.CurrentQuery}'";
        StatusText = total > 0
            ? $"{loaded} von {total} Mods geladen{qHint}"
            : $"{loaded} Mods{qHint}";
    }

    [RelayCommand]
    private async Task LoadFirstPageAsync()
    {
        if (!_nexus.HasApiKey)
        {
            StatusText = "Kein Nexus-API-Key im Host-Settings — bitte unter 🌐 Nexus eintragen.";
            Rows.Clear();
            return;
        }
        if (!await _loadGate.WaitAsync(0)) return;
        try
        {
            IsBusy = true;
            StatusText = "Lade Katalog …";
            await _catalog.LoadFirstPageAsync(SelectedSort.Value, SearchQuery);
            RebuildRowsFromCatalog();
            _ = LoadCoversAsync(0);
            if (_categoryMap.Count == 0) _ = LoadCategoriesAsync();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Cyberpunk Nexus-Load-First fehlgeschlagen");
            StatusText = "Fehler: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            _loadGate.Release();
        }
    }

    /// <summary>„Mehr laden" — appended die naechste Seite an die vorhandenen
    /// Rows, ohne die bereits geladenen Cover zu verlieren.</summary>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!_catalog.HasMore) return;
        if (!await _loadGate.WaitAsync(0)) return;
        try
        {
            IsBusy = true;
            var beforeCount = _catalog.Cached.Count;
            await _catalog.LoadNextPageAsync();
            var added = _catalog.Cached.Count - beforeCount;
            for (int i = beforeCount; i < _catalog.Cached.Count; i++)
                Rows.Add(new NexusRow(_catalog.Cached[i]) { IsPremium = IsPremium });
            UpdateStatus();
            OnPropertyChanged(nameof(HasMore));
            _ = LoadCoversAsync(beforeCount);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Cyberpunk Nexus-Load-More fehlgeschlagen");
            StatusText = "Load-More-Fehler: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            _loadGate.Release();
        }
    }

    /// <summary>Ausgeloest vom Search-Button oder Enter im Suchfeld. Setzt
    /// die Query im Katalog und laedt die erste Seite frisch. Leerer
    /// String = Suche zuruecksetzen, alle Mods.</summary>
    [RelayCommand]
    private Task SearchAsync() => LoadFirstPageAsync();

    private async Task LoadCategoriesAsync()
    {
        try
        {
            var cats = await _nexus.GetCategoriesAsync(CyberpunkNexusCatalog.GameSlug);
            _categoryMap = cats.ToDictionary(c => c.CategoryId, c => c.Name);
        }
        catch (Exception ex)
        {
            _host.Logger.Debug(ex, "Cyberpunk Nexus-Kategorien konnten nicht geladen werden");
        }
    }

    /// <summary>Cover fuer eine bestimmte Row-Range (ab startIndex bis Ende
    /// von Rows) laden. Wird pro geladener Seite aufgerufen — Bereits
    /// geladene Cover werden nicht angefasst.</summary>
    private async Task LoadCoversAsync(int startIndex)
    {
        // Rows-Snapshot damit spaeteres LoadMore die Iteration nicht stoert.
        var snapshot = new List<NexusRow>();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            for (int i = startIndex; i < Rows.Count; i++) snapshot.Add(Rows[i]);
        });
        foreach (var row in snapshot)
        {
            if (string.IsNullOrEmpty(row.Source.PictureUrl)) continue;
            if (row.Cover is not null) continue;
            var path = await _covers.GetOrDownloadCoverAsync(row.Source.PictureUrl);
            if (path is null) continue;
            try
            {
                var bmp = await Task.Run(() =>
                {
                    using var s = File.OpenRead(path);
                    return new Bitmap(s);
                });
                await Dispatcher.UIThread.InvokeAsync(() => row.Cover = bmp);
            }
            catch (Exception ex)
            {
                _host.Logger.Debug(ex, "Cover-Bitmap-Load fuer {Id} fehlgeschlagen", row.Source.ModId);
            }
            await Task.Delay(150);
        }
    }

    [RelayCommand]
    private void OpenRowInBrowser(NexusRow? row)
    {
        if (row is null) return;
        _host.Shell.OpenExternalUrl(
            $"https://www.nexusmods.com/{CyberpunkNexusCatalog.GameSlug}/mods/{row.Source.ModId}");
    }

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

    /// <summary>Muss auf der Row selbst liegen weil RelativeSource
    /// FindAncestor-Bind vom Button in einem FuncDataTemplate in Avalonia 12
    /// nicht zuverlaessig aufloest (liefert null → default(bool) = false →
    /// Button disabled auch fuer Premium-User).</summary>
    [ObservableProperty] private bool _isPremium;

    public string Name => Source.Name;
    public string Author => Source.Author;
    public string Summary => Source.Summary;

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

/// <summary>Combobox-Option fuer die Sort-Auswahl im Katalog-Header.</summary>
public sealed record NexusSortOption(string Label, NexusSort Value);
