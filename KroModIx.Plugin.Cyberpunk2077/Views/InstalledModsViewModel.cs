using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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
    private readonly IHostServices _host;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _filterText = "";

    public ObservableCollection<ModRow> Rows { get; } = new();
    private List<ModRow> _allRows = new();

    public InstalledModsViewModel(DetectedGame game, CyberpunkModScanner scanner,
        CyberpunkModInstallService installer, CyberpunkPathResolver paths,
        IHostServices host)
    {
        _game = game;
        _scanner = scanner;
        _installer = installer;
        _paths = paths;
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

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            IsBusy = true;
            if (!_paths.LooksLikeCyberpunkInstall(_game))
            {
                StatusText = $"Kein Cyberpunk-Layout unter: {_game.InstallDir}";
                _allRows = new();
                Rows.Clear();
                return;
            }
            var mods = _scanner.ScanAll(_game);
            _allRows = mods.Select(m => new ModRow(m)).ToList();
            var byType = mods.GroupBy(m => m.Type).ToDictionary(g => g.Key, g => g.Count());
            StatusText = mods.Count == 0
                ? "Keine Mods installiert."
                : $"{mods.Count} Mod(s) — " + string.Join(", ",
                    byType.OrderBy(kv => kv.Key).Select(kv =>
                        $"{kv.Value}× {kv.Key}"));
            ApplyFilter();
        }
        finally { IsBusy = false; }
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
            _host.Notifications.Notify($"Deinstalliert: {row.Mod.Name}",
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
            _host.Notifications.Notify("Keine aktiven Mods.", NotificationLevel.Info);
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
        _host.Notifications.Notify($"{done} deaktiviert, {failed} Fehler.",
            failed == 0 ? NotificationLevel.Success : NotificationLevel.Warning);
        Refresh();
    }

    /// <summary>Bulk-Enable aller deaktivierten Mods.</summary>
    [RelayCommand]
    private async Task EnableAllAsync()
    {
        var targets = Rows.Where(r => !r.Mod.IsEnabled).ToList();
        if (targets.Count == 0)
        {
            _host.Notifications.Notify("Keine deaktivierten Mods.", NotificationLevel.Info);
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
        _host.Notifications.Notify($"{done} aktiviert, {failed} Fehler.",
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

    public string StatusLabel => Mod.IsEnabled ? "aktiv" : "deaktiviert";
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
            if (!string.IsNullOrEmpty(Mod.Version)) parts.Add($"v{Mod.Version}");
            if (!string.IsNullOrEmpty(Mod.Author)) parts.Add(Mod.Author);
            if (!string.IsNullOrEmpty(SizeText)) parts.Add(SizeText);
            return string.Join(" · ", parts);
        }
    }
    public string ToggleButtonLabel => Mod.IsEnabled ? "⏸  Deaktivieren" : "▶  Aktivieren";

    /// <summary>Callback aus dem VM nach externem Mod-Change (Toggle) —
    /// triggert PropertyChanged für alle Compute-Properties.</summary>
    public void OnModChanged()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(ToggleButtonLabel));
    }
}
