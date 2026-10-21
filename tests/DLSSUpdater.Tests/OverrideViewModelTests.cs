using System.Net;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>
/// The pickers of the game page that hold an override: Detection, the HDR mod list, the Engine.ini path, the own ini values and the
/// proxy name. Each shows "Auto (X)" first, saves what the user picks, and Reset to auto goes back.
/// </summary>
public sealed class OverrideViewModelTests : IDisposable
{
    private sealed class NoWeb : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-ovvm-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly ComponentStore _store;
    private readonly AppSettings _settings = new();

    public OverrideViewModelTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        Directory.CreateDirectory(Path.Combine(_root, @"Game\Binaries\Win64"));
        File.WriteAllText(Path.Combine(_root, @"Game\Binaries\Win64\Game-Win64-Shipping.exe"), "EXE");
        File.WriteAllText(Path.Combine(_root, "Game.exe"), "BOOT");
        File.WriteAllText(AppPaths.CatalogFile, HdrFixtures.Json());
        _store = new ComponentStore(new GitHubClient(), () => true);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); }
        catch (IOException) { }
    }

    private GameViewModel Vm(Action<Services>? tweak = null, Action<GameInfo>? game = null)
    {
        var gh = new GitHubClient(new NoWeb());
        var installer = new Installer(_store);
        var s = new Services
        {
            Settings = _settings, Store = _store, Installer = installer,
            Catalog = new CatalogState(new CatalogClient(gh)), Hdr = new HdrService(_store, installer, new CatalogDownloader(gh)),
        };
        s.Catalog.LoadCached();
        tweak?.Invoke(s);
        var info = GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));
        info.Tech = new GameTech { Engine = GameEngine.Unreal, EngineVersion = "5.3.2", Bits = 64, Apis = GraphicsApis.Dx12, UeProject = "Game" };
        game?.Invoke(info);
        return new GameViewModel(info, s);
    }

    private static string Auto(AutoPick p) => p.Choices[0].Label;

    // ---------- Detection ----------

    [Fact]
    public void Every_detection_picker_starts_on_Auto_with_the_detected_value_and_says_so()
    {
        var vm = Vm();
        var d = vm.Detection;

        Assert.Equal("Auto (DX12)", Auto(d.Api));
        Assert.Equal("Auto (64-bit)", Auto(d.Bits));
        Assert.Equal("Auto (Unreal Engine 4/5)", Auto(d.Engine));
        Assert.Equal("Auto (5.3.2)", Auto(d.UeVersion));
        Assert.StartsWith("Auto (", Auto(d.MainExe));
        Assert.StartsWith("Auto (", Auto(d.ModFolder));
        Assert.All(new[] { d.Api, d.Bits, d.Engine, d.UeVersion, d.MainExe, d.ModFolder }, p =>
        {
            Assert.Null(p.Selected!.Tag);
            Assert.False(p.IsSet);
            Assert.Equal("auto", p.StateText);
        });
        Assert.False(d.AnySet);
        Assert.DoesNotContain("set by you", vm.TechBadge);
    }

    [Fact]
    public void Picking_a_value_saves_it_reaches_the_game_and_the_badge_and_Reset_returns_to_the_scan()
    {
        var vm = Vm();

        vm.Detection.Bits.Selected = vm.Detection.Bits.Choices.Single(c => c.Tag == "32");
        vm.Detection.Api.Selected = vm.Detection.Api.Choices.Single(c => c.Label == "DX9");

        Assert.Equal(32, _settings.Games[vm.Id].Tech!.Bits);
        Assert.Equal(GraphicsApis.Dx9, _settings.Games[vm.Id].Tech!.Apis);
        Assert.Equal(32, vm.Info.Effective.Bits);
        Assert.Equal(GraphicsApis.Dx9, vm.Info.Effective.PrimaryApi);
        Assert.Contains("DX9 · 32-bit", vm.TechBadge);
        Assert.EndsWith("set by you", vm.TechBadge);
        Assert.True(vm.Detection.Bits.IsSet);
        Assert.Equal("set by you", vm.Detection.Bits.StateText);
        // The scan result stays what it was: the Auto entry still names it.
        Assert.Equal("Auto (64-bit)", Auto(vm.Detection.Bits));
        Assert.Equal("d3d9.dll", ReShadeNaming.Resolve(vm.Info.Effective, false, null).File);

        vm.Detection.Bits.ResetCommand.Execute(null);

        Assert.Equal(64, vm.Info.Effective.Bits);
        Assert.Equal(GraphicsApis.Dx9, vm.Info.Effective.PrimaryApi);
        Assert.False(vm.Detection.Bits.IsSet);

        vm.Detection.ResetAllCommand.Execute(null);

        Assert.Null(_settings.Games[vm.Id].Tech);
        Assert.Equal(GraphicsApis.Dx12, vm.Info.Effective.PrimaryApi);
        Assert.False(vm.Detection.AnySet);
        Assert.DoesNotContain("set by you", vm.TechBadge);
    }

    [Fact]
    public void The_Unreal_version_row_follows_the_effective_engine()
    {
        var vm = Vm();
        Assert.True(vm.Detection.UeVersion.Visible);

        vm.Detection.Engine.Selected = vm.Detection.Engine.Choices.Single(c => c.Tag == nameof(GameEngine.Unity));

        Assert.False(vm.Detection.UeVersion.Visible);
        Assert.False(vm.Info.Effective.IsUnreal);

        vm.Detection.Engine.ResetCommand.Execute(null);
        vm.Detection.UeVersion.Selected = vm.Detection.UeVersion.Choices.Single(c => c.Tag == "4.27");

        Assert.True(vm.Detection.UeVersion.Visible);
        Assert.Equal(4, vm.Info.Effective.UeMajor);
    }

    [Fact]
    public void The_saved_override_is_applied_when_the_game_loads_and_the_HDR_pick_follows_it()
    {
        var gameId = GameInfo.MakeId(_root);
        _settings.For(gameId).Tech = new TechOverride { Engine = GameEngine.Unity, Bits = 32 };

        var vm = Vm();

        Assert.Equal(HdrKind.UnityGeneric, vm.Hdr.Choice!.Kind);
        Assert.Equal("x86", vm.Hdr.Choice.File!.Arch);
        Assert.True(vm.Detection.Engine.IsSet);

        vm.Detection.ResetAllCommand.Execute(null);

        Assert.Equal(HdrKind.UeExtended, vm.Hdr.Choice!.Kind);
    }

    [Fact]
    public void A_main_exe_pick_asks_for_a_rescan_because_API_and_bitness_come_from_that_exe()
    {
        var asked = 0;
        var vm = Vm(s => s.Rescan = _ => { asked++; return Task.CompletedTask; });
        var other = vm.Detection.MainExe.Choices.FirstOrDefault(c => c.Tag is not null);
        Assert.NotNull(other);

        vm.Detection.MainExe.Selected = other;

        Assert.Equal(1, asked);
        Assert.Equal(other!.Tag, _settings.Games[vm.Id].Tech!.MainExe);

        vm.Detection.MainExe.ResetCommand.Execute(null);

        Assert.Equal(2, asked);
        Assert.Null(_settings.Games[vm.Id].Tech);
    }

    [Fact]
    public void A_mod_folder_pick_becomes_the_install_location_unless_the_game_has_an_install()
    {
        var other = Path.Combine(_root, "Mods");
        Directory.CreateDirectory(other);
        var vm = Vm();

        vm.Detection.ModFolder.Selected = vm.Detection.ModFolder.Choices.FirstOrDefault(c => c.Tag is not null) ?? new DlssChoice(other, "Mods");
        Assert.Equal(_settings.Games[vm.Id].Tech!.ModFolder, vm.SelectedTarget!.Dir, ignoreCase: true);

        vm.Detection.ModFolder.ResetCommand.Execute(null);
        Assert.Null(_settings.Games[vm.Id].Tech);
    }

    // ---------- HDR mod list ----------

    [Fact]
    public void The_HDR_mod_list_starts_on_Auto_naming_the_pick_then_holds_the_catalog_and_Reset_returns_to_it()
    {
        var vm = Vm();
        var pick = vm.Hdr.ModPick;

        Assert.Equal("Auto (Unreal Engine Extended)", Auto(pick));
        Assert.Contains(pick.Choices, c => c.Tag == "hades" && c.Label == "Hades");

        pick.Selected = pick.Choices.Single(c => c.Tag == "hades");

        Assert.Equal("hades", _settings.Games[vm.Id].HdrMod);
        Assert.Equal("hades", vm.Hdr.Choice!.ModId);
        Assert.True(pick.IsSet);

        pick.ResetCommand.Execute(null);

        Assert.Null(_settings.Games[vm.Id].HdrMod);
        Assert.Equal(HdrKind.UeExtended, vm.Hdr.Choice!.Kind);
        Assert.False(pick.IsSet);
    }

    [Fact]
    public void Searching_shortens_the_list_but_keeps_Auto_and_the_current_pick()
    {
        var vm = Vm();
        var pick = vm.Hdr.ModPick;
        pick.Selected = pick.Choices.Single(c => c.Tag == "hades");

        vm.Hdr.ModSearch = "alien";

        Assert.StartsWith("Auto (", Auto(pick));
        Assert.Contains(pick.Choices, c => c.Tag == "alien-isolation");
        Assert.Contains(pick.Choices, c => c.Tag == "hades");
        Assert.DoesNotContain(pick.Choices, c => c.Tag == "deaths-door");
        Assert.Equal("hades", pick.Selected!.Tag);
        Assert.Equal("hades", _settings.Games[vm.Id].HdrMod);
    }

    [Fact]
    public void An_imported_add_on_is_listed_picked_installed_from_the_library_and_dropped_on_Reset()
    {
        var src = Path.Combine(_tmp, "src", "renodx-mine.addon64");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        File.WriteAllText(src, "MZ mine");
        var build = _store.ImportHdrBuild(src);
        var vm = Vm();

        Assert.Contains(vm.Hdr.ModPick.Choices, c => c.Tag == "custom:" + build.Id && c.Label.Contains("renodx-mine.addon64"));
        vm.Hdr.ModPick.Selected = vm.Hdr.ModPick.Choices.Single(c => c.Tag == "custom:" + build.Id);

        Assert.Equal(HdrManual.CustomMod, _settings.Games[vm.Id].HdrMod);
        Assert.Equal(build.Id, _settings.Games[vm.Id].BuildPick(ComponentRegistry.RenoDxHdr));
        Assert.Equal(HdrKind.Custom, vm.Hdr.Choice!.Kind);
        Assert.True(vm.Hdr.Choice.IsOffered);
        Assert.False(vm.Hdr.ShowBuildChoices);

        vm.Hdr.ModPick.ResetCommand.Execute(null);

        Assert.Null(_settings.Games[vm.Id].HdrMod);
        Assert.Null(_settings.Games[vm.Id].BuildPick(ComponentRegistry.RenoDxHdr));
        Assert.Equal(HdrKind.UeExtended, vm.Hdr.Choice!.Kind);
    }

    [Fact]
    public void An_imported_add_on_that_left_the_library_says_so_instead_of_failing()
    {
        _settings.For(GameInfo.MakeId(_root)).SetHdrMod(HdrManual.CustomMod);
        _settings.For(GameInfo.MakeId(_root)).SetBuildPick(ComponentRegistry.RenoDxHdr, new string('a', 64));

        var vm = Vm();

        Assert.False(vm.Hdr.CanInstall);
        Assert.Contains("no longer in the library", vm.Hdr.PickedText);
    }

    // ---------- Engine.ini path and own ini values ----------

    [Fact]
    public void A_typed_Engine_ini_path_is_saved_a_path_outside_the_allowed_folders_is_refused_and_Reset_clears_it()
    {
        var vm = Vm();
        var local = InstallManifest.KnownFolder("|localappdata|") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        vm.Hdr.EngineIniText = Path.Combine(local, "Chosen", "Engine.ini");
        Assert.Equal("", vm.Hdr.EngineIniProblem);
        Assert.Equal(Path.Combine(local, "Chosen", "Engine.ini"), _settings.Games[vm.Id].EngineIniPath);
        Assert.True(vm.Hdr.HasEngineIniOverride);
        Assert.Contains(Path.Combine(local, "Chosen", "Engine.ini"), vm.Hdr.EngineIniNow);

        vm.Hdr.EngineIniText = @"C:\Windows\System32\Engine.ini";
        Assert.NotEqual("", vm.Hdr.EngineIniProblem);
        Assert.Equal(Path.Combine(local, "Chosen", "Engine.ini"), _settings.Games[vm.Id].EngineIniPath);

        vm.Hdr.ResetEngineIniCommand.Execute(null);
        Assert.Null(_settings.Games[vm.Id].EngineIniPath);
        Assert.False(vm.Hdr.HasEngineIniOverride);
    }

    [Fact]
    public void Own_ini_rows_are_saved_as_typed_flagged_when_they_cannot_be_written_and_removed_with_the_row()
    {
        var vm = Vm();

        vm.Hdr.AddIniRowCommand.Execute(null);
        var row = Assert.Single(vm.Hdr.IniRows);
        Assert.True(row.HasProblem);

        row.Key = "ToneMapPeakNits";
        row.Value = "800";
        Assert.False(row.HasProblem);
        Assert.Equal("800", Assert.Single(_settings.Games[vm.Id].HdrIni!).Value);

        row.Section = "GENERAL";
        Assert.True(row.HasProblem);
        Assert.Contains("renodx", row.Problem);
        Assert.Empty(HdrUserEdits.Clean(_settings.Games[vm.Id].HdrIni));

        vm.Hdr.RemoveIniRowCommand.Execute(row);
        Assert.Empty(vm.Hdr.IniRows);
        Assert.Null(_settings.Games[vm.Id].HdrIni);
    }

    [Fact]
    public void Own_ini_rows_come_back_when_the_game_page_loads_again_and_change_the_status_line()
    {
        var gameId = GameInfo.MakeId(_root);
        _settings.For(gameId).HdrIni = [new HdrIniEdit("reshade", "renodx", "MyKey", "1")];

        var vm = Vm();

        var row = Assert.Single(vm.Hdr.IniRows);
        Assert.Equal("MyKey", row.Key);
    }

    // ---------- Proxy file name ----------

    [Fact]
    public void The_proxy_picker_shows_the_automatic_name_and_Reset_returns_to_it()
    {
        var vm = Vm();
        Assert.False(vm.ProxyDiffersFromAuto);
        Assert.Equal("Auto: dxgi.dll", vm.ProxyAutoText);

        vm.Proxy = "winmm.dll";
        Assert.True(vm.ProxyDiffersFromAuto);

        vm.ResetProxyCommand.Execute(null);

        Assert.Equal("dxgi.dll", vm.Proxy);
        Assert.False(vm.ProxyDiffersFromAuto);
    }

    [Fact]
    public void The_detection_help_topic_exists_with_advice()
    {
        Assert.NotNull(HelpTopics.Get("detection-override"));
        Assert.NotNull(HelpTopics.AdviceFor("detection-override"));
    }
}
