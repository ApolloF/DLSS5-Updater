using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>ReShade installed into a (temp) game: builds from the library, bitness, foreign dlls backed up and restored, the Vulkan route, the ini template.</summary>
public sealed class ReShadeInstallTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-rsinst-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly string _target;
    private readonly ComponentStore _store;
    private readonly ReShadeBuild _build;

    internal sealed class FakeLayer(VulkanLayerState state) : IVulkanLayerHost
    {
        public string LayerDir => @"C:\ProgramData\ReShade";
        public bool FileExists(string path) => state != VulkanLayerState.NotInstalled;
        public string? ReadText(string path) => state == VulkanLayerState.Installed && path.EndsWith(VulkanLayer.MarkerFile)
            ? """{"version":"6.8.0","dll64":"aa","dll32":null}""" : null;
        public void Copy(string source, string dest) => throw new InvalidOperationException("the installer must not write the layer");
        public void WriteText(string path, string text) => throw new InvalidOperationException("the installer must not write the layer");
        public void Delete(string path) => throw new InvalidOperationException("the installer must not write the layer");
        public void DeleteDirectoryIfEmpty(string dir) { }
        public string Sha256(string path) => "";
        public void AllowAppPackages(string dir) { }
        public bool IsRegistered(string manifestPath, int bits) => state != VulkanLayerState.NotInstalled && bits == 64;
        public void Register(string manifestPath, int bits) => throw new InvalidOperationException();
        public void Unregister(string manifestPath, int bits) => throw new InvalidOperationException();
    }

    public ReShadeInstallTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, "bin");
        Directory.CreateDirectory(_target);
        File.WriteAllText(Path.Combine(_target, "Game.exe"), "EXE");
        _store = new ComponentStore(new GitHubClient(), () => true);
        _build = _store.ImportReShade(ReShadeFixtures.Folder(Path.Combine(_tmp, "src"), "a", "6.8.0.2155"));
        _store.SelectedReShade = () => _build.Id;
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_tmp, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_tmp, true);
        }
        catch (IOException) { }
    }

    private GameInfo Game(int bits = 64, GraphicsApis apis = GraphicsApis.Dx11)
    {
        var g = GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));
        g.Tech.Bits = bits;
        g.Tech.Apis = apis;
        return g;
    }

    private static InstallOptions Opts(string proxy = "dxgi.dll", IReadOnlyList<IniOverride>? template = null, bool vulkan = false, string? build = null) => new()
    {
        Mode = InstallMode.ReShadeOnly, ReShade = true, Proxy = proxy, ReShadeTemplate = template ?? [], ReShadeVulkan = vulkan, ReShadeBuildPick = build,
    };

    private Installer Installer(VulkanLayerState layer = VulkanLayerState.NotInstalled) => new(_store) { VulkanHost = new FakeLayer(layer) };

    private string Path_(string name) => Path.Combine(_target, name);

    [Fact]
    public async Task A64BitGame_GetsReShade64_UnderTheApiName_AndA32BitGame_GetsReShade32()
    {
        await Installer().InstallAsync(Game(64), _target, Opts("dxgi.dll"), null, default);
        Assert.Equal(FileUtil.Sha256(ComponentStore.ReShadeFilePath(_build, 64)), FileUtil.Sha256(Path_("dxgi.dll")));
        var m = InstallManifest.Load(_target)!;
        Assert.Equal(_build.Id, m.ReShadeId);
        Assert.Equal("dxgi.dll", m.Proxy);
        await Installer().UninstallAsync(Game(64), _target, default);

        await Installer().InstallAsync(Game(32, GraphicsApis.Dx9), _target, Opts("d3d9.dll"), null, default);
        Assert.Equal(FileUtil.Sha256(ComponentStore.ReShadeFilePath(_build, 32)), FileUtil.Sha256(Path_("d3d9.dll")));
        Assert.False(File.Exists(Path_("dxgi.dll")));
    }

    [Fact]
    public async Task BesideOptiScaler_ItIsAlwaysReShade64Dll()
    {
        var o = new InstallOptions { Mode = InstallMode.OptiScaler, ReShade = true, Proxy = "dxgi.dll" };
        await Installer().InstallAsync(Game(32, GraphicsApis.Dx9), _target, o, null, default);
        Assert.Equal(FileUtil.Sha256(ComponentStore.ReShadeFilePath(_build, 64)), FileUtil.Sha256(Path_("ReShade64.dll")));
    }

    [Fact]
    public async Task AGamesOwnBuildPick_DecidesTheFile()
    {
        var other = _store.ImportReShade(ReShadeFixtures.Folder(Path.Combine(_tmp, "src"), "b", "6.5.1.1"));
        await Installer().InstallAsync(Game(), _target, Opts(build: other.Id), null, default);
        Assert.Equal(other.Sha64, FileUtil.Sha256(Path_("dxgi.dll")));
        Assert.Equal(other.Id, InstallManifest.Load(_target)!.ReShadeId);
    }

    [Theory]
    [InlineData("Special K")]
    [InlineData("DXVK")]
    [InlineData("unknown")]
    public async Task AForeignDll_IsBackedUp_ThenRestoredOnUninstall(string owner)
    {
        var foreign = Path_("dxgi.dll");
        var bytes = owner switch
        {
            "Special K" => "SK_ImGui_ControlPanel Special K",
            "DXVK" => "DXVK_CONFIG_FILE",
            _ => "something else",
        };
        File.WriteAllText(foreign, bytes);
        var game = Game();
        var warning = ReShadeInstall.ForeignWarning(game, _target, "dxgi.dll", null);
        Assert.NotNull(warning);
        Assert.DoesNotContain('\n', warning);

        await Installer().InstallAsync(game, _target, Opts(), null, default);
        Assert.Equal(FileUtil.Sha256(ComponentStore.ReShadeFilePath(_build, 64)), FileUtil.Sha256(foreign));
        // Our own file is no warning (an update), and the backup holds the original.
        Assert.Null(ReShadeInstall.ForeignWarning(game, _target, "dxgi.dll", InstallManifest.Load(_target)));
        Assert.Contains(InstallManifest.Load(_target)!.Backups, b => b.Original.EndsWith("dxgi.dll"));

        await Installer().UninstallAsync(game, _target, default);
        Assert.Equal(bytes, File.ReadAllText(foreign));
    }

    [Fact]
    public void NoForeignWarning_ForAnEmptySlotOrReShadeItself()
    {
        var game = Game();
        Assert.Null(ReShadeInstall.ForeignWarning(game, _target, "dxgi.dll", null));
        File.Copy(ComponentStore.ReShadeFilePath(_build, 64), Path_("dxgi.dll"));
        Assert.Null(ReShadeInstall.ForeignWarning(game, _target, "dxgi.dll", null));
    }

    // ---- ini template ----

    [Fact]
    public async Task Template_IsSeeded_KeepsTheGamesOwnValues_AndLeavesTheAddonSectionsAlone()
    {
        File.WriteAllText(Path_("ReShade.ini"), "[GENERAL]\nEffectSearchPaths=C:\\mine\n[RENODX-DLSS]\nSomething=1\n");
        var template = ReShadeIniTemplate.Parse(ReShadeIniTemplate.DefaultText + "\n[RENODX-DLSS]\nSomething=9\n[RenoDX.DLSS5]\nNRHookPoint=5\n[Uplift]\nEnabled=1\n");
        Assert.DoesNotContain(template, t => t.Section.StartsWith("RENODX", StringComparison.OrdinalIgnoreCase) || t.Section is "RenoDX.DLSS5" or "Uplift");

        await Installer().InstallAsync(Game(), _target, Opts(template: template), null, default);
        var ini = IniFile.Load(Path_("ReShade.ini"));
        Assert.Equal("C:\\mine", ini.Get("GENERAL", "EffectSearchPaths"));          // the game's value kept
        Assert.Equal(@".\reshade-shaders\Textures\**", ini.Get("GENERAL", "TextureSearchPaths"));   // a missing one seeded
        Assert.Equal(@".\reshade-screenshots", ini.Get("SCREENSHOT", "SavePath"));
        Assert.Equal("36,0,0,0", ini.Get("INPUT", "KeyOverlay"));
        Assert.Equal("44,0,0,0", ini.Get("INPUT", "KeyScreenshot"));
        Assert.Equal("Generic Depth,Effect Runtime Sync", ini.Get("ADDON", "DisabledAddons"));
        Assert.Equal("1", ini.Get("RENODX-DLSS", "Something"));                     // another component's section untouched
        Assert.Null(ini.Get("RenoDX.DLSS5", "NRHookPoint"));
        Assert.Null(ini.Get("Uplift", "Enabled"));

        await Installer().UninstallAsync(Game(), _target, default);
        Assert.Equal("[GENERAL]\nEffectSearchPaths=C:\\mine\n[RENODX-DLSS]\nSomething=1\n", File.ReadAllText(Path_("ReShade.ini")));
    }

    [Fact]
    public async Task Template_AFreshGame_GetsTheIni_AndUninstallRemovesIt()
    {
        await Installer().InstallAsync(Game(), _target, Opts(template: ReShadeIniTemplate.Parse(ReShadeIniTemplate.DefaultText)), null, default);
        Assert.True(File.Exists(Path_("ReShade.ini")));
        await Installer().UninstallAsync(Game(), _target, default);
        Assert.False(File.Exists(Path_("ReShade.ini")));
        Assert.False(File.Exists(Path_("dxgi.dll")));
    }

    [Fact]
    public async Task Template_AGamesOwnOverride_BeatsTheTemplate()
    {
        var o = Opts(template: ReShadeIniTemplate.Parse(ReShadeIniTemplate.DefaultText)) with { ReShadeOverrides = [new("SCREENSHOT", "SavePath", @"D:\shots")] };
        await Installer().InstallAsync(Game(), _target, o, null, default);
        Assert.Equal(@"D:\shots", IniFile.Load(Path_("ReShade.ini")).Get("SCREENSHOT", "SavePath"));
    }

    // ---- Vulkan ----

    [Fact]
    public async Task Vulkan_PlacesNoDll_OnlyTheIniThatSwitchesReShadeOn()
    {
        var o = Opts(vulkan: true, template: ReShadeIniTemplate.Parse(ReShadeIniTemplate.DefaultText));
        await Installer(VulkanLayerState.Installed).InstallAsync(Game(64, GraphicsApis.Vulkan), _target, o, null, default);
        Assert.False(File.Exists(Path_("dxgi.dll")));
        Assert.False(File.Exists(Path_("ReShade64.dll")));
        var ini = IniFile.Load(Path_("ReShade.ini"));
        Assert.Contains("RESHADE_DEPTH_INPUT_IS_REVERSED=1", ini.Get("GENERAL", "PreprocessorDefinitions"));
        var m = InstallManifest.Load(_target)!;
        Assert.True(m.ReShadeVulkan);
        Assert.True(m.ReShade);
        Assert.Null(m.Proxy);

        await Installer(VulkanLayerState.Installed).UninstallAsync(Game(64, GraphicsApis.Vulkan), _target, default);
        Assert.False(File.Exists(Path_("ReShade.ini")));
    }

    [Fact]
    public async Task Vulkan_WithoutTheLayer_SaysWhatToDo_AndChangesNothing()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Installer(VulkanLayerState.NotInstalled).InstallAsync(Game(64, GraphicsApis.Vulkan), _target, Opts(vulkan: true), null, default));
        Assert.Contains("Vulkan layer", ex.Message);
        Assert.Null(InstallManifest.Load(_target));
        Assert.False(File.Exists(Path_("ReShade.ini")));
    }

    [Fact]
    public async Task Vulkan_SwitchingAnExistingProxyInstallToTheLayer_TakesTheOldDllOut()
    {
        await Installer().InstallAsync(Game(), _target, Opts("dxgi.dll"), null, default);
        Assert.True(File.Exists(Path_("dxgi.dll")));
        await Installer(VulkanLayerState.Installed).InstallAsync(Game(64, GraphicsApis.Vulkan), _target, Opts("dxgi.dll", vulkan: true), null, default);
        Assert.False(File.Exists(Path_("dxgi.dll")));
        Assert.True(InstallManifest.Load(_target)!.ReShadeVulkan);
    }

    [Fact]
    public async Task Vulkan_BesideOptiScaler_IsRefused()
    {
        var o = new InstallOptions { Mode = InstallMode.OptiScaler, ReShade = true, ReShadeVulkan = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Installer(VulkanLayerState.Installed).InstallAsync(Game(), _target, o, null, default));
    }

    // ---- existing installs ----

    [Fact]
    public void AnOlderRecord_WithoutTheNewFields_StillReadsAndShowsTheShaAsItsBuild()
    {
        var m = new InstallManifest { ReShade = true, Mode = InstallMode.ReShadeOnly, Proxy = "dxgi.dll", ReShadeSha = "abc" };
        m.Files.Add("bin\\dxgi.dll");
        var c = LegacyComponents.Project(m).Single(x => x.Id == ComponentRegistry.ReShade);
        Assert.Equal("abc", c.BuildId);
        m.ReShadeId = "def";
        Assert.Equal("def", LegacyComponents.Project(m).Single(x => x.Id == ComponentRegistry.ReShade).BuildId);
        m.ReShadeVulkan = true;
        Assert.Equal("true", LegacyComponents.Project(m).Single(x => x.Id == ComponentRegistry.ReShade).Extras["Vulkan"]);
    }
}

/// <summary>The editable ReShade.ini template file in the data folder.</summary>
public sealed class ReShadeIniTemplateTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-rstpl-" + Guid.NewGuid().ToString("N")[..8]);

    public ReShadeIniTemplateTests()
    {
        AppPaths.Root = _tmp;
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); }
        catch (IOException) { }
    }

    [Fact]
    public void TheDefaults_AreWrittenOnce_AndTheUsersEditsKept()
    {
        var keys = ReShadeIniTemplate.Load().Select(k => k.Id).ToList();
        Assert.Contains("SCREENSHOT/SavePath", keys);
        Assert.Contains("INPUT/KeyOverlay", keys);
        Assert.Contains("INPUT/KeyScreenshot", keys);
        Assert.Contains("GENERAL/EffectSearchPaths", keys);
        Assert.Contains("GENERAL/TextureSearchPaths", keys);
        Assert.Contains("ADDON/DisabledAddons", keys);

        File.WriteAllText(AppPaths.ReShadeTemplate, "[SCREENSHOT]\nSavePath=D:\\shots\n; a comment\n[INPUT]\nKeyOverlay=\n");
        var edited = ReShadeIniTemplate.Load();
        Assert.Equal([("SCREENSHOT/SavePath", @"D:\shots")], edited.Select(k => (k.Id, k.Value)));   // empty values are not managed

        ReShadeIniTemplate.Reset();
        Assert.Contains("INPUT/KeyOverlay", ReShadeIniTemplate.Load().Select(k => k.Id));
    }

    [Fact]
    public void OwnedSections_AreIgnored_WhateverTheCase()
    {
        foreach (var s in new[] { "RENODX-DLSS", "renodx-dlss-preset1", "RenoDX.DLSS5", "UPLIFT", "dfc" })
            Assert.True(ReShadeIniTemplate.IsOwnedSection(s), s);
        foreach (var s in new[] { "GENERAL", "ADDON", "INPUT" })
            Assert.False(ReShadeIniTemplate.IsOwnedSection(s), s);
    }
}
