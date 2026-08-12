using System.IO;
using FluentAssertions;
using KroModIx.Plugin.Cyberpunk2077.Services;
using Xunit;

namespace KroModIx.Plugin.Cyberpunk2077.Tests;

public sealed class InstallServiceTests : IDisposable
{
    private readonly string _root;
    private readonly CyberpunkModInstallService _installer = new();

    public InstallServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kromodix-cp-install-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void SetEnabled_Archive_File_Toggle()
    {
        var enabledPath = Path.Combine(_root, "SuperMod.archive");
        File.WriteAllText(enabledPath, "data");
        var mod = new CyberpunkMod(CyberpunkModType.Archive, "SuperMod", enabledPath, true);

        var disabled = _installer.SetEnabled(mod, false);
        disabled.Should().EndWith(".archive.disabled");
        File.Exists(enabledPath).Should().BeFalse();
        File.Exists(disabled).Should().BeTrue();

        // Re-Enable: das mod-Objekt hat noch IsEnabled=true — wir müssen ein
        // frisches Mod-Objekt mit disabled-State bauen (analog zu dem was
        // der Scanner beim nächsten Refresh liefern würde).
        var reMod = new CyberpunkMod(CyberpunkModType.Archive, "SuperMod", disabled, false);
        var reEnabled = _installer.SetEnabled(reMod, true);
        reEnabled.Should().Be(enabledPath);
        File.Exists(enabledPath).Should().BeTrue();
        File.Exists(disabled).Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_RedMod_Ordner_Toggle()
    {
        var enabledDir = Path.Combine(_root, "ImmersiveHacking");
        Directory.CreateDirectory(enabledDir);
        File.WriteAllText(Path.Combine(enabledDir, "info.json"), "{}");
        var mod = new CyberpunkMod(CyberpunkModType.RedMod, "ImmersiveHacking", enabledDir, true);

        var disabledDir = _installer.SetEnabled(mod, false);
        disabledDir.Should().EndWith(".disabled");
        Directory.Exists(enabledDir).Should().BeFalse();
        Directory.Exists(disabledDir).Should().BeTrue();
        File.Exists(Path.Combine(disabledDir, "info.json")).Should().BeTrue();
    }

    [Fact]
    public void SetEnabled_no_op_wenn_Zustand_stimmt()
    {
        var path = Path.Combine(_root, "AlreadyEnabled.archive");
        File.WriteAllText(path, "data");
        var mod = new CyberpunkMod(CyberpunkModType.Archive, "AlreadyEnabled", path, true);

        var result = _installer.SetEnabled(mod, true);
        result.Should().Be(path);
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void Uninstall_loescht_File_und_Directory()
    {
        var file = Path.Combine(_root, "gone.archive");
        File.WriteAllText(file, "bye");
        _installer.Uninstall(new CyberpunkMod(CyberpunkModType.Archive, "gone", file, true));
        File.Exists(file).Should().BeFalse();

        var dir = Path.Combine(_root, "gone-dir");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "content.reds"), "x");
        _installer.Uninstall(new CyberpunkMod(CyberpunkModType.Redscript, "gone-dir", dir, true));
        Directory.Exists(dir).Should().BeFalse();
    }
}
