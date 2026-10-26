using System.IO.Compression;
using System.Net;
using System.Text;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// The frame limiters in a temp game tree against a fake NVIDIA driver: install and removal of each add-on, the Engine.ini cap, the driver
/// limiter, switching between every pair of methods, the driver limit going to 0 while an add-on runs, 32-bit refusal, config keys written
/// and put back, the user's own lines, imports and the library.
/// </summary>
public class FrameLimitInstallTests : IDisposable
{
    private sealed class FakeWeb(Func<string, string?> body) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            return Task.FromResult(body(url) is { } text
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(text)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private const uint Fps = NvidiaProfileCatalog.FrameRateLimit;
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-fl-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root, _target, _local, _docs;
    private readonly ComponentStore _store;
    private readonly ReShadeBuild _build;
    private readonly FakeDrs _drs = new();
    private readonly FakeWeb _web;
    private readonly FrameLimitService _service;
    private readonly GameOverride _over = new();
    private string _re64 = FrameLimitFixtures.Re64;
    private int _saves;

    public FrameLimitInstallTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, @"Game\Binaries\Win64");
        _local = Path.Combine(_tmp, "LocalAppData");
        _docs = Path.Combine(_tmp, "Documents");
        InstallManifest.KnownFolder = tag => tag switch { "|localappdata|" => _local, "|documents|" => _docs, _ => null };
        _store = new ComponentStore(new GitHubClient(), () => true);
        _web = new FakeWeb(url => url switch
        {
            var u when u.EndsWith("relimiter.addon64") => _re64,
            var u when u.EndsWith("relimiter.addon32") => FrameLimitFixtures.Re32,
            var u when u.EndsWith("zzz_display_commander.addon64") => FrameLimitFixtures.Dc64,
            var u when u.EndsWith("zzz_display_commander.addon32") => FrameLimitFixtures.Dc32,
            _ => null,
        });
        _drs.Define(Fps);
        _service = new FrameLimitService(_store, new Installer(_store), new CatalogDownloader(new GitHubClient(_web)), new NvidiaProfile(_drs));

        _build = _store.ImportReShade(ReShadeFixtures.Folder(Path.Combine(_tmp, "src"), "a", "6.8.0.2155"));
        _store.SelectedReShade = () => _build.Id;
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

    private string T(string rel) => Path.Combine(_target, rel);
    private string EngineIni => Path.Combine(_local, @"Game\Saved\Config\Windows\Engine.ini");

    private GameInfo Game(int bits = 64, string? antiCheat = null, bool unreal = true)
    {
        var g = GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));
        g.Tech.Bits = bits;
        g.AntiCheat = antiCheat;
        g.Tech.Apis = GraphicsApis.Dx12;
        g.Tech.Engine = unreal ? GameEngine.Unreal : GameEngine.Unknown;
        g.Tech.EngineVersion = unreal ? "5.3.2" : null;
        g.Tech.UeProject = unreal ? "Game" : null;
        return g;
    }

    /// <summary>A driver profile for the game that already holds a user limit, the way a user's NVIDIA Control Panel setting would.</summary>
    private void DriverLimitOf(uint fps) => _drs.AddProfile(FakeDrs.Exe).Values[Fps] = fps;

    private FrameLimitRequest Req(GameInfo g, LimitMethod method, int fps = 117, bool? osd = null, string? userIni = null, string? pick = null,
        CatalogLimiter? catalog = null, bool force = true, bool acConfirmed = false) =>
        new(g, _target, new NvidiaGame(FakeDrs.Exe, "Game", _over), method, fps, osd, userIni, pick,
            catalog ?? FrameLimitFixtures.Section().For(method == LimitMethod.DisplayCommander ? "display-commander" : "relimiter"), null, null, force, acConfirmed);

    private Task<FrameLimitResult> Apply(GameInfo g, LimitMethod method, int fps = 117, bool? osd = null, string? userIni = null, string? pick = null,
        CatalogLimiter? catalog = null, bool force = true, bool acConfirmed = false) =>
        _service.ApplyAsync(Req(g, method, fps, osd, userIni, pick, catalog, force, acConfirmed), () => _saves++, null, default);

    private LimitMethod Active() => FrameLimitService.Active(InstallManifest.Read(_target), _over);
    private InstalledComponent? Record(string id) => InstallManifest.Read(_target)?.Component(id);

    // ---------- the add-ons ----------

    [Fact]
    public async Task ReLimiter_is_downloaded_with_its_hash_ReShade_comes_along_and_the_target_fps_is_written()
    {
        var g = Game();

        var result = await Apply(g, LimitMethod.ReLimiter, 117);

        var sha = CatalogFixtures.Sha(FrameLimitFixtures.Re64);
        Assert.Equal(LimitMethod.ReLimiter, result.Method);
        var build = Assert.Single(_store.LimiterBuilds);
        Assert.Equal((sha, "3.3.5", false), (build.Id, build.Version, build.Imported));
        Assert.Equal(FrameLimitFixtures.Re64, File.ReadAllText(ComponentStore.LimiterPathOf(build)));
        Assert.Equal(_build.Sha64, FileUtil.Sha256(T("dxgi.dll")));
        Assert.Equal(FrameLimitFixtures.Re64, File.ReadAllText(T("relimiter.addon64")));
        var c = Record(ComponentRegistry.ReLimiter)!;
        Assert.Equal(sha, c.BuildId);
        Assert.Equal(sha, c.Files.Single().Sha256, ignoreCase: true);
        Assert.Equal("117", c.Extras["fps"]);
        Assert.Equal("117", IniFile.Load(T("relimiter.ini")).Get("FrameLimiter", "target_fps"));
        Assert.Equal(LimitMethod.ReLimiter, Active());
    }

    [Fact]
    public async Task Display_Commander_gets_its_two_documented_keys_beside_the_exe()
    {
        await Apply(Game(), LimitMethod.DisplayCommander, 90);

        Assert.Equal(FrameLimitFixtures.Dc64, File.ReadAllText(T("zzz_display_commander.addon64")));
        var ini = IniFile.Load(T("DisplayCommander.ini"));
        Assert.Equal("90", ini.Get("DisplayCommander", "fps_limit"));
        Assert.Equal("true", ini.Get("DisplayCommander", "fps_limiter_enabled"));
        Assert.Equal(LimitMethod.DisplayCommander, Active());
    }

    [Fact]
    public async Task The_OSD_toggle_is_written_for_ReLimiter_only_when_chosen()
    {
        await Apply(Game(), LimitMethod.ReLimiter, osd: null);
        Assert.Null(IniFile.Load(T("relimiter.ini")).Get("FrameLimiter", "osd_enabled"));

        await Apply(Game(), LimitMethod.ReLimiter, osd: true);
        Assert.Equal("true", IniFile.Load(T("relimiter.ini")).Get("FrameLimiter", "osd_enabled"));
    }

    [Fact]
    public async Task The_add_on_goes_into_the_AddonPath_folder_and_ReLimiter_reads_its_ini_there()
    {
        Write(T("ReShade.ini"), "[ADDON]\r\nAddonPath=.\\addons\r\n");
        Write(T("dxgi.dll"), "RESHADE");
        var m = new InstallManifest { Mode = InstallMode.ReShadeOnly, ReShade = true, Proxy = "dxgi.dll" };
        m.Save(_target);

        await Apply(Game(), LimitMethod.ReLimiter);

        Assert.True(File.Exists(T(@"addons\relimiter.addon64")));
        Assert.Equal("117", IniFile.Load(T(@"addons\relimiter.ini")).Get("FrameLimiter", "target_fps"));
    }

    [Fact]
    public async Task A_32_bit_game_gets_the_32_bit_file_when_the_release_has_one()
    {
        await Apply(Game(bits: 32), LimitMethod.ReLimiter);

        Assert.Equal(_build.Sha32, FileUtil.Sha256(T("dxgi.dll")));
        Assert.Equal(FrameLimitFixtures.Re32, File.ReadAllText(T("relimiter.addon32")));
        Assert.False(File.Exists(T("relimiter.addon64")));
        Assert.Equal(CatalogFixtures.Sha(FrameLimitFixtures.Re32), Record(ComponentRegistry.ReLimiter)!.BuildId);
    }

    [Fact]
    public async Task A_32_bit_game_is_refused_when_the_release_has_no_32_bit_file_and_the_previous_limiter_stays()
    {
        DriverLimitOf(144);
        var g = Game(bits: 32);
        await Apply(g, LimitMethod.Driver, 100);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Apply(g, LimitMethod.ReLimiter, catalog: FrameLimitFixtures.Section(FrameLimitFixtures.Json(relimiter32: false)).For("relimiter")));

        Assert.Contains("no 32-bit build", ex.Message);
        Assert.Equal(LimitMethod.Driver, Active());
        Assert.Equal(100u, _drs.Value(Fps));
        Assert.Empty(_store.LimiterBuilds);
    }

    [Fact]
    public async Task Anti_cheat_games_get_an_add_on_only_after_the_user_confirmed_and_always_the_driver_limiter()
    {
        var g = Game(antiCheat: "EasyAntiCheat");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(g, LimitMethod.DisplayCommander));
        Assert.Contains("EasyAntiCheat", ex.Message);
        Assert.Null(InstallManifest.Read(_target));
        Assert.Empty(_web.Urls);

        await Apply(g, LimitMethod.Driver, 100);
        Assert.Equal(100u, _drs.Value(Fps));

        var result = await Apply(g, LimitMethod.DisplayCommander, acConfirmed: true);
        Assert.Contains(result.Notes, n => n.Contains("EasyAntiCheat"));
        Assert.Equal(LimitMethod.DisplayCommander, Active());
    }

    [Fact]
    public async Task A_refused_download_leaves_the_previous_limiter_in_place()
    {
        DriverLimitOf(144);
        var g = Game();
        await Apply(g, LimitMethod.Driver, 100);
        _re64 = "MZ something else than the catalog says";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(g, LimitMethod.ReLimiter));

        Assert.Contains("refused", ex.Message);
        Assert.Equal(LimitMethod.Driver, Active());
        Assert.Equal(100u, _drs.Value(Fps));
        Assert.Null(InstallManifest.Read(_target));
    }

    [Fact]
    public async Task Removing_an_add_on_takes_its_file_and_its_new_config_away_and_ReShade_stays()
    {
        var g = Game();
        await Apply(g, LimitMethod.ReLimiter);

        await _service.RemoveAsync(Req(g, LimitMethod.None), () => _saves++, default);

        Assert.False(File.Exists(T("relimiter.addon64")));
        Assert.False(File.Exists(T("relimiter.ini")));
        Assert.Equal(_build.Sha64, FileUtil.Sha256(T("dxgi.dll")));
        Assert.Null(Record(ComponentRegistry.ReLimiter));
        Assert.Equal(LimitMethod.None, Active());
    }

    // ---------- config keys: previous values, restored ----------

    [Fact]
    public async Task An_existing_ini_gets_the_key_changed_and_the_old_value_comes_back_on_removal_with_other_keys_untouched()
    {
        Write(T("relimiter.ini"), "[FrameLimiter]\r\ntarget_fps = 100\r\nbackground_fps = 30\r\n");
        var g = Game();
        await Apply(g, LimitMethod.ReLimiter, 144);
        var during = IniFile.Load(T("relimiter.ini"));
        Assert.Equal("144", during.Get("FrameLimiter", "target_fps"));
        Assert.Equal("30", during.Get("FrameLimiter", "background_fps"));

        await _service.RemoveAsync(Req(g, LimitMethod.None), () => _saves++, default);

        var after = IniFile.Load(T("relimiter.ini"));
        Assert.Equal("100", after.Get("FrameLimiter", "target_fps"));
        Assert.Equal("30", after.Get("FrameLimiter", "background_fps"));
    }

    [Fact]
    public async Task Changing_the_cap_updates_in_place_and_keeps_the_first_previous_value()
    {
        Write(T("DisplayCommander.ini"), "[DisplayCommander]\r\nfps_limit=75\r\n");
        var g = Game();
        await Apply(g, LimitMethod.DisplayCommander, 60);
        await Apply(g, LimitMethod.DisplayCommander, 120);
        Assert.Equal("120", IniFile.Load(T("DisplayCommander.ini")).Get("DisplayCommander", "fps_limit"));

        await _service.RemoveAsync(Req(g, LimitMethod.None), () => _saves++, default);

        Assert.Equal("75", IniFile.Load(T("DisplayCommander.ini")).Get("DisplayCommander", "fps_limit"));
        Assert.Null(IniFile.Load(T("DisplayCommander.ini")).Get("DisplayCommander", "fps_limiter_enabled"));
    }

    [Fact]
    public async Task A_key_the_user_changed_since_stays_when_the_limiter_is_removed_and_is_not_overwritten_by_an_update()
    {
        var g = Game();
        await Apply(g, LimitMethod.ReLimiter, 144);
        Write(T("relimiter.ini"), "[FrameLimiter]\r\ntarget_fps = 90\r\n");

        // an update of the add-on (not an explicit Apply) keeps the user's value
        await Apply(g, LimitMethod.ReLimiter, 144, force: false);
        Assert.Equal("90", IniFile.Load(T("relimiter.ini")).Get("FrameLimiter", "target_fps"));

        await _service.RemoveAsync(Req(g, LimitMethod.None), () => _saves++, default);
        Assert.Equal("90", IniFile.Load(T("relimiter.ini")).Get("FrameLimiter", "target_fps"));
    }

    [Fact]
    public async Task An_explicit_apply_replaces_a_value_the_user_set_in_the_game()
    {
        var g = Game();
        await Apply(g, LimitMethod.ReLimiter, 144);
        Write(T("relimiter.ini"), "[FrameLimiter]\r\ntarget_fps = 90\r\n");

        await Apply(g, LimitMethod.ReLimiter, 120);

        Assert.Equal("120", IniFile.Load(T("relimiter.ini")).Get("FrameLimiter", "target_fps"));
    }

    [Fact]
    public async Task The_users_own_ini_lines_win_over_ours_and_go_back_with_the_rest()
    {
        Write(T("relimiter.ini"), "[FrameLimiter]\r\nbackground_fps = 30\r\n");
        var g = Game();

        await Apply(g, LimitMethod.ReLimiter, 144, userIni: "target_fps=75\nbackground_fps=45");

        var ini = IniFile.Load(T("relimiter.ini"));
        Assert.Equal("75", ini.Get("FrameLimiter", "target_fps"));
        Assert.Equal("45", ini.Get("FrameLimiter", "background_fps"));
        await _service.RemoveAsync(Req(g, LimitMethod.None), () => _saves++, default);
        var after = IniFile.Load(T("relimiter.ini"));
        Assert.Null(after.Get("FrameLimiter", "target_fps"));
        Assert.Equal("30", after.Get("FrameLimiter", "background_fps"));
    }

    [Fact]
    public async Task A_broken_user_line_stops_the_install_before_anything_changes()
    {
        var g = Game();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(g, LimitMethod.ReLimiter, userIni: "this is not an ini line"));

        Assert.Contains("extra ini lines", ex.Message);
        Assert.False(File.Exists(T("relimiter.addon64")));
    }

    // ---------- the Unreal cap ----------

    [Fact]
    public async Task The_Unreal_cap_writes_t_MaxFPS_into_SystemSettings_and_the_old_value_comes_back()
    {
        Write(EngineIni, "[SystemSettings]\r\nr.VSync=0\r\nt.MaxFPS=0\r\n\r\n[Other]\r\nx=1\r\n");
        var g = Game();

        await Apply(g, LimitMethod.Unreal, 60);
        var ini = IniFile.Load(EngineIni);
        Assert.Equal("60", ini.Get("SystemSettings", "t.MaxFPS"));
        Assert.Equal("0", ini.Get("SystemSettings", "r.VSync"));
        Assert.Equal(LimitMethod.Unreal, Active());
        Assert.Empty(Record(ComponentRegistry.UnrealFrameCap)!.Files);

        await _service.RemoveAsync(Req(g, LimitMethod.None), () => _saves++, default);
        Assert.Equal("0", IniFile.Load(EngineIni).Get("SystemSettings", "t.MaxFPS"));
        Assert.Equal("1", IniFile.Load(EngineIni).Get("Other", "x"));
    }

    [Fact]
    public async Task An_Engine_ini_the_app_created_goes_away_with_the_cap()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EngineIni)!);
        var g = Game();

        await Apply(g, LimitMethod.Unreal, 60);
        Assert.Equal("60", IniFile.Load(EngineIni).Get("SystemSettings", "t.MaxFPS"));
        await _service.RemoveAsync(Req(g, LimitMethod.None), () => _saves++, default);

        Assert.False(File.Exists(EngineIni));
    }

    [Fact]
    public async Task A_read_only_Engine_ini_is_written_and_stays_read_only()
    {
        Write(EngineIni, "[SystemSettings]\r\nr.AllowHDR=1\r\n");
        File.SetAttributes(EngineIni, FileAttributes.ReadOnly);

        await Apply(Game(), LimitMethod.Unreal, 60);

        Assert.Equal("60", IniFile.Load(EngineIni).Get("SystemSettings", "t.MaxFPS"));
        Assert.True(new FileInfo(EngineIni).IsReadOnly);
    }

    [Fact]
    public async Task The_Unreal_cap_is_refused_for_a_game_that_is_not_Unreal_and_asks_nothing_of_the_game_files()
    {
        var g = Game(unreal: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(g, LimitMethod.Unreal));

        Assert.Contains("Unreal Engine game", ex.Message);
        Assert.False(File.Exists(EngineIni));
    }

    // ---------- the driver limiter and the add-ons ----------

    [Fact]
    public async Task The_driver_limiter_sets_the_cap_records_it_and_reset_puts_back_the_old_value()
    {
        DriverLimitOf(237);
        var g = Game();

        await Apply(g, LimitMethod.Driver, 117);
        Assert.Equal(117u, _drs.Value(Fps));
        Assert.Equal(117, FrameLimitSettings.DriverCap(_over.PickOf(ComponentRegistry.FrameLimit)));
        Assert.Equal(LimitMethod.Driver, Active());

        await _service.RemoveAsync(Req(g, LimitMethod.None), () => _saves++, default);
        Assert.Equal(237u, _drs.Value(Fps));
        Assert.Null(FrameLimitSettings.DriverCap(_over.PickOf(ComponentRegistry.FrameLimit)));
        Assert.Equal(LimitMethod.None, Active());
    }

    [Fact]
    public async Task Installing_an_add_on_sets_a_driver_limit_to_0_and_removing_it_restores_the_limit()
    {
        DriverLimitOf(144);
        var g = Game();

        var result = await Apply(g, LimitMethod.ReLimiter, 117);

        Assert.Equal(0u, _drs.Value(Fps));
        Assert.Contains(result.Notes, n => n.Contains("144"));
        Assert.Null(FrameLimitSettings.DriverCap(_over.PickOf(ComponentRegistry.FrameLimit)));

        await _service.RemoveAsync(Req(g, LimitMethod.None), () => _saves++, default);
        Assert.Equal(144u, _drs.Value(Fps));
    }

    [Fact]
    public async Task The_driver_profile_is_left_alone_when_no_limit_would_apply()
    {
        var g = Game();

        await Apply(g, LimitMethod.DisplayCommander);

        Assert.Equal(0, _drs.Saves);
        Assert.Null(_drs.Profile());
    }

    [Fact]
    public async Task A_global_driver_limit_is_overridden_by_a_0_in_the_games_own_profile()
    {
        _drs.Global[Fps] = 120;
        var g = Game();

        await Apply(g, LimitMethod.ReLimiter);
        Assert.Equal(0u, _drs.Value(Fps));

        await _service.RemoveAsync(Req(g, LimitMethod.None), () => _saves++, default);
        // the profile held nothing of its own before, so nothing of ours stays
        Assert.Null(_drs.Value(Fps));
    }

    [Fact]
    public async Task A_driver_that_does_not_keep_the_cap_is_an_error_and_is_not_recorded()
    {
        _drs.FailSet = (id, _) => id == Fps;
        var g = Game();

        await Assert.ThrowsAsync<NvApiException>(() => Apply(g, LimitMethod.Driver, 117));

        Assert.Null(FrameLimitSettings.DriverCap(_over.PickOf(ComponentRegistry.FrameLimit)));
        Assert.Equal(LimitMethod.None, Active());
    }

    [Fact]
    public async Task Without_an_NVIDIA_driver_the_driver_limiter_is_refused_and_an_add_on_still_installs()
    {
        _drs.Available = false;
        var g = Game();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(g, LimitMethod.Driver));
        await Apply(g, LimitMethod.ReLimiter);

        Assert.Equal(LimitMethod.ReLimiter, Active());
        Assert.Equal(0, _drs.Saves);
    }

    // ---------- switching: every pair ----------

    public static IEnumerable<object[]> Pairs() =>
        from a in LimitMethods.All
        from b in LimitMethods.All
        select new object[] { a, b };

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Switching_from_any_method_to_any_other_leaves_exactly_one_active_and_the_old_one_removed(LimitMethod from, LimitMethod to)
    {
        DriverLimitOf(144);
        Write(EngineIni, "[SystemSettings]\r\nr.VSync=0\r\n");
        var g = Game();
        if (from != LimitMethod.None) await Apply(g, from, 100);
        Assert.Equal(from, Active());

        await Apply(g, to, 117);

        Assert.Equal(to, Active());
        var m = InstallManifest.Read(_target);
        // add-ons and the Engine.ini cap: present only for the active method
        foreach (var other in new[] { LimitMethod.ReLimiter, LimitMethod.DisplayCommander, LimitMethod.Unreal })
            Assert.Equal(other == to, m?.Component(LimitMethods.ComponentOf(other)!) is not null);
        Assert.Equal(to == LimitMethod.ReLimiter, File.Exists(T("relimiter.addon64")));
        Assert.Equal(to == LimitMethod.DisplayCommander, File.Exists(T("zzz_display_commander.addon64")));
        Assert.Equal(to == LimitMethod.Unreal ? "117" : null, IniFile.Load(EngineIni).Get("SystemSettings", "t.MaxFPS"));
        Assert.False(File.Exists(T("relimiter.ini")) && to != LimitMethod.ReLimiter);
        Assert.False(File.Exists(T("DisplayCommander.ini")) && to != LimitMethod.DisplayCommander);
        // the driver: our cap while it is the method, 0 while an add-on runs, the user's own limit otherwise
        var driver = to switch { LimitMethod.Driver => 117u, LimitMethod.ReLimiter or LimitMethod.DisplayCommander => 0u, _ => 144u };
        Assert.Equal(driver, _drs.Value(Fps));
        Assert.Equal(to == LimitMethod.Driver ? 117 : null, FrameLimitSettings.DriverCap(_over.PickOf(ComponentRegistry.FrameLimit)));

        // and back to nothing leaves the game as it was
        await Apply(g, LimitMethod.None);
        Assert.Equal(LimitMethod.None, Active());
        Assert.Equal(144u, _drs.Value(Fps));
        Assert.False(File.Exists(T("relimiter.addon64")) || File.Exists(T("zzz_display_commander.addon64")));
        Assert.Null(IniFile.Load(EngineIni).Get("SystemSettings", "t.MaxFPS"));
    }

    [Fact]
    public async Task The_cap_must_be_inside_the_range_every_method_accepts()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Apply(Game(), LimitMethod.Driver, 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Apply(Game(), LimitMethod.ReLimiter, 5000));
    }

    // ---------- library: build picks, imports, cleanup ----------

    /// <summary>A PE file of the bitness; <paramref name="tag"/> makes two files differ.</summary>
    private string Pe(bool x64, string name, string tag = "a")
    {
        var path = Path.Combine(_tmp, "import-" + Guid.NewGuid().ToString("N")[..6], name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, SyntheticPe.Build(x64, [tag + ".dll"]));
        return path;
    }

    [Fact]
    public void Importing_a_file_copies_it_byte_for_byte_as_an_import()
    {
        var src = Pe(true, "relimiter.addon64");

        var build = Assert.Single(_store.ImportLimiter(src));

        Assert.True(build.Imported);
        Assert.Equal("relimiter", build.LimiterId);
        Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(ComponentStore.LimiterPathOf(build)));
        Assert.Equal(FileUtil.Sha256(src), build.Id, ignoreCase: true);
        Assert.Single(_store.LimiterBuilds);
    }

    [Fact]
    public void Importing_a_folder_or_a_zip_finds_the_add_on_files_by_name()
    {
        var folder = Path.Combine(_tmp, "release");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllBytes(Path.Combine(folder, "zzz_display_commander.addon64"), SyntheticPe.Build(true, ["a.dll"]));
        File.WriteAllBytes(Path.Combine(folder, "sub", "zzz_display_commander.addon32"), SyntheticPe.Build(false, ["b.dll"]));
        File.WriteAllText(Path.Combine(folder, "readme.txt"), "x");
        Assert.Equal(2, _store.ImportLimiter(folder).Count);

        var zip = Path.Combine(_tmp, "relimiter.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var s = z.CreateEntry("release/relimiter.addon64").Open();
            s.Write(SyntheticPe.Build(true, ["a.dll"]));
        }
        var fromZip = Assert.Single(_store.ImportLimiter(zip));

        Assert.Equal("relimiter", fromZip.LimiterId);
        Assert.Equal(3, _store.LimiterBuilds.Count);
    }

    [Fact]
    public void A_file_with_another_name_or_the_wrong_bitness_is_refused_and_nothing_is_stored()
    {
        Assert.Contains("isn't a limiter add-on", Assert.Throws<InvalidDataException>(() => _store.ImportLimiter(Pe(true, "something.addon64"))).Message);
        Assert.Contains("32-bit file", Assert.Throws<InvalidDataException>(() => _store.ImportLimiter(Pe(false, "relimiter.addon64"))).Message);
        var notPe = Path.Combine(_tmp, "relimiter.addon64");
        File.WriteAllText(notPe, "not a dll");
        Assert.Throws<InvalidDataException>(() => _store.ImportLimiter(notPe));
        Assert.Throws<InvalidDataException>(() => _store.ImportLimiter(_tmp));
        Assert.Empty(_store.LimiterBuilds);
    }

    [Fact]
    public async Task A_picked_imported_build_wins_over_the_catalog_and_Auto_takes_the_catalog_build_again()
    {
        var imported = _store.ImportLimiter(Pe(true, "relimiter.addon64")).Single();
        var g = Game();

        await Apply(g, LimitMethod.ReLimiter, pick: imported.Id);
        Assert.Equal(imported.Id, Record(ComponentRegistry.ReLimiter)!.BuildId);
        Assert.Empty(_web.Urls);

        await Apply(g, LimitMethod.ReLimiter, pick: null);
        Assert.Equal(CatalogFixtures.Sha(FrameLimitFixtures.Re64), Record(ComponentRegistry.ReLimiter)!.BuildId);
    }

    [Fact]
    public async Task Offline_Auto_falls_back_to_the_newest_build_in_the_library()
    {
        var imported = _store.ImportLimiter(Pe(true, "relimiter.addon64")).Single();

        var r = LimiterResolution.Resolve(_store, ComponentRegistry.ReLimiter, "x64", null, null);

        Assert.Equal(imported.Id, r.BuildId);
        Assert.True(r.Auto);
        await Apply(Game(), LimitMethod.ReLimiter, catalog: null);
    }

    [Fact]
    public void A_pick_that_left_the_library_falls_back_to_Auto_with_a_note()
    {
        var r = LimiterResolution.Resolve(_store, ComponentRegistry.ReLimiter, "x64", FrameLimitFixtures.Section().For("relimiter"), new string('a', 64));

        Assert.True(r.Auto);
        Assert.Equal(CatalogFixtures.Sha(FrameLimitFixtures.Re64), r.BuildId);
        Assert.Contains("no longer in the library", r.Note);
    }

    [Fact]
    public void Nothing_in_the_library_and_no_catalog_is_not_found()
    {
        Assert.False(LimiterResolution.Resolve(_store, ComponentRegistry.ReLimiter, "x64", null, null).Found);
    }

    [Fact]
    public async Task A_new_catalog_build_means_an_update_and_the_old_one_stays_in_the_library()
    {
        var g = Game();
        await Apply(g, LimitMethod.ReLimiter);
        var oldSha = CatalogFixtures.Sha(FrameLimitFixtures.Re64);
        _re64 = "MZ relimiter 64 v3.3.6";
        var newer = FrameLimitFixtures.Section(FrameLimitFixtures.Json(re64: _re64)).For("relimiter");

        Assert.NotEqual(oldSha, LimiterResolution.Resolve(_store, ComponentRegistry.ReLimiter, "x64", newer, null).BuildId);
        await Apply(g, LimitMethod.ReLimiter, catalog: newer, force: false);

        Assert.Equal(CatalogFixtures.Sha(_re64), Record(ComponentRegistry.ReLimiter)!.BuildId);
        Assert.Equal(2, _store.LimiterBuilds.Count);
        Assert.Equal(_re64, File.ReadAllText(T("relimiter.addon64")));
    }

    [Fact]
    public async Task The_cleanup_keeps_the_installed_build_and_never_deletes_an_import_automatically()
    {
        var g = Game();
        await Apply(g, LimitMethod.ReLimiter);
        _store.ImportLimiter(Pe(true, "relimiter.addon64"));
        _store.ImportLimiter(Pe(true, "zzz_display_commander.addon64", "dc"));
        var settings = new AppSettings();
        settings.KeepBuilds[LibraryKind.FrameLimiter.ToString()] = 1;
        var manifests = new[] { InstallManifest.Read(_target)! };

        var all = _store.Library(LibraryKind.FrameLimiter);
        var candidates = _store.CleanupCandidates(LibraryKind.FrameLimiter, settings, manifests, downloadsOnly: true);

        Assert.Equal(3, all.Count);
        Assert.Contains(all, b => b.Downloaded);
        Assert.Empty(candidates);
        Assert.Contains(_store.ProtectionReasons(LibraryKind.FrameLimiter, settings, manifests, null), p => p.Id == CatalogFixtures.Sha(FrameLimitFixtures.Re64));
    }

    [Fact]
    public async Task A_game_pick_and_the_default_are_protected_by_component_id()
    {
        var imported = _store.ImportLimiter(Pe(true, "relimiter.addon64")).Single();
        var settings = new AppSettings();
        settings.SetDefaultBuild(ComponentRegistry.ReLimiter, imported.Id);

        var reasons = _store.ProtectionReasons(LibraryKind.FrameLimiter, settings, [], null).ToList();

        Assert.Contains((imported.Id, "default"), reasons);
        await Task.CompletedTask;
    }

    [Fact]
    public void Removing_a_build_from_the_library_deletes_its_folder()
    {
        var b = _store.ImportLimiter(Pe(true, "relimiter.addon64")).Single();

        _store.RemoveLimiter(b.Id);

        Assert.Empty(_store.LimiterBuilds);
        Assert.False(File.Exists(ComponentStore.LimiterPathOf(b)));
    }
}
