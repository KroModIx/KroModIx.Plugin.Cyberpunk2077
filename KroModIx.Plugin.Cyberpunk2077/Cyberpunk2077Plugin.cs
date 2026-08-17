using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.Cyberpunk2077.Services;
using KroModIx.Plugin.Cyberpunk2077.Views;

namespace KroModIx.Plugin.Cyberpunk2077;

/// <summary>KroModIx-Plugin für Cyberpunk 2077. v0.1: Installiert-Tab mit
/// Discovery aller fünf gängigen Mod-Typen (Archive, REDmod, CET, RED4ext,
/// redscript). Enable/Disable via <c>.disabled</c>-Suffix, Uninstall via
/// File-/Directory-Delete, Bulk-Aktionen.
///
/// <para>Nexus-Katalog + Update-Discovery kommen in v0.2+ (analog Icarus).</para></summary>
public sealed class Cyberpunk2077Plugin : IGameModPlugin, IUpdateNotifier, IConflictSource
{
    public PluginMetadata Metadata { get; } = new(
        Id: "kroste.cyberpunk2077",
        DisplayName: "Cyberpunk 2077 Mod-Manager",
        Version: "0.13.0",
        Author: "Kroste",
        Description: "Mod-Verwaltung für Cyberpunk 2077 — Installiert / Nexus-Katalog / Downloads. " +
            "v0.13.0: IConflictSource-Implementierung (Contracts v1.24.0) — meldet fuer " +
            "jeden installierten Mod (Archive/REDmod/CET/RED4ext/Redscript) die relativen " +
            "Dateien an den zentralen Host-Konflikt-Scanner. Damit erscheinen Mod-Overlaps " +
            "(z.B. zwei .archive-Files gleichen Namens, doppelte REDmod-Ordner) im neuen " +
            "„⚠ Konflikte pruefen…\"-Fenster in der Sidebar. Referenz-Migration fuer den " +
            "v1.24-Baukasten. MinHostVersion 1.24.0. " +
            "v0.12.2: Manifest-GC im UpdateChecker — verwaiste Install-Manifests " +
            "(Mod-Datei/Ordner manuell geloescht, JSON blieb liegen) werden vor dem " +
            "Update-Vergleich garbage-collectet. Kein Phantom-Update-Badge mehr fuer " +
            "nicht mehr installierte REDmods. " +
            "v0.12.1: Detail-Dialog rendert Rich-HTML via _host.Descriptions.CreateRichView " +
            "(Host v1.21 HtmlRenderer-Baukasten) — Bold/Italic/Farben/Bilder/Listen inline sichtbar. " +
            "Plain-Text bleibt fuer KI-Prompts. " +
            "v0.12.0: HTML/BBCode-Description-Parser aus _host.Descriptions " +
            "(zentraler Baukasten Contracts v1.20). " +
            "v0.11.0: Cover-Decode ueber Host-IImageDecoder-Baukasten (Contracts v1.18.0) — " +
            "zentrale WebP/AVIF/DDS-Convert-Chain, Cover-Bug-Fixes an einer Stelle. " +
            "v0.10: Update-Install pro Row (nur REDmods, ⬆-Button mit Zielversion), " +
            "⚙ REDmod-Deploy-Trigger in der Toolbar (redmod.exe deploy, Windows-nativ), " +
            "Client-side Kategorie-Filter im Nexus-Katalog, Cover-Loading-Progress im Header. " +
            "v0.8: DE+EN-Uebersetzung aller User-facing Strings (Tab-Labels, Buttons, " +
            "Statusmeldungen, Notifications, Dialoge) ueber Strings.T(). " +
            "v0.7: Voll-Katalog via Nexus-GraphQL (~23000 Cyberpunk-Mods verfuegbar), " +
            "Pagination mit 'Mehr laden'-Button, Server-side Volltextsuche, Sort-Dropdown " +
            "(Neueste Updates / Neu / Endorsements / Downloads). " +
            "v0.6: Screenshot-Galerie im Detail-Dialog + Fullscreen-Viewer. " +
            "v0.5: Nexus-Detail-Dialog mit voller Beschreibung + KI-Zusammenfassung, " +
            "Premium-Direct-Download, Adult-Warning-Badge. " +
            "v0.4: Update-Discovery fuer REDmods + gruener ↑-Badge (IUpdateNotifier). " +
            "Nutzt Host-Konflikt-Scanner-Baukasten (Contracts v1.24.0), " +
            "MinHostVersion 1.24.0.");

    public IReadOnlyList<GameTarget> Targets { get; } = new[]
    {
        new GameTarget(
            GameId: "cyberpunk-2077",
            DisplayName: "Cyberpunk 2077",
            SteamAppId: 1091500,
            AlternativeExecutableNames: new[] { "Cyberpunk2077.exe", "REDprelauncher.exe" },
            Platforms: Platforms.Both),
    };

    private IHostServices? _host;
    private CyberpunkPathResolver? _paths;
    private CyberpunkModScanner? _scanner;
    private CyberpunkModInstallService? _installer;
    private CyberpunkNexusCatalog? _catalog;
    private CoverCache? _covers;
    private CyberpunkPaths? _pluginPaths;
    private CyberpunkDownloader? _downloader;
    private CyberpunkZipInstaller? _zipInstaller;
    private CyberpunkUpdateChecker? _updateChecker;
    private DownloadEventBus? _downloadBus;
    private NexusMediaScraper? _mediaScraper;
    private InstallManifestStore? _manifests;
    private IReadOnlyList<DetectedGame> _activatedGames = Array.Empty<DetectedGame>();

    public Task InitializeAsync(IHostServices host,
        IReadOnlyList<DetectedGame> activatedGames, CancellationToken ct)
    {
        _host = host;
        // v0.8.0: Uebersetzungen. Muss VOR jedem GetTabContributions/CreateView
        // aufgerufen werden — die Views lesen Strings.T() im Constructor.
        Strings.Init(host.Localization);
        _paths = new CyberpunkPathResolver();
        // v0.9.0: InstallManifestStore VOR Scanner + Installer weil beide
        // ihn injiziert bekommen.
        _manifests = new InstallManifestStore(host);
        _scanner = new CyberpunkModScanner(_paths, _manifests);
        _installer = new CyberpunkModInstallService();
        _catalog = new CyberpunkNexusCatalog(host.Nexus);
        _covers = new CoverCache(host.CreateHttpClient("cyberpunk-covers"), host);
        _pluginPaths = new CyberpunkPaths(host);
        _downloader = new CyberpunkDownloader(host.Nexus,
            host.CreateHttpClient("cyberpunk-downloads"), _pluginPaths);
        _zipInstaller = new CyberpunkZipInstaller(_manifests);
        _updateChecker = new CyberpunkUpdateChecker(_scanner, _catalog, _manifests);
        // v0.12.2: dem UpdateChecker die Liste aktuell installierter Mods
        // liefern — er garbage-collectet damit verwaiste Manifests (User
        // hat REDmod-Ordner / Archive-Datei manuell geloescht, Manifest
        // blieb liegen → Phantom-Update-Badge). Cyberpunk hat 5 Mod-Typen
        // (Archive/REDmod/CET/RED4ext/redscript) — ScanAll liefert alle,
        // ManifestKey wird pro Mod korrekt gebildet (Type + Name), also
        // matcht der Schluessel eins-zu-eins mit dem was der Installer
        // beim Save schreibt.
        _updateChecker.InstalledKeysProvider = () =>
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in _activatedGames)
            {
                try
                {
                    foreach (var mod in _scanner.ScanAll(g))
                        keys.Add(mod.ManifestKey);
                }
                catch (Exception ex) { host.Logger.Debug(ex, "Scan fuer Manifest-GC fehlgeschlagen: {Dir}", g.InstallDir); }
            }
            return keys;
        };
        _downloadBus = new DownloadEventBus();
        _mediaScraper = new NexusMediaScraper(host.CreateHttpClient("cyberpunk-nexus-scrape"));
        _activatedGames = activatedGames;

        // v0.4: Auto-Update-Check nach 15s Bootstrap-Delay. Kein Katalog-
        // Live-Refresh — die 3 Nexus-Endpoints werden im Nexus-Tab manuell
        // getriggert. Der Check nutzt was der Katalog gerade hat.
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); }
            catch { return; }
            foreach (var g in activatedGames)
            {
                if (!_paths.LooksLikeCyberpunkInstall(g)) continue;
                try { await _updateChecker.CheckAsync(g, ct); }
                catch (Exception ex) { host.Logger.Debug(ex, "Auto-Update-Check fehlgeschlagen"); }
            }
            try { await host.RequestUpdateBadgeRefreshAsync(); } catch { }
        }, ct);

        foreach (var game in activatedGames)
        {
            if (_paths.LooksLikeCyberpunkInstall(game))
            {
                host.Logger.Info("Cyberpunk 2077 initialisiert: {Dir}", game.InstallDir);
            }
            else
            {
                host.Logger.Warn("Cyberpunk 2077: {Dir} sieht nicht wie ein CP-Install aus " +
                    "(fehlt bin/x64/ oder engine/). Discovery wird trotzdem versucht.",
                    game.InstallDir);
            }
        }
        return Task.CompletedTask;
    }

    public IEnumerable<IGameTabContribution> GetTabContributions(DetectedGame game)
    {
        if (_host is null || _paths is null || _scanner is null || _installer is null
            || _catalog is null || _covers is null || _pluginPaths is null
            || _downloader is null || _zipInstaller is null || _downloadBus is null
            || _mediaScraper is null || _manifests is null)
            yield break;
        yield return new InstalledTab(game, _scanner, _installer, _paths,
            _host.Nexus, _downloader, _downloadBus, _mediaScraper, _covers,
            _manifests, _updateChecker!, _zipInstaller, _host);
        yield return new NexusTab(_catalog, _covers, _downloader, _downloadBus,
            _mediaScraper, _host);
        yield return new DownloadsTab(game, _pluginPaths, _zipInstaller, _downloadBus,
            _downloader, _covers, _mediaScraper, _host);
    }

    public Task ShutdownAsync()
    {
        _host?.Logger.Info("Cyberpunk 2077 shutdown");
        return Task.CompletedTask;
    }

    // ---- IUpdateNotifier (Contracts v1.7.0+) ----

    public Task<IReadOnlyList<GameUpdateInfo>> GetPendingUpdatesAsync(CancellationToken ct)
    {
        if (_updateChecker is null || _activatedGames.Count == 0)
            return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(Array.Empty<GameUpdateInfo>());
        var count = _updateChecker.PendingCount;
        if (count <= 0)
            return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(Array.Empty<GameUpdateInfo>());
        var summary = count == 1
            ? $"1 Mod-Update verfügbar: {_updateChecker.Pending[0].InstalledName}"
            : $"{count} Mod-Updates verfügbar";
        var infos = _activatedGames
            .Where(g => g.Target.SteamAppId is int)
            .Select(g => new GameUpdateInfo(g.Target.SteamAppId!.Value, count, summary))
            .ToList();
        return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(infos);
    }

    // ---- IConflictSource (Contracts v1.24.0+) ----
    //
    // Liefert on-demand alle installierten Mods dieses Plugins mit ihren
    // relativen Dateien an den Host-IConflictScanner. Der Host aggregiert
    // ueber alle Plugins die IConflictSource implementieren und findet
    // Files mit > 1 Owner (case-insensitive, Slash-Normalisierung erledigt
    // der Host — wir liefern trotzdem schon '/'-normalisiert weil das die
    // Debug-Logs lesbarer macht).
    //
    // Design-Entscheidung: WIR LISTEN AUCH .disabled-MODS. Grund: der User
    // ist gerade beim Ein-/Ausschalten und will potenzielle Konflikte
    // vorab sehen. Die Konflikt-UI zeigt Owner-Namen mit [Type]-Suffix,
    // dort ist implizit sichtbar was gerade aktiv ist (Enable/Disable
    // steuert der User im Installiert-Tab).
    public async Task<IReadOnlyList<ModFileset>> GetOwnedFilesAsync(
        string gameKey, CancellationToken cancellationToken = default)
    {
        // Off-UI-Thread — Directory.EnumerateFiles(SearchOption.AllDirectories)
        // kann bei grossen REDmod-Ordnern rechenintensiv werden.
        await Task.Yield();

        if (_scanner is null || _activatedGames.Count == 0)
            return Array.Empty<ModFileset>();

        // Der Host baut den gameKey aus SteamAppId ("steam:1091500") bzw. bei
        // Manual-Games aus einer plugin-nicht-sichtbaren ManualId. Cyberpunk
        // hat eine feste SteamAppId und ist im offiziellen Setup Steam-only —
        // wir matchen ausschliesslich ueber "steam:<appId>". Manual-Adds mit
        // beliebigen Ids bleiben aussen vor (kein Owner in der Konflikt-UI —
        // waere ohnehin exotisch fuer CP2077).
        var game = _activatedGames.FirstOrDefault(g =>
            g.Target.SteamAppId is int appId
            && string.Equals(gameKey, $"steam:{appId}", StringComparison.OrdinalIgnoreCase));
        if (game is null)
            return Array.Empty<ModFileset>();

        var result = new List<ModFileset>();
        IReadOnlyList<CyberpunkMod> mods;
        try
        {
            mods = _scanner.ScanAll(game);
        }
        catch (Exception ex)
        {
            _host?.Logger.Warn(ex, "Cyberpunk IConflictSource: ScanAll fehlgeschlagen fuer {Dir}", game.InstallDir);
            return Array.Empty<ModFileset>();
        }

        foreach (var mod in mods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var files = CollectFilesFor(mod, game.InstallDir);
                if (files.Count == 0) continue;
                var display = $"{mod.Name} [{mod.TypeLabel}]";
                result.Add(new ModFileset(mod.ManifestKey, display, files));
            }
            catch (Exception ex)
            {
                _host?.Logger.Debug(ex, "Cyberpunk IConflictSource: File-Collect fuer {Mod} fehlgeschlagen", mod.Name);
            }
        }
        return result;
    }

    /// <summary>Sammelt die Files eines <see cref="CyberpunkMod"/> als
    /// relative, '/'-normalisierte Pfade unter <paramref name="installDir"/>.
    /// Sondermuenzen:
    /// <list type="bullet">
    /// <item>Archive: `.archive`-Datei + optionaler `.archive.xl`-Sidecar.</item>
    /// <item>REDmod/CET/RED4ext: Ordner rekursiv.</item>
    /// <item>Redscript: Datei ODER Ordner (beide Layouts moeglich).</item>
    /// </list>
    /// Fuer disabled-Mods (Path enthaelt `.disabled`) werden die Files
    /// trotzdem gelistet — der Konflikt ist echt sobald User re-enabled.</summary>
    internal static IReadOnlyList<string> CollectFilesFor(CyberpunkMod mod, string installDir)
    {
        var list = new List<string>();

        switch (mod.Type)
        {
            case CyberpunkModType.Archive:
                if (File.Exists(mod.Path))
                {
                    AddRelative(list, installDir, mod.Path);
                    // .archive.xl-Sidecar (ArchiveXL-Companion-File)
                    var xl = mod.Path + ".xl";
                    if (File.Exists(xl)) AddRelative(list, installDir, xl);
                    // Wenn das Archive selbst .disabled ist, den Sidecar-
                    // Pfad ohne .disabled auch checken (User re-enabled ggf.
                    // beides zusammen).
                    if (mod.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        var stripped = mod.Path[..^".disabled".Length];
                        var xlStripped = stripped + ".xl";
                        if (File.Exists(xlStripped)) AddRelative(list, installDir, xlStripped);
                    }
                }
                break;

            case CyberpunkModType.Redscript:
                // Zwei Layouts: einzelne .reds-Datei ODER Ordner mit .reds drin.
                if (File.Exists(mod.Path))
                    AddRelative(list, installDir, mod.Path);
                else if (Directory.Exists(mod.Path))
                    AddDirectoryRecursive(list, installDir, mod.Path);
                break;

            case CyberpunkModType.RedMod:
            case CyberpunkModType.CyberEngineTweaks:
            case CyberpunkModType.Red4Ext:
                if (Directory.Exists(mod.Path))
                    AddDirectoryRecursive(list, installDir, mod.Path);
                break;
        }
        return list;
    }

    private static void AddDirectoryRecursive(List<string> list, string installDir, string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            AddRelative(list, installDir, file);
    }

    private static void AddRelative(List<string> list, string installDir, string absolutePath)
    {
        var rel = Path.GetRelativePath(installDir, absolutePath).Replace('\\', '/');
        // Paranoid: Pfade die aus dem InstallDir raus zeigen (`..`) verwerfen.
        if (rel.StartsWith("..", StringComparison.Ordinal)) return;
        list.Add(rel);
    }

    private sealed class InstalledTab : IGameTabContribution
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

        public InstalledTab(DetectedGame game, CyberpunkModScanner scanner,
            CyberpunkModInstallService installer, CyberpunkPathResolver paths,
            INexusService nexus, CyberpunkDownloader downloader,
            DownloadEventBus downloadBus, NexusMediaScraper mediaScraper,
            CoverCache covers, InstallManifestStore manifests,
            CyberpunkUpdateChecker updateChecker, CyberpunkZipInstaller zipInstaller,
            IHostServices host)
        {
            _game = game; _scanner = scanner; _installer = installer;
            _paths = paths; _nexus = nexus; _downloader = downloader;
            _downloadBus = downloadBus; _mediaScraper = mediaScraper;
            _covers = covers; _manifests = manifests;
            _updateChecker = updateChecker; _zipInstaller = zipInstaller;
            _host = host;
        }

        public string Id => "installed";
        public string Label => Strings.T("tab.installed");
        public string Icon => "\U0001F4E6"; // 📦
        public int Order => 0;
        public bool IsVisible(DetectedGame game) => true;

        public Control CreateView(DetectedGame game, IHostServices host) =>
            new InstalledModsView
            {
                DataContext = new InstalledModsViewModel(_game, _scanner, _installer,
                    _paths, _nexus, _downloader, _downloadBus, _mediaScraper,
                    _covers, _manifests, _updateChecker, _zipInstaller, _host),
            };
    }

    private sealed class NexusTab : IGameTabContribution
    {
        private readonly CyberpunkNexusCatalog _catalog;
        private readonly CoverCache _covers;
        private readonly CyberpunkDownloader _downloader;
        private readonly DownloadEventBus _downloadBus;
        private readonly NexusMediaScraper _mediaScraper;
        private readonly IHostServices _host;

        public NexusTab(CyberpunkNexusCatalog catalog, CoverCache covers,
            CyberpunkDownloader downloader, DownloadEventBus downloadBus,
            NexusMediaScraper mediaScraper, IHostServices host)
        {
            _catalog = catalog; _covers = covers; _downloader = downloader;
            _downloadBus = downloadBus; _mediaScraper = mediaScraper; _host = host;
        }

        public string Id => "nexus";
        public string Label => Strings.T("tab.nexus");
        public string Icon => "\U0001F310"; // 🌐
        public int Order => 10;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new NexusView
            {
                DataContext = new NexusViewModel(_catalog, _covers, _host.Nexus,
                    _downloader, _downloadBus, _mediaScraper, _host),
            };
    }

    private sealed class DownloadsTab : IGameTabContribution
    {
        private readonly DetectedGame _game;
        private readonly CyberpunkPaths _paths;
        private readonly CyberpunkZipInstaller _installer;
        private readonly DownloadEventBus _downloadBus;
        private readonly CyberpunkDownloader _downloader;
        private readonly CoverCache _covers;
        private readonly NexusMediaScraper _mediaScraper;
        private readonly IHostServices _host;

        public DownloadsTab(DetectedGame game, CyberpunkPaths paths,
            CyberpunkZipInstaller installer, DownloadEventBus downloadBus,
            CyberpunkDownloader downloader, CoverCache covers,
            NexusMediaScraper mediaScraper, IHostServices host)
        {
            _game = game; _paths = paths; _installer = installer;
            _downloadBus = downloadBus; _downloader = downloader;
            _covers = covers; _mediaScraper = mediaScraper; _host = host;
        }

        public string Id => "downloads";
        public string Label => Strings.T("tab.downloads");
        public string Icon => "\U0001F4E5"; // 📥
        public int Order => 20;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new DownloadsView
            {
                DataContext = new DownloadsViewModel(_game, _paths, _installer,
                    _downloadBus, _host.Nexus, _downloader, _covers, _mediaScraper, _host),
            };
    }
}
