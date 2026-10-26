using System.Net;
using System.Text;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>The Frame limit section of a game page: Auto and the manual choice, why a method is unavailable, the hint, apply and update detection.</summary>
public class FrameLimitViewModelTests : IDisposable
{
    private sealed class FakeWeb(Func<string, string?> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(body(request.RequestUri!.ToString()) is { } text
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(text)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private const uint Fps = NvidiaProfileCatalog.FrameRateLimit;
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-flvm-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root, _target, _local;
    private readonly ComponentStore _store;
    private readonly AppSettings _settings = new();
    private readonly FakeDrs _drs = new();
    private string _re64 = FrameLimitFixtures.Re64;
    private int _hz = 144;

    public FrameLimitViewModelTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, @"Game\Binaries\Win64");
        _local = Path.Combine(_tmp, "LocalAppData");
        InstallManifest.KnownFolder = tag => tag switch { "|localappdata|" => _local, "|documents|" => Path.Combine(_tmp, "Documents"), _ => null };
        _store = new ComponentStore(new GitHubClient(), () => true);
        var build = _store.ImportReShade(ReShadeFixtures.Folder(Path.Combine(_tmp, "src"), "a", "6.8.0.2155"));
        _store.SelectedReShade = () => build.Id;
        _drs.Define(Fps);
        Write(Path.Combine(_target, "Game-Win64-Shipping.exe"), "EXE");
        Write(Path.Combine(_root, "Game.exe"), "BOOT");
    }

    public void Dispose()
    {
        InstallManifest.KnownFolder = InstallManifest.DefaultKnownFolder;
        try
        {
            foreach (var f in Directory.EnumerateFiles(_tmp, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_tmp, true);
        }
        catch (IOException) { }
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private GameInfo Game(Action<GameInfo>? tweak = null)
    {
        var g = GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));
        g.Tech.Bits = 64;
        g.Tech.Engine = GameEngine.Unknown;
        g.Tech.UeProject = null;
        g.Tech.Apis = GraphicsApis.Dx12;
        tweak?.Invoke(g);
        return g;
    }

    private Services NewServices(bool frameLimit = true, bool nvidia = true)
    {
        _drs.Available = nvidia;
        var web = new FakeWeb(url => url switch
        {
            var u when u.EndsWith("relimiter.addon64") => _re64,
            var u when u.EndsWith("zzz_display_commander.addon64") => FrameLimitFixtures.Dc64,
            _ => null,
        });
        var gh = new GitHubClient(web);
        var installer = new Installer(_store);
        return new Services
        {
            Settings = _settings, Store = _store, Installer = installer,
            Catalog = new CatalogState(new CatalogClient(gh)),
            FrameLimit = frameLimit ? new FrameLimitService(_store, installer, new CatalogDownloader(gh), new NvidiaProfile(_drs)) : null,
            RefreshHz = () => _hz,
        };
    }

    private GameViewModel NewVm(Services s, GameInfo? info = null, string? catalogJson = null)
    {
        File.WriteAllText(AppPaths.CatalogFile, catalogJson ?? FrameLimitFixtures.Json());
        s.Catalog!.LoadCached();
        return new GameViewModel(info ?? Game(), s);
    }

    private static DlssChoice Choice(FrameLimitViewModel vm, LimitMethod m) => vm.MethodChoices.Single(c => c.Tag == LimitMethods.Id(m));

    [Fact]
    public void Without_the_service_the_section_is_off()
    {
        var vm = NewVm(NewServices(frameLimit: false));

        Assert.False(vm.FrameLimit.Visible);
        Assert.False(vm.FrameLimit.CanApply);
        Assert.False(vm.NeedsFrameLimitUpdate);
    }

    [Fact]
    public void Auto_names_what_it_picked_and_the_manual_choices_follow()
    {
        var vm = NewVm(NewServices());

        var fl = vm.FrameLimit;
        Assert.Equal("Auto (NVIDIA driver limiter)", fl.MethodChoices[0].Label);
        Assert.Equal(fl.MethodChoices[0], fl.MethodChoice);
        Assert.Equal(["driver", "relimiter", "display-commander", "unreal", "none"], fl.MethodChoices.Skip(1).Select(c => c.Tag!).ToArray());
        Assert.Contains("(not available)", Choice(fl, LimitMethod.Unreal).Label);
        Assert.False(fl.IsManual);
        Assert.Equal(LimitMethod.Driver, fl.Method);
        Assert.Equal("60", fl.FpsText);
        Assert.True(fl.CanApply);
        Assert.False(fl.CanRemove);
    }

    [Fact]
    public void Without_an_NVIDIA_card_Auto_does_not_pick_the_driver()
    {
        var vm = NewVm(NewServices(nvidia: false));

        Assert.Equal("Auto (Display Commander)", vm.FrameLimit.MethodChoices[0].Label);
        Assert.Equal(LimitMethod.DisplayCommander, vm.FrameLimit.Method);
    }

    [Fact]
    public void A_manual_choice_wins_is_stored_per_game_and_Reset_to_auto_removes_it()
    {
        var s = NewServices();
        var vm = NewVm(s);
        var fl = vm.FrameLimit;

        fl.MethodChoice = Choice(fl, LimitMethod.ReLimiter);

        Assert.True(fl.IsManual);
        Assert.Equal(LimitMethod.ReLimiter, fl.Method);
        Assert.Equal(LimitMethod.ReLimiter, FrameLimitSettings.Method(_settings.Games[vm.Id].PickOf(ComponentRegistry.FrameLimit)));
        Assert.True(fl.ShowBuildChoices);
        Assert.True(fl.ShowOsd);
        Assert.True(fl.ShowIniLines);
        Assert.Equal("Auto (3.3.5)", fl.BuildChoices[0].Label);

        fl.ResetMethodCommand.Execute(null);

        Assert.False(fl.IsManual);
        Assert.Equal(LimitMethod.Driver, fl.Method);
        Assert.Null(_settings.Games[vm.Id].Components);
    }

    [Fact]
    public void The_Settings_default_applies_until_the_game_chooses_and_the_game_choice_wins()
    {
        FrameLimitSettings.SetMethod(_settings, LimitMethod.DisplayCommander);
        FrameLimitSettings.SetFps(_settings, 90);
        var vm = NewVm(NewServices());

        Assert.Equal(LimitMethod.DisplayCommander, vm.FrameLimit.Method);
        Assert.Equal("90", vm.FrameLimit.FpsText);
        Assert.False(vm.FrameLimit.IsManual);

        vm.FrameLimit.MethodChoice = Choice(vm.FrameLimit, LimitMethod.Driver);
        Assert.Equal(LimitMethod.Driver, vm.FrameLimit.Method);
    }

    [Fact]
    public void A_method_the_game_cannot_use_is_explained_and_Auto_is_used()
    {
        var vm = NewVm(NewServices(), Game(g => g.Tech.Engine = GameEngine.Unknown));
        var fl = vm.FrameLimit;

        fl.MethodChoice = Choice(fl, LimitMethod.Unreal);

        Assert.Equal(LimitMethod.Driver, fl.Method);
        Assert.True(fl.HasNote);
        Assert.Contains("Unreal", fl.Note);
        Assert.Contains("(not available)", Choice(fl, LimitMethod.Unreal).Label);
    }

    [Fact]
    public void An_add_on_in_an_anti_cheat_game_can_be_chosen_but_asks_for_confirmation_and_keeps_its_warning()
    {
        var vm = NewVm(NewServices(), Game(g => g.AntiCheat = "EasyAntiCheat"));
        var fl = vm.FrameLimit;

        fl.MethodChoice = Choice(fl, LimitMethod.ReLimiter);

        Assert.Equal(LimitMethod.ReLimiter, fl.Method);
        Assert.True(fl.NeedsAntiCheatConfirm);
        Assert.Contains("EasyAntiCheat", fl.Note);
        Assert.DoesNotContain("(not available)", Choice(fl, LimitMethod.ReLimiter).Label);
    }

    [Fact]
    public void The_cap_is_kept_per_game_and_an_invalid_one_is_ignored()
    {
        var vm = NewVm(NewServices());

        vm.FrameLimit.FpsText = "141";
        Assert.Equal("141", vm.FrameLimit.FpsText);
        Assert.Equal(141, FrameLimitSettings.Fps(_settings.Games[vm.Id].PickOf(ComponentRegistry.FrameLimit)));

        vm.FrameLimit.FpsText = "abc";
        vm.FrameLimit.FpsText = "5";
        Assert.Equal("141", vm.FrameLimit.FpsText);
    }

    [Fact]
    public void The_hint_shows_with_frame_generation_and_only_suggests()
    {
        var withFg = Game(g => g.Dlss.Add(new DlssDll { Name = "nvngx_dlssg.dll", Path = @"C:\g\nvngx_dlssg.dll" }));
        var fl = NewVm(NewServices(), withFg).FrameLimit;

        Assert.True(fl.HasHint);
        Assert.Contains("72 FPS", fl.HintText);
        Assert.Equal("60", fl.FpsText);
        Assert.False(NewVm(NewServices()).FrameLimit.HasHint);
    }

    [Fact]
    public async Task Apply_sets_the_limiter_the_status_line_says_so_and_a_newer_catalog_build_is_an_update()
    {
        var s = NewServices();
        var vm = NewVm(s);
        var fl = vm.FrameLimit;
        fl.MethodChoice = Choice(fl, LimitMethod.ReLimiter);
        fl.FpsText = "117";

        await fl.ApplyAsync(null, default);
        vm.Manifest = InstallManifest.Read(vm.SelectedTarget!.Dir);
        vm.RefreshHdr();

        Assert.Contains("Active: ReLimiter 3.3.5 at 117 FPS", fl.StatusLine);
        Assert.False(fl.NeedsUpdate);
        Assert.False(MainViewModel.HasUpdate(vm));
        Assert.True(fl.CanRemove);

        _re64 = "MZ relimiter 64 v3.3.6";
        File.WriteAllText(AppPaths.CatalogFile, FrameLimitFixtures.Json(re64: _re64).Replace("\"release\": \"v3.3.5\"", "\"release\": \"v3.3.6\""));
        s.Catalog!.LoadCached();
        vm.RefreshHdr();

        Assert.True(fl.NeedsUpdate);
        Assert.True(vm.NeedsFrameLimitUpdate);
        Assert.True(MainViewModel.HasUpdate(vm));
        Assert.Contains("update to 3.3.6", fl.StatusLine);
        Assert.Equal("Update", fl.ApplyText);

        await fl.ApplyAsync(null, default, update: true);
        vm.Manifest = InstallManifest.Read(vm.SelectedTarget!.Dir);
        vm.RefreshHdr();

        Assert.False(fl.NeedsUpdate);
        Assert.Contains("ReLimiter 3.3.6 at 117 FPS", fl.StatusLine);
    }

    [Fact]
    public async Task Remove_takes_the_limiter_away_and_the_status_goes_back_to_nothing()
    {
        var vm = NewVm(NewServices());
        vm.FrameLimit.MethodChoice = Choice(vm.FrameLimit, LimitMethod.DisplayCommander);
        await vm.FrameLimit.ApplyAsync(null, default);
        vm.Manifest = InstallManifest.Read(vm.SelectedTarget!.Dir);
        vm.RefreshHdr();
        Assert.True(vm.FrameLimit.CanRemove);

        await vm.FrameLimit.RemoveAsync(default);
        vm.Manifest = InstallManifest.Read(vm.SelectedTarget!.Dir);
        vm.RefreshHdr();

        Assert.False(vm.FrameLimit.CanRemove);
        Assert.Contains("has not set a frame limit", vm.FrameLimit.StatusLine);
        Assert.False(File.Exists(Path.Combine(_target, "zzz_display_commander.addon64")));
    }

    [Fact]
    public void The_help_topics_exist_and_name_the_sources()
    {
        Assert.Contains("Reset to auto", HelpTopics.Get("frame-limit")!.Body);
        var methods = HelpTopics.Get("frame-limit-method")!.Body;
        Assert.Contains("[SystemSettings]", methods);
        Assert.Contains("target_fps", methods);
        Assert.Contains("fps_limit", methods);
        Assert.NotNull(HelpTopics.Get("frame-limit-ini"));
    }
}
