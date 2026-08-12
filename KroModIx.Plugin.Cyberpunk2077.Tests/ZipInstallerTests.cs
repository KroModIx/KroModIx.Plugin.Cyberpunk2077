using System.IO;
using System.IO.Compression;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.Cyberpunk2077.Services;
using Xunit;

namespace KroModIx.Plugin.Cyberpunk2077.Tests;

public sealed class ZipInstallerTests : IDisposable
{
    private readonly string _installRoot;
    private readonly string _tmp;
    private readonly DetectedGame _game;
    private readonly CyberpunkZipInstaller _installer = new();

    public ZipInstallerTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "kromodix-cp-zip-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tmp);
        _installRoot = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(_installRoot);
        Directory.CreateDirectory(Path.Combine(_installRoot, "bin", "x64"));
        Directory.CreateDirectory(Path.Combine(_installRoot, "engine"));

        _game = new DetectedGame(
            Target: new GameTarget("cyberpunk-2077", "Cyberpunk 2077", 1091500,
                Array.Empty<string>(), Platforms.Both),
            InstallDir: _installRoot,
            UserDataDir: null,
            ProtonPrefix: null,
            Runtime: RuntimeKind.Native,
            Source: GameSource.Steam);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    private string BuildZip(params (string Path, string Content)[] entries)
    {
        var zipPath = Path.Combine(_tmp, "test.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (path, content) in entries)
        {
            var entry = archive.CreateEntry(path);
            using var s = entry.Open();
            using var sw = new StreamWriter(s);
            sw.Write(content);
        }
        return zipPath;
    }

    [Fact]
    public void Direct_Layout_Archive_wird_ins_Game_Root_extrahiert()
    {
        var zip = BuildZip(
            ("archive/pc/mod/SuperMod.archive", "data"),
            ("archive/pc/mod/SuperMod.xl", "data"));
        var result = _installer.Install(zip, _game);
        result.Success.Should().BeTrue();
        result.InstalledPaths.Should().HaveCount(2);
        File.Exists(Path.Combine(_installRoot, "archive", "pc", "mod", "SuperMod.archive"))
            .Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "archive", "pc", "mod", "SuperMod.xl"))
            .Should().BeTrue();
    }

    [Fact]
    public void Direct_Layout_RedMod_wird_extrahiert()
    {
        var zip = BuildZip(
            ("mods/ImmersiveHacking/info.json", "{}"),
            ("mods/ImmersiveHacking/archives/x.archive", "data"));
        var result = _installer.Install(zip, _game);
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "mods", "ImmersiveHacking", "info.json"))
            .Should().BeTrue();
    }

    [Fact]
    public void Direct_Layout_CET_wird_extrahiert()
    {
        var zip = BuildZip(
            ("bin/x64/plugins/cyber_engine_tweaks/mods/AutoDrive/init.lua", "-- lua"));
        var result = _installer.Install(zip, _game);
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "bin", "x64", "plugins",
            "cyber_engine_tweaks", "mods", "AutoDrive", "init.lua")).Should().BeTrue();
    }

    [Fact]
    public void Flat_Layout_Archive_wird_nach_archive_pc_mod_gemappt()
    {
        var zip = BuildZip(
            ("SuperMod.archive", "data"));
        var result = _installer.Install(zip, _game);
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "archive", "pc", "mod", "SuperMod.archive"))
            .Should().BeTrue();
    }

    [Fact]
    public void Unbekanntes_Layout_liefert_Failure()
    {
        var zip = BuildZip(
            ("random/stuff.txt", "hi"),
            ("more/thing.bin", "x"));
        var result = _installer.Install(zip, _game);
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Unbekanntes");
    }

    [Fact]
    public void Zip_Slip_wird_verhindert()
    {
        var zip = BuildZip(
            ("archive/pc/mod/../../../evil.archive", "boom"));
        var result = _installer.Install(zip, _game);
        // Zip-Slip-Path wird beim ExtractDirect uebersprungen; die anderen
        // wuerden greifen. Hier gibts nur die eine Datei — Result soll
        // "Direkt-Layout erkannt" sein, aber die evil-Datei wurde NICHT
        // ausserhalb geschrieben.
        File.Exists(Path.Combine(_tmp, "evil.archive")).Should().BeFalse();
    }
}
