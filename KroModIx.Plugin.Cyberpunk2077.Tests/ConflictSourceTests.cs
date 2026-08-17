using System.IO;
using FluentAssertions;
using KroModIx.Plugin.Cyberpunk2077.Services;
using Xunit;

namespace KroModIx.Plugin.Cyberpunk2077.Tests;

/// <summary>Tests fuer die <c>CollectFilesFor</c>-Helper der
/// <c>IConflictSource</c>-Implementierung (v0.13.0). Deckt die drei
/// Sonderfaelle ab, die ueber „Ordner rekursiv"-Default hinausgehen:
/// (1) Archive + optionaler .archive.xl-Sidecar, (2) Redscript Datei-Layout,
/// (3) Redscript Ordner-Layout, (4) Relative-Pfad-Normalisierung mit '/'.</summary>
public sealed class ConflictSourceTests : IDisposable
{
    private readonly string _root;

    public ConflictSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kromodix-cp-conflict-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Archive_ohne_Sidecar_liefert_einen_relativen_Pfad_mit_Slash()
    {
        var archDir = Path.Combine(_root, "archive", "pc", "mod");
        Directory.CreateDirectory(archDir);
        var archPath = Path.Combine(archDir, "SuperMod.archive");
        File.WriteAllText(archPath, "fake");
        var mod = new CyberpunkMod(CyberpunkModType.Archive, "SuperMod", archPath, true);

        var files = Cyberpunk2077Plugin.CollectFilesFor(mod, _root);

        files.Should().ContainSingle();
        files[0].Should().Be("archive/pc/mod/SuperMod.archive");
    }

    [Fact]
    public void Archive_mit_XL_Sidecar_liefert_beide_Files()
    {
        var archDir = Path.Combine(_root, "archive", "pc", "mod");
        Directory.CreateDirectory(archDir);
        var archPath = Path.Combine(archDir, "Weapons.archive");
        File.WriteAllText(archPath, "fake");
        File.WriteAllText(archPath + ".xl", "sidecar");
        var mod = new CyberpunkMod(CyberpunkModType.Archive, "Weapons", archPath, true);

        var files = Cyberpunk2077Plugin.CollectFilesFor(mod, _root);

        files.Should().HaveCount(2);
        files.Should().Contain("archive/pc/mod/Weapons.archive");
        files.Should().Contain("archive/pc/mod/Weapons.archive.xl");
    }

    [Fact]
    public void Redscript_als_Einzeldatei_wird_als_ein_File_erkannt()
    {
        var scriptsDir = Path.Combine(_root, "r6", "scripts");
        Directory.CreateDirectory(scriptsDir);
        var reds = Path.Combine(scriptsDir, "MyMod.reds");
        File.WriteAllText(reds, "// reds");
        var mod = new CyberpunkMod(CyberpunkModType.Redscript, "MyMod", reds, true);

        var files = Cyberpunk2077Plugin.CollectFilesFor(mod, _root);

        files.Should().ContainSingle().Which.Should().Be("r6/scripts/MyMod.reds");
    }

    [Fact]
    public void Redscript_als_Ordner_wird_rekursiv_gelistet()
    {
        var modDir = Path.Combine(_root, "r6", "scripts", "MyMod");
        Directory.CreateDirectory(modDir);
        Directory.CreateDirectory(Path.Combine(modDir, "sub"));
        File.WriteAllText(Path.Combine(modDir, "main.reds"), "// main");
        File.WriteAllText(Path.Combine(modDir, "sub", "helper.reds"), "// helper");
        var mod = new CyberpunkMod(CyberpunkModType.Redscript, "MyMod", modDir, true);

        var files = Cyberpunk2077Plugin.CollectFilesFor(mod, _root);

        files.Should().HaveCount(2);
        files.Should().Contain("r6/scripts/MyMod/main.reds");
        files.Should().Contain("r6/scripts/MyMod/sub/helper.reds");
    }

    [Fact]
    public void RedMod_Ordner_liefert_alle_Files_relativ_mit_Slash()
    {
        var modDir = Path.Combine(_root, "mods", "CoolRedMod");
        Directory.CreateDirectory(Path.Combine(modDir, "archives"));
        File.WriteAllText(Path.Combine(modDir, "info.json"), "{}");
        File.WriteAllText(Path.Combine(modDir, "archives", "cool.archive"), "fake");
        var mod = new CyberpunkMod(CyberpunkModType.RedMod, "CoolRedMod", modDir, true);

        var files = Cyberpunk2077Plugin.CollectFilesFor(mod, _root);

        files.Should().HaveCount(2);
        files.Should().Contain("mods/CoolRedMod/info.json");
        files.Should().Contain("mods/CoolRedMod/archives/cool.archive");
    }

    [Fact]
    public void Nicht_existierender_Pfad_liefert_leere_Liste_ohne_Exception()
    {
        var missing = Path.Combine(_root, "archive", "pc", "mod", "Ghost.archive");
        var mod = new CyberpunkMod(CyberpunkModType.Archive, "Ghost", missing, true);

        var files = Cyberpunk2077Plugin.CollectFilesFor(mod, _root);

        files.Should().BeEmpty();
    }
}
