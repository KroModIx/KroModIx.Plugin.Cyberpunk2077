using System;
using System.Collections.Generic;
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
public sealed class Cyberpunk2077Plugin : IGameModPlugin, IUpdateNotifier
{
    public PluginMetadata Metadata { get; } = new(
        Id: "kroste.cyberpunk2077",
        DisplayName: "Cyberpunk 2077 Mod-Manager",
        Version: "0.8.2",
        Author: "Kroste",
        Description: "Mod-Verwaltung für Cyberpunk 2077 — Installiert / Nexus-Katalog / Downloads. " +
            "v0.8: DE+EN-Uebersetzung aller User-facing Strings (Tab-Labels, Buttons, " +
            "Statusmeldungen, Notifications, Dialoge) ueber Strings.T(). " +
            "v0.7: Voll-Katalog via Nexus-GraphQL (~23000 Cyberpunk-Mods verfuegbar), " +
            "Pagination mit 'Mehr laden'-Button, Server-side Volltextsuche, Sort-Dropdown " +
            "(Neueste Updates / Neu / Endorsements / Downloads). " +
            "v0.6: Screenshot-Galerie im Detail-Dialog + Fullscreen-Viewer. " +
            "v0.5: Nexus-Detail-Dialog mit voller Beschreibung + KI-Zusammenfassung, " +
            "Premium-Direct-Download, Adult-Warning-Badge. " +
            "v0.4: Update-Discovery fuer REDmods + gruener ↑-Badge (IUpdateNotifier). " +
            "Nutzt Host-Nexus-Baukasten (Contracts v1.15.0), MinHostVersion 1.15.0.");

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
    private IReadOnlyList<DetectedGame> _activatedGames = Array.Empty<DetectedGame>();

    public Task InitializeAsync(IHostServices host,
        IReadOnlyList<DetectedGame> activatedGames, CancellationToken ct)
    {
        _host = host;
        // v0.8.0: Uebersetzungen. Muss VOR jedem GetTabContributions/CreateView
        // aufgerufen werden — die Views lesen Strings.T() im Constructor.
        Strings.Init(host.Localization);
        _paths = new CyberpunkPathResolver();
        _scanner = new CyberpunkModScanner(_paths);
        _installer = new CyberpunkModInstallService();
        _catalog = new CyberpunkNexusCatalog(host.Nexus);
        _covers = new CoverCache(host.CreateHttpClient("cyberpunk-covers"), host);
        _pluginPaths = new CyberpunkPaths(host);
        _downloader = new CyberpunkDownloader(host.Nexus,
            host.CreateHttpClient("cyberpunk-downloads"), _pluginPaths);
        _zipInstaller = new CyberpunkZipInstaller();
        _updateChecker = new CyberpunkUpdateChecker(_scanner, _catalog);
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
            || _mediaScraper is null)
            yield break;
        yield return new InstalledTab(game, _scanner, _installer, _paths, _host);
        yield return new NexusTab(_catalog, _covers, _downloader, _downloadBus,
            _mediaScraper, _host);
        yield return new DownloadsTab(game, _pluginPaths, _zipInstaller, _downloadBus, _host);
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

    private sealed class InstalledTab : IGameTabContribution
    {
        private readonly DetectedGame _game;
        private readonly CyberpunkModScanner _scanner;
        private readonly CyberpunkModInstallService _installer;
        private readonly CyberpunkPathResolver _paths;
        private readonly IHostServices _host;

        public InstalledTab(DetectedGame game, CyberpunkModScanner scanner,
            CyberpunkModInstallService installer, CyberpunkPathResolver paths,
            IHostServices host)
        {
            _game = game; _scanner = scanner; _installer = installer;
            _paths = paths; _host = host;
        }

        public string Id => "installed";
        public string Label => Strings.T("tab.installed");
        public string Icon => "\U0001F4E6"; // 📦
        public int Order => 0;
        public bool IsVisible(DetectedGame game) => true;

        public Control CreateView(DetectedGame game, IHostServices host) =>
            new InstalledModsView
            {
                DataContext = new InstalledModsViewModel(_game, _scanner, _installer, _paths, _host),
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
        private readonly IHostServices _host;

        public DownloadsTab(DetectedGame game, CyberpunkPaths paths,
            CyberpunkZipInstaller installer, DownloadEventBus downloadBus,
            IHostServices host)
        {
            _game = game; _paths = paths; _installer = installer;
            _downloadBus = downloadBus; _host = host;
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
                    _downloadBus, _host),
            };
    }
}
