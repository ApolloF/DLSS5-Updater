using System.Globalization;
using System.Text;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>Lecram's DLSS5 add-on (import-only, two generations): import checks, ReShade-only install, next to ShortFuse's add-on. Fake files only.</summary>
public sealed class LecramTests : IDisposable
{
    private const string V10File = ComponentStore.Lecram10File;
    private const string V11File = ComponentStore.Lecram11File;
    private static readonly Version V10Version = new(0, 2026, 1013, 940);

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-lecram-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly ComponentStore _store = new(new GitHubClient(), () => true);
    private readonly NrVersion _nr;
    private readonly GameInfo _game;

    public LecramTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(_root);
        FakeVersionDll.Write(_store.ReShadePath, new Version(6, 8, 0, 0), ComponentStore.ReShadeFile);
        var nr = Path.Combine(_tmp, "nr.dll");
        FakeVersionDll.Write(nr, new Version(310, 9, 16, 0), ComponentStore.DlssNrFile);
        _nr = _store.ImportDlssNr(nr);
        _game = new GameInfo { Id = _root, Root = _root, Name = "Lecram game", Dlss = [new DlssDll { Name = "nvngx_dlss.dll" }] };

        var pkg = Path.Combine(ComponentStore.TagDir(Component.OptiScaler, "v9.9.9"), "pkg");
        Directory.CreateDirectory(pkg);
        File.WriteAllText(Path.Combine(pkg, "OptiScaler.dll"), "OPTI");
        File.WriteAllText(Path.Combine(pkg, "OptiScaler.ini"), "[DlssNr]\nEnabled=auto\n");
        var release = new ReleaseInfo { Tag = "v9.9.9" };
        ComponentStore.MarkComplete(Component.OptiScaler, release);
        _store.Opti = release;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private string Src(string fileName)
    {
        var path = Path.Combine(_tmp, "src-" + Guid.NewGuid().ToString("N")[..6], fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    /// <summary>A v10-style add-on: it names itself in the version resource.</summary>
    private string FakeV10(string original = V10File, Version? version = null)
    {
        var path = Src(V10File);
        FakeVersionDll.Write(path, version ?? V10Version, original);
        return path;
    }

    /// <summary>A v11-style add-on: a PE file with no version resource and the strings of its log lines.</summary>
    private string FakeV11(string version = "v11.0.0-beta.6", bool markers = true)
    {
        var path = Src(V11File);
        var bytes = new byte[0x200];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        var text = (markers ? "RenoDX.DLSS5\0NR-SETTINGS event=effective\0NR-COMPAT v1 coinstalled=0\0" : "") +
                   $"{version}\0{version}NR on\0{version}\0v11.0.0-\0{Guid.NewGuid()}\0";
        File.WriteAllBytes(path, [.. bytes, .. Encoding.ASCII.GetBytes(text)]);
        return path;
    }

    private AddonBuild ImportSf(Version version)
    {
        var path = Src(ComponentStore.RenoDxFile);
        FakeVersionDll.Write(path, version, ComponentStore.RenoDxFile);
        return _store.ImportRenoDx(path);
    }

    private string G(string name) => Path.Combine(_root, name);
    private IniFile Ini() => IniFile.Load(G("ReShade.ini"));
    private string? Value(string key) => File.Exists(G("ReShade.ini")) ? Ini().Get(LecramIni.Section, key) : null;
    private Dictionary<string, string> Snapshot() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(_root, f), FileUtil.Sha256);

    private InstallOptions Lec(bool lecram = true, bool renoDx = false, IReadOnlyList<IniOverride>? overrides = null, string? lecramId = null, string? renoDxId = null) => new()
    {
        Mode = InstallMode.ReShadeOnly, ReShade = true, Lecram = lecram, RenoDx = renoDx, DlssNr = lecram || renoDx, DlssNrId = _nr.Id,
        Proxy = "dxgi.dll", ReShadeOverrides = overrides ?? [], LecramId = lecramId, RenoDxId = renoDxId,
    };

    private static IniOverride Key(string key, string value) => new(LecramIni.Section, key, value);

    // ---------- import ----------

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FreshConfig_ClearsOldGenerationAndKeepsChosenHookPoint_WithoutMigrationMarkers(bool v11, bool setHookPoint)
    {
        var build = _store.ImportLecram(v11 ? FakeV11() : FakeV10());
        const string original = "[GENERAL]\nPresetPath=mine.ini\n[OTHER]\nSetting=keep\n[RenoDX.DLSS5]\nConfigVersion=10\nNeuralUplift=1\nNRHookPoint=2\nNRModelStyleOffset=9\nJunk=old\n[RENODX-DLSS]\nJunk=foreign\n";
        File.WriteAllText(G("ReShade.ini"), original);
        await new Installer(_store).InstallAsync(_game, _root,
            Lec(lecramId: build.Id, overrides: setHookPoint ? [Key("NRHookPoint", "1")] : []) with { CarryOverIni = false }, null, default);
        var ini = Ini();
        Assert.Equal(setHookPoint ? "1" : null, Value("NRHookPoint"));
        Assert.Null(Value("ConfigVersion"));
        Assert.Null(Value("NRModelCarryoverReset"));
        Assert.Null(Value("NeuralUplift"));
        Assert.Null(Value("NRModelStyleOffset"));
        Assert.Null(Value("Junk"));
        Assert.False(LecramIni.NeedsV11Migration(ini));
        Assert.Equal("mine.ini", ini.Get("GENERAL", "PresetPath"));
        Assert.Equal("keep", ini.Get("OTHER", "Setting"));
        Assert.Equal("foreign", ini.Get(RenoDxIni.Section, "Junk"));
        var m = InstallManifest.Read(_root)!;
        if (setHookPoint)
        {
            var applied = Assert.Single(m.ReShadeIni);
            Assert.Equal($"{LecramIni.Section}/NRHookPoint", applied.Key);
            Assert.Equal("1", applied.Value);
        }
        else Assert.Empty(m.ReShadeIni);
        var backup = Assert.Single(m.Backups, b => b.Original == "ReShade.ini");
        Assert.Equal(original, File.ReadAllText(Path.Combine(InstallManifest.BackupDirFor(_root), backup.Backup)));
    }

    [Fact]
    public async Task FreshConfig_V11WithNeuralUpliftAndHookPoint_MarksTheProfileV11ToPreventHookReset()
    {
        var build = _store.ImportLecram(FakeV11());
        File.WriteAllText(G("ReShade.ini"), "[RenoDX.DLSS5]\nConfigVersion=10\nNRModelStyleOffset=9\nJunk=old\n");
        await new Installer(_store).InstallAsync(_game, _root,
            Lec(lecramId: build.Id, overrides: [Key("NeuralUplift", "0"), Key("NRHookPoint", "1")]) with { CarryOverIni = false }, null, default);
        Assert.Equal("0", Value("NeuralUplift"));
        Assert.Equal("1", Value("NRHookPoint"));
        Assert.Equal("11", Value("ConfigVersion"));
        Assert.Null(Value("NRModelCarryoverReset"));
        Assert.Null(Value("NRModelStyleOffset"));
        Assert.Null(Value("Junk"));
        Assert.False(LecramIni.NeedsV11Migration(Ini()));
        var applied = InstallManifest.Read(_root)!.ReShadeIni;
        Assert.Equal(3, applied.Count);
        Assert.Equal("0", applied[$"{LecramIni.Section}/NeuralUplift"]);
        Assert.Equal("1", applied[$"{LecramIni.Section}/NRHookPoint"]);
        Assert.Equal("11", applied[$"{LecramIni.Section}/ConfigVersion"]);
    }

    [Fact]
    public async Task FreshConfig_WithBothAddons_ClearsBothAndWritesObserve()
    {
        _store.ImportLecram(FakeV11());
        ImportSf(new Version(0, 2026, 928, 306));
        File.WriteAllText(G("ReShade.ini"), "[RENODX-DLSS]\nJunk=old\n[RENODX-DLSS-preset1]\nJunk=old\n[RenoDX.DLSS5]\nJunk=old\nForeignNr=yield\n");
        await new Installer(_store).InstallAsync(_game, _root, Lec(renoDx: true) with { CarryOverIni = false }, null, default);
        var ini = Ini();
        Assert.Equal("observe", Value("ForeignNr"));
        Assert.Null(Value("Junk"));
        Assert.Null(ini.Get(RenoDxIni.Section, "Junk"));
        Assert.Null(ini.Get(RenoDxIni.PresetSection, "Junk"));
        Assert.Single(InstallManifest.Read(_root)!.Backups, b => b.Original == "ReShade.ini");
    }

    [Fact]
    public void Import_AcceptsV10ByItsVersionResourceAndV11ByItsStrings_AndRejectsOtherAddons()
    {
        Assert.False(_store.LecramImported);
        Assert.Empty(_store.LecramBuilds);

        var sf = Assert.Throws<InvalidDataException>(() => _store.ImportLecram(FakeV10(ComponentStore.RenoDxFile)));
        Assert.Contains("ShortFuse", sf.Message);
        var fix = Assert.Throws<InvalidDataException>(() => _store.ImportLecram(FakeV10("renodx-dlssfix.addon64")));
        Assert.Contains("ShortFuse", fix.Message);
        var dfc = Assert.Throws<InvalidDataException>(() => _store.ImportLecram(FakeV10(ComponentStore.DfcFile)));
        Assert.Contains("Deep Fried Chicken", dfc.Message);
        Assert.Throws<InvalidDataException>(() => _store.ImportLecram(FakeV10("other.addon64")));
        Assert.Throws<InvalidDataException>(() => _store.ImportLecram(FakeV11(markers: false)));
        var text = Src("text.addon64");
        File.WriteAllText(text, "not a PE file");
        Assert.Throws<InvalidDataException>(() => _store.ImportLecram(text));
        Assert.Throws<FileNotFoundException>(() => _store.ImportLecram(Path.Combine(_tmp, "missing.addon64")));
        Assert.False(_store.LecramImported);

        var v10 = FakeV10();
        _store.Import(v10, V11File); // the file name only picks the import; the file itself decides the generation
        var build = Assert.Single(_store.LecramBuilds);
        Assert.Equal(LecramGeneration.V10, build.Generation);
        Assert.Equal(V10File, build.FileName);
        Assert.Equal("0.2026.1013.940", build.FileVersion);
        // The label is whatever the highest v1.2.3 string of the binary says; this fixture is a copy of the test assembly.
        Assert.Equal(ComponentStore.FindBuildLabel(File.ReadAllBytes(v10)), build.DetectedLabel);
        Assert.Equal(build.DetectedLabel + "-" + FileUtil.Sha256(v10)[..12], build.Tag);
        var file = _store.RequireAddon(AddonKind.Lecram, build.Id);
        Assert.Equal(Path.Combine(AppPaths.Components, "lecram-dlss5", FileUtil.Sha256(v10), V10File), file.Path);

        _store.RemoveAddon(AddonKind.Lecram, build.Id);
        Assert.False(_store.LecramImported);
        Assert.Null(_store.LecramBuildFor(null));
        Assert.Throws<FileNotFoundException>(() => _store.RequireAddon(AddonKind.Lecram, null));
    }

    [Fact]
    public void Import_V11_IsFoundByContent_AndKeepsItsOwnLabelAndTag()
    {
        var v11 = FakeV11();
        var build = _store.ImportLecram(v11);

        Assert.Equal(LecramGeneration.V11, build.Generation);
        Assert.Equal(V11File, build.FileName);
        Assert.Equal("", build.FileVersion);
        Assert.Equal("v11.0.0-beta.6", build.DetectedLabel);
        Assert.Equal("v11.0.0-beta.6-" + FileUtil.Sha256(v11)[..12], build.Tag);
        Assert.Equal(build, _store.LecramBuildFor(null));
        Assert.True(File.Exists(ComponentStore.AddonPathOf(AddonKind.Lecram, build)));
    }

    [Fact]
    public void Import_KeepsBothGenerations_AndNeverDeletesTheOther()
    {
        var v10 = _store.ImportLecram(FakeV10());
        var v11 = _store.ImportLecram(FakeV11());
        Assert.Equal(2, _store.LecramBuilds.Count);
        Assert.True(File.Exists(ComponentStore.AddonPathOf(AddonKind.Lecram, v10)));
        Assert.True(File.Exists(ComponentStore.AddonPathOf(AddonKind.Lecram, v11)));
        Assert.Equal(LecramGeneration.V10, _store.LecramBuildFor(v10.Id)!.Generation);
        Assert.Equal(LecramGeneration.V11, _store.LecramBuildFor(v11.Id)!.Generation);

        Assert.Equal(v10, _store.ImportLecram(FakeV10()));
        Assert.Equal(2, _store.LecramBuilds.Count);
    }

    [Fact]
    public void RenoDxImport_PointsLecramFilesToTheLecramImport()
    {
        var v10 = Assert.Throws<InvalidDataException>(() => _store.ImportRenoDx(FakeV10()));
        Assert.Contains(V10File, v10.Message);
        Assert.Contains("Lecram", v10.Message);
        var v11 = Assert.Throws<InvalidDataException>(() => _store.ImportRenoDx(FakeV11()));
        Assert.Contains("Lecram", v11.Message);
        Assert.False(_store.RenoDxImported);
    }

    /// <summary>Uses the real builds when they are lying in Downloads; nothing to check on a machine without them.</summary>
    [Fact]
    public void RealBuilds_AreToldApartByTheirOwnMarkers()
    {
        var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
        var v11 = Path.Combine(downloads, V11File);
        var v10 = Path.Combine(downloads, "renodx-dlss5-v10.7-with-studio", V10File);
        if (File.Exists(v11))
        {
            var build = _store.ImportLecram(v11);
            Assert.Equal(LecramGeneration.V11, build.Generation);
            Assert.StartsWith("v11.", build.DetectedLabel); // whichever v11 build is lying in Downloads
        }
        if (File.Exists(v10))
        {
            var build = _store.ImportLecram(v10);
            Assert.Equal(LecramGeneration.V10, build.Generation);
            // The binary also carries "v10.1.0-rc3" in tooltip texts; "v10.7.0" is the highest and is the add-on's own version string.
            Assert.Equal("v10.7.0", build.DetectedLabel);
            Assert.False(ComponentStore.LooksLikeLecramV11(v10));
        }
    }

    // ---------- install ----------

    [Fact]
    public async Task Install_PlacesTheImportedGeneration_RecordsIt_WritesOnlyItsSection_AndUninstallRestores()
    {
        var v10 = FakeV10();
        var v10Build = _store.ImportLecram(v10);
        File.WriteAllText(G("ReShade.ini"), "[OTHER]\nSetting=keep\n");
        var before = File.ReadAllText(G("ReShade.ini"));
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Lec(overrides: [Key("NRPasses", "2")]), null, default);

        Assert.True(File.Exists(G(V10File)));
        Assert.False(File.Exists(G(V11File)));
        Assert.Equal(FileUtil.Sha256(v10), FileUtil.Sha256(G(V10File)));
        var ini = Ini();
        Assert.Equal("2", ini.Get(LecramIni.Section, "NRPasses"));
        Assert.Null(ini.Get("ADDON", "LoadFromDllMain"));
        Assert.Equal("keep", ini.Get("OTHER", "Setting"));
        var m = InstallManifest.Read(_root)!;
        Assert.True(m.Lecram);
        Assert.Equal(V10File, m.LecramFile);
        Assert.Equal(v10Build.Tag, m.LecramTag);
        Assert.Equal(v10Build.Id, m.LecramId);
        Assert.False(m.RenoDx);

        await installer.InstallAsync(_game, _root, Lec(overrides: [Key("NRPasses", "2")]), null, default);
        Assert.Equal("2", Ini().Get(LecramIni.Section, "NRPasses"));
        Assert.True(InstallManifest.Read(_root)!.ReShadeIni.ContainsKey(LecramIni.Section + "/NRPasses"));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal(before, File.ReadAllText(G("ReShade.ini")));
        Assert.False(File.Exists(G(V10File)));
    }

    [Fact]
    public async Task SwitchingGeneration_RemovesTheOtherOwnedFile()
    {
        var v10 = _store.ImportLecram(FakeV10());
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Lec(lecramId: v10.Id), null, default);
        Assert.True(File.Exists(G(V10File)));

        var v11 = _store.ImportLecram(FakeV11());
        await installer.InstallAsync(_game, _root, Lec(lecramId: v11.Id), null, default);

        Assert.False(File.Exists(G(V10File)));
        Assert.True(File.Exists(G(V11File)));
        var m = InstallManifest.Read(_root)!;
        Assert.Equal(V11File, m.LecramFile);
        Assert.Equal(v11.Id, m.LecramId);
        Assert.Equal("v11.0.0-beta.6", m.LecramTag![..m.LecramTag!.LastIndexOf('-')]);
        Assert.DoesNotContain(m.Files, f => f.Equals(V10File, StringComparison.OrdinalIgnoreCase));

        await installer.UninstallAsync(_game, _root, default);
        Assert.False(File.Exists(G(V11File)));
    }

    [Fact]
    public async Task Beta29Keys_AreWrittenForV11_AndRemovedOnUninstall()
    {
        _store.ImportLecram(FakeV11("v11.0.0-beta.29"));
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root,
            Lec(overrides: [Key("NRPasses", "2"), Key("NRPass2Preset", "2"), Key("NRGpuCrashWitness", "0")]), null, default);
        var ini = Ini();
        Assert.Equal("2", ini.Get(LecramIni.Section, "NRPass2Preset"));
        Assert.Equal("0", ini.Get(LecramIni.Section, "NRGpuCrashWitness"));
        Assert.True(File.Exists(G(V11File)));

        await installer.UninstallAsync(_game, _root, default);
        Assert.False(File.Exists(G("ReShade.ini")) && Ini().Get(LecramIni.Section, "NRPass2Preset") is not null);
        Assert.False(File.Exists(G("ReShade.ini")) && Ini().Get(LecramIni.Section, "NRGpuCrashWitness") is not null);
    }

    [Theory]
    [InlineData("v11.0.0-beta.48", true)]
    [InlineData("v11.0.0-beta.45", false)]
    [InlineData("v11.0.0-beta.29", false)]
    public async Task PresentFgPath_IsWrittenOnlyForBuildsThatReadIt(string label, bool written)
    {
        var build = _store.ImportLecram(FakeV11(label));
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root,
            Lec(lecramId: build.Id, overrides: [Key("NRHookPoint", "2"), Key("NRPresentFgPath", "1")]), null, default);
        Assert.Equal("2", Value("NRHookPoint"));
        Assert.Equal(written ? "1" : null, Value("NRPresentFgPath"));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Null(Value("NRPresentFgPath"));
    }

    [Fact]
    public async Task SwitchingToAnOlderBuild_DropsTheKeyTheAppWroteForTheNewerOne()
    {
        var beta48 = _store.ImportLecram(FakeV11("v11.0.0-beta.48"));
        var beta45 = _store.ImportLecram(FakeV11("v11.0.0-beta.45"));
        var installer = new Installer(_store);
        IniOverride[] overrides = [Key("NRPresentFgPath", "1")];

        await installer.InstallAsync(_game, _root, Lec(lecramId: beta48.Id, overrides: overrides), null, default);
        Assert.Equal("1", Value("NRPresentFgPath"));
        await installer.InstallAsync(_game, _root, Lec(lecramId: beta45.Id, overrides: overrides), null, default);
        Assert.Null(Value("NRPresentFgPath"));
    }

    [Fact]
    public async Task ForeignCopiesOfEitherGeneration_AreDisplaced_AndRestoredOnUninstall()
    {
        File.WriteAllText(G(V10File), "FOREIGN-10");
        File.WriteAllText(G(V11File), "FOREIGN-11");
        _store.ImportLecram(FakeV11());
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Lec(), null, default);
        Assert.False(File.Exists(G(V10File)));
        Assert.NotEqual("FOREIGN-11", File.ReadAllText(G(V11File)));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal("FOREIGN-10", File.ReadAllText(G(V10File)));
        Assert.Equal("FOREIGN-11", File.ReadAllText(G(V11File)));
    }

    [Fact]
    public async Task SwitchingGeneration_KeepsAForeignCopyOfTheOldOneOutOfTheGame()
    {
        File.WriteAllText(G(V10File), "FOREIGN-10");
        var v10 = _store.ImportLecram(FakeV10());
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Lec(lecramId: v10.Id), null, default);
        Assert.NotEqual("FOREIGN-10", File.ReadAllText(G(V10File)));

        var v11 = _store.ImportLecram(FakeV11());
        await installer.InstallAsync(_game, _root, Lec(lecramId: v11.Id), null, default);
        Assert.False(File.Exists(G(V10File)));
        Assert.True(File.Exists(G(V11File)));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal("FOREIGN-10", File.ReadAllText(G(V10File)));
        Assert.False(File.Exists(G(V11File)));
    }

    [Fact]
    public async Task NextToShortFuse_BothAddonsAreInstalled_AndForeignNrObserveIsWritten()
    {
        ImportSf(new Version(0, 2026, 928, 2324));
        _store.ImportLecram(FakeV10());
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Lec(renoDx: true), null, default);

        Assert.True(File.Exists(G(ComponentStore.RenoDxFile)));
        Assert.True(File.Exists(G(V10File)));
        var ini = Ini();
        Assert.Equal("observe", ini.Get(LecramIni.Section, "ForeignNr"));
        Assert.Equal(ComponentStore.RenoDxFile, ini.Get("ADDON", "LoadFromDllMain"));
        var m = InstallManifest.Read(_root)!;
        Assert.True(m.RenoDx);
        Assert.True(m.Lecram);

        await installer.InstallAsync(_game, _root, Lec(renoDx: true), null, default);
        Assert.True(File.Exists(G(V10File)));
        Assert.Equal("observe", Ini().Get(LecramIni.Section, "ForeignNr"));

        await installer.UninstallAsync(_game, _root, default);
        Assert.False(File.Exists(G(ComponentStore.RenoDxFile)));
        Assert.False(File.Exists(G(V10File)));
    }

    [Fact]
    public async Task ForeignNr_TheAddonsOwnYieldMakesWayForObserve_AButAnExplicitChoiceStays()
    {
        ImportSf(new Version(0, 2026, 928, 2324));
        _store.ImportLecram(FakeV11());
        File.WriteAllText(G("ReShade.ini"), $"[{LecramIni.Section}]\nForeignNr=yield\nNRPasses=3\n");
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Lec(renoDx: true), null, default);
        Assert.Equal("observe", Ini().Get(LecramIni.Section, "ForeignNr"));
        Assert.Equal("3", Ini().Get(LecramIni.Section, "NRPasses"));

        await installer.InstallAsync(_game, _root, Lec(renoDx: true, overrides: [Key("ForeignNr", "yield")]), null, default);
        Assert.Equal("yield", Ini().Get(LecramIni.Section, "ForeignNr"));
    }

    [Fact]
    public async Task LecramAlone_DoesNotForceForeignNr_AndDropsTheObserveItWroteBefore()
    {
        ImportSf(new Version(0, 2026, 928, 2324));
        _store.ImportLecram(FakeV10());
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Lec(), null, default);
        Assert.Null(Value("ForeignNr"));

        await installer.InstallAsync(_game, _root, Lec(renoDx: true), null, default);
        Assert.Equal("observe", Value("ForeignNr"));

        await installer.InstallAsync(_game, _root, Lec(), null, default);
        Assert.False(File.Exists(G(ComponentStore.RenoDxFile)));
        Assert.True(File.Exists(G(V10File)));
        Assert.Null(Value("ForeignNr"));
        Assert.DoesNotContain(InstallManifest.Read(_root)!.ReShadeIni.Keys, k => k.EndsWith("/ForeignNr", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ShortFuseAlone_DisplacesAForeignLecramFile_ButKeepsTheOwnedOneWhenBothAreSelected()
    {
        ImportSf(new Version(0, 2026, 928, 2324));
        _store.ImportLecram(FakeV10());
        File.WriteAllText(G(V11File), "FOREIGN-11");
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Lec(lecram: false, renoDx: true), null, default);
        Assert.False(File.Exists(G(V11File)));
        Assert.False(File.Exists(G(V10File)));

        await installer.InstallAsync(_game, _root, Lec(renoDx: true), null, default);
        Assert.True(File.Exists(G(V10File)));
        await installer.InstallAsync(_game, _root, Lec(renoDx: true), null, default);
        Assert.True(File.Exists(G(V10File)));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal("FOREIGN-11", File.ReadAllText(G(V11File)));
    }

    [Fact]
    public async Task DroppingLecram_RemovesItsFileAndTheKeysItWrote_ButKeepsTheUsers()
    {
        _store.ImportLecram(FakeV10());
        File.WriteAllText(G("ReShade.ini"), $"[{LecramIni.Section}]\nNREdit=mine\n");
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Lec(overrides: [Key("NRPasses", "2"), Key("NRStyle", "1")]), null, default);
        var tuned = Ini();
        tuned.Set(LecramIni.Section, "NRStyle", "2");
        tuned.Save(G("ReShade.ini"));

        await installer.InstallAsync(_game, _root, Lec(lecram: false), null, default);

        Assert.False(File.Exists(G(V10File)));
        var ini = Ini();
        Assert.Null(ini.Get(LecramIni.Section, "NRPasses"));
        Assert.Equal("2", ini.Get(LecramIni.Section, "NRStyle")); // the user changed it since
        Assert.Equal("mine", ini.Get(LecramIni.Section, "NREdit"));
        var m = InstallManifest.Read(_root)!;
        Assert.False(m.Lecram);
        Assert.Null(m.LecramFile);
        Assert.Null(m.LecramTag);
    }

    [Fact]
    public async Task LecramSettings_AreOnlyWrittenWhileTheAddonIsEnabled()
    {
        _store.ImportLecram(FakeV10());
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Lec(lecram: false, overrides: [Key("NRPasses", "2")]), null, default);

        Assert.False(File.Exists(G(V10File)));
        Assert.False(File.Exists(G("ReShade.ini")) && Ini().Get(LecramIni.Section, "NRPasses") is not null);
    }

    [Fact]
    public async Task OurLecramKeys_SurviveAReinstall_WhileStaleKeysOfAnOlderAppAreStillCleaned()
    {
        _store.ImportLecram(FakeV11());
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Lec(overrides: [Key("NRPasses", "2")]), null, default);
        await installer.InstallAsync(_game, _root, Lec(overrides: [Key("NRPasses", "3")]), null, default);
        Assert.Equal("3", Ini().Get(LecramIni.Section, "NRPasses"));

        // A key an earlier app version wrote for the wrong add-on is cleaned once Lecram is not installed.
        var m = InstallManifest.Read(_root)!;
        m.ReShadeIni["RenoDX.DLSS5/NRHookPoint"] = "1";
        m.Save(_root);
        var ini = Ini();
        ini.Set(LecramIni.Section, "NRHookPoint", "1");
        ini.Save(G("ReShade.ini"));
        await installer.InstallAsync(_game, _root, Lec(lecram: false), null, default);
        Assert.Null(Ini().Get(LecramIni.Section, "NRHookPoint"));
    }

    [Fact]
    public async Task Lecram_WithoutImport_FailsWithClearMessage_AndTouchesNothing()
    {
        var before = Snapshot();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => new Installer(_store).InstallAsync(_game, _root, Lec(), null, default));
        Assert.Contains("Import Lecram's", error.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Lecram_InOptiScalerMode_OrWithDfc_Throws()
    {
        _store.ImportLecram(FakeV10());
        var before = Snapshot();
        var installer = new Installer(_store);

        var opti = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.InstallAsync(_game, _root, Lec() with { Mode = InstallMode.OptiScaler, Opti = true }, null, default));
        Assert.Contains("ReShade + add-ons mode", opti.Message);
        var dfc = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.InstallAsync(_game, _root, Lec() with { Dfc = true }, null, default));
        Assert.Contains("Lecram", dfc.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task OptiScalerNr_DisplacesForeignAndOwnedLecramFiles_AndUninstallRestoresTheForeignOnes()
    {
        File.WriteAllText(G(V10File), "USERS-10");
        File.WriteAllText(G(V11File), "USERS-11");
        var installer = new Installer(_store);
        var opti = new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true, DlssNr = true, DlssNrId = _nr.Id, Proxy = "dxgi.dll" };

        await installer.InstallAsync(_game, _root, opti, null, default);
        Assert.False(File.Exists(G(V10File)));
        Assert.False(File.Exists(G(V11File)));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal("USERS-10", File.ReadAllText(G(V10File)));
        Assert.Equal("USERS-11", File.ReadAllText(G(V11File)));

        // Our own file goes when the game moves to OptiScaler-NR.
        File.Delete(G(V10File));
        File.Delete(G(V11File));
        _store.ImportLecram(FakeV11());
        await installer.InstallAsync(_game, _root, Lec(), null, default);
        Assert.True(File.Exists(G(V11File)));
        await installer.InstallAsync(_game, _root, opti, null, default);
        Assert.False(File.Exists(G(V11File)));
        Assert.False(InstallManifest.Read(_root)!.Lecram);
    }

    // ---------- compatibility with ShortFuse's add-on ----------

    [Theory]
    [InlineData(AddonPairStatus.Works, true, true, null)]
    [InlineData(AddonPairStatus.Untested, true, true, null)]
    [InlineData(AddonPairStatus.Crashed, true, true, "crashed before (marked 2026-10-18)")]
    [InlineData(AddonPairStatus.Crashed, true, false, null)]
    [InlineData(AddonPairStatus.Crashed, false, true, null)]
    public void PairMessage_UsesRecordedStatus_OnlyWhileBothAddonsAreSelected(AddonPairStatus status, bool renoDx, bool lecram, string? expected)
    {
        var pair = new AddonPairTest("sf", "lec", status, new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Local));
        var message = LecramCompat.Message(renoDx, lecram, pair);
        if (expected is null) Assert.Null(message);
        else Assert.Contains(expected, message);
        Assert.Null(LecramCompat.Message(true, true, null));
    }

    [Theory]
    [InlineData(AddonPairStatus.Untested)]
    [InlineData(AddonPairStatus.Crashed)]
    [InlineData(AddonPairStatus.Works)]
    public async Task Install_LogsOnlyCrashedAsWarning_AndNeverBlocksEitherAddon(AddonPairStatus status)
    {
        var sf = ImportSf(new Version(0, 2026, 1016, 1704));
        var lecram = _store.ImportLecram(FakeV10());
        var settings = new AppSettings();
        var otherLecram = _store.ImportLecram(FakeV11());
        _store.SelectedLecram = () => otherLecram.Id;
        _store.PairTests = () => settings.AddonPairTests;
        if (status != AddonPairStatus.Untested) _store.MarkAddonPair(settings, sf.Id, lecram.Id, status);
        var lines = new List<string>();
        void Capture(string l) => lines.Add(l);
        Log.Line += Capture;
        try
        {
            await new Installer(_store).InstallAsync(_game, _root, Lec(renoDx: true, renoDxId: sf.Id, lecramId: lecram.Id), null, default);
        }
        finally { Log.Line -= Capture; }

        var messages = lines.Where(l => l.Contains("This ShortFuse + Lecram pair")).ToList();
        if (status != AddonPairStatus.Crashed) Assert.Empty(messages);
        else
        {
            var message = Assert.Single(messages);
            Assert.Contains("WARNING", message);
            Assert.Contains("crashed before", message);
        }
        Assert.True(File.Exists(G(ComponentStore.RenoDxFile)));
        Assert.True(File.Exists(G(V10File)));
    }

    // ---------- options ----------

    private static IniOptionViewModel Option(IReadOnlyList<IniOptionViewModel> all, string key) => all.Single(o => o.Key == key);

    private static double[] Numbers(IniOptionViewModel o) => o.Choices
        .Where(c => c.Value is not null).Select(c => double.Parse(c.Value!, CultureInfo.InvariantCulture)).ToArray();

    [Fact]
    public void Options_AllUseTheLecramSection_AndEveryOneHasHelpAndAdvice()
    {
        var options = SettingsOptions.Lecram([]);
        Assert.All(options, o => Assert.Equal(LecramIni.Section, o.Section));
        Assert.All(options, o => Assert.True(HelpTopics.Get(o.Topic) is not null, $"{o.Key} has no help topic"));
        Assert.All(options, o => Assert.True(HelpTopics.AdviceFor(o.Topic!) is not null, $"{o.Key} has no advice"));
        Assert.Equal(options.Length, options.Select(o => o.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(options.Where(o => !o.IsToggle), o => Assert.Equal("Add-on default", o.Choices[0].Label));
        Assert.Contains("lecram", HelpTopics.All.Select(t => t.Id));
        Assert.NotNull(HelpTopics.AdviceFor("lecram"));
    }

    [Fact]
    public void Options_HideKeysTheImportedGenerationLacks_AndSwitchChoicesPerGeneration()
    {
        var options = SettingsOptions.Lecram([]);
        string[] v11Only = ["NeuralUplift", "NRPreset", "NRPass2Preset", "NRPass3Preset", "NRPass4Preset", "NRVramBuffering", "NRGpuCrashWitness", "NRPresentFgPath"];
        string[] values(string key) => Option(options, key).Choices.Select(c => c.Value ?? "").ToArray();

        foreach (var o in options) o.ApplyGeneration(null);
        Assert.All(options, o => Assert.True(o.IsAvailable));
        Assert.Contains("3", values("NRHookPoint"));

        foreach (var o in options) o.ApplyGeneration(LecramGeneration.V10);
        Assert.All(v11Only, k => Assert.False(Option(options, k).IsAvailable));
        Assert.True(Option(options, "NRAutoMask").IsAvailable);
        Assert.DoesNotContain("3", values("NRHookPoint"));
        Assert.Equal(["Add-on default", "Default", "Natural", "Cinematic"], Option(options, "NRStyle").Choices.Select(c => c.Label));
        Assert.Contains("2.000000", values("NRLocalStructure"));
        Assert.Contains("-1.000000", values("NRSkinStructure"));
        Assert.Equal(["Add-on default", "Optional", "Required", "Never"], Option(options, "NRPresentGuides").Choices.Select(c => c.Label));
        Assert.True(Option(options, "NRFeedMode").IsAvailable);

        foreach (var o in options) o.ApplyGeneration(LecramGeneration.V11);
        Assert.All(v11Only, k => Assert.True(Option(options, k).IsAvailable));
        Assert.False(Option(options, "NRAutoMask").IsAvailable);
        Assert.Contains("3", values("NRHookPoint"));
        Assert.Equal(["Add-on default", "Model A", "Model B", "Model C"], Option(options, "NRStyle").Choices.Select(c => c.Label));
        Assert.DoesNotContain("1.500000", values("NRLocalStructure"));
        Assert.DoesNotContain("-1.000000", values("NRSkinStructure"));
        Assert.Equal(["", "1", "2"], values("NRPresentGuides"));
        Assert.True(Option(options, "NRFeedMode").IsAvailable);

        foreach (var o in options) o.ApplyGeneration(null);
        Assert.All(options, o => Assert.True(o.IsAvailable));
        Assert.Contains("Default / Model A", Option(options, "NRStyle").Choices.Select(c => c.Label));
    }

    [Fact]
    public void Options_KeepTheUsersValue_WhenTheGenerationChangesTheChoices()
    {
        var list = new System.Collections.ObjectModel.ObservableCollection<IniOverride> { Key("NRLocalStructure", "2.000000") };
        var structure = Option(SettingsOptions.Lecram(list), "NRLocalStructure");

        structure.ApplyGeneration(LecramGeneration.V10);
        Assert.Equal("2.0", structure.Selected.Label);
        structure.ApplyGeneration(LecramGeneration.V11);
        Assert.Contains("custom", structure.Selected.Label);
        Assert.Equal("2.000000", list.Single().Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(LecramGeneration.V10)]
    [InlineData(LecramGeneration.V11)]
    public void Options_StayInsideTheRangesTheDocsGive(LecramGeneration? generation)
    {
        var options = SettingsOptions.Lecram([]);
        foreach (var o in options) o.ApplyGeneration(generation);
        var v10 = generation == LecramGeneration.V10;

        Assert.All(Numbers(Option(options, "NRIntensity")), v => Assert.InRange(v, 0, 1));
        Assert.All(Numbers(Option(options, "NRLocalTone")), v => Assert.InRange(v, 0, 2));
        Assert.All(Numbers(Option(options, "NRLocalStructure")), v => Assert.InRange(v, 0, v10 ? 2 : 1));
        Assert.All(Numbers(Option(options, "NRSkinStructure")), v => Assert.InRange(v, v10 ? -1 : 0, 1));
        Assert.All(Numbers(Option(options, "NRPasses")), v => Assert.InRange(v, 1, 4));
        Assert.All(Numbers(Option(options, "NRResolutionScale")), v => Assert.InRange(v, 0.33, 1));
        Assert.All(Numbers(Option(options, "NRHookPoint")), v => Assert.InRange(v, 0, v10 ? 2 : 3));
        Assert.All(Numbers(Option(options, "NRStyle")), v => Assert.InRange(v, 0, 2));
        Assert.All(Numbers(Option(options, "NRPreset")), v => Assert.InRange(v, 0, 2));
        // NRPass2..4Preset (beta.29): 0 same as pass 1, 1 and 2 the two models of DLL 310.9.16; 3 is accepted but names no model.
        Assert.All(new[] { "NRPass2Preset", "NRPass3Preset", "NRPass4Preset" }, k => Assert.Equal([0.0, 1, 2], Numbers(Option(options, k))));
        Assert.Equal([0.0, 1], Numbers(Option(options, "NRGpuCrashWitness")));
        // NRPresentFgPath (beta.48): only 1 switches to the new path; 2, -1 and text read as 0.
        Assert.Equal([0.0, 1], Numbers(Option(options, "NRPresentFgPath")));
        Assert.All(Numbers(Option(options, "NRVramBuffering")), v => Assert.InRange(v, 0, 2));
        Assert.All(Numbers(Option(options, "EnableHooks")), v => Assert.InRange(v, 0, 2));
        // NRPresentGuides 0 is Always on in v11 beta.18+ but DLSS frames in beta.6: only 1 and 2 mean the same in every v11 build.
        Assert.Equal(v10 ? [0.0, 1, 2] : [1.0, 2], Numbers(Option(options, "NRPresentGuides")));
        Assert.Equal([1.0, 2], Numbers(Option(options, "NRFeedMode")));
        Assert.Equal([0.0], Numbers(Option(options, "NeuralUplift")));
        Assert.Equal(["yield", "observe"], Option(options, "ForeignNr").Choices.Where(c => c.Value is not null).Select(c => c.Value));
        Assert.Equal(["auto", "native", "foreign", "off"], Option(options, "DX11Source").Choices.Where(c => c.Value is not null).Select(c => c.Value));
    }

    [Fact]
    public void Options_HookPointHint_NamesTheBeta18Default_AndNewKeysWriteTheirValue()
    {
        var list = new System.Collections.ObjectModel.ObservableCollection<IniOverride>();
        var options = SettingsOptions.Lecram(list);
        foreach (var o in options) o.ApplyGeneration(LecramGeneration.V11);

        Assert.Contains("beta.18+: After upscaling", Option(options, "NRHookPoint").Choices[0].Description);
        Assert.Contains("beta.18+: Always on", Option(options, "NRPresentGuides").Choices[0].Description);

        var guides = Option(options, "NRPresentGuides");
        guides.Selected = guides.Choices.First(c => c.Label == "Always on");
        var feed = Option(options, "NRFeedMode");
        feed.Selected = feed.Choices.First(c => c.Label == "Game exposure");

        Assert.Contains(list, o => LecramIni.IsLecram(o) && o.Key == "NRPresentGuides" && o.Value == "2");
        Assert.Contains(list, o => LecramIni.IsLecram(o) && o.Key == "NRFeedMode" && o.Value == "2");
    }

    [Fact]
    public void LecramIni_RecognisesItsSection_AndTheForeignNrKey()
    {
        Assert.True(LecramIni.IsLecram(new("renodx.dlss5", "NRPasses", "2")));
        Assert.False(LecramIni.IsLecram(new(RenoDxIni.Section, "NRPasses", "2")));
        Assert.True(LecramIni.IsForeignNr(new(LecramIni.Section, " foreignnr ", "observe")));
        Assert.False(LecramIni.IsForeignNr(new(LecramIni.Section, "NRPasses", "2")));
        Assert.True(LecramIni.IsAvailable("NeuralUplift", null));
        Assert.False(LecramIni.IsAvailable("NeuralUplift", LecramGeneration.V10));
        Assert.False(LecramIni.IsAvailable("NRAutoMask", LecramGeneration.V11));
    }

    [Theory]
    [InlineData("NRPresentFgPath", "v11.0.0-beta.48", true)]
    [InlineData("NRPresentFgPath", "v11.0.0-beta.49", true)]
    [InlineData("NRPresentFgPath", "v11.0.1", true)]
    [InlineData("NRPresentFgPath", "v11", true)]
    [InlineData("NRPresentFgPath", null, true)]
    [InlineData("NRPresentFgPath", "v11.0.0-beta.45", false)]
    [InlineData("NRPresentFgPath", "v11.0.0-beta.40", false)]
    [InlineData("nrpresentfgpath", "v11.0.0-beta.29", false)]
    [InlineData("NRPass2Preset", "v11.0.0-beta.29", true)]
    [InlineData("NRPass4Preset", "v11.0.0-beta.22", false)]
    [InlineData("NRGpuCrashWitness", "v11.0.0-beta.22", true)]
    [InlineData("NRGpuCrashWitness", "v11.0.0-beta.18", false)]
    [InlineData("NRPreset", "v11.0.0-beta.6", true)]
    public void LecramIni_HidesV11KeysFromBuildsOlderThanTheFirstThatReadsThem(string key, string? build, bool available)
    {
        Assert.Equal(available, LecramIni.IsAvailable(key, LecramGeneration.V11, build));
        Assert.False(LecramIni.IsAvailable(key, LecramGeneration.V10, build));
        Assert.True(LecramIni.IsAvailable(key, null, build));
    }

    [Fact]
    public void Options_FollowTheBuild_NotJustTheGeneration()
    {
        var options = SettingsOptions.Lecram([]);
        var fgPath = Option(options, "NRPresentFgPath");
        var passPreset = Option(options, "NRPass2Preset");

        fgPath.ApplyGeneration(LecramGeneration.V11, "v11.0.0-beta.48");
        passPreset.ApplyGeneration(LecramGeneration.V11, "v11.0.0-beta.48");
        Assert.True(fgPath.IsAvailable);
        Assert.True(passPreset.IsAvailable);

        fgPath.ApplyGeneration(LecramGeneration.V11, "v11.0.0-beta.45");
        passPreset.ApplyGeneration(LecramGeneration.V11, "v11.0.0-beta.45");
        Assert.False(fgPath.IsAvailable);
        Assert.True(passPreset.IsAvailable);
        Assert.False(fgPath.ViewFor(LecramGeneration.V11, "v11.0.0-beta.29").IsAvailable);
        Assert.False(passPreset.ViewFor(LecramGeneration.V11, "v11.0.0-beta.22").IsAvailable);

        fgPath.ApplyGeneration(LecramGeneration.V11);
        Assert.True(fgPath.IsAvailable);
        Assert.Equal(["Add-on default", "Usual path", "Real frames only (test)"], fgPath.Choices.Select(c => c.Label));
        Assert.Contains("ROUTE-02", HelpTopics.Get("lc-fg-path")!.Body);
        // beta.45 renamed the preset control Model version: 1 Old, 2 New; the key and its values stayed.
        Assert.Equal(["", "0", "1", "2"], Option(options, "NRPreset").Choices.Select(c => c.Value ?? ""));
        Assert.Equal(["Add-on default", "Default", "Old (preset 1)", "New (preset 2)"], Option(options, "NRPreset").Choices.Select(c => c.Label));
    }

    [Fact]
    public void MigrateToV11_V10Section_ResetsPresentAxesAndSetsConfigVersion11_LeavingHookPointAndOtherKeys()
    {
        var text = $"[{LecramIni.Section}]\nConfigVersion=10\nNRHookPoint=2\nNRModelStyleOffset=0.25\nNRModelSkip64Gain=1.2\nNRStyle=1\n";
        var ini = IniFile.Parse(text);
        Assert.True(LecramIni.NeedsV11Migration(ini));
        Assert.True(LecramIni.MigrateToV11(ini));
        Assert.Equal("11", ini.Get(LecramIni.Section, "ConfigVersion"));
        Assert.Equal("1", ini.Get(LecramIni.Section, "NRModelCarryoverReset"));
        Assert.Equal("0", ini.Get(LecramIni.Section, "NRModelStyleOffset"));
        Assert.Equal("1", ini.Get(LecramIni.Section, "NRModelSkip64Gain"));
        Assert.Equal("2", ini.Get(LecramIni.Section, "NRHookPoint"));
        Assert.Equal("1", ini.Get(LecramIni.Section, "NRStyle"));
    }

    [Fact]
    public void MigrateToV11_AbsentConfigVersionWithNeuralUplift_IsMigrated()
    {
        var text = $"[{LecramIni.Section}]\nNeuralUplift=1\nNRHookPoint=2\nNRModelToneOffset=0.3\n";
        var ini = IniFile.Parse(text);
        Assert.True(LecramIni.NeedsV11Migration(ini));
        Assert.True(LecramIni.MigrateToV11(ini));
        Assert.Equal("11", ini.Get(LecramIni.Section, "ConfigVersion"));
        Assert.Equal("1", ini.Get(LecramIni.Section, "NRModelCarryoverReset"));
        Assert.Equal("0", ini.Get(LecramIni.Section, "NRModelToneOffset"));
        Assert.Equal("2", ini.Get(LecramIni.Section, "NRHookPoint"));
    }

    [Fact]
    public void MigrateToV11_AbsentConfigVersionWithoutNeuralUplift_IsUnchanged()
    {
        var text = $"[{LecramIni.Section}]\nNRHookPoint=1\n";
        var ini = IniFile.Parse(text);
        Assert.False(LecramIni.NeedsV11Migration(ini));
        Assert.False(LecramIni.MigrateToV11(ini));
        Assert.Equal(text, ini.ToString());
    }

    [Fact]
    public void MigrateToV11_ConfigVersion11_IsUnchanged()
    {
        var text = $"[{LecramIni.Section}]\nConfigVersion=11\nNRHookPoint=2\nNRModelStyleOffset=0.25\n";
        var ini = IniFile.Parse(text);
        Assert.False(LecramIni.NeedsV11Migration(ini));
        Assert.False(LecramIni.MigrateToV11(ini));
        Assert.Equal(text, ini.ToString());
    }

    [Fact]
    public void MigrateToV11_AbsentModelKeys_AreNotAdded()
    {
        var text = $"[{LecramIni.Section}]\nConfigVersion=10\nNRHookPoint=2\n";
        var ini = IniFile.Parse(text);
        Assert.True(LecramIni.MigrateToV11(ini));
        Assert.Equal("11", ini.Get(LecramIni.Section, "ConfigVersion"));
        Assert.Equal("1", ini.Get(LecramIni.Section, "NRModelCarryoverReset"));
        Assert.All(LecramIni.ModelAxisKeys, k => Assert.Null(ini.Get(LecramIni.Section, k)));
    }

    [Fact]
    public void ModelAxisKeys_HasAll31Axes()
    {
        Assert.Equal(31, LecramIni.ModelAxisKeys.Length);
        Assert.All(LecramIni.ModelAxisKeys, k => Assert.StartsWith("NRModel", k, StringComparison.Ordinal));
        Assert.Equal(31, LecramIni.ModelAxisKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Install_V11WithHookPointOverride_PreMigratesV10ReShadeIni_WhileV10KeepsConfigVersion10()
    {
        var v11 = _store.ImportLecram(FakeV11());
        var v10 = _store.ImportLecram(FakeV10());
        var installer = new Installer(_store);

        var v10Ini = $"[{LecramIni.Section}]\nConfigVersion=10\nNeuralUplift=1\nNRHookPoint=2\nNRModelToneOffset=0.3\n";
        File.WriteAllText(G("ReShade.ini"), v10Ini);
        var m = new InstallManifest();
        m.ReShadeIni[$"{LecramIni.Section}/NRHookPoint"] = "2";
        m.Save(_root);

        await installer.InstallAsync(_game, _root, Lec(lecramId: v11.Id, overrides: [Key("NRHookPoint", "1")]), null, default);

        Assert.Equal("1", Value("NRHookPoint"));
        Assert.Equal("11", Value("ConfigVersion"));
        Assert.Equal("1", Value("NRModelCarryoverReset"));
        Assert.Equal("0", Value("NRModelToneOffset"));

        File.WriteAllText(G("ReShade.ini"), v10Ini);
        m = new InstallManifest();
        m.ReShadeIni[$"{LecramIni.Section}/NRHookPoint"] = "2";
        m.Save(_root);

        await installer.InstallAsync(_game, _root, Lec(lecramId: v10.Id, overrides: [Key("NRHookPoint", "1")]), null, default);

        Assert.Equal("1", Value("NRHookPoint"));
        Assert.Equal("10", Value("ConfigVersion"));
        Assert.Null(Value("NRModelCarryoverReset"));
        Assert.Equal("0.3", Value("NRModelToneOffset"));
    }

    [Fact]
    public async Task Install_V11WithoutHookPointOverride_LeavesFileForV11sOwnMigration()
    {
        var v11 = _store.ImportLecram(FakeV11());
        var installer = new Installer(_store);

        var v10Ini = $"[{LecramIni.Section}]\nConfigVersion=10\nNeuralUplift=1\nNRHookPoint=2\nNRModelToneOffset=0.3\n";
        File.WriteAllText(G("ReShade.ini"), v10Ini);

        await installer.InstallAsync(_game, _root, Lec(lecramId: v11.Id), null, default);

        Assert.Equal("10", Value("ConfigVersion"));
        Assert.Null(Value("NRModelCarryoverReset"));
        Assert.Equal("0.3", Value("NRModelToneOffset"));
        Assert.Equal("2", Value("NRHookPoint"));
    }
}
