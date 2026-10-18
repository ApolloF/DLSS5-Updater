using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>The imported Deep Fried Chicken add-on: import checks, ReShade-only install, cfg kept, one NR provider per game. Fake files only.</summary>
public sealed class DfcTests : IDisposable
{
    private static readonly Version AddonVersion = new(2, 0, 0, 0);
    private const string DefaultCfg = "config_schema=13\narm=1\nenabled=1\npasses=1.0\nneural_work_percent=100\nnative_vulkan_enabled=0\ncolor_strength=1.000\n";

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-dfc-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly ComponentStore _store = new(new GitHubClient(), () => true);
    private readonly NrVersion _nr;
    private readonly GameInfo _game;

    public DfcTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(_root);
        FakeVersionDll.Write(_store.ReShadePath, new Version(6, 8, 0, 0), ComponentStore.ReShadeFile);
        var nr = Path.Combine(_tmp, "nr.dll");
        FakeVersionDll.Write(nr, new Version(310, 9, 16, 0), ComponentStore.DlssNrFile);
        _nr = _store.ImportDlssNr(nr);
        _game = new GameInfo { Id = _root, Root = _root, Name = "Dfc game", Dlss = [new DlssDll { Name = "nvngx_dlss.dll" }] };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    /// <summary>A release folder with the add-on (version resource), a bridge without one and, optionally, the default cfg.</summary>
    private string Release(string original = ComponentStore.DfcFile, bool bridge = true, bool cfg = true, string cfgText = DefaultCfg)
    {
        var dir = Path.Combine(_tmp, "rel-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        FakeVersionDll.Write(Path.Combine(dir, ComponentStore.DfcFile), AddonVersion, original);
        if (bridge) File.WriteAllText(Path.Combine(dir, ComponentStore.DfcBridgeFile), "BRIDGE");
        if (cfg) File.WriteAllText(Path.Combine(dir, ComponentStore.DfcConfigFile), cfgText);
        return dir;
    }

    private const string V4Cfg = DefaultCfg + "preserve_native_tone_color=0\nclean_fry_enabled=0\noutput_hud_enabled=0\noutput_highlights_enabled=0\noutput_deband_enabled=0\n";
    private static readonly string[] NeuralFiles = ["dfc-neural-helper64.exe", "dfc-neural-motion.onnx", "deep-fried-chicken-ort.dll", "DirectML.dll"];
    private static readonly string Effect = Path.Combine("reshade-shaders", "Shaders", "DFC_OutputState.addonfx");

    /// <summary>The 4.x package layout: INSTALL.txt, a 64-bit folder with the whole set and a 32-bit folder with its host64 companion set.</summary>
    private string Package(bool presentSupport = true, bool neural = true)
    {
        var root = Path.Combine(_tmp, "pkg-" + Guid.NewGuid().ToString("N")[..6]);
        var x64 = Path.Combine(root, "64-bit");
        Directory.CreateDirectory(x64);
        File.WriteAllText(Path.Combine(root, "INSTALL.txt"), "Deep Fried Chicken 4.0.6 preview\n");
        FakeVersionDll.Write(Path.Combine(x64, ComponentStore.DfcFile), new Version(4, 0, 0, 0), ComponentStore.DfcFile);
        File.WriteAllText(Path.Combine(x64, ComponentStore.DfcBridgeFile), "BRIDGE4");
        File.WriteAllText(Path.Combine(x64, ComponentStore.DfcConfigFile), V4Cfg);
        File.WriteAllText(Path.Combine(x64, "notes.txt"), "not part of the set");
        if (presentSupport) File.WriteAllText(Path.Combine(x64, ComponentStore.DfcPresentSupportFile), "PRESENT");
        if (neural)
            foreach (var name in NeuralFiles)
            {
                var f = Path.Combine(x64, ComponentStore.DfcNeuralDir, name);
                Directory.CreateDirectory(Path.GetDirectoryName(f)!);
                File.WriteAllText(f, "NEURAL-" + name);
            }
        Directory.CreateDirectory(Path.Combine(x64, "reshade-shaders", "Shaders"));
        File.WriteAllText(Path.Combine(x64, Effect), "technique DFC_OutputState {}");
        var host = Path.Combine(root, "32-bit", "host64");
        Directory.CreateDirectory(host);
        File.WriteAllText(Path.Combine(root, "32-bit", "deep-fried-chicken.addon32"), "ADDON32");
        FakeVersionDll.Write(Path.Combine(host, ComponentStore.DfcFile), new Version(4, 0, 0, 0), ComponentStore.DfcFile);
        File.WriteAllText(Path.Combine(host, ComponentStore.DfcBridgeFile), "BRIDGE4");
        File.WriteAllText(Path.Combine(host, ComponentStore.DfcHost64Exe), "HOST");
        return root;
    }

    /// <summary>Paths of the 64-bit set relative to the game folder, the cfg excluded.</summary>
    private static string[] V4Set() =>
    [
        ComponentStore.DfcFile, ComponentStore.DfcBridgeFile, ComponentStore.DfcPresentSupportFile, Effect,
        .. NeuralFiles.Select(n => Path.Combine(ComponentStore.DfcNeuralDir, n)),
    ];

    private string RenoDxAddon()
    {
        var path = Path.Combine(_tmp, "reno-src", ComponentStore.RenoDxFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FakeVersionDll.Write(path, new Version(0, 2026, 1016, 1704), ComponentStore.RenoDxFile);
        return path;
    }

    private string G(string name) => Path.Combine(_root, name);
    private Dictionary<string, string> Snapshot() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(_root, f), FileUtil.Sha256);

    private InstallOptions Dfc() => new()
    {
        Mode = InstallMode.ReShadeOnly, ReShade = true, Dfc = true, DlssNr = true, DlssNrId = _nr.Id, Proxy = "dxgi.dll",
    };

    private InstallOptions Reno() => new()
    {
        Mode = InstallMode.ReShadeOnly, ReShade = true, RenoDx = true, DlssNr = true, DlssNrId = _nr.Id, Proxy = "dxgi.dll",
    };

    [Theory]
    [InlineData(true, false, "4.0", "old")]
    [InlineData(true, true, "2.0", "old")]
    [InlineData(false, false, "2.0", null)]
    public async Task IniModes_FreshRebuildsImportedDefaultsPlusOptions_AndBacksUpEachReset(bool carryOver, bool overwrite, string passes, string? junk)
    {
        _store.ImportDfc(Release());
        const string original = "config_schema=12\npasses=4.0\ncolor_strength=0.2\njunk=old\n";
        File.WriteAllText(G(ComponentStore.DfcConfigFile), original);
        const string reshade = "[GENERAL]\nPresetPath=mine.ini\n[OTHER]\nSetting=keep\n";
        File.WriteAllText(G("ReShade.ini"), reshade);
        var options = DfcWith(O("passes", "2.0")) with { CarryOverIni = carryOver, OverwriteIni = overwrite };
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, options, null, default);
        var cfg = Cfg(G(ComponentStore.DfcConfigFile));
        Assert.Equal(passes, cfg["passes"]);
        Assert.Equal(junk, cfg.GetValueOrDefault("junk"));
        Assert.Equal(reshade, File.ReadAllText(G("ReShade.ini")));
        var m = InstallManifest.Read(_root)!;
        Assert.All(m.DfcCfg, entry => Assert.Equal(entry.Value, cfg[entry.Key.Split('/')[1]]));
        if (carryOver && !overwrite)
        {
            Assert.Empty(m.DfcCfg);
            Assert.DoesNotContain(m.Backups, b => b.Original == ComponentStore.DfcConfigFile);
            return;
        }
        var backup = Assert.Single(m.Backups, b => b.Original == ComponentStore.DfcConfigFile);
        Assert.Equal(original, File.ReadAllText(Path.Combine(InstallManifest.BackupDirFor(_root), backup.Backup)));
        if (carryOver) return;
        Assert.Equal(DefaultCfg.Replace("passes=1.0", "passes=2.0"), File.ReadAllText(G(ComponentStore.DfcConfigFile)));
        var beforeReset = File.ReadAllText(G(ComponentStore.DfcConfigFile)) + "junk=new tuning\n";
        File.WriteAllText(G(ComponentStore.DfcConfigFile), beforeReset);
        m.DfcCfg["DFC/junk"] = "new tuning";
        m.Save(_root);
        await installer.InstallAsync(_game, _root, options with { ReShadeOverrides = [] }, null, default);
        Assert.Equal(DefaultCfg, File.ReadAllText(G(ComponentStore.DfcConfigFile)));
        m = InstallManifest.Read(_root)!;
        Assert.Empty(m.DfcCfg);
        backup = Assert.Single(m.Backups, b => b.Original == ComponentStore.DfcConfigFile);
        Assert.Equal(beforeReset, File.ReadAllText(Path.Combine(InstallManifest.BackupDirFor(_root), backup.Backup)));
        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal(beforeReset, File.ReadAllText(G(ComponentStore.DfcConfigFile)));
    }

    [Fact]
    public async Task FreshConfig_WithoutProfileOrPreviousWrittenKeys_StillResetsOwnedCfg()
    {
        _store.ImportDfc(Release());
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        const string original = "passes=8\njunk=old\n";
        File.WriteAllText(G(ComponentStore.DfcConfigFile), original);
        await installer.InstallAsync(_game, _root, Dfc() with { CarryOverIni = false }, null, default);
        Assert.Equal(DefaultCfg, File.ReadAllText(G(ComponentStore.DfcConfigFile)));
        var m = InstallManifest.Read(_root)!;
        Assert.Empty(m.DfcCfg);
        var backup = Assert.Single(m.Backups, b => b.Original == ComponentStore.DfcConfigFile);
        Assert.Equal(original, File.ReadAllText(Path.Combine(InstallManifest.BackupDirFor(_root), backup.Backup)));
    }

    [Fact]
    public void Import_ValidatesTheAddonAndBridge_FromFileOrFolder()
    {
        Assert.False(_store.DfcImported);
        var error = Assert.Throws<InvalidDataException>(() => _store.ImportDfc(Release(original: "renodx-dlss.addon64")));
        Assert.Contains("renodx-dlss.addon64", error.Message);
        Assert.Throws<InvalidDataException>(() => _store.ImportDfc(Release(bridge: false)));
        Assert.Throws<InvalidDataException>(() => _store.ImportDfc(Path.Combine(_tmp, "no-such-folder")));
        var text = Path.Combine(_tmp, "text.addon64");
        File.WriteAllText(text, "not a PE file");
        Assert.Throws<InvalidDataException>(() => _store.ImportDfc(text));
        Assert.False(_store.DfcImported);

        var folder = Release();
        _store.ImportDfc(folder);
        Assert.True(_store.DfcImported);
        Assert.Equal("2.0.0", _store.DfcVersion);
        var addon = Path.Combine(folder, ComponentStore.DfcFile);
        Assert.Equal("2.0.0-" + FileUtil.Sha256(addon)[..12], _store.DfcTag);
        Assert.Equal(FileUtil.Sha256(addon), FileUtil.Sha256(_store.DfcPath));
        Assert.Equal("BRIDGE", File.ReadAllText(_store.DfcBridgePath));
        Assert.True(File.Exists(_store.DfcConfigPath));

        _store.RemoveDfcImport();
        Assert.False(_store.DfcImported);
        Assert.Null(_store.DfcTag);

        _store.Import(Path.Combine(Release(cfg: false), ComponentStore.DfcFile), ComponentStore.DfcFile);
        Assert.True(_store.DfcImported);
        Assert.False(File.Exists(_store.DfcConfigPath));
    }

    [Fact]
    public async Task Install_PlacesAddonBridgeAndCfg_UnchangedAndUninstallRemovesThem()
    {
        var folder = Release();
        _store.ImportDfc(folder);
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Dfc(), null, default);

        foreach (var name in new[] { ComponentStore.DfcFile, ComponentStore.DfcBridgeFile, ComponentStore.DfcConfigFile })
            Assert.Equal(FileUtil.Sha256(Path.Combine(folder, name)), FileUtil.Sha256(G(name)));
        var m = InstallManifest.Read(_root)!;
        Assert.True(m.Dfc);
        Assert.Equal(_store.DfcTag, m.DfcTag);
        Assert.False(File.Exists(G("ReShade.ini")));

        await installer.UninstallAsync(_game, _root, default);
        Assert.False(File.Exists(G(ComponentStore.DfcFile)));
        Assert.False(File.Exists(G(ComponentStore.DfcBridgeFile)));
        Assert.False(File.Exists(G(ComponentStore.DfcConfigFile)));
    }

    [Fact]
    public async Task UserCfg_SurvivesReinstallAndUpdate_AndAForeignCfgIsNeverTaken()
    {
        _store.ImportDfc(Release());
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        File.WriteAllText(G(ComponentStore.DfcConfigFile), "tuned=1\n");

        _store.ImportDfc(Release(cfgText: "config_schema=14\n"));
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.Equal("tuned=1\n", File.ReadAllText(G(ComponentStore.DfcConfigFile)));
        Assert.Equal(FileUtil.Sha256(_store.DfcPath), FileUtil.Sha256(G(ComponentStore.DfcFile)));

        await installer.UninstallAsync(_game, _root, default);
        File.WriteAllText(G(ComponentStore.DfcConfigFile), "users-own\n");
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.Equal("users-own\n", File.ReadAllText(G(ComponentStore.DfcConfigFile)));
        Assert.DoesNotContain(ComponentStore.DfcConfigFile, InstallManifest.Read(_root)!.Files);
        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal("users-own\n", File.ReadAllText(G(ComponentStore.DfcConfigFile)));
    }

    [Fact]
    public async Task Dfc_WithoutImport_FailsWithClearMessage_AndTouchesNothing()
    {
        var before = Snapshot();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => new Installer(_store).InstallAsync(_game, _root, Dfc(), null, default));
        Assert.Contains("Import deep-fried-chicken.addon64 in Settings → Local components first.", error.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Dfc_NeedsReShadeOnlyMode_AndExcludesRenoDx()
    {
        _store.ImportDfc(Release());
        _store.ImportRenoDx(RenoDxAddon());
        var before = Snapshot();
        var installer = new Installer(_store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(_game, _root, Dfc() with { Mode = InstallMode.OptiScaler, Opti = true }, null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(_game, _root, Dfc() with { RenoDx = true }, null, default));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task SwitchingBetweenDfcAndRenoDx_NeverLeavesBoth()
    {
        _store.ImportDfc(Release());
        _store.ImportRenoDx(RenoDxAddon());
        File.WriteAllText(G(ComponentStore.RenoDxFile), "USERS-RENODX");
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.True(File.Exists(G(ComponentStore.DfcFile)));
        Assert.False(File.Exists(G(ComponentStore.RenoDxFile)));

        await installer.InstallAsync(_game, _root, Reno(), null, default);
        Assert.True(File.Exists(G(ComponentStore.RenoDxFile)));
        Assert.False(File.Exists(G(ComponentStore.DfcFile)));
        Assert.False(File.Exists(G(ComponentStore.DfcBridgeFile)));
        var m = InstallManifest.Read(_root)!;
        Assert.True(m.RenoDx);
        Assert.False(m.Dfc);

        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.False(File.Exists(G(ComponentStore.RenoDxFile)));
        Assert.True(File.Exists(G(ComponentStore.DfcFile)));
        m = InstallManifest.Read(_root)!;
        Assert.True(m.Dfc);
        Assert.False(m.RenoDx);

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal("USERS-RENODX", File.ReadAllText(G(ComponentStore.RenoDxFile)));
        Assert.False(File.Exists(G(ComponentStore.DfcFile)));
    }

    [Fact]
    public async Task OptiScalerNr_DisplacesForeignDfcFiles_AndUninstallRestoresThem()
    {
        var pkg = Path.Combine(ComponentStore.TagDir(Component.OptiScaler, "v9.9.9"), "pkg");
        Directory.CreateDirectory(pkg);
        File.WriteAllText(Path.Combine(pkg, "OptiScaler.dll"), "OPTI");
        File.WriteAllText(Path.Combine(pkg, "OptiScaler.ini"), "[DlssNr]\nEnabled=auto\n");
        var release = new ReleaseInfo { Tag = "v9.9.9" };
        ComponentStore.MarkComplete(Component.OptiScaler, release);
        _store.Opti = release;
        File.WriteAllText(G(ComponentStore.DfcFile), "USERS-DFC");
        File.WriteAllText(G(ComponentStore.DfcBridgeFile), "USERS-BRIDGE");
        File.WriteAllText(G("dfc-universal-feed.addon64"), "USERS-FEED");
        var installer = new Installer(_store);
        var opti = new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true, DlssNr = true, DlssNrId = _nr.Id, Proxy = "dxgi.dll" };

        await installer.InstallAsync(_game, _root, opti, null, default);
        Assert.False(File.Exists(G(ComponentStore.DfcFile)));
        Assert.False(File.Exists(G(ComponentStore.DfcBridgeFile)));
        Assert.False(File.Exists(G("dfc-universal-feed.addon64")));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal("USERS-DFC", File.ReadAllText(G(ComponentStore.DfcFile)));
        Assert.Equal("USERS-BRIDGE", File.ReadAllText(G(ComponentStore.DfcBridgeFile)));
        Assert.Equal("USERS-FEED", File.ReadAllText(G("dfc-universal-feed.addon64")));
    }

    [Fact]
    public void BuildOptions_WithDfc_ImpliesReShadeAndNr_AndExcludesRenoDx()
    {
        var root = Path.Combine(_tmp, "mode-game"); Directory.CreateDirectory(root);
        var settings = new AppSettings { DefaultMode = InstallMode.ReShadeOnly };
        var services = new DLSSUpdater.ViewModels.Services { Settings = settings, Store = _store, Installer = new Installer(_store) };
        var vm = new DLSSUpdater.ViewModels.GameViewModel(new GameInfo { Id = root, Root = root, Name = "Fake dfc game" }, services);
        Assert.False(vm.BuildOptions(false, false).Dfc);

        vm.Components.First(c => c.Key == "renodx").Enabled = true;
        vm.Components.First(c => c.Key == "dfc").Enabled = true;
        var options = vm.BuildOptions(false, false);
        Assert.True(options.Dfc);
        Assert.False(options.RenoDx);
        Assert.True(options.DlssNr);
        Assert.True(options.ReShade);
        Assert.False(vm.Components.First(c => c.Key == "renodx").Enabled);

        vm.Components.First(c => c.Key == "renodx").Enabled = true;
        Assert.False(vm.Components.First(c => c.Key == "dfc").Enabled);
        Assert.True(vm.BuildOptions(false, false).RenoDx);
        Assert.False(vm.BuildOptions(false, false).Dfc);
    }

    private static IniOverride O(string key, string value) => new(DfcConfig.Section, key, value);

    private InstallOptions DfcWith(params IniOverride[] overrides) => Dfc() with { ReShadeOverrides = overrides };

    private static Dictionary<string, string> Cfg(string path) =>
        File.ReadAllLines(path).Where(l => l.Contains('=')).Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);

    [Fact]
    public async Task Options_WriteTheirKeysIntoTheGameCfg_AndKeepEverythingElse()
    {
        _store.ImportDfc(Release());
        File.WriteAllText(G("ReShade.ini"), "[OTHER]\nSetting=keep\n");
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root,
            DfcWith(O("passes", "2.0"), O("neural_work_percent", "75"), O("arm", "0"), O("color_strength", "0.500"), O("preserve_native_tone_color", "1")),
            null, default);

        var cfg = Cfg(G(ComponentStore.DfcConfigFile));
        Assert.Equal("2.0", cfg["passes"]);
        Assert.Equal("75", cfg["neural_work_percent"]);
        Assert.Equal("0", cfg["arm"]);
        Assert.Equal("0.500", cfg["color_strength"]);
        Assert.Equal("1", cfg["preserve_native_tone_color"]);
        Assert.Equal("13", cfg["config_schema"]);
        Assert.Equal("1", cfg["enabled"]);
        Assert.DoesNotContain("[", File.ReadAllText(G(ComponentStore.DfcConfigFile)));
        Assert.DoesNotContain("DFC", File.ReadAllText(G("ReShade.ini")));
        Assert.Equal("2.0", InstallManifest.Read(_root)!.DfcCfg["DFC/passes"]);
    }

    [Fact]
    public async Task UnknownKeysAndKeysTunedInTheOverlay_Survive_AndAForeignCfgIsRestoredOnUninstall()
    {
        _store.ImportDfc(Release());
        const string original = "config_schema=13\nhook_mode=2\npasses=4.0\nfuture_key=abc\n";
        File.WriteAllText(G(ComponentStore.DfcConfigFile), original);
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, DfcWith(O("passes", "2.0"), O("arm", "0")), null, default);
        var cfg = Cfg(G(ComponentStore.DfcConfigFile));
        Assert.Equal("2", cfg["hook_mode"]);
        Assert.Equal("abc", cfg["future_key"]);
        Assert.Equal("4.0", cfg["passes"]); // the user's own value wins in keep mode
        Assert.Equal("0", cfg["arm"]);

        await installer.InstallAsync(_game, _root, DfcWith(O("passes", "2.0"), O("arm", "0")) with { OverwriteIni = true }, null, default);
        cfg = Cfg(G(ComponentStore.DfcConfigFile));
        Assert.Equal("2.0", cfg["passes"]);
        Assert.Equal("2", cfg["hook_mode"]);

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal(original, File.ReadAllText(G(ComponentStore.DfcConfigFile)));
    }

    [Fact]
    public async Task ValueChangedInTheOverlayAfterOurWrite_IsNotOverwrittenOnUpdate()
    {
        _store.ImportDfc(Release());
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, DfcWith(O("passes", "2.0")), null, default);
        var path = G(ComponentStore.DfcConfigFile);
        File.WriteAllText(path, File.ReadAllText(path).Replace("passes=2.0", "passes=3.5"));

        await installer.InstallAsync(_game, _root, DfcWith(O("passes", "2.0"), O("neural_work_percent", "125")), null, default);
        var cfg = Cfg(path);
        Assert.Equal("3.5", cfg["passes"]);
        Assert.Equal("125", cfg["neural_work_percent"]);
    }

    [Fact]
    public void PerGameLayer_OverridesTheBase_AndUnsetDropsTheKey()
    {
        var s = new AppSettings();
        s.ReShadeOverrides.Add(O("passes", "2.0"));
        s.ReShadeOverrides.Add(O("arm", "0"));
        var g = s.For("game");
        GameConfig.SetLayer(g, true, DfcConfig.Section, "passes", "3.0", baseSetsKey: true);
        GameConfig.SetLayer(g, true, DfcConfig.Section, "arm", null, baseSetsKey: true);
        GameConfig.SetLayer(g, true, DfcConfig.Section, "enabled", "0", baseSetsKey: false);

        var effective = GameConfig.EffectiveReShade(s, "game").ToDictionary(o => o.Id, o => o.Value);
        Assert.Equal("3.0", effective["DFC/passes"]);
        Assert.Equal("0", effective["DFC/enabled"]);
        Assert.DoesNotContain("DFC/arm", effective.Keys);
        Assert.Equal("2.0", GameConfig.EffectiveReShade(s, "other").First(o => o.Key == "passes").Value);
    }

    [Fact]
    public void Options_HaveUniqueKeysAndHelp_AndNumbersMatchByValue()
    {
        var options = ViewModels.SettingsOptions.Dfc([]);
        Assert.Equal(options.Length, options.Select(o => o.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(options, o =>
        {
            Assert.Equal(DfcConfig.Section, o.Section);
            Assert.Contains(ViewModels.HelpTopics.All, t => t.Id == o.Topic);
            Assert.NotNull(ViewModels.HelpTopics.AdviceFor(o.Topic!));
        });
        Assert.Contains(options.Single(o => o.Key == "color_strength").Choices, c => c.Value == "0.500");
        Assert.True(ViewModels.IniOptionViewModel.Same("0.5", "0.500"));
        Assert.Null(options.Single(o => o.Key == "arm").Choices[0].Value); // Default writes nothing
    }

    [Fact]
    public void SchemaAndVersionMismatch_AreReported_AndMissingKeysListed()
    {
        var managed = ViewModels.SettingsOptions.Dfc([]).Select(o => o.Key).ToList();

        var same = DfcConfig.Check(DfcConfig.TestedVersion, "config_schema=13\n" + string.Join("", managed.Select(k => k + "=0\n")), managed);
        Assert.False(same.HasIssues);
        Assert.Equal(13, same.Schema);

        var newer = DfcConfig.Check("2.1.0", "config_schema=14\narm=1\nenabled=1\n", managed);
        Assert.True(newer.VersionDiffers);
        Assert.True(newer.SchemaDiffers);
        Assert.Equal(14, newer.Schema);
        Assert.Contains("passes", newer.MissingKeys);
        Assert.DoesNotContain("arm", newer.MissingKeys);
        Assert.Contains("config_schema is 14", newer.Summary);

        Assert.Null(DfcConfig.Check("2.0.0", "arm=1\n", managed).Schema);
        Assert.True(DfcConfig.Check("2.0.0", "arm=1\n", managed).SchemaDiffers);
    }

    [Fact]
    public void ImportedCfg_DrivesTheCompatReport()
    {
        Assert.Null(_store.DfcCompat(["arm"]));
        _store.ImportDfc(Release(cfgText: "config_schema=14\narm=1\n"));
        var report = _store.DfcCompat(["arm", "passes"])!;
        Assert.True(report.SchemaDiffers);
        Assert.True(report.VersionDiffers); // the fake legacy add-on is 2.0.0
        Assert.Equal(["passes"], report.MissingKeys);

        _store.ImportDfc(Package());
        report = _store.DfcCompat(ViewModels.SettingsOptions.Dfc([]).Select(o => o.Key))!;
        Assert.False(report.HasIssues, report.Summary);
    }

    // ---------- 4.x package ----------

    [Fact]
    public void Import_4xPackage_FromThePackageFolderOrThe64BitAddon_StoresTheWholeSetOnly()
    {
        var pkg = Package();
        _store.ImportDfc(pkg);
        Assert.True(_store.DfcImported);
        Assert.Equal("4.0.0", _store.DfcVersion);
        Assert.Equal(V4Set().Order(StringComparer.OrdinalIgnoreCase), _store.DfcInstallFiles());
        var x64 = Path.Combine(pkg, "64-bit");
        foreach (var rel in V4Set())
            Assert.Equal(FileUtil.Sha256(Path.Combine(x64, rel)), FileUtil.Sha256(Path.Combine(ComponentStore.DfcDir, rel)));
        Assert.Equal(V4Cfg, File.ReadAllText(_store.DfcConfigPath));
        Assert.False(File.Exists(Path.Combine(ComponentStore.DfcDir, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(ComponentStore.DfcDir, "INSTALL.txt")));

        _store.RemoveDfcImport();
        _store.Import(Path.Combine(x64, ComponentStore.DfcFile), ComponentStore.DfcFile);
        Assert.Equal(V4Set().Length, _store.DfcInstallFiles().Count);
    }

    [Fact]
    public void Import_4xWithoutPresentSupportOrNeuralFolder_OrThe32BitHostSet_IsRefused_AndKeepsTheOldImport()
    {
        _store.ImportDfc(Release());
        var tag = _store.DfcTag;
        Assert.Contains(ComponentStore.DfcPresentSupportFile, Assert.Throws<InvalidDataException>(() => _store.ImportDfc(Package(presentSupport: false))).Message);
        Assert.Contains(ComponentStore.DfcNeuralDir, Assert.Throws<InvalidDataException>(() => _store.ImportDfc(Package(neural: false))).Message);
        var host = Path.Combine(Package(), "32-bit", "host64", ComponentStore.DfcFile);
        Assert.Contains("32-bit", Assert.Throws<InvalidDataException>(() => _store.ImportDfc(host)).Message);
        Assert.Equal(tag, _store.DfcTag);
    }

    [Fact]
    public void Reimport_ReplacesTheWholeSet_InBothDirections()
    {
        _store.ImportDfc(Release());
        Assert.Equal([ComponentStore.DfcBridgeFile, ComponentStore.DfcFile], _store.DfcInstallFiles());

        _store.ImportDfc(Package());
        Assert.Equal(V4Set().Length, _store.DfcInstallFiles().Count);
        Assert.Equal("BRIDGE4", File.ReadAllText(_store.DfcBridgePath));

        _store.ImportDfc(Release(cfg: false));
        Assert.Equal([ComponentStore.DfcBridgeFile, ComponentStore.DfcFile], _store.DfcInstallFiles());
        Assert.False(Directory.Exists(Path.Combine(ComponentStore.DfcDir, ComponentStore.DfcNeuralDir)));
        Assert.False(File.Exists(_store.DfcConfigPath));
        Assert.False(Directory.Exists(ComponentStore.DfcDir + ".new"));
    }

    [Fact]
    public async Task Install_4x_PlacesTheWholeSet_CoversTheEffectFolder_AndUninstallLeavesNothing()
    {
        var pkg = Package();
        _store.ImportDfc(pkg);
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Dfc(), null, default);

        var x64 = Path.Combine(pkg, "64-bit");
        foreach (var rel in V4Set().Append(ComponentStore.DfcConfigFile))
            Assert.Equal(FileUtil.Sha256(Path.Combine(x64, rel)), FileUtil.Sha256(G(rel)));
        Assert.False(File.Exists(G("notes.txt")));
        var m = InstallManifest.Read(_root)!;
        Assert.All(V4Set(), rel => Assert.Contains(rel, m.Files, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(@".\,.\reshade-shaders\Shaders\**", IniFile.Load(G("ReShade.ini")).Get("GENERAL", "EffectSearchPaths"));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task Install_4x_OverAManualCopy_BacksItUp_KeepsACoveringSearchPath_AndUninstallRestoresIt()
    {
        _store.ImportDfc(Package());
        var neural = G(Path.Combine(ComponentStore.DfcNeuralDir, NeuralFiles[0]));
        Directory.CreateDirectory(Path.GetDirectoryName(neural)!);
        File.WriteAllText(neural, "USERS-HELPER");
        File.WriteAllText(G(ComponentStore.DfcPresentSupportFile), "USERS-PRESENT");
        const string ini = "[GENERAL]\nEffectSearchPaths=.\\reshade-shaders\\Shaders\\**\n";
        File.WriteAllText(G("ReShade.ini"), ini);
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.Equal("NEURAL-" + NeuralFiles[0], File.ReadAllText(neural));
        Assert.Equal("PRESENT", File.ReadAllText(G(ComponentStore.DfcPresentSupportFile)));
        Assert.Equal(ini, File.ReadAllText(G("ReShade.ini")));

        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal("USERS-HELPER", File.ReadAllText(neural));
        Assert.Equal("USERS-PRESENT", File.ReadAllText(G(ComponentStore.DfcPresentSupportFile)));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(neural)!));
        Assert.False(File.Exists(G(Effect)));
        Assert.False(Directory.Exists(G("reshade-shaders")));
    }

    [Fact]
    public async Task UpdatingFromA2xInstall_AddsTheNewFiles_AndGoingBackTakesThemOut()
    {
        _store.ImportDfc(Release());
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.False(File.Exists(G(ComponentStore.DfcPresentSupportFile)));

        _store.ImportDfc(Package());
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.All(V4Set(), rel => Assert.True(File.Exists(G(rel)), rel));
        Assert.Equal(FileUtil.Sha256(_store.DfcPath), FileUtil.Sha256(G(ComponentStore.DfcFile)));
        Assert.Equal(_store.DfcTag, InstallManifest.Read(_root)!.DfcTag);

        _store.ImportDfc(Release());
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.True(File.Exists(G(ComponentStore.DfcFile)));
        Assert.False(File.Exists(G(ComponentStore.DfcPresentSupportFile)));
        Assert.False(Directory.Exists(G(ComponentStore.DfcNeuralDir)));
        Assert.False(File.Exists(G(Effect)));
        Assert.DoesNotContain(InstallManifest.Read(_root)!.Files, f => f.Contains(ComponentStore.DfcNeuralDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SwitchingFrom4xToRenoDx_TakesOutTheWholeSet()
    {
        _store.ImportDfc(Package());
        _store.ImportRenoDx(RenoDxAddon());
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Dfc(), null, default);

        await installer.InstallAsync(_game, _root, Reno(), null, default);
        Assert.True(File.Exists(G(ComponentStore.RenoDxFile)));
        Assert.All(V4Set().Append(ComponentStore.DfcConfigFile), rel => Assert.False(File.Exists(G(rel)), rel));
        Assert.False(Directory.Exists(G(ComponentStore.DfcNeuralDir)));
        Assert.False(Directory.Exists(G("reshade-shaders")));
        Assert.DoesNotContain(InstallManifest.Read(_root)!.Files, ComponentStore.IsDfcPackagePath);
    }

    [Fact]
    public async Task OptiScalerNr_DisplacesAForeignPresentSupportDll_AndUninstallRestoresIt()
    {
        var pkg = Path.Combine(ComponentStore.TagDir(Component.OptiScaler, "v9.9.9"), "pkg");
        Directory.CreateDirectory(pkg);
        File.WriteAllText(Path.Combine(pkg, "OptiScaler.dll"), "OPTI");
        File.WriteAllText(Path.Combine(pkg, "OptiScaler.ini"), "[DlssNr]\nEnabled=auto\n");
        var release = new ReleaseInfo { Tag = "v9.9.9" };
        ComponentStore.MarkComplete(Component.OptiScaler, release);
        _store.Opti = release;
        File.WriteAllText(G(ComponentStore.DfcPresentSupportFile), "USERS-PRESENT");
        var installer = new Installer(_store);
        var opti = new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true, DlssNr = true, DlssNrId = _nr.Id, Proxy = "dxgi.dll" };

        await installer.InstallAsync(_game, _root, opti, null, default);
        Assert.False(File.Exists(G(ComponentStore.DfcPresentSupportFile)));
        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal("USERS-PRESENT", File.ReadAllText(G(ComponentStore.DfcPresentSupportFile)));
    }

    [Theory]
    [InlineData(null, @".\,.\reshade-shaders\Shaders\**")]
    [InlineData("", @".\reshade-shaders\Shaders\**")]
    [InlineData(@".\reshade-shaders\Shaders\**", null)]
    [InlineData(@".\reshade-shaders\Shaders", null)]
    [InlineData(@".\reshade-shaders\**", null)]
    [InlineData(@".\**", null)]
    [InlineData("reshade-shaders/Shaders/", null)]
    [InlineData(@".\reshade-shaders", @".\reshade-shaders,.\reshade-shaders\Shaders\**")]
    [InlineData(@".\,D:\Shaders\**", @".\,D:\Shaders\**,.\reshade-shaders\Shaders\**")]
    public void EffectSearchPaths_GetTheEffectFolder_OnlyWhenNothingCoversIt(string? current, string? expected)
    {
        Assert.Equal(expected, DfcConfig.EffectSearchPathsFor(current, _root));
        Assert.Null(DfcConfig.EffectSearchPathsFor(Path.Combine(_root, "reshade-shaders", "Shaders"), _root));
    }

    [Fact]
    public async Task NewOutputOptions_AreWrittenIntoThe4xCfg()
    {
        _store.ImportDfc(Package());
        await new Installer(_store).InstallAsync(_game, _root,
            DfcWith(O("output_hud_enabled", "1"), O("output_highlights_enabled", "1"), O("output_deband_enabled", "1")), null, default);
        var cfg = Cfg(G(ComponentStore.DfcConfigFile));
        Assert.Equal("1", cfg["output_hud_enabled"]);
        Assert.Equal("1", cfg["output_highlights_enabled"]);
        Assert.Equal("1", cfg["output_deband_enabled"]);
        Assert.Equal("1", cfg["arm"]);
    }

    [Fact]
    public void NewOutputOptions_AreHiddenFor2xImports()
    {
        _store.ImportDfc(Release());
        var missing = _store.DfcCompat(ViewModels.SettingsOptions.Dfc([]).Select(o => o.Key))!.MissingKeys;
        Assert.Contains("output_hud_enabled", missing);
        Assert.DoesNotContain("passes", missing);
    }
}
