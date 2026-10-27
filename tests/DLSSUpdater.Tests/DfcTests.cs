using System.IO.Compression;
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

    /// <summary>Files a newer package might add; the whole 64-bit folder is taken, so they come along.</summary>
    private static readonly string[] ExtraFiles = ["notes.txt", Path.Combine("reshade-shaders", "Shaders", "ReShade.fxh"), Path.Combine("future", "model.bin")];
    /// <summary>Files a user may have dropped into the 64-bit folder (README step 3, a test run) that the app places itself or never copies.</summary>
    private static readonly string[] SkippedFiles = ["nvngx_dlssnr.dll", "nvngx_dlss.dll", "dxgi.dll", "ReShade.ini", "deep-fried-chicken.log"];

    /// <summary>
    /// The 4.x/5.x package layout: README.txt, a 64-bit folder with the whole set (plus <see cref="ExtraFiles"/> and
    /// <see cref="SkippedFiles"/> when <paramref name="extras"/>) and a 32-bit folder with its host64 companion set.
    /// </summary>
    private string Package(bool presentSupport = true, bool neural = true, bool extras = true, int major = 4)
    {
        var root = Path.Combine(_tmp, "pkg-" + Guid.NewGuid().ToString("N")[..6]);
        var x64 = Path.Combine(root, "64-bit");
        Directory.CreateDirectory(x64);
        File.WriteAllText(Path.Combine(root, "README.txt"), $"Deep Fried Chicken {major}.0\n");
        FakeVersionDll.Write(Path.Combine(x64, ComponentStore.DfcFile), new Version(major, 0, 0, 0), ComponentStore.DfcFile);
        File.WriteAllText(Path.Combine(x64, ComponentStore.DfcBridgeFile), "BRIDGE4");
        File.WriteAllText(Path.Combine(x64, ComponentStore.DfcConfigFile), V4Cfg);
        if (extras)
            foreach (var rel in ExtraFiles.Concat(SkippedFiles))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(x64, rel))!);
                File.WriteAllText(Path.Combine(x64, rel), "PKG-" + rel);
            }
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
    private static string[] V4Set(bool extras = true) =>
    [
        ComponentStore.DfcFile, ComponentStore.DfcBridgeFile, ComponentStore.DfcPresentSupportFile, Effect,
        .. NeuralFiles.Select(n => Path.Combine(ComponentStore.DfcNeuralDir, n)),
        .. extras ? ExtraFiles : [],
    ];

    private string RenoDxAddon()
    {
        var path = Path.Combine(_tmp, "reno-src", ComponentStore.RenoDxFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FakeVersionDll.Write(path, new Version(0, 2026, 1016, 1704), ComponentStore.RenoDxFile);
        return path;
    }

    /// <summary>The build installs use when the game picks none.</summary>
    private AddonBuild Default => _store.DfcBuildFor(null)!;
    private static string Stored(AddonBuild build, string rel) => Path.Combine(ComponentStore.DfcBuildDir(build), rel);

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
    public async Task IniModes_FreshRebuildsImportedDefaultsPlusOptions_AndKeepsOneBackup(bool carryOver, bool overwrite, string passes, string? junk)
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
        Assert.Equal(original, File.ReadAllText(Path.Combine(InstallManifest.BackupDirFor(_root), backup.Backup)));
        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal(original, File.ReadAllText(G(ComponentStore.DfcConfigFile)));
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
        Assert.DoesNotContain(m.Backups, b => b.Original == ComponentStore.DfcConfigFile);
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
        var build = _store.ImportDfc(folder);
        Assert.True(_store.DfcImported);
        Assert.Equal("2.0.0", build.FileVersion);
        var addon = Path.Combine(folder, ComponentStore.DfcFile);
        Assert.Equal(FileUtil.Sha256(addon), build.Id);
        Assert.Equal("2.0.0-" + FileUtil.Sha256(addon)[..12], build.Tag);
        Assert.Equal(FileUtil.Sha256(addon), FileUtil.Sha256(Stored(build, ComponentStore.DfcFile)));
        Assert.Equal("BRIDGE", File.ReadAllText(Stored(build, ComponentStore.DfcBridgeFile)));
        Assert.True(File.Exists(ComponentStore.DfcConfigOf(build)));

        _store.RemoveAddon(AddonKind.Dfc, build.Id);
        Assert.False(_store.DfcImported);
        Assert.Null(_store.DfcBuildFor(null));

        build = _store.ImportDfc(Path.Combine(Release(cfg: false), ComponentStore.DfcFile));
        Assert.True(_store.DfcImported);
        Assert.False(File.Exists(ComponentStore.DfcConfigOf(build)));
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
        Assert.Equal(Default.Tag, m.DfcTag);
        Assert.Equal(Default.Id, m.DfcId);
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

        _store.ImportDfc(Package());
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.Equal("tuned=1\n", File.ReadAllText(G(ComponentStore.DfcConfigFile)));
        Assert.Equal(FileUtil.Sha256(Stored(Default, ComponentStore.DfcFile)), FileUtil.Sha256(G(ComponentStore.DfcFile)));

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
    public async Task Dfc_NeedsReShadeOnlyMode_AndKeepsRenoDxWhenAlsoPicked()
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
        Assert.True(options.RenoDx);
        Assert.True(options.DlssNr);
        Assert.True(options.ReShade);
        Assert.NotNull(vm.ConflictWarning);

        vm.Components.First(c => c.Key == "renodx").Enabled = false;
        Assert.True(vm.BuildOptions(false, false).Dfc);
        Assert.Null(vm.ConflictWarning);
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

        _store.ImportDfc(Package(major: 5));
        report = _store.DfcCompat(ViewModels.SettingsOptions.Dfc([]).Select(o => o.Key))!;
        Assert.False(report.HasIssues, report.Summary);
    }

    // ---------- 4.x package ----------

    [Fact]
    public void Import_4xPackage_FromThePackageFolderOrThe64BitAddon_StoresTheWhole64BitFolder()
    {
        var pkg = Package();
        var build = _store.ImportDfc(pkg);
        Assert.True(_store.DfcImported);
        Assert.Equal("4.0.0", build.FileVersion);
        Assert.Equal(V4Set().Order(StringComparer.OrdinalIgnoreCase), ComponentStore.DfcInstallFiles(build));
        var x64 = Path.Combine(pkg, "64-bit");
        foreach (var rel in V4Set())
            Assert.Equal(FileUtil.Sha256(Path.Combine(x64, rel)), FileUtil.Sha256(Stored(build, rel)));
        Assert.Equal(V4Cfg, File.ReadAllText(ComponentStore.DfcConfigOf(build)));
        Assert.All(SkippedFiles, rel => Assert.False(File.Exists(Stored(build, rel)), rel));
        Assert.False(File.Exists(Stored(build, "README.txt")));

        _store.RemoveAddon(AddonKind.Dfc, build.Id);
        _store.Import(Path.Combine(x64, ComponentStore.DfcFile), ComponentStore.DfcFile);
        Assert.Equal(V4Set().Length, ComponentStore.DfcInstallFiles(Default).Count);
    }

    [Fact]
    public void Import_4xWithoutPresentSupportOrNeuralFolder_OrThe32BitHostSet_IsRefused_AndKeepsTheOldImport()
    {
        var old = _store.ImportDfc(Release());
        Assert.Contains(ComponentStore.DfcPresentSupportFile, Assert.Throws<InvalidDataException>(() => _store.ImportDfc(Package(presentSupport: false))).Message);
        Assert.Contains(ComponentStore.DfcNeuralDir, Assert.Throws<InvalidDataException>(() => _store.ImportDfc(Package(neural: false))).Message);
        var host = Path.Combine(Package(), "32-bit", "host64", ComponentStore.DfcFile);
        Assert.Contains("32-bit", Assert.Throws<InvalidDataException>(() => _store.ImportDfc(host)).Message);
        Assert.Equal([old.Id], _store.DfcBuilds.Select(b => b.Id));
    }

    [Fact]
    public void Imports_AreKeptSideBySide_NewestFirst_AndTheSameAddonAgainReplacesOnlyItsOwnSet()
    {
        var v2 = _store.ImportDfc(Release());
        Assert.Equal([ComponentStore.DfcBridgeFile, ComponentStore.DfcFile], ComponentStore.DfcInstallFiles(v2));

        var v4 = _store.ImportDfc(Package());
        var v5 = _store.ImportDfc(Package(major: 5));
        Assert.Equal([v5.Id, v4.Id, v2.Id], _store.DfcBuilds.Select(b => b.Id));
        Assert.Equal(["5.0.0", "4.0.0", "2.0.0"], _store.DfcBuilds.Select(b => b.Name));
        Assert.Equal(V4Set().Length, ComponentStore.DfcInstallFiles(v4).Count);
        Assert.Equal("BRIDGE4", File.ReadAllText(Stored(v4, ComponentStore.DfcBridgeFile)));
        Assert.Equal("BRIDGE", File.ReadAllText(Stored(v2, ComponentStore.DfcBridgeFile)));

        // The same add-on again: still one build with its name and date, its set replaced as a whole.
        _store.RenameAddon(AddonKind.Dfc, v2.Id, "my 2.0");
        var again = _store.ImportDfc(Release(cfg: false));
        Assert.Equal(v2.Id, again.Id);
        Assert.Equal("my 2.0", again.Name);
        Assert.Equal(v2.Imported, again.Imported);
        Assert.Equal(3, _store.DfcBuilds.Count);
        Assert.False(File.Exists(ComponentStore.DfcConfigOf(again)));
        Assert.Equal(V4Set().Length, ComponentStore.DfcInstallFiles(v4).Count);
        Assert.False(Directory.Exists(ComponentStore.DfcDir + ".new"));
        Assert.Equal(new[] { v2.Id, v4.Id, v5.Id }.Order(), Directory.EnumerateDirectories(ComponentStore.DfcDir).Select(d => Path.GetFileName(d)).Order());
    }

    [Fact]
    public void Import_Zip_OfTheReleaseOrIts64BitFolder_TakesTheSameSet_AndLeavesNoUnpackedCopy()
    {
        var pkg = Package(major: 5);
        var whole = Path.Combine(_tmp, "release.zip");
        ZipFile.CreateFromDirectory(pkg, whole, CompressionLevel.Fastest, includeBaseDirectory: true);
        var build = _store.ImportDfc(whole);
        Assert.Equal("5.0.0", build.FileVersion);
        Assert.Equal(V4Set().Order(StringComparer.OrdinalIgnoreCase), ComponentStore.DfcInstallFiles(build));
        Assert.Equal(V4Cfg, File.ReadAllText(ComponentStore.DfcConfigOf(build)));
        Assert.False(Directory.Exists(ComponentStore.DfcDir + ".zip"));

        _store.RemoveAddon(AddonKind.Dfc, build.Id);
        var x64 = Path.Combine(_tmp, "x64.zip");
        ZipFile.CreateFromDirectory(Path.Combine(pkg, "64-bit"), x64);
        _store.Import(x64, ComponentStore.DfcFile);
        Assert.Equal(V4Set().Order(StringComparer.OrdinalIgnoreCase), ComponentStore.DfcInstallFiles(Default));
    }

    [Fact]
    public void Import_SevenZipOrABadZip_IsRefused_AndKeepsTheOldImport()
    {
        var build = _store.ImportDfc(Package());
        var sevenZip = Path.Combine(_tmp, "Deep-Fried-Chicken-v5.0.0-essentials.7z");
        File.WriteAllText(sevenZip, "7z");
        Assert.Contains("Extract it first", Assert.Throws<InvalidDataException>(() => _store.ImportDfc(sevenZip)).Message);

        var escape = Path.Combine(_tmp, "escape.zip");
        using (var zip = ZipFile.Open(escape, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("../outside.txt").Open()))
            w.Write("x");
        Assert.Throws<IOException>(() => _store.ImportDfc(escape));
        Assert.False(File.Exists(Path.Combine(AppPaths.Components, "outside.txt")));

        var empty = Path.Combine(_tmp, "empty.zip");
        using (ZipFile.Open(empty, ZipArchiveMode.Create)) { }
        Assert.Contains(ComponentStore.DfcFile, Assert.Throws<InvalidDataException>(() => _store.ImportDfc(empty)).Message);
        Assert.Equal([build.Id], _store.DfcBuilds.Select(b => b.Id));
        Assert.False(Directory.Exists(ComponentStore.DfcDir + ".zip"));
    }

    [Fact]
    public void Import_AFolderHoldingMuchMoreThanTheSet_IsRefused()
    {
        var x64 = Path.Combine(Package(), "64-bit");
        for (var i = 0; i < ComponentStore.DfcMaxFiles; i++)
            File.WriteAllText(Path.Combine(x64, $"download-{i}.txt"), "x");
        Assert.Contains("holds more than", Assert.Throws<InvalidDataException>(() => _store.ImportDfc(x64)).Message);
        Assert.False(_store.DfcImported);
    }

    [Theory]
    [InlineData("deep-fried-chicken.addon64", true)]
    [InlineData(@"deep-fried-chicken-neural\THIRD-PARTY-NOTICES.txt", true)]
    [InlineData(@"reshade-shaders\Shaders\ReShade.fxh", true)]
    [InlineData(@"future\nvngx_helper.dll", true)]
    [InlineData("deep-fried-chicken.cfg", false)]
    [InlineData("deep-fried-chicken-hooks.log", false)]
    [InlineData(@"deep-fried-chicken-neural\helper.log", false)]
    [InlineData("nvngx_dlssnr.dll", false)]
    [InlineData("NVNGX_DLSS.DLL", false)]
    [InlineData("ReShade64.dll", false)]
    [InlineData("ReShade.ini", false)]
    [InlineData("d3d11.dll", false)]
    [InlineData("opengl32.dll", false)]
    [InlineData(@".dlssupdater\manifest.json", false)]
    public void SetFiles_AreTheWhole64BitFolder_ButCfgLogsAndWhatTheAppPlacesItself(string rel, bool taken) =>
        Assert.Equal(taken, ComponentStore.IsDfcSetFile(rel));

    [Fact]
    public async Task Reinstall_TakesOutFilesTheNewSetNoLongerHas_IncludingExtraFolders()
    {
        _store.ImportDfc(Package());
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.All(ExtraFiles, rel => Assert.True(File.Exists(G(rel)), rel));

        _store.ImportDfc(Package(extras: false, major: 5));
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.All(V4Set(extras: false), rel => Assert.True(File.Exists(G(rel)), rel));
        Assert.All(ExtraFiles, rel => Assert.False(File.Exists(G(rel)), rel));
        Assert.False(Directory.Exists(G("future")));
        var m = InstallManifest.Read(_root)!;
        Assert.Equal(V4Set(extras: false).Order(StringComparer.OrdinalIgnoreCase), m.DfcFiles.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnInstallRecordedBeforeDfcFiles_IsStillTakenOutWhole()
    {
        _store.ImportDfc(Package(extras: false));
        _store.ImportRenoDx(RenoDxAddon());
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        var m = InstallManifest.Read(_root)!;
        m.DfcFiles.Clear();
        m.Save(_root);

        await installer.InstallAsync(_game, _root, Reno(), null, default);
        Assert.All(V4Set(extras: false).Append(ComponentStore.DfcConfigFile), rel => Assert.False(File.Exists(G(rel)), rel));
        Assert.False(Directory.Exists(G(ComponentStore.DfcNeuralDir)));
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
        Assert.False(File.Exists(G("deep-fried-chicken.log")));
        Assert.All(["nvngx_dlssnr.dll", "dxgi.dll", "ReShade.ini"], rel => Assert.NotEqual(FileUtil.Sha256(Path.Combine(x64, rel)), FileUtil.Sha256(G(rel))));
        var m = InstallManifest.Read(_root)!;
        Assert.All(V4Set(), rel => Assert.Contains(rel, m.Files, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(V4Set().Order(StringComparer.OrdinalIgnoreCase), m.DfcFiles.Order(StringComparer.OrdinalIgnoreCase));
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
        var v2 = _store.ImportDfc(Release());
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.False(File.Exists(G(ComponentStore.DfcPresentSupportFile)));

        var v4 = _store.ImportDfc(Package());
        await installer.InstallAsync(_game, _root, Dfc(), null, default);
        Assert.All(V4Set(), rel => Assert.True(File.Exists(G(rel)), rel));
        Assert.Equal(v4.Id, FileUtil.Sha256(G(ComponentStore.DfcFile)));
        Assert.Equal(v4.Tag, InstallManifest.Read(_root)!.DfcTag);

        // The game picks the older build: its set replaces the newer one.
        await installer.InstallAsync(_game, _root, Dfc() with { DfcId = v2.Id }, null, default);
        Assert.Equal(v2.Id, InstallManifest.Read(_root)!.DfcId);
        Assert.True(File.Exists(G(ComponentStore.DfcFile)));
        Assert.False(File.Exists(G(ComponentStore.DfcPresentSupportFile)));
        Assert.False(Directory.Exists(G(ComponentStore.DfcNeuralDir)));
        Assert.False(File.Exists(G(Effect)));
        Assert.All(ExtraFiles, rel => Assert.False(File.Exists(G(rel)), rel));
        Assert.False(Directory.Exists(G("reshade-shaders")));
        Assert.DoesNotContain(InstallManifest.Read(_root)!.Files, f => f.Contains(ComponentStore.DfcNeuralDir, StringComparison.OrdinalIgnoreCase));
        Assert.Equal([ComponentStore.DfcBridgeFile, ComponentStore.DfcFile], InstallManifest.Read(_root)!.DfcFiles.Order(StringComparer.OrdinalIgnoreCase));
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

    // ---------- games without their own DLSS (4.x) ----------

    private const string Sr = "nvngx_dlss.dll";
    private const string Fg = "nvngx_dlssg.dll";

    /// <summary>NVIDIA's DLSS release in the component cache, as the installer fetches it (fake files, offline).</summary>
    private void SeedDlss(string tag, string content)
    {
        foreach (var f in ComponentStore.DlssFiles)
        {
            var path = Path.Combine(ComponentStore.TagDir(Component.Dlss, tag), f);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"{content}-{f}");
        }
        var r = new ReleaseInfo { Tag = tag };
        ComponentStore.MarkComplete(Component.Dlss, r);
        _store.Dlss = r;
    }

    private string PlainRoot => Path.Combine(_tmp, "plain");
    private string P(string rel) => Path.Combine(PlainRoot, rel);

    /// <summary>A game folder with an exe and no DLSS, scanned like the app does (so files we add show up in the next scan).</summary>
    private GameInfo PlainGame()
    {
        Directory.CreateDirectory(PlainRoot);
        if (!File.Exists(P("Game.exe"))) File.WriteAllText(P("Game.exe"), "EXE");
        return GameScanner.Inspect(new GameEntry("Plain", PlainRoot, "Manual"));
    }

    private List<string> PlainFiles() => Directory.EnumerateFiles(PlainRoot, "*", SearchOption.AllDirectories).Order().ToList();

    [Fact]
    public async Task NonDlssGame_4x_PlacesTheSetNrAndNvidiasSr_OwnedAndRecorded_AndUninstallLeavesTheGameAsItWas()
    {
        SeedDlss("v310.9.1", "NEW");
        _store.ImportDfc(Package());
        var game = PlainGame();
        Assert.False(Installer.HasNativeDlss(game, null, PlainRoot));
        var installer = new Installer(_store);

        await installer.InstallAsync(game, PlainRoot, Dfc(), null, default);

        Assert.All(V4Set().Append(ComponentStore.DfcConfigFile), rel => Assert.True(File.Exists(P(rel)), rel));
        Assert.All(NeuralFiles, n => Assert.True(File.Exists(P(Path.Combine(ComponentStore.DfcNeuralDir, n))), n));
        Assert.Equal(_nr.Sha256, FileUtil.Sha256(P(ComponentStore.DlssNrFile)));
        Assert.Equal("NEW-" + Sr, File.ReadAllText(P(Sr)));
        Assert.False(File.Exists(P(Fg)));
        var m = InstallManifest.Read(PlainRoot)!;
        Assert.True(m.Dfc);
        Assert.True(m.DlssNr);
        Assert.Contains(Sr, m.Files, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(ComponentStore.DlssNrFile, m.Files, StringComparer.OrdinalIgnoreCase);
        Assert.All(V4Set(), rel => Assert.Contains(rel, m.Files, StringComparer.OrdinalIgnoreCase));
        Assert.Equal([Sr], m.DfcRuntimes);
        Assert.False(m.Dlss);

        // The next scan sees our nvngx_dlss.dll, but it doesn't make this a DLSS game: a reinstall keeps it current.
        game = PlainGame();
        Assert.Contains(game.Dlss, d => d.Name == Sr);
        Assert.False(Installer.HasNativeDlss(game, m, PlainRoot));
        // A DLSS update replaced the game's own file instead: ours, but the backed-up original keeps the game native.
        var updated = InstallManifest.Read(PlainRoot)!;
        updated.Backups.Add(new BackupEntry { Original = Sr });
        Assert.True(Installer.HasNativeDlss(game, updated, PlainRoot));
        SeedDlss("v310.10.0", "NEWER");
        await installer.InstallAsync(game, PlainRoot, Dfc(), null, default);
        Assert.Equal("NEWER-" + Sr, File.ReadAllText(P(Sr)));
        Assert.Equal([Sr], InstallManifest.Read(PlainRoot)!.DfcRuntimes);

        await installer.UninstallAsync(game, PlainRoot, default);
        Assert.Equal(["Game.exe"], Directory.EnumerateFileSystemEntries(PlainRoot).Select(Path.GetFileName));
    }

    [Fact]
    public async Task NonDlssGame_2xImport_IsRefusedWithThe4xHint_AndTouchesNothing()
    {
        SeedDlss("v310.9.1", "NEW");
        _store.ImportDfc(Release());
        var game = PlainGame();
        var before = PlainFiles();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new Installer(_store).InstallAsync(game, PlainRoot, Dfc(), null, default));
        Assert.Equal(Installer.DfcNeedsNativeDlss("2.0.0"), error.Message);
        Assert.Contains("4.x", error.Message);
        Assert.DoesNotContain("Chicken Assist", error.Message);
        Assert.Equal(before, PlainFiles());
        Assert.False(ComponentStore.DfcSupportsNonDlssGames(Default));

        // A 2.x import still installs into a game with native DLSS.
        await new Installer(_store).InstallAsync(_game, _root, Dfc(), null, default);
        Assert.True(File.Exists(G(ComponentStore.DfcFile)));
    }

    [Fact]
    public async Task NonDlssGame_FrameGenToggle_PlacesAndTakesOutNvidiasFgRuntime()
    {
        SeedDlss("v310.9.1", "NEW");
        _store.ImportDfc(Package());
        var installer = new Installer(_store);

        await installer.InstallAsync(PlainGame(), PlainRoot, Dfc() with { DfcFrameGen = true }, null, default);
        Assert.Equal("NEW-" + Fg, File.ReadAllText(P(Fg)));
        Assert.Equal("NEW-" + Sr, File.ReadAllText(P(Sr)));
        var m = InstallManifest.Read(PlainRoot)!;
        Assert.Equal([Sr, Fg], m.DfcRuntimes);
        Assert.Contains(Fg, m.Files, StringComparer.OrdinalIgnoreCase);

        await installer.InstallAsync(PlainGame(), PlainRoot, Dfc(), null, default);
        Assert.False(File.Exists(P(Fg)));
        Assert.True(File.Exists(P(Sr)));
        m = InstallManifest.Read(PlainRoot)!;
        Assert.Equal([Sr], m.DfcRuntimes);
        Assert.DoesNotContain(Fg, m.Files, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonDlssGame_AnFgRuntimeThatIsNotOurs_IsLeftAlone_AndStaysAfterUninstall()
    {
        SeedDlss("v310.9.1", "NEW");
        _store.ImportDfc(Package());
        PlainGame();
        File.WriteAllText(P(Fg), "USERS-FG");
        var installer = new Installer(_store);

        await installer.InstallAsync(PlainGame(), PlainRoot, Dfc() with { DfcFrameGen = true }, null, default);
        Assert.Equal("USERS-FG", File.ReadAllText(P(Fg)));
        Assert.Equal([Sr], InstallManifest.Read(PlainRoot)!.DfcRuntimes);

        await installer.UninstallAsync(PlainGame(), PlainRoot, default);
        Assert.Equal("USERS-FG", File.ReadAllText(P(Fg)));
        Assert.False(File.Exists(P(Sr)));
    }

    [Fact]
    public async Task NonDlssGame_SwitchingRoutes_TakesOutTheRuntimes_AndRenoDxStaysRefused()
    {
        SeedDlss("v310.9.1", "NEW");
        _store.ImportDfc(Package());
        _store.ImportRenoDx(RenoDxAddon());
        var installer = new Installer(_store);
        await installer.InstallAsync(PlainGame(), PlainRoot, Dfc() with { DfcFrameGen = true }, null, default);

        // Our nvngx_dlss.dll doesn't count as the game's DLSS, so a route that needs native DLSS is still refused.
        var before = PlainFiles().ToDictionary(f => f, FileUtil.Sha256);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(PlainGame(), PlainRoot, Reno(), null, default));
        Assert.Contains("native DLSS", error.Message);
        Assert.Equal(before, PlainFiles().ToDictionary(f => f, FileUtil.Sha256));

        // ReShade-only with NR and no add-on: DFC and both runtimes go, NR and ReShade stay.
        await installer.InstallAsync(PlainGame(), PlainRoot, Dfc() with { Dfc = false }, null, default);
        Assert.False(File.Exists(P(Sr)));
        Assert.False(File.Exists(P(Fg)));
        Assert.False(File.Exists(P(ComponentStore.DfcFile)));
        Assert.False(Directory.Exists(P(ComponentStore.DfcNeuralDir)));
        Assert.True(File.Exists(P(ComponentStore.DlssNrFile)));
        var m = InstallManifest.Read(PlainRoot)!;
        Assert.False(m.Dfc);
        Assert.Empty(m.DfcRuntimes);
        Assert.DoesNotContain(m.Files, f => ComponentStore.DlssFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NonDlssGame_SwitchingToOptiScalerNr_TakesOutDfcAndItsRuntimes()
    {
        SeedDlss("v310.9.1", "NEW");
        var pkg = Path.Combine(ComponentStore.TagDir(Component.OptiScaler, "v9.9.9"), "pkg");
        Directory.CreateDirectory(pkg);
        File.WriteAllText(Path.Combine(pkg, "OptiScaler.dll"), "OPTI");
        File.WriteAllText(Path.Combine(pkg, "OptiScaler.ini"), "[DlssNr]\nEnabled=auto\n");
        var release = new ReleaseInfo { Tag = "v9.9.9" };
        ComponentStore.MarkComplete(Component.OptiScaler, release);
        _store.Opti = release;
        _store.ImportDfc(Package());
        var installer = new Installer(_store);
        await installer.InstallAsync(PlainGame(), PlainRoot, Dfc() with { DfcFrameGen = true }, null, default);

        var opti = new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true, ReShade = true, DlssNr = true, DlssNrId = _nr.Id, Proxy = "dxgi.dll" };
        await installer.InstallAsync(PlainGame(), PlainRoot, opti, null, default);
        Assert.Equal("OPTI", File.ReadAllText(P("dxgi.dll")));
        Assert.False(File.Exists(P(Sr)));
        Assert.False(File.Exists(P(Fg)));
        Assert.All(V4Set(), rel => Assert.False(File.Exists(P(rel)), rel));
        Assert.Empty(InstallManifest.Read(PlainRoot)!.DfcRuntimes);

        await installer.UninstallAsync(PlainGame(), PlainRoot, default);
        Assert.Equal(["Game.exe"], Directory.EnumerateFileSystemEntries(PlainRoot).Select(Path.GetFileName));
    }

    [Fact]
    public async Task NonDlssGame_RestoringDlss_KeepsTheRuntimesDfcNeeds()
    {
        SeedDlss("v310.9.1", "NEW");
        _store.ImportDfc(Package());
        var installer = new Installer(_store);
        await installer.InstallAsync(PlainGame(), PlainRoot, Dfc(), null, default);

        await installer.RestoreDlssAsync(PlainGame(), PlainRoot, default);
        Assert.Equal("NEW-" + Sr, File.ReadAllText(P(Sr)));
        Assert.Equal([Sr], InstallManifest.Read(PlainRoot)!.DfcRuntimes);
    }

    [Fact]
    public async Task NativeDlssGame_GetsNoExtraRuntimes_EvenWithTheFrameGenToggle()
    {
        SeedDlss("v310.9.1", "NEW");
        _store.ImportDfc(Package());
        var sub = Path.Combine(_root, "Engine", "DLSS");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, Sr), "GAMES-SR");
        File.WriteAllText(G("Game.exe"), "EXE");
        var game = GameScanner.Inspect(new GameEntry("Native", _root, "Manual"));
        Assert.True(Installer.HasNativeDlss(game, null, _root));

        await new Installer(_store).InstallAsync(game, _root, Dfc() with { DfcFrameGen = true }, null, default);
        Assert.False(File.Exists(G(Sr)));
        Assert.False(File.Exists(G(Fg)));
        Assert.Equal("GAMES-SR", File.ReadAllText(Path.Combine(sub, Sr)));
        var m = InstallManifest.Read(_root)!;
        Assert.True(m.Dfc);
        Assert.Empty(m.DfcRuntimes);
        Assert.DoesNotContain(m.Files, f => ComponentStore.DlssFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void NativeDlss_CountsStreamlineAndTheGamesOwnFiles_NotOurs()
    {
        var target = Path.Combine(_tmp, "g");
        var game = new GameInfo { Root = target, Dlss = [new DlssDll { Path = Path.Combine(target, Sr), Name = Sr }] };
        var m = new InstallManifest();
        Assert.True(Installer.HasNativeDlss(game, m, target));
        m.AddFile(Sr);
        Assert.False(Installer.HasNativeDlss(game, m, target));
        game.Dlss.Add(new DlssDll { Path = Path.Combine(target, Fg), Name = Fg });
        Assert.False(Installer.HasNativeDlss(game, m, target)); // frame generation alone is not DLSS SR
        game.Streamline.Add(new DlssDll { Path = Path.Combine(target, "sl.dlss.dll"), Name = "sl.dlss.dll" });
        Assert.True(Installer.HasNativeDlss(game, m, target));
    }

    [Fact]
    public void GamePage_ShowsTheNonDlssHintOrThe2xWarning_AndPassesTheFrameGenToggle()
    {
        var root = Path.Combine(_tmp, "vm-game"); Directory.CreateDirectory(root);
        var settings = new AppSettings { DefaultMode = InstallMode.ReShadeOnly };
        var services = new DLSSUpdater.ViewModels.Services { Settings = settings, Store = _store, Installer = new Installer(_store) };
        var vm = new DLSSUpdater.ViewModels.GameViewModel(new GameInfo { Id = root, Root = root, Name = "No DLSS game" }, services);
        Assert.False(vm.DfcWithoutNativeDlss);

        vm.Components.First(c => c.Key == "dfc").Enabled = true;
        Assert.True(vm.DfcWithoutNativeDlss);
        Assert.True(vm.ShowDfcNoDlssHint); // nothing imported yet: the hint, no warning
        Assert.False(vm.BuildOptions(false, false).DfcFrameGen);

        _store.ImportDfc(Release());
        vm.RefreshStatus();
        Assert.Equal(Installer.DfcNeedsNativeDlss("2.0.0"), vm.DfcNoDlssWarning);
        Assert.False(vm.ShowDfcNoDlssHint);

        _store.ImportDfc(Package());
        vm.RefreshStatus();
        Assert.False(vm.HasDfcNoDlssWarning);
        Assert.True(vm.ShowDfcNoDlssHint);
        vm.DfcFrameGen = true;
        Assert.True(settings.Games[vm.Id].DfcFrameGen);
        Assert.True(vm.BuildOptions(false, false).DfcFrameGen);

        var native = new DLSSUpdater.ViewModels.GameViewModel(new GameInfo { Id = root + "2", Root = root, Name = "DLSS game", Dlss = [new DlssDll { Name = Sr, Path = Path.Combine(root, "Engine", Sr) }] }, services);
        native.Components.First(c => c.Key == "dfc").Enabled = true;
        Assert.False(native.DfcWithoutNativeDlss);
        Assert.False(native.ShowDfcNoDlssHint);
    }

    // ---------- library ----------

    [Fact]
    public void FlatImportFromBeforeTheLibrary_MovesIntoItOnce_AndAnOldInstallRecordMatchesItByTag()
    {
        // The single-slot layout: the set and the cfg directly in components\dfc.
        var x64 = Path.Combine(Package(extras: false), "64-bit");
        foreach (var rel in V4Set(extras: false).Append(ComponentStore.DfcConfigFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(ComponentStore.DfcDir, rel))!);
            File.Copy(Path.Combine(x64, rel), Path.Combine(ComponentStore.DfcDir, rel));
        }
        var id = FileUtil.Sha256(Path.Combine(x64, ComponentStore.DfcFile));
        Assert.False(_store.DfcImported);

        _store.MigrateDfc();
        var build = Assert.Single(_store.DfcBuilds);
        Assert.Equal(id, build.Id);
        Assert.Equal("4.0.0", build.Name);
        Assert.Equal(V4Set(extras: false).Order(StringComparer.OrdinalIgnoreCase), ComponentStore.DfcInstallFiles(build));
        Assert.Equal(V4Cfg, File.ReadAllText(ComponentStore.DfcConfigOf(build)));
        Assert.Equal([id], Directory.EnumerateFileSystemEntries(ComponentStore.DfcDir).Select(Path.GetFileName));
        _store.MigrateDfc();
        Assert.Single(_store.DfcBuilds);

        // An install recorded before the library names the build by its tag only.
        var old = new InstallManifest { Dfc = true, DfcTag = $"4.0.0-{id[..12]}", Dir = _root };
        Assert.True(build.Matches(old.DfcId, old.DfcTag));
        Assert.Contains(_store.ProtectionReasons(LibraryKind.Dfc, new AppSettings(), [old], null), p => p.Id == id && p.Reason.StartsWith("installed in"));
    }

    [Fact]
    public void Default_FollowsTheNewestBuild_APickPins_AndARemovedPinGoesBackToNewest()
    {
        var settings = new AppSettings();
        _store.SelectedDfc = () => settings.DfcId;
        var v4 = _store.ImportDfc(Package());
        Assert.False(_store.AdoptImportedAddon(settings, AddonKind.Dfc, v4));
        Assert.Equal(v4.Id, Default.Id);

        // A newer import becomes the default without a settings write; an older one doesn't.
        var v5 = _store.ImportDfc(Package(major: 5));
        Assert.False(_store.AdoptImportedAddon(settings, AddonKind.Dfc, v5));
        var v2 = _store.ImportDfc(Release());
        Assert.False(_store.AdoptImportedAddon(settings, AddonKind.Dfc, v2));
        Assert.False(_store.EnsureAddonDefaults(settings));
        Assert.Null(settings.DfcId);
        Assert.Equal(v5.Id, Default.Id);

        settings.DfcId = v4.Id;
        Assert.False(_store.AdoptImportedAddon(settings, AddonKind.Dfc, v2));
        Assert.False(_store.EnsureAddonDefaults(settings));
        Assert.Equal(v4.Id, Default.Id);

        _store.RemoveAddon(AddonKind.Dfc, v4.Id);
        Assert.True(_store.EnsureAddonDefaults(settings));
        Assert.Null(settings.DfcId);
        Assert.Equal(v5.Id, Default.Id);
    }

    [Fact]
    public async Task PerGamePick_InstallsThatBuild_SwitchingReplacesTheSet_AndTheBuildDecidesNonDlssSupport()
    {
        SeedDlss("v310.9.1", "NEW");
        var v2 = _store.ImportDfc(Release());
        var v4 = _store.ImportDfc(Package());
        var installer = new Installer(_store);

        // In a game without DLSS the newest build installs, the game's 2.x pick is refused.
        var game = PlainGame();
        var before = PlainFiles();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(game, PlainRoot, Dfc() with { DfcId = v2.Id }, null, default));
        Assert.Equal(Installer.DfcNeedsNativeDlss("2.0.0"), error.Message);
        Assert.Equal(before, PlainFiles());
        await installer.InstallAsync(game, PlainRoot, Dfc(), null, default);
        Assert.Equal(v4.Id, InstallManifest.Read(PlainRoot)!.DfcId);

        // A pick that left the library is a clear error.
        var gone = await Assert.ThrowsAnyAsync<Exception>(() => installer.InstallAsync(_game, _root, Dfc() with { DfcId = new string('0', 64) }, null, default));
        Assert.Contains("no longer in the library", gone.Message);

        await installer.InstallAsync(_game, _root, Dfc() with { DfcId = v4.Id }, null, default);
        Assert.All(V4Set(), rel => Assert.True(File.Exists(G(rel)), rel));
        File.WriteAllText(G(ComponentStore.DfcConfigFile), "tuned=1\n");

        await installer.InstallAsync(_game, _root, Dfc() with { DfcId = v2.Id }, null, default);
        Assert.Equal(v2.Id, FileUtil.Sha256(G(ComponentStore.DfcFile)));
        Assert.Equal("BRIDGE", File.ReadAllText(G(ComponentStore.DfcBridgeFile)));
        Assert.False(File.Exists(G(ComponentStore.DfcPresentSupportFile)));
        Assert.False(Directory.Exists(G(ComponentStore.DfcNeuralDir)));
        Assert.All(ExtraFiles, rel => Assert.False(File.Exists(G(rel)), rel));
        Assert.Equal("tuned=1\n", File.ReadAllText(G(ComponentStore.DfcConfigFile)));
        var m = InstallManifest.Read(_root)!;
        Assert.Equal((v2.Id, v2.Tag), (m.DfcId, m.DfcTag));
        Assert.Equal([ComponentStore.DfcBridgeFile, ComponentStore.DfcFile], m.DfcFiles.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void GamePage_PickerListsTheBuilds_AndThePickDecidesWarningOptionsAndInstall()
    {
        var settings = new AppSettings { DefaultMode = InstallMode.ReShadeOnly };
        _store.SelectedDfc = () => settings.DfcId;
        var v2 = _store.ImportDfc(Release());
        var v5 = _store.ImportDfc(Package(major: 5));
        var root = Path.Combine(_tmp, "pick-game"); Directory.CreateDirectory(root);
        var services = new DLSSUpdater.ViewModels.Services { Settings = settings, Store = _store, Installer = new Installer(_store) };
        var vm = new DLSSUpdater.ViewModels.GameViewModel(new GameInfo { Id = root, Root = root, Name = "Pick game" }, services);
        vm.RebuildAddonChoices();
        vm.Components.First(c => c.Key == "dfc").Enabled = true;
        vm.DetailTab = "Config";
        DLSSUpdater.ViewModels.GameOptionViewModel Hud() => vm.DfcRows.Single(r => r.Label == "HUD protection");

        Assert.Equal(["Newest · 5.0.0", "2.0.0"], vm.DfcChoices.Select(c => c.Label));
        Assert.Null(vm.DfcChoice!.Tag);
        Assert.False(vm.HasDfcNoDlssWarning);
        Assert.True(Hud().IsAvailable);

        vm.DfcChoice = vm.DfcChoices.Single(c => c.Tag == v2.Id);
        Assert.Equal(v2.Id, settings.Games[vm.Id].DfcId);
        Assert.Equal(v2.Id, vm.BuildOptions(false, false).DfcId);
        Assert.Equal(Installer.DfcNeedsNativeDlss("2.0.0"), vm.DfcNoDlssWarning);
        Assert.False(Hud().IsAvailable); // the 2.x cfg has no output protection keys

        vm.DfcChoice = vm.DfcChoices[0];
        Assert.Null(settings.Games[vm.Id].DfcId);
        Assert.Null(vm.BuildOptions(false, false).DfcId);
        Assert.True(Hud().IsAvailable);
    }

    // ---------- DLSS5-Feeder next to Deep Fried Chicken ----------

    private const string FeederTag = "v1.19.0";

    /// <summary>A Feeder build already in the library (what the download leaves), so the install needs no network.</summary>
    private void SeedFeeder()
    {
        var dir = Path.Combine(FeederLibrary.Root, FeederTag);
        foreach (var (rel, text) in new[]
        {
            ("dlss5-feed.addon64", "F64"), ("dlss5-feed.addon32", "F32"), (@"host64\dlss5-feed-host64.exe", "HOST"), (@"reshade-shaders\Shaders\DLSS5_Feed.fx", "FX"),
        })
        {
            var path = Path.Combine(dir, "pkg", rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        File.WriteAllText(Path.Combine(dir, ".complete"),
            System.Text.Json.JsonSerializer.Serialize(new CachedComponent { Tag = FeederTag, Fetched = DateTime.UtcNow }, JsonCtx.Default.CachedComponent));
    }

    [Fact]
    public async Task Feeder_InstallsAfterDfc_InTheSameRun_AndUninstallLeavesTheGameAsItWas()
    {
        SeedDlss("v310.9.1", "NEW");
        SeedFeeder();
        _store.ImportDfc(Package());
        var game = PlainGame();
        var installer = new Installer(_store);

        await installer.InstallAsync(game, PlainRoot, Dfc() with { Feeder = true, FeederId = FeederTag }, null, default);

        Assert.Equal("F64", File.ReadAllText(P("dlss5-feed.addon64")));
        Assert.Equal("FX", File.ReadAllText(P(@"reshade-shaders\Shaders\DLSS5_Feed.fx")));
        Assert.True(File.Exists(P(ComponentStore.DfcFile)));
        var m = InstallManifest.Read(PlainRoot)!;
        Assert.True(m.Dfc);
        Assert.Equal(FeederTag, m.Component(ComponentRegistry.Feeder)!.BuildId);
        Assert.Contains("dlss5-feed.addon64", m.Files, StringComparer.OrdinalIgnoreCase);

        // Installing again keeps both: DFC's own foreign-add-on sweep must not take Feeder's files, they are ours.
        await installer.InstallAsync(PlainGame(), PlainRoot, Dfc() with { Feeder = true }, null, default);
        Assert.Equal("F64", File.ReadAllText(P("dlss5-feed.addon64")));
        Assert.NotNull(InstallManifest.Read(PlainRoot)!.Component(ComponentRegistry.Feeder));

        await installer.UninstallAsync(game, PlainRoot, default);
        Assert.Equal(["Game.exe"], Directory.EnumerateFileSystemEntries(PlainRoot).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Feeder_ARunWithoutIt_TakesItOut_AndDfcStays()
    {
        SeedDlss("v310.9.1", "NEW");
        SeedFeeder();
        _store.ImportDfc(Package());
        var game = PlainGame();
        var installer = new Installer(_store);
        await installer.InstallAsync(game, PlainRoot, Dfc() with { Feeder = true }, null, default);

        await installer.InstallAsync(PlainGame(), PlainRoot, Dfc(), null, default);

        Assert.False(File.Exists(P("dlss5-feed.addon64")));
        Assert.False(File.Exists(P(@"reshade-shaders\Shaders\DLSS5_Feed.fx")));
        Assert.True(File.Exists(P(ComponentStore.DfcFile)));
        Assert.Null(InstallManifest.Read(PlainRoot)!.Component(ComponentRegistry.Feeder));
    }

    [Fact]
    public async Task Feeder_WithoutDfc_IsRefusedBeforeAnythingIsTouched()
    {
        SeedFeeder();
        var game = PlainGame();
        var installer = new Installer(_store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.InstallAsync(game, PlainRoot, Dfc() with { Dfc = false, Feeder = true }, null, default));

        Assert.Contains("DLSS5-Feeder needs one neural consumer", ex.Message);
        Assert.Equal(["Game.exe"], Directory.EnumerateFileSystemEntries(PlainRoot).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Dfc_NextToAForeignDx11Bridge_NeedsConfirmation_WithTheOneConsumerReason()
    {
        _store.ImportDfc(Package());
        var game = PlainGame();
        File.WriteAllText(P("dlss5-bridge.addon64"), "BRIDGE");
        var installer = new Installer(_store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(game, PlainRoot, Dfc(), null, default));

        Assert.Contains("don't work well in the same game", ex.Message);
        Assert.Contains("DX11 bridge", ex.Message);
        Assert.False(File.Exists(P(ComponentStore.DfcFile)));

    }
}
