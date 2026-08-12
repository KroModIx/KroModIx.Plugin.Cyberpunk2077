using System.IO;
using System.Text.Json;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.Cyberpunk2077.Services;
using Xunit;

namespace KroModIx.Plugin.Cyberpunk2077.Tests;

/// <summary>Legt einen kompletten Cyberpunk-Fake-Install im TempPath an
/// (leere Marker-Ordner + Sample-Files je Mod-Typ) und prüft die
/// Scanner-Ausgabe End-to-End.</summary>
public sealed class ScannerTests : IDisposable
{
    private readonly string _root;
    private readonly DetectedGame _game;
    private readonly CyberpunkPathResolver _paths = new();
    private readonly CyberpunkModScanner _scanner;

    public ScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kromodix-cp-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        // Marker-Ordner damit LooksLikeCyberpunkInstall() true liefert.
        Directory.CreateDirectory(Path.Combine(_root, "bin", "x64"));
        Directory.CreateDirectory(Path.Combine(_root, "engine"));

        _game = new DetectedGame(
            Target: new GameTarget("cyberpunk-2077", "Cyberpunk 2077", 1091500,
                Array.Empty<string>(), Platforms.Both),
            InstallDir: _root,
            UserDataDir: null,
            ProtonPrefix: null,
            Runtime: RuntimeKind.Native,
            Source: GameSource.Steam);
        _scanner = new CyberpunkModScanner(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void LooksLikeCyberpunkInstall_findet_Standard_Layout()
    {
        _paths.LooksLikeCyberpunkInstall(_game).Should().BeTrue();
    }

    [Fact]
    public void Archive_Scanner_erkennt_enabled_und_disabled()
    {
        var mod = Path.Combine(_root, "archive", "pc", "mod");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "SuperMod.archive"), "fake");
        File.WriteAllText(Path.Combine(mod, "AnotherMod.archive.disabled"), "fake");
        File.WriteAllText(Path.Combine(mod, "readme.txt"), "ignoriere mich");

        var result = _scanner.ScanArchives(_game).OrderBy(m => m.Name).ToList();
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("AnotherMod");
        result[0].IsEnabled.Should().BeFalse();
        result[0].Type.Should().Be(CyberpunkModType.Archive);
        result[1].Name.Should().Be("SuperMod");
        result[1].IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void RedMod_Scanner_liest_info_json()
    {
        var modsDir = Path.Combine(_root, "mods");
        var modA = Path.Combine(modsDir, "ImmersiveHacking");
        Directory.CreateDirectory(modA);
        var info = new
        {
            name = "Immersive Hacking",
            description = "Cooler hacking overhaul",
            version = "2.4.1",
            author = "Cyber Cat",
        };
        File.WriteAllText(Path.Combine(modA, "info.json"),
            JsonSerializer.Serialize(info));

        var modB = Path.Combine(modsDir, "BrokenMod.disabled");
        Directory.CreateDirectory(modB);
        // Kein info.json → Fallback auf Ordner-Namen (ohne .disabled-Suffix).

        var result = _scanner.ScanRedMods(_game).OrderBy(m => m.Name).ToList();
        result.Should().HaveCount(2);
        var immersive = result.First(m => m.Name == "Immersive Hacking");
        immersive.IsEnabled.Should().BeTrue();
        immersive.Version.Should().Be("2.4.1");
        immersive.Author.Should().Be("Cyber Cat");
        var broken = result.First(m => m.Name == "BrokenMod");
        broken.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void CET_und_Red4Ext_Scanner_erkennen_Ordner_Toggle()
    {
        var cetDir = Path.Combine(_root, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods");
        Directory.CreateDirectory(Path.Combine(cetDir, "AutoDrive"));
        Directory.CreateDirectory(Path.Combine(cetDir, "OldMod.disabled"));
        var red4Dir = Path.Combine(_root, "red4ext", "plugins");
        Directory.CreateDirectory(Path.Combine(red4Dir, "AudioEngine"));

        var cet = _scanner.ScanCetMods(_game).OrderBy(m => m.Name).ToList();
        cet.Should().HaveCount(2);
        cet[0].Name.Should().Be("AutoDrive");
        cet[0].IsEnabled.Should().BeTrue();
        cet[1].Name.Should().Be("OldMod");
        cet[1].IsEnabled.Should().BeFalse();

        var red4 = _scanner.ScanRed4ExtMods(_game).ToList();
        red4.Should().HaveCount(1);
        red4[0].Name.Should().Be("AudioEngine");
        red4[0].Type.Should().Be(CyberpunkModType.Red4Ext);
    }

    [Fact]
    public void Redscript_Scanner_findet_beide_Layouts()
    {
        var scriptsDir = Path.Combine(_root, "r6", "scripts");
        // Layout 1: Unterordner
        Directory.CreateDirectory(Path.Combine(scriptsDir, "MyOverhaul"));
        // Layout 2: Einzelfile
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllText(Path.Combine(scriptsDir, "quick_tweak.reds"), "// reds");
        File.WriteAllText(Path.Combine(scriptsDir, "old_tweak.reds.disabled"), "// reds");

        var result = _scanner.ScanRedscriptMods(_game).OrderBy(m => m.Name).ToList();
        result.Should().HaveCount(3);
        result.Select(m => m.Name).Should()
            .BeEquivalentTo(new[] { "MyOverhaul", "old_tweak", "quick_tweak" });
        result.First(m => m.Name == "old_tweak").IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ScanAll_liefert_gruppiert_nach_Typ()
    {
        // Ein Mod je Typ anlegen, dann prüfen ob ScanAll alle findet und
        // stabil sortiert (Type, dann alphabetisch).
        Directory.CreateDirectory(Path.Combine(_root, "archive", "pc", "mod"));
        File.WriteAllText(Path.Combine(_root, "archive", "pc", "mod", "aaa.archive"), "");
        Directory.CreateDirectory(Path.Combine(_root, "mods", "zzz"));
        Directory.CreateDirectory(Path.Combine(_root, "bin", "x64", "plugins",
            "cyber_engine_tweaks", "mods", "middle"));
        Directory.CreateDirectory(Path.Combine(_root, "red4ext", "plugins", "aaa"));
        Directory.CreateDirectory(Path.Combine(_root, "r6", "scripts", "zzz"));

        var all = _scanner.ScanAll(_game).ToList();
        all.Should().HaveCount(5);
        all[0].Type.Should().Be(CyberpunkModType.Archive);
        all[1].Type.Should().Be(CyberpunkModType.RedMod);
        all[2].Type.Should().Be(CyberpunkModType.CyberEngineTweaks);
        all[3].Type.Should().Be(CyberpunkModType.Red4Ext);
        all[4].Type.Should().Be(CyberpunkModType.Redscript);
    }
}
