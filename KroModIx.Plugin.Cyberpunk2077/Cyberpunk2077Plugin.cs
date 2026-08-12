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
public sealed class Cyberpunk2077Plugin : IGameModPlugin
{
    public PluginMetadata Metadata { get; } = new(
        Id: "kroste.cyberpunk2077",
        DisplayName: "Cyberpunk 2077 Mod-Manager",
        Version: "0.1.0",
        Author: "Kroste",
        Description: "Mod-Verwaltung für Cyberpunk 2077. Erkennt Archive-, REDmod-, " +
            "CET-, RED4ext- und redscript-Mods; Enable/Disable via Rename, Bulk-Aktionen.");

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

    public Task InitializeAsync(IHostServices host,
        IReadOnlyList<DetectedGame> activatedGames, CancellationToken ct)
    {
        _host = host;
        _paths = new CyberpunkPathResolver();
        _scanner = new CyberpunkModScanner(_paths);
        _installer = new CyberpunkModInstallService();

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
        if (_host is null || _paths is null || _scanner is null || _installer is null)
            yield break;
        yield return new InstalledTab(game, _scanner, _installer, _paths, _host);
    }

    public Task ShutdownAsync()
    {
        _host?.Logger.Info("Cyberpunk 2077 shutdown");
        return Task.CompletedTask;
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
        public string Label => "Installiert";
        public string Icon => "\U0001F4E6"; // 📦
        public int Order => 0;
        public bool IsVisible(DetectedGame game) => true;

        public Control CreateView(DetectedGame game, IHostServices host) =>
            new InstalledModsView
            {
                DataContext = new InstalledModsViewModel(_game, _scanner, _installer, _paths, _host),
            };
    }
}
