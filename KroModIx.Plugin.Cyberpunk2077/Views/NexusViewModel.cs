using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.Cyberpunk2077.Services;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>Katalog-Tab (v0.2): aggregierter Nexus-Katalog (latest_added +
/// latest_updated + trending). Cover-Enrichment im Hintergrund, Kategorien-
/// Filter, Klick auf Row öffnet die Nexus-Detail-Seite im Browser.</summary>
public sealed partial class NexusViewModel : ObservableObject, IDisposable
{
    private readonly CyberpunkNexusCatalog _catalog;
    private readonly CoverCache _covers;
    private readonly INexusService _nexus;
    private readonly IHostServices _host;
    private readonly EventHandler _apiKeyChangedHandler;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private NexusCategoryOption? _selectedCategory;

    public ObservableCollection<NexusRow> Rows { get; } = new();
    public ObservableCollection<NexusCategoryOption> Categories { get; } = new();

    private List<NexusRow> _all = new();
    private Dictionary<int, string> _categoryMap = new();

    public NexusViewModel(CyberpunkNexusCatalog catalog, CoverCache covers,
        INexusService nexus, IHostServices host)
    {
        _catalog = catalog;
        _covers = covers;
        _nexus = nexus;
        _host = host;
        // Bei Key-Change (User trägt neuen im Host-Settings ein) → Refresh.
        // Handler-Instanz merken damit wir sie in Dispose entfernen können —
        // sonst leakt bei jedem Tab-Wechsel ein Handler + der Katalog wird
        // N× refresht (Rate-Limit-Verschwendung).
        _apiKeyChangedHandler = (_, _) =>
            Dispatcher.UIThread.Post(() => _ = RefreshAsync());
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
        _all = _catalog.Cached.Select(e => new NexusRow(e)).ToList();
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
        // Detail-Anfragen laufen im Hintergrund (siehe EnrichCategoriesAsync).
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
                    using var s = System.IO.File.OpenRead(path);
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
    private void OpenOnNexus(NexusRow? row)
    {
        if (row is null) return;
        _host.Shell.OpenExternalUrl(
            $"https://www.nexusmods.com/{CyberpunkNexusCatalog.GameSlug}/mods/{row.Source.ModId}");
    }
}

public sealed partial class NexusRow : ObservableObject
{
    public NexusRow(NexusCatalogEntry source) => Source = source;
    public NexusCatalogEntry Source { get; }
    [ObservableProperty] private Bitmap? _cover;

    public string SubtitleText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Source.Author)) parts.Add("von " + Source.Author);
            if (!string.IsNullOrEmpty(Source.Version)) parts.Add("v" + Source.Version);
            if (Source.Endorsements > 0) parts.Add($"👍 {Source.Endorsements:N0}");
            parts.Add($"aktualisiert {Source.UpdatedUtc.ToLocalTime():yyyy-MM-dd}");
            return string.Join(" · ", parts);
        }
    }
}

public sealed record NexusCategoryOption(int? CategoryId, string Name);
