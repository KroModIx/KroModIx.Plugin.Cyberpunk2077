using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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

/// <summary>VM für den „Installiert"-Tab. Listet alle Cyberpunk-Mods gruppiert
/// nach Typ (Archive/REDmod/CET/RED4ext/redscript), pro Row Enable/Disable/
/// Uninstall + Bulk-Aktionen.
///
/// <para>Kein async in Refresh() — Scanner ist sync und schnell (&lt; 100 ms
/// für 200 Mods). Bei erstem Real-World-Perf-Problem: `Task.Run(() =&gt;
/// _scanner.ScanAll(...))` einfügen.</para></summary>
public sealed partial class InstalledModsViewModel : ObservableObject
{
    private readonly DetectedGame _game;
    private readonly CyberpunkModScanner _scanner;
    private readonly CyberpunkModInstallService _installer;
    private readonly CyberpunkPathResolver _paths;
    private readonly INexusService _nexus;
    private readonly CyberpunkDownloader _downloader;
    private readonly DownloadEventBus _downloadBus;
    private readonly NexusMediaScraper _mediaScraper;
    private readonly CoverCache _covers;
    private readonly InstallManifestStore _manifests;
    private readonly CyberpunkUpdateChecker _updateChecker;
    private readonly CyberpunkZipInstaller _zipInstaller;
    private readonly IHostServices _host;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _filterText = "";

    public ObservableCollection<ModRow> Rows { get; } = new();
    private List<ModRow> _allRows = new();

    public InstalledModsViewModel(DetectedGame game, CyberpunkModScanner scanner,
        CyberpunkModInstallService installer, CyberpunkPathResolver paths,
        INexusService nexus, CyberpunkDownloader downloader,
        DownloadEventBus downloadBus, NexusMediaScraper mediaScraper,
        CoverCache covers, InstallManifestStore manifests,
        CyberpunkUpdateChecker updateChecker,
        CyberpunkZipInstaller zipInstaller,
        IHostServices host)
    {
        _game = game;
        _scanner = scanner;
        _installer = installer;
        _paths = paths;
        _nexus = nexus;
        _downloader = downloader;
        _downloadBus = downloadBus;
        _mediaScraper = mediaScraper;
        _covers = covers;
        _manifests = manifests;
        _updateChecker = updateChecker;
        _zipInstaller = zipInstaller;
        _host = host;
        Refresh();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = FilterText?.Trim() ?? "";
        Rows.Clear();
        var matched = string.IsNullOrEmpty(q)
            ? _allRows
            : _allRows.Where(r =>
                r.Mod.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Mod.TypeLabel.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var r in matched) Rows.Add(r);
    }

    /// <summary>Sync-Wrapper — der eigentliche Scan laeuft off-UI-Thread.
    ///
    /// <para>v0.13.1: vorher lief hier alles synchron, und der Ctor ruft das
    /// direkt auf. <c>_scanner.ScanAll</c> ist aber kein billiger Aufruf:
    /// fuenf Verzeichnis-Scans (archive/, REDmod/, CET/, RED4ext/,
    /// redscript/) plus ein <c>File.ReadAllText</c> pro gefundenem Mod fuer
    /// das Install-Manifest. Bei einer gewachsenen Cyberpunk-Installation
    /// friert damit die ganze App-Sidebar ein, sobald der Installiert-Tab
    /// gebaut wird (Kernprinzip 3 / LS25-v1.8.1-Muster).</para>
    ///
    /// <para>Signatur bleibt sync, damit alle Callsites und das
    /// RelayCommand-Binding unveraendert bleiben. Sofort-Feedback via
    /// StatusText, Rows werden nach dem Scan auf dem UI-Thread
    /// materialisiert.</para></summary>
    [RelayCommand]
    private void Refresh()
    {
        IsBusy = true;
        StatusText = Strings.T("status.reading_mods");
        _ = Task.Run(() =>
        {
            List<ModRow> rows;
            string status;
            try
            {
                if (!_paths.LooksLikeCyberpunkInstall(_game))
                {
                    rows = new();
                    status = $"Kein Cyberpunk-Layout unter: {_game.InstallDir}";
                }
                else
                {
                    var mods = _scanner.ScanAll(_game);
                    rows = mods.Select(m => new ModRow(m)).ToList();
                    var byType = mods.GroupBy(m => m.Type).ToDictionary(g => g.Key, g => g.Count());
                    status = mods.Count == 0
                        ? "Keine Mods installiert."
                        : $"{mods.Count} Mod(s) — " + string.Join(", ",
                            byType.OrderBy(kv => kv.Key).Select(kv =>
                                $"{kv.Value}× {kv.Key}"));
                }
            }
            catch (Exception ex)
            {
                _host.Logger.Warn(ex, "Installiert-Scan fehlgeschlagen: {Dir}", _game.InstallDir);
                rows = new();
                status = $"Scan fehlgeschlagen: {ex.Message}";
            }

            Dispatcher.UIThread.Post(() =>
            {
                _allRows = rows;
                StatusText = status;
                ApplyFilter();
                IsBusy = false;
                // Async: Nexus-Enrichment (Cover + Meta) fuer alle Rows mit
                // NexusModId aus dem Install-Manifest.
                _ = EnrichRowsAsync(_allRows.ToArray());
                // v0.10.0: parallel REDmod-Update-Discovery. Katalog ist ggf.
                // schon geladen → billig; sonst holt _updateChecker.CheckAsync
                // via RefreshAsync die erste Katalog-Seite.
                _ = RefreshUpdatesAsync();
            });
        });
    }

    /// <summary>Fuer alle Rows mit erkannter <see cref="CyberpunkMod.NexusModId"/>
    /// (aus dem InstallManifest) Nexus-Detail-Fetch + Cover-Load. 250 ms
    /// Throttling zwischen Requests analog Downloads-Tab.</summary>
    private async Task EnrichRowsAsync(ModRow[] rows)
    {
        if (!_nexus.HasApiKey) return;
        foreach (var row in rows)
        {
            if (row.Mod.NexusModId is not int modId) continue;
            try
            {
                var detail = await _nexus.GetModDetailAsync(CyberpunkNexusCatalog.GameSlug, modId);
                if (detail is null) continue;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    row.NexusAuthor = detail.Author;
                    row.NexusSummary = detail.Summary;
                    row.NexusVersion = detail.Version;
                    row.OnEnrichmentChanged();
                });
                if (!string.IsNullOrEmpty(detail.PictureUrl))
                    await LoadCoverAsync(row, detail.PictureUrl);
            }
            catch (Exception ex)
            {
                _host.Logger.Debug(ex, "Installiert-Enrichment fehlgeschlagen fuer mod_id={Id}", modId);
            }
            await Task.Delay(250);
        }
    }

    private async Task LoadCoverAsync(ModRow row, string pictureUrl)
    {
        // v0.11.0: Bytes vom Cache holen, Decode via zentralem Host-Baukasten
        // (WebP/AVIF/DDS-Fallback + Thread-Affinity werden vom Host erledigt).
        var bytes = await _covers.GetOrDownloadBytesAsync(pictureUrl);
        if (bytes is null) return;
        var bmp = await _host.Images.DecodeAsync(bytes);
        if (bmp is null)
        {
            _host.Logger.Debug("Installiert-Cover-Decode fehlgeschlagen fuer mod_id={Id}", row.Mod.NexusModId);
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(() => row.Cover = bmp);
    }

    /// <summary>Oeffnet den Nexus-Mod-Detail-Dialog fuer die Row. Nur moeglich
    /// wenn ein Install-Manifest existiert (row.Mod.NexusModId != null).</summary>
    [RelayCommand]
    private void ShowDetail(ModRow? row)
    {
        if (row is null) return;
        if (row.Mod.NexusModId is not int modId)
        {
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.no_nexus_id"), row.Mod.Name),
                NotificationLevel.Info);
            return;
        }
        var fakeEntry = new NexusCatalogEntry(
            ModId: modId,
            Name: row.Mod.Name,
            Author: row.NexusAuthor ?? row.Mod.Author ?? "",
            Summary: row.NexusSummary ?? row.Mod.Description ?? "",
            Category: "",
            Version: row.NexusVersion ?? row.Mod.Version ?? "",
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

    [RelayCommand]
    private async Task ToggleEnabledAsync(ModRow? row)
    {
        if (row is null) return;
        try
        {
            IsBusy = true;
            var newPath = _installer.SetEnabled(row.Mod, !row.Mod.IsEnabled);
            // Row-Instanz austauschen mit neuen Werten
            row.Mod = row.Mod with { IsEnabled = !row.Mod.IsEnabled, Path = newPath };
            row.OnModChanged();
            _host.Notifications.Notify(
                $"{row.Mod.Name}: {(row.Mod.IsEnabled ? "aktiviert" : "deaktiviert")}",
                NotificationLevel.Success);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Toggle fehlgeschlagen: {Name}", row.Mod.Name);
            await _host.Dialogs.ShowMessageAsync("Fehler", ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UninstallAsync(ModRow? row)
    {
        if (row is null) return;
        var ok = await _host.Dialogs.ConfirmAsync("Deinstallieren?",
            $"{row.Mod.Name} ({row.Mod.TypeLabel}) wirklich löschen?\n\n" +
            $"Pfad: {row.Mod.Path}",
            okLabel: "Löschen");
        if (!ok) return;
        try
        {
            IsBusy = true;
            _installer.Uninstall(row.Mod);
            // v0.9.0: Manifest auch loeschen — sonst bleibt ein orphan
            // NexusModId-Eintrag, der beim naechsten Install desselben Mods
            // versehentlich einen fremden Nexus-Mod als „passend" markieren
            // koennte.
            _manifests.Delete(row.Mod.ManifestKey);
            _host.Notifications.Notify(Strings.T("notify.uninstalled_prefix") + row.Mod.Name,
                NotificationLevel.Success);
            Refresh();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Uninstall fehlgeschlagen: {Name}", row.Mod.Name);
            await _host.Dialogs.ShowMessageAsync("Fehler", ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        // Öffnet den archive/pc/mod-Ordner. Der User-häufigste Fall — für
        // die anderen 4 Ordner reicht der File-Manager selbst.
        _host.Shell.OpenDirectory(_paths.GetArchiveDir(_game));
    }

    /// <summary>Bulk-Disable aller sichtbaren enabled Mods. Für Debug/
    /// Vanilla-Test — schneller als Row-für-Row.</summary>
    [RelayCommand]
    private async Task DisableAllAsync()
    {
        var targets = Rows.Where(r => r.Mod.IsEnabled).ToList();
        if (targets.Count == 0)
        {
            _host.Notifications.Notify(Strings.T("notify.no_enabled_mods"), NotificationLevel.Info);
            return;
        }
        var ok = await _host.Dialogs.ConfirmAsync("Alle deaktivieren?",
            $"{targets.Count} Mod(s) werden deaktiviert (Rename mit .disabled-Suffix). " +
            $"Kein Datei-Verlust — jederzeit reversibel.",
            okLabel: "Deaktivieren");
        if (!ok) return;
        int done = 0, failed = 0;
        using var scope = _host.BeginProgress($"Deaktiviere {targets.Count} Mod(s) …");
        foreach (var row in targets)
        {
            scope.Report((double)(done + failed) / targets.Count,
                $"{done + failed + 1}/{targets.Count}: {row.Mod.Name}");
            try { _installer.SetEnabled(row.Mod, false); done++; }
            catch (Exception ex)
            {
                _host.Logger.Warn(ex, "Bulk-Disable fehlgeschlagen: {Name}", row.Mod.Name);
                failed++;
            }
        }
        _host.Notifications.Notify(string.Format(Strings.T("notify.bulk_disable_result"), done, failed),
            failed == 0 ? NotificationLevel.Success : NotificationLevel.Warning);
        Refresh();
    }

    /// <summary>v0.10.0: Retrofit — fuer eine Row ohne Nexus-Match ein
    /// Install-Manifest anlegen. User klebt Nexus-URL oder tippt Mod-ID ein,
    /// der Manifest-Store speichert dann permanent den Link. Ab dem naechsten
    /// Refresh greifen Cover-Enrichment und (bei REDmods) Update-Check.</summary>
    [RelayCommand]
    private async Task AssignNexusMatchAsync(ModRow? row)
    {
        if (row is null) return;
        var owner = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var dialog = new NexusMatchDialog(row.Mod.Name);
        if (owner is not null) await dialog.ShowDialog(owner);
        else dialog.Show();
        if (dialog.Result is not int modId) return;

        // Manifest speichern — InstalledPaths = der eine bekannte Mod-Pfad
        // (relativ zum InstallDir). Reicht fuer Enrichment; Uninstall via
        // CyberpunkModInstallService.Uninstall greift eh auf Mod.Path direkt.
        var rel = Path.GetRelativePath(_game.InstallDir, row.Mod.Path).Replace('\\', '/');
        _manifests.Save(row.Mod.ManifestKey, new InstallManifest(
            NexusModId: modId,
            OriginalFilename: null,
            InstalledAtUtc: DateTime.UtcNow,
            InstalledPaths: new[] { rel }));
        _host.Notifications.Notify(
            string.Format(Strings.T("notify.nexus_match_saved"), modId),
            NotificationLevel.Success);
        Refresh();
    }

    /// <summary>v0.10.0: nach jedem Refresh + wann immer der Katalog frisch
    /// ist gerufen — matcht installierte REDmods gegen den Nexus-Katalog und
    /// setzt <see cref="ModRow.PendingUpdateModId"/>/<see cref="ModRow.PendingUpdateVersion"/>
    /// auf den Rows fuer die ein neues File verfuegbar ist.</summary>
    public async Task RefreshUpdatesAsync(CancellationToken ct = default)
    {
        try
        {
            await _updateChecker.CheckAsync(_game, ct);
            var lookup = _updateChecker.Pending
                .ToDictionary(u => NormalizeName(u.InstalledName), u => u,
                    StringComparer.OrdinalIgnoreCase);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var row in _allRows)
                {
                    if (row.Mod.Type != CyberpunkModType.RedMod) continue;
                    var key = NormalizeName(row.Mod.Name);
                    if (!lookup.TryGetValue(key, out var cand)) continue;
                    row.PendingUpdateModId = cand.NexusModId;
                    row.PendingUpdateVersion = cand.NexusVersion;
                    row.OnUpdateChanged();
                }
            });
        }
        catch (Exception ex) { _host.Logger.Debug(ex, "RefreshUpdatesAsync fehlgeschlagen"); }
    }

    private static string NormalizeName(string s) => s.Replace(" ", "").ToLowerInvariant();

    /// <summary>v0.10.0: fuer eine Row mit PendingUpdate den primary File
    /// downloaden und via ZipInstaller ins Game extrahieren. Danach Refresh
    /// damit die neue Version im UI sichtbar wird.</summary>
    [RelayCommand]
    private async Task InstallUpdateAsync(ModRow? row)
    {
        if (row is null || row.PendingUpdateModId is not int modId) return;
        if (!_nexus.IsPremium)
        {
            _host.Notifications.Notify(Strings.T("notify.premium_required"),
                NotificationLevel.Warning);
            return;
        }
        using var scope = _host.BeginProgress($"Update: {row.Mod.Name}");
        scope.Report(0, Strings.T("notify.update_install_running"));
        try
        {
            IsBusy = true;
            var progress = new Progress<double>(f =>
                scope.Report(f * 0.6, $"⬇ {(int)(f * 100)}%"));
            var target = await _downloader.DownloadPrimaryAsync(modId, progress);
            if (target is null)
            {
                _host.Notifications.Notify(Strings.T("notify.download_fail_check_log"),
                    NotificationLevel.Error);
                return;
            }
            scope.Report(0.7, "📥 Install …");
            var result = _zipInstaller.Install(target, _game);
            if (!result.Success)
            {
                _host.Notifications.Notify(
                    Strings.T("notify.update_install_fail") + result.Message,
                    NotificationLevel.Error);
                return;
            }
            scope.Report(1.0, "OK");
            _host.Notifications.Notify(
                Strings.T("notify.update_install_ok") + $"{row.Mod.Name} → v{row.PendingUpdateVersion}",
                NotificationLevel.Success);
            _host.Logger.Info("Cyberpunk-Update installiert: {Name} ({N} Dateien)",
                row.Mod.Name, result.InstalledPaths.Count);
            Refresh();
            _ = RefreshUpdatesAsync();
            try { await _host.RequestUpdateBadgeRefreshAsync(); } catch { }
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Update-Install fehlgeschlagen: {Name}", row.Mod.Name);
            _host.Notifications.Notify(
                Strings.T("notify.update_install_fail") + ex.Message,
                NotificationLevel.Error);
        }
        finally { IsBusy = false; }
    }

    /// <summary>v0.10.0: <c>redmod.exe deploy</c> nach REDmod-Install manuell
    /// triggern. redmod.exe liegt im Game-Install unter tools/redmod/bin/.
    /// Auf Linux: Hinweis dass der User das im Game-Menu (Settings → Mods →
    /// Deploy) machen sollte — Proton-Invocation von redmod.exe hier waere
    /// bruechig (Prefix-Path, Wine-Version-Kompatibilitaet, Prompt).</summary>
    [RelayCommand]
    private async Task RunRedmodDeployAsync()
    {
        var exe = Path.Combine(_game.InstallDir, "tools", "redmod", "bin", "redmod.exe");
        if (!File.Exists(exe))
        {
            _host.Notifications.Notify(Strings.T("notify.redmod_exe_missing"),
                NotificationLevel.Warning);
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            _host.Notifications.Notify(
                "Auf Linux: bitte im Game-Menu Settings → Mods → Deploy klicken " +
                "(Proton-Invocation von redmod.exe waere hier zu bruechig).",
                NotificationLevel.Info);
            return;
        }
        var ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("dialog.redmod_deploy_title"),
            Strings.T("dialog.redmod_deploy_msg"),
            okLabel: Strings.T("dialog.redmod_deploy_ok"));
        if (!ok) return;

        using var scope = _host.BeginProgress("REDmod deploy");
        scope.Report(0, Strings.T("notify.redmod_deploy_running"));
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = _game.InstallDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("deploy");
            psi.ArgumentList.Add("-reportProgress");
            psi.ArgumentList.Add($"-root={_game.InstallDir}");
            using var proc = Process.Start(psi);
            if (proc is null) throw new InvalidOperationException("Process.Start returned null");
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"redmod.exe deploy exited with code {proc.ExitCode}");
            _host.Notifications.Notify(Strings.T("notify.redmod_deploy_ok"),
                NotificationLevel.Success);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "REDmod deploy fehlgeschlagen");
            _host.Notifications.Notify(
                Strings.T("notify.redmod_deploy_fail") + ex.Message,
                NotificationLevel.Error);
        }
    }

    /// <summary>Bulk-Enable aller deaktivierten Mods.</summary>
    [RelayCommand]
    private async Task EnableAllAsync()
    {
        var targets = Rows.Where(r => !r.Mod.IsEnabled).ToList();
        if (targets.Count == 0)
        {
            _host.Notifications.Notify(Strings.T("notify.no_disabled_mods"), NotificationLevel.Info);
            return;
        }
        int done = 0, failed = 0;
        using var scope = _host.BeginProgress($"Aktiviere {targets.Count} Mod(s) …");
        foreach (var row in targets)
        {
            scope.Report((double)(done + failed) / targets.Count,
                $"{done + failed + 1}/{targets.Count}: {row.Mod.Name}");
            try { _installer.SetEnabled(row.Mod, true); done++; }
            catch (Exception ex)
            {
                _host.Logger.Warn(ex, "Bulk-Enable fehlgeschlagen: {Name}", row.Mod.Name);
                failed++;
            }
        }
        _host.Notifications.Notify(string.Format(Strings.T("notify.bulk_enable_result"), done, failed),
            failed == 0 ? NotificationLevel.Success : NotificationLevel.Warning);
        Refresh();
    }
}

/// <summary>UI-Wrapper um <see cref="CyberpunkMod"/> — mit ObservableProperty
/// für Reaktivität nach Toggle-Änderung ohne Full-Refresh.</summary>
public sealed partial class ModRow : ObservableObject
{
    public ModRow(CyberpunkMod mod) => Mod = mod;

    // Mod ist mutable damit ToggleEnabled ohne Full-Refresh reagiert.
    [ObservableProperty] private CyberpunkMod _mod;

    public string StatusLabel => Mod.IsEnabled ? Strings.T("row.status_active") : Strings.T("row.status_inactive");
    public string SizeText => Mod.SizeBytes switch
    {
        null => "",
        < 1024 => $"{Mod.SizeBytes} B",
        < 1024 * 1024 => $"{Mod.SizeBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{Mod.SizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{Mod.SizeBytes / (1024.0 * 1024 * 1024):F2} GB",
    };
    public string SubtitleText
    {
        get
        {
            var parts = new List<string> { Mod.TypeLabel };
            var v = NexusVersion ?? Mod.Version;
            if (!string.IsNullOrEmpty(v)) parts.Add(char.IsDigit(v[0]) ? "v" + v : v);
            var a = NexusAuthor ?? Mod.Author;
            if (!string.IsNullOrEmpty(a)) parts.Add(a);
            if (!string.IsNullOrEmpty(SizeText)) parts.Add(SizeText);
            return string.Join(" · ", parts);
        }
    }
    public string ToggleButtonLabel => Mod.IsEnabled ? Strings.T("btn.disable") : Strings.T("btn.enable");

    /// <summary>Nexus-Enrichment-Werte (async vom VM gefuellt wenn eine
    /// NexusModId im Install-Manifest steht). Ueberschreiben SubtitleText
    /// wenn vorhanden (Nexus-Author + Nexus-Version sind aktueller/besser
    /// als Manifest/info.json-Daten).</summary>
    public string? NexusAuthor { get; set; }
    public string? NexusSummary { get; set; }
    public string? NexusVersion { get; set; }

    /// <summary>Cover vom Nexus-CDN (async geladen). null → View zeigt
    /// den Typ-Icon-Fallback.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private Bitmap? _cover;

    public bool HasCover => Cover is not null;
    public bool HasNexusMatch => Mod.NexusModId is not null;
    public bool HasSummary => !string.IsNullOrWhiteSpace(NexusSummary);
    /// <summary>v0.10.0: Retrofit-Button ist sichtbar wenn kein Manifest da
    /// ist. Nutzt Nicht-HasNexusMatch statt einer eigenen Property um in
    /// XAML/Bindings ohne Converter auszukommen.</summary>
    public bool NeedsNexusRetrofit => Mod.NexusModId is null;

    /// <summary>v0.10.0: gesetzt vom UpdateChecker wenn eine neuere Nexus-
    /// Version verfuegbar ist. Rendert den ⬆-Button auf der Row.</summary>
    public int? PendingUpdateModId { get; set; }
    public string? PendingUpdateVersion { get; set; }
    public bool HasPendingUpdate => PendingUpdateModId is not null;
    public string UpdateButtonLabel => PendingUpdateVersion is null
        ? Strings.T("btn.install_update")
        : $"⬆  v{PendingUpdateVersion}";

    public void OnUpdateChanged()
    {
        OnPropertyChanged(nameof(HasPendingUpdate));
        OnPropertyChanged(nameof(UpdateButtonLabel));
    }

    /// <summary>Callback aus dem VM nach externem Mod-Change (Toggle) —
    /// triggert PropertyChanged für alle Compute-Properties.</summary>
    public void OnModChanged()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(ToggleButtonLabel));
    }

    /// <summary>Callback nach Nexus-Enrichment — triggert SubtitleText +
    /// HasSummary neu.</summary>
    public void OnEnrichmentChanged()
    {
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(NexusSummary));
        OnPropertyChanged(nameof(HasSummary));
    }
}
