using System.IO;
using System.IO.Compression;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.Cyberpunk2077.Services;
using KroModIx.Plugin.TestKit;
using Xunit;

namespace KroModIx.Plugin.Cyberpunk2077.Tests;

public sealed class ZipInstallerTests : IDisposable
{
    private readonly string _installRoot;
    private readonly string _tmp;
    private readonly DetectedGame _game;
    private readonly FakeArchiveService _archives = new();
    private readonly CyberpunkZipInstaller _installer;

    public ZipInstallerTests()
    {
        _installer = new CyberpunkZipInstaller(_archives);
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

    /// <summary>Gezählt werden muss, wie weit die <c>..</c> wirklich
    /// führen. <c>archive/pc/mod/../../../x</c> sind drei Ebenen hinauf aus
    /// drei Ebenen hinein — das landet genau wieder im Spielverzeichnis und
    /// ist <b>kein</b> Ausbruch. Der Test davor prüfte genau diesen Pfad und
    /// stellte nur fest, dass die Datei nicht an einer Stelle lag, an der
    /// sie auch nie gelandet wäre; er konnte nicht fehlschlagen. Hier eine
    /// Ebene mehr, und beide Fälle getrennt geprüft.</summary>
    [Fact]
    public void Punkt_Punkt_Pfad_wird_abgelehnt()
    {
        var zip = BuildZip(
            ("archive/pc/mod/ok.archive", "gut"),
            ("archive/pc/mod/../../../../evil.archive", "boom"));
        var result = _installer.Install(zip, _game);

        File.Exists(Path.Combine(_tmp, "evil.archive")).Should().BeFalse();
        result.Success.Should().BeFalse("ein Ausbruchsversuch bricht den Install ab");
        result.Message.Should().Contain("herausschreiben");
    }

    [Fact]
    public void Punkt_Punkt_innerhalb_des_Ziels_ist_erlaubt()
    {
        var zip = BuildZip(
            ("archive/pc/mod/../../../mods/MeineMod/info.json", "{}"));
        var result = _installer.Install(zip, _game);

        result.Success.Should().BeTrue("der Pfad bleibt unter dem InstallDir");
        File.Exists(Path.Combine(_installRoot, "mods", "MeineMod", "info.json"))
            .Should().BeTrue();
    }

    /// <summary>Der Fall, der die Migration auf den Host-Baukasten ausgelöst
    /// hat. Bis v0.15.0 prüfte das Plugin <c>name.Contains("..")</c> — ein
    /// <b>absoluter</b> Eintragsname enthält kein <c>..</c>, kommt also
    /// durch, und <c>Path.Combine</c> verwirft dann das Zielverzeichnis.
    ///
    /// <para>Am 03.10.2026 gegen den damaligen Code gemessen:
    /// <c>Install.Success = True</c>, zwei Dateien, und die zweite lag
    /// außerhalb des InstallDir mit dem Inhalt des Archiv-Eintrags. Dieser
    /// Test hält den Befund fest, damit ein künftiger eigener
    /// „Schutz" nicht wieder dieselbe Lücke aufreißt.</para></summary>
    [Fact]
    public void Absoluter_Eintragsname_bricht_nicht_aus()
    {
        var opfer = Path.Combine(_tmp, "ausserhalb.txt");
        var zip = BuildZip(
            ("archive/pc/mod/ok.archive", "gut"),
            (opfer, "UEBERNOMMEN"));

        var result = _installer.Install(zip, _game);

        File.Exists(opfer).Should().BeFalse(
            "der absolute Pfad darf das Zielverzeichnis nicht verwerfen");
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("herausschreiben");
    }

    /// <summary>Was wirklich im Spiel landete, muss in der Meldung stehen —
    /// sonst weiß der Nutzer nach dem Abbruch nicht, was er aufräumen
    /// soll.</summary>
    [Fact]
    public void Abbruch_nennt_die_schon_geschriebenen_Dateien()
    {
        var zip = BuildZip(
            ("archive/pc/mod/ok.archive", "gut"),
            ("/tmp/boese.txt", "boom"));

        var result = _installer.Install(zip, _game);

        result.InstalledPaths.Should().ContainSingle()
            .Which.Should().EndWith("ok.archive");
        result.Message.Should().Contain("1 Datei(en) waren schon geschrieben");
    }

    /// <summary>Eine Datei mit Archiv-Endung, die kein Archiv ist (ein
    /// abgebrochener Download), wird am Inhalt erkannt und sauber
    /// abgelehnt — nicht mit einer Ausnahme aus der Archiv-Bibliothek.</summary>
    [Fact]
    public void Kein_Archiv_wird_am_Inhalt_erkannt()
    {
        var kaputt = Path.Combine(_tmp, "abgebrochen.zip");
        File.WriteAllText(kaputt, "das ist kein ZIP");

        var result = _installer.Install(kaputt, _game);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("kein lesbares Archiv");
    }

    /// <summary>Der Endungs-Vorfilter des Downloads-Tabs kommt seit v0.16.0
    /// aus dem Host — ein dort neu unterstütztes Format muss nicht in neun
    /// Plugins nachgetragen werden.</summary>
    [Fact]
    public void Endungs_Vorfilter_kommt_aus_dem_Baukasten()
    {
        _installer.SupportedExtensions.Should().BeEquivalentTo([".zip", ".rar", ".7z"]);
        _installer.HasSupportedExtension("mod.RAR").Should().BeTrue();
        _installer.HasSupportedExtension("liesmich.txt").Should().BeFalse();
    }
}
