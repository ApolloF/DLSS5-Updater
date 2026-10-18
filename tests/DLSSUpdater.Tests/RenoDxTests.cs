using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>The imported ShortFuse renodx-dlss add-on: import checks, ReShade-only install, conflicting NR injectors.</summary>
public sealed class RenoDxTests : IDisposable
{
    private const string Dlss5 = "renodx-dlss5.addon64";
    private const string Fix = "renodx-dlssfix.addon64";
    private static readonly Version AddonVersion = new(0, 2026, 1016, 1704);

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-reno-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly ComponentStore _store = new(new GitHubClient(), () => true);
    private readonly NrVersion _nr;
    private readonly GameInfo _game;

    public RenoDxTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(_root);
        FakeVersionDll.Write(_store.ReShadePath, new Version(6, 8, 0, 0), ComponentStore.ReShadeFile);
        var nr = Path.Combine(_tmp, "nr.dll");
        FakeVersionDll.Write(nr, new Version(310, 9, 16, 0), ComponentStore.DlssNrFile);
        _nr = _store.ImportDlssNr(nr);
        _game = new GameInfo { Id = _root, Root = _root, Name = "Reno game", Dlss = [new DlssDll { Name = "nvngx_dlss.dll" }] };

        var pkg = Path.Combine(ComponentStore.TagDir(Component.OptiScaler, "v9.9.9"), "pkg");
        Directory.CreateDirectory(pkg);
        File.WriteAllText(Path.Combine(pkg, "OptiScaler.dll"), "OPTI");
        File.WriteAllText(Path.Combine(pkg, "OptiScaler.ini"), "[DLSSG]\nAdaMfgUnlock=auto\n[DlssNr]\nEnabled=auto\n");
        var release = new ReleaseInfo { Tag = "v9.9.9" };
        ComponentStore.MarkComplete(Component.OptiScaler, release);
        _store.Opti = release;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private string Addon(string fileName, string original, Version? version = null)
    {
        var path = Path.Combine(_tmp, "src-" + Guid.NewGuid().ToString("N")[..6], fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FakeVersionDll.Write(path, version ?? AddonVersion, original);
        return path;
    }

    private AddonBuild ImportGood() => _store.ImportRenoDx(Addon("renodx-dlss.addon64", ComponentStore.RenoDxFile));

    private string G(string name) => Path.Combine(_root, name);
    private IniFile Ini() => IniFile.Load(G("ReShade.ini"));
    private Dictionary<string, string> Snapshot() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(_root, f), FileUtil.Sha256);

    private InstallOptions Reno(bool renoDx = true) => new()
    {
        Mode = InstallMode.ReShadeOnly, ReShade = true, RenoDx = renoDx, DlssNr = renoDx, DlssNrId = _nr.Id, Proxy = "dxgi.dll",
        ReShadeOverrides = [new("RENODX-DLSS", "DirectNeuralRenderingHookStage", "1"), new(RenoDxIni.PresetSection, "DirectNeuralRenderingPassCount", "2")],
    };

    [Theory]
    [InlineData(true, false, "3", "old")]
    [InlineData(true, true, "1", "old")]
    [InlineData(false, false, "1", null)]
    public async Task IniModes_ResetOnlyInstalledAddonSections_AndBackUpEachFreshRun(bool carryOver, bool overwrite, string hook, string? junk)
    {
        ImportGood();
        const string original = "[GENERAL]\nPresetPath=mine.ini\n[OTHER]\nSetting=keep\n[RENODX-DLSS]\nDirectNeuralRenderingHookStage=3\nJunk=old\n[RENODX-DLSS-preset1]\nDirectNeuralRenderingPassCount=4\nJunk=old\n[RenoDX.DLSS5]\nNRStyle=2\n[Uplift]\nStyle=2\n";
        File.WriteAllText(G("ReShade.ini"), original);
        var installer = new Installer(_store);
        var options = Reno() with { CarryOverIni = carryOver, OverwriteIni = overwrite };
        await installer.InstallAsync(_game, _root, options, null, default);
        var ini = Ini();
        Assert.Equal(hook, ini.Get(RenoDxIni.Section, "DirectNeuralRenderingHookStage"));
        Assert.Equal(carryOver && !overwrite ? "4" : "2", ini.Get(RenoDxIni.PresetSection, "DirectNeuralRenderingPassCount"));
        Assert.Equal(junk, ini.Get(RenoDxIni.Section, "Junk"));
        Assert.Equal(junk, ini.Get(RenoDxIni.PresetSection, "Junk"));
        Assert.Equal("mine.ini", ini.Get("GENERAL", "PresetPath"));
        Assert.Equal("keep", ini.Get("OTHER", "Setting"));
        Assert.Equal("2", ini.Get(LecramIni.Section, "NRStyle"));
        Assert.Equal("2", ini.Get(UpliftConfig.Section, "Style"));
        var m = InstallManifest.Read(_root)!;
        var backup = Assert.Single(m.Backups, b => b.Original == "ReShade.ini");
        Assert.Equal(original, File.ReadAllText(Path.Combine(InstallManifest.BackupDirFor(_root), backup.Backup)));
        Assert.All(m.ReShadeIni, entry =>
        {
            var parts = entry.Key.Split('/');
            Assert.Equal(entry.Value, ini.Get(parts[0], parts[1]));
        });
        if (carryOver) return;

        var edited = Ini();
        edited.Set(RenoDxIni.Section, "Junk", "new tuning");
        edited.Save(G("ReShade.ini"));
        var beforeReset = File.ReadAllText(G("ReShade.ini"));
        m.ReShadeIni[$"{RenoDxIni.Section}/Junk"] = "new tuning";
        m.Save(_root);
        await installer.InstallAsync(_game, _root, options, null, default);
        m = InstallManifest.Read(_root)!;
        backup = Assert.Single(m.Backups, b => b.Original == "ReShade.ini");
        Assert.Equal(beforeReset, File.ReadAllText(Path.Combine(InstallManifest.BackupDirFor(_root), backup.Backup)));
        Assert.Null(Ini().Get(RenoDxIni.Section, "Junk"));
        Assert.DoesNotContain($"{RenoDxIni.Section}/Junk", m.ReShadeIni.Keys);
        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal(beforeReset, File.ReadAllText(G("ReShade.ini")));
    }

    [Fact]
    public void Import_AcceptsOnlyTheRenoDxDlssAddon()
    {
        Assert.False(_store.RenoDxImported);
        var fix = Assert.Throws<InvalidDataException>(() => _store.ImportRenoDx(Addon(Fix, Fix)));
        Assert.Contains(Fix, fix.Message);
        var five = Assert.Throws<InvalidDataException>(() => _store.ImportRenoDx(Addon(Dlss5, Dlss5)));
        Assert.Contains(Dlss5, five.Message);
        Assert.Throws<InvalidDataException>(() => _store.ImportRenoDx(Addon("other.addon64", "other.addon64")));
        var text = Path.Combine(_tmp, "text.addon64");
        File.WriteAllText(text, "not a PE file");
        Assert.Throws<InvalidDataException>(() => _store.ImportRenoDx(text));
        Assert.False(_store.RenoDxImported);

        var good = Addon("renamed.bin", "RenoDX-DLSS.ADDON64");
        _store.Import(good, ComponentStore.RenoDxFile);
        Assert.True(_store.RenoDxImported);
        var build = Assert.Single(_store.RenoDxBuilds);
        Assert.Equal("0.2026.1016.1704", build.FileVersion);
        Assert.Equal("0.2026.1016.1704", build.DetectedLabel);
        Assert.Equal("0.2026.1016.1704-" + FileUtil.Sha256(good)[..12], build.Tag);
        Assert.Equal(ComponentStore.RenoDxFile, Path.GetFileName(ComponentStore.AddonPathOf(AddonKind.RenoDx, build)));

        _store.RemoveAddon(AddonKind.RenoDx, build.Id);
        Assert.False(_store.RenoDxImported);
        Assert.Null(_store.RenoDxBuildFor(null));
    }

    [Fact]
    public async Task ReShadeOnly_PlacesAddonEarlyLoadAndSettings_OnlyWhenEnabled_AndUninstallRestores()
    {
        var build = ImportGood();
        File.WriteAllText(G("ReShade.ini"), "[ADDON]\nLoadFromDllMain=other.addon64\n[OTHER]\nSetting=keep\n");
        var before = File.ReadAllText(G("ReShade.ini"));
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Reno(renoDx: false), null, default);
        var plain = Ini();
        Assert.Null(plain.Get("RENODX-DLSS", "DirectNeuralRenderingHookStage"));
        Assert.Equal("other.addon64", plain.Get("ADDON", "LoadFromDllMain"));
        Assert.False(File.Exists(G(ComponentStore.RenoDxFile)));

        await installer.InstallAsync(_game, _root, Reno(), null, default);
        var ini = Ini();
        Assert.True(File.Exists(G(ComponentStore.RenoDxFile)));
        Assert.Equal("other.addon64," + ComponentStore.RenoDxFile, ini.Get("ADDON", "LoadFromDllMain"));
        Assert.Equal("1", ini.Get("RENODX-DLSS", "DirectNeuralRenderingHookStage"));
        Assert.Equal("2", ini.Get(RenoDxIni.PresetSection, "DirectNeuralRenderingPassCount"));
        Assert.Null(ini.Get("RenoDX.DLSS5", "NRHookPoint"));
        var m = InstallManifest.Read(_root)!;
        Assert.True(m.RenoDx);
        Assert.Equal(build.Tag, m.RenoDxTag);
        Assert.Equal(build.Id, m.RenoDxId);

        await installer.InstallAsync(_game, _root, Reno(), null, default);
        Assert.Equal("other.addon64," + ComponentStore.RenoDxFile, Ini().Get("ADDON", "LoadFromDllMain"));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal(before, File.ReadAllText(G("ReShade.ini")));
        Assert.False(File.Exists(G(ComponentStore.RenoDxFile)));
    }

    [Fact]
    public async Task PerPresetKeyWrittenToTheGlobalSection_MovesToPreset1_UnlessTheUserChangedIt()
    {
        ImportGood();
        var installer = new Installer(_store);
        var old = Reno() with
        {
            ReShadeOverrides =
            [
                new(RenoDxIni.Section, "DirectNeuralRenderingPassCount", "2"),
                new(RenoDxIni.Section, "DirectNeuralRenderingIntensity", "1.5"),
            ],
        };
        await installer.InstallAsync(_game, _root, old, null, default);
        var tuned = Ini();
        tuned.Set(RenoDxIni.Section, "DirectNeuralRenderingIntensity", "0.8");
        tuned.Save(G("ReShade.ini"));

        await installer.InstallAsync(_game, _root, Reno(), null, default);

        var ini = Ini();
        Assert.Null(ini.Get(RenoDxIni.Section, "DirectNeuralRenderingPassCount"));
        Assert.Equal("2", ini.Get(RenoDxIni.PresetSection, "DirectNeuralRenderingPassCount"));
        Assert.Equal("0.8", ini.Get(RenoDxIni.Section, "DirectNeuralRenderingIntensity"));
        Assert.Equal("1", ini.Get(RenoDxIni.Section, "DirectNeuralRenderingHookStage"));
        var m = InstallManifest.Read(_root)!;
        Assert.DoesNotContain(m.ReShadeIni.Keys, k => k.StartsWith(RenoDxIni.Section + "/DirectNeuralRenderingIntensity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Uninstall_RemovesWhatWasWrittenToBothSections()
    {
        ImportGood();
        File.WriteAllText(G("ReShade.ini"), "[OTHER]\nSetting=keep\n");
        var before = File.ReadAllText(G("ReShade.ini"));
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Reno(), null, default);
        Assert.Equal("2", Ini().Get(RenoDxIni.PresetSection, "DirectNeuralRenderingPassCount"));

        var opti = new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true, ReShade = true, Proxy = "dxgi.dll" };
        await installer.InstallAsync(_game, _root, opti, null, default);
        Assert.Null(Ini().Get(RenoDxIni.PresetSection, "DirectNeuralRenderingPassCount"));
        Assert.Null(Ini().Get(RenoDxIni.Section, "DirectNeuralRenderingHookStage"));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal(before, File.ReadAllText(G("ReShade.ini")));
    }

    [Fact]
    public void Options_UseTheSectionTheAddonReadsEachKeyFrom()
    {
        var options = ViewModels.SettingsOptions.RenoDx([]);
        Assert.All(options, o => Assert.Equal(RenoDxIni.SectionOf(o.Key), o.Section));
        Assert.Contains(options, o => o.Section == RenoDxIni.PresetSection);
        Assert.Contains(options, o => o.Section == RenoDxIni.Section);
        Assert.All(ConfigProfile.RecommendedReShade().Where(RenoDxIni.IsRenoDx), o => Assert.Equal(RenoDxIni.SectionOf(o.Key), o.Section));

        // Ranges and label counts from the add-on's Setting constructors (0928 / 1016).
        double[] Values(string key) => options.Single(o => o.Key == "DirectNeuralRendering" + key).Choices
            .Where(c => c.Value is not null).Select(c => double.Parse(c.Value!, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        foreach (var key in new[] { "Intensity", "LocalStructureStrength", "LocalToneStrength", "SkinStructureStrength" })
            Assert.All(Values(key), v => Assert.InRange(v, 0, 1));
        Assert.All(Values("PassCount"), v => Assert.InRange(v, 1, 10));
        Assert.All(Values("ProcessingScale"), v => Assert.InRange(v, 0, 200));
        Assert.Equal([0, 1, 2, 3, 4, 5], Values("Encoding"));
        Assert.Equal([0, 1, 2, 3], Values("HookStage"));
        Assert.Equal([0, 1, 2], Values("Preset"));
        Assert.Equal([0, 1, 2], Values("ReuseDlssUpscalingResources"));
    }

    [Fact]
    public void Migrate_MovesPerPresetKeys_KeepsGlobals_AndPrefersAnExistingPreset1Value()
    {
        List<IniOverride> list =
        [
            new(RenoDxIni.Section, "DirectNeuralRenderingHookStage", "1"),
            new(RenoDxIni.Section, "DirectNeuralRenderingPassCount", "2"),
            new(RenoDxIni.Section, "DirectNeuralRenderingStyle", "1"),
            new(RenoDxIni.PresetSection, "DirectNeuralRenderingStyle", "2"),
            new("OVERLAY", "TutorialProgress", "4"),
        ];
        Assert.True(RenoDxIni.Migrate(list));
        Assert.Equal(
            ["RENODX-DLSS/DirectNeuralRenderingHookStage", "RENODX-DLSS-preset1/DirectNeuralRenderingPassCount",
             "RENODX-DLSS-preset1/DirectNeuralRenderingStyle", "OVERLAY/TutorialProgress"],
            list.Select(o => o.Id));
        Assert.Equal("2", list.Single(o => o.Key == "DirectNeuralRenderingStyle").Value);
        Assert.False(RenoDxIni.Migrate(list));
    }

    [Fact]
    public void Migrate_CoversPresetsAndGameLayers_AndLoadSavesTheResult()
    {
        IniOverride Old() => new(RenoDxIni.Section, "DirectNeuralRenderingIntensity", "1.25");
        var s = new AppSettings { ReShadeOverrides = [Old()], Presets = [new ConfigPreset { Name = "Mine", ReShade = [Old()] }] };
        s.For("g").ReShade = [Old()];
        s.For("g").Presets = [new ConfigPreset { Name = "Game", ReShade = [Old()] }];
        s.Save();

        var loaded = AppSettings.Load();
        var all = loaded.ReShadeOverrides.Concat(loaded.Presets[0].ReShade)
            .Concat(loaded.Games["g"].ReShade!).Concat(loaded.Games["g"].Presets![0].ReShade).ToList();
        Assert.Equal(4, all.Count);
        Assert.All(all, o => Assert.Equal(RenoDxIni.PresetSection, o.Section));
        Assert.Contains(RenoDxIni.PresetSection, File.ReadAllText(AppPaths.SettingsFile));
    }

    [Fact]
    public async Task RenoDx_WithoutImport_FailsWithClearMessage_AndTouchesNothing()
    {
        var before = Snapshot();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => new Installer(_store).InstallAsync(_game, _root, Reno(), null, default));
        Assert.Contains("Import renodx-dlss.addon64 in Settings → Local components first.", error.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task RenoDx_InOptiScalerMode_Throws()
    {
        ImportGood();
        var before = Snapshot();
        var o = Reno() with { Mode = InstallMode.OptiScaler, Opti = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Installer(_store).InstallAsync(_game, _root, o, null, default));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task LegacyDlssfixAndStaleKeys_AreRemoved_UserEditsKept()
    {
        ImportGood();
        File.WriteAllText(G(Fix), "OLD-FIX");
        File.WriteAllText(G("ReShade.ini"),
            "[ADDON]\nLoadFromDllMain=other.addon64," + Fix + "\n[RenoDX.DLSS5]\nNRHookPoint=1\nNRPasses=3\nNRDetailStability=2\n");
        Directory.CreateDirectory(InstallManifest.DirFor(_root));
        new InstallManifest
        {
            RootRel = ".", Mode = InstallMode.ReShadeOnly, Proxy = "dxgi.dll", ReShade = true, RenoDx = true, RenoDxTag = "snapshot-old",
            Files = [Fix],
            ReShadeIni = new(StringComparer.OrdinalIgnoreCase)
            {
                ["RenoDX.DLSS5/NRHookPoint"] = "1", ["RenoDX.DLSS5/NRPasses"] = "1", ["RenoDX.DLSS5/NRDetailStability"] = "2",
            },
        }.Save(_root);

        await new Installer(_store).InstallAsync(_game, _root, Reno(), null, default);

        Assert.False(File.Exists(G(Fix)));
        Assert.True(File.Exists(G(ComponentStore.RenoDxFile)));
        var ini = Ini();
        Assert.Equal("other.addon64," + ComponentStore.RenoDxFile, ini.Get("ADDON", "LoadFromDllMain"));
        Assert.Null(ini.Get("RenoDX.DLSS5", "NRHookPoint"));
        Assert.Null(ini.Get("RenoDX.DLSS5", "NRDetailStability"));
        Assert.Equal("3", ini.Get("RenoDX.DLSS5", "NRPasses")); // the user changed it, so it stays
        var m = InstallManifest.Read(_root)!;
        Assert.DoesNotContain(m.ReShadeIni.Keys, k => k.StartsWith("RenoDX.DLSS5/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Fix, m.Files);
    }

    [Fact]
    public async Task ForeignNrAddons_AreDisplaced_AndRestoredOnUninstall()
    {
        ImportGood();
        File.WriteAllText(G(Dlss5), "FOREIGN-5");
        File.WriteAllText(G(Fix), "FOREIGN-FIX");
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Reno(), null, default);
        Assert.False(File.Exists(G(Dlss5)));
        Assert.False(File.Exists(G(Fix)));
        Assert.True(File.Exists(G(ComponentStore.RenoDxFile)));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal("FOREIGN-5", File.ReadAllText(G(Dlss5)));
        Assert.Equal("FOREIGN-FIX", File.ReadAllText(G(Fix)));
        Assert.False(File.Exists(G(ComponentStore.RenoDxFile)));
    }

    [Fact]
    public async Task SwitchingToOptiScaler_RemovesTheAddonAndOnlyItsEarlyLoadEntry()
    {
        ImportGood();
        File.WriteAllText(G("ReShade.ini"), "[ADDON]\nLoadFromDllMain=other.addon64\n");
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Reno(), null, default);
        Assert.True(File.Exists(G(ComponentStore.RenoDxFile)));

        var opti = new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true, ReShade = true, Proxy = "dxgi.dll" };
        await installer.InstallAsync(_game, _root, opti, null, default);

        Assert.False(File.Exists(G(ComponentStore.RenoDxFile)));
        var ini = Ini();
        Assert.Equal("other.addon64", ini.Get("ADDON", "LoadFromDllMain"));
        Assert.Null(ini.Get("RENODX-DLSS", "DirectNeuralRenderingHookStage"));
        var m = InstallManifest.Read(_root)!;
        Assert.False(m.RenoDx);
        Assert.Null(m.RenoDxTag);
        Assert.Equal("OPTI", File.ReadAllText(G("dxgi.dll")));
    }

    [Fact]
    public async Task OptiScalerNr_DisplacesAForeignRenoDxDlss_AndUninstallRestoresIt()
    {
        File.WriteAllText(G(ComponentStore.RenoDxFile), "USERS-OWN");
        File.WriteAllText(G(Dlss5), "USERS-DLSS5");
        var installer = new Installer(_store);
        var opti = new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true, DlssNr = true, DlssNrId = _nr.Id, Proxy = "dxgi.dll" };

        await installer.InstallAsync(_game, _root, opti, null, default);
        Assert.False(File.Exists(G(ComponentStore.RenoDxFile)));
        Assert.False(File.Exists(G(Dlss5)));
        Assert.True(File.Exists(G(ComponentStore.DlssNrFile)));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal("USERS-OWN", File.ReadAllText(G(ComponentStore.RenoDxFile)));
        Assert.Equal("USERS-DLSS5", File.ReadAllText(G(Dlss5)));
    }

    [Theory]
    [InlineData("AdaMfgUnlock", null)]
    [InlineData("AmpereMfgUnlock", null)]
    [InlineData(null, "AmpereMfgUnlock")]
    public async Task BuiltInMfgUnlock_WithTheMfgAddon_ThrowsBeforeAnyFileChanges(string? overrideKey, string? carriedKey)
    {
        if (carriedKey is not null) File.WriteAllText(G("OptiScaler.ini"), $"[DLSSG]\n{carriedKey}=true\n");
        File.WriteAllText(G("other.txt"), "untouched");
        var before = Snapshot();
        var o = new InstallOptions
        {
            Mode = InstallMode.OptiScaler, Opti = true, Mfg = true, Proxy = "dxgi.dll", CarryOverIni = true,
            Overrides = overrideKey is null ? [] : [new("DLSSG", overrideKey, "true")],
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new Installer(_store).InstallAsync(_game, _root, o, null, default));
        Assert.Contains("MFG Unlock", error.Message);
        Assert.Equal(before, Snapshot());
        Assert.False(Directory.Exists(InstallManifest.DirFor(_root)));
    }

    [Fact]
    public async Task MfgOnlyUpdate_NextToOptiWithBuiltInUnlock_Throws()
    {
        File.WriteAllText(G("OptiScaler.ini"), "[DLSSG]\nAdaMfgUnlock=true\n");
        var before = Snapshot();
        var o = new InstallOptions { Mode = InstallMode.OptiScaler, Mfg = true, Proxy = "dxgi.dll", CarryOverIni = false };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new Installer(_store).InstallAsync(_game, _root, o, null, default));
        Assert.Equal(before, Snapshot());
    }
}
