using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// Per-game detection overrides: the effective value is the detected one unless the user set another, every consumer reads it, and
/// resetting goes back to what the scan found. Games are built in memory or in a temp tree.
/// </summary>
public sealed class TechOverrideTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-tech-" + Guid.NewGuid().ToString("N")[..8]);

    public TechOverrideTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); }
        catch (IOException) { }
    }

    private static GameInfo Game(GraphicsApis apis = GraphicsApis.Dx12, int bits = 64, GameEngine engine = GameEngine.Unreal, string version = "5.3.2") => new()
    {
        Id = "g1", Name = "Test Game", Root = @"C:\Games\Test", Exes = [@"C:\Games\Test\game.exe"],
        Tech = new GameTech { Apis = apis, Bits = bits, Engine = engine, EngineVersion = engine == GameEngine.Unreal ? version : null, UeProject = "Proj", ModFolder = @"C:\Games\Test\Proj\Binaries\Win64" },
    };

    // ---------- the effective value ----------

    [Fact]
    public void Without_an_override_the_effective_tech_is_the_detected_one()
    {
        var g = Game();

        Assert.Same(g.Tech, g.Effective);
    }

    [Fact]
    public void Each_override_replaces_only_its_own_field()
    {
        var g = Game();
        g.Manual = new TechOverride { Apis = GraphicsApis.Dx11, Bits = 32, ModFolder = @"C:\Games\Test\Other" };

        Assert.Equal(GraphicsApis.Dx11, g.Effective.PrimaryApi);
        Assert.True(g.Effective.Is32Bit);
        Assert.Equal(@"C:\Games\Test\Other", g.Effective.ModFolder);
        Assert.Equal(GameEngine.Unreal, g.Effective.Engine);
        Assert.Equal("5.3.2", g.Effective.EngineVersion);
        Assert.Equal("Proj", g.Effective.UeProject);
        // The scan result itself is untouched, so the page can still show "Auto (DX12)".
        Assert.Equal(GraphicsApis.Dx12, g.Tech.Apis);
        Assert.Equal(64, g.Tech.Bits);
    }

    [Fact]
    public void A_set_API_is_no_longer_marked_as_guessed()
    {
        var g = Game();
        g.Tech.ApisGuessed = true;
        g.Manual = new TechOverride { Apis = GraphicsApis.Vulkan };

        Assert.False(g.Effective.ApisGuessed);
        Assert.Equal(GraphicsApis.Vulkan, g.Effective.PrimaryApi);
    }

    [Fact]
    public void Changing_the_engine_drops_a_detected_Unreal_version_and_a_set_version_counts_for_Unreal_only()
    {
        var g = Game();

        g.Manual = new TechOverride { Engine = GameEngine.Unity };
        Assert.Equal(GameEngine.Unity, g.Effective.Engine);
        Assert.Null(g.Effective.EngineVersion);
        Assert.False(g.Effective.IsUnreal);

        g.Manual = new TechOverride { EngineVersion = "4.27" };
        Assert.Equal("4.27", g.Effective.EngineVersion);
        Assert.Equal(4, g.Effective.UeMajor);

        g.Manual = new TechOverride { Engine = GameEngine.Unity, EngineVersion = "4.27" };
        Assert.Null(g.Effective.EngineVersion);
    }

    [Fact]
    public void A_game_not_detected_as_Unreal_can_be_set_to_Unreal_with_a_version()
    {
        var g = Game(engine: GameEngine.Unknown);
        g.Manual = new TechOverride { Engine = GameEngine.Unreal, EngineVersion = "5.4" };

        Assert.True(g.Effective.IsUnreal);
        Assert.Equal(5, g.Effective.UeMajor);
    }

    [Fact]
    public void The_effective_value_follows_a_new_scan_and_a_new_override()
    {
        var g = Game();
        g.Manual = new TechOverride { Bits = 32 };
        var first = g.Effective;
        Assert.Same(first, g.Effective);

        g.Tech = new GameTech { Apis = GraphicsApis.Dx11, Bits = 64 };
        Assert.Equal(GraphicsApis.Dx11, g.Effective.PrimaryApi);
        Assert.Equal(32, g.Effective.Bits);

        g.Manual = null;
        Assert.Equal(64, g.Effective.Bits);
    }

    [Fact]
    public void The_main_exe_is_the_users_while_it_exists_else_the_best_ranked()
    {
        var exe = Path.Combine(_tmp, "other.exe");
        File.WriteAllText(exe, "MZ");
        var g = Game();

        Assert.Equal(@"C:\Games\Test\game.exe", g.MainExe);
        g.Manual = new TechOverride { MainExe = exe };
        Assert.Equal(exe, g.MainExe);
        File.Delete(exe);
        Assert.Equal(@"C:\Games\Test\game.exe", g.MainExe);
    }

    // ---------- settings: set, reset, old files ----------

    [Fact]
    public void Update_stores_the_override_and_mirrors_it_and_an_emptied_override_is_dropped()
    {
        var s = new AppSettings();
        var g = Game();

        TechOverrides.Update(s, g, t => t with { Bits = 32 });
        Assert.Equal(32, s.Games["g1"].Tech!.Bits);
        Assert.Equal(32, g.Effective.Bits);

        TechOverrides.Update(s, g, t => t with { Bits = null });
        Assert.Null(s.Games["g1"].Tech);
        Assert.Null(g.Manual);
        Assert.Equal(64, g.Effective.Bits);
    }

    [Fact]
    public void ResetAll_returns_every_field_to_the_detected_value()
    {
        var s = new AppSettings();
        var g = Game();
        TechOverrides.Update(s, g, t => t with { Apis = GraphicsApis.Dx9, Bits = 32, Engine = GameEngine.Unity, ModFolder = @"C:\x" });

        TechOverrides.ResetAll(s, g);

        Assert.Same(g.Tech, g.Effective);
        Assert.Null(s.Games["g1"].Tech);
    }

    [Fact]
    public void Attach_copies_the_saved_override_onto_a_freshly_scanned_game()
    {
        var s = new AppSettings();
        s.For("g1").Tech = new TechOverride { Apis = GraphicsApis.OpenGL };
        var scanned = Game();

        TechOverrides.Attach(s, scanned);

        Assert.Equal(GraphicsApis.OpenGL, scanned.Effective.PrimaryApi);
    }

    [Fact]
    public void The_override_survives_a_settings_round_trip_and_a_file_without_it_still_loads()
    {
        var s = new AppSettings();
        s.For("g1").Tech = new TechOverride { Apis = GraphicsApis.Dx11 | GraphicsApis.Dx12, Bits = 32, Engine = GameEngine.Unity, MainExe = @"C:\a.exe", ModFolder = @"C:\m" };

        var json = JsonSerializer.Serialize(s, JsonCtx.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, JsonCtx.Default.AppSettings)!;

        Assert.Equal(s.Games["g1"].Tech, back.Games["g1"].Tech);

        var old = JsonSerializer.Deserialize("""{ "Games": { "g1": { "Proxy": "winmm.dll" } } }""", JsonCtx.Default.AppSettings)!;
        Assert.Equal("winmm.dll", old.Games["g1"].Proxy);
        Assert.Null(old.Games["g1"].Tech);
    }

    [Fact]
    public void The_override_is_not_written_into_the_scan_cache()
    {
        var g = Game();
        g.Manual = new TechOverride { Bits = 32 };

        var json = JsonSerializer.Serialize(new List<GameInfo> { g }, JsonCtx.Default.ListGameInfo);

        Assert.DoesNotContain("Manual", json);
        Assert.Equal(64, JsonSerializer.Deserialize(json, JsonCtx.Default.ListGameInfo)![0].Effective.Bits);
    }

    // ---------- consumers read the effective value; reset returns to the detected one ----------

    [Fact]
    public void ReShade_naming_follows_the_set_API_and_bitness()
    {
        var g = Game(GraphicsApis.Dx12);
        Assert.Equal("dxgi.dll", ReShadeNaming.Resolve(g.Effective, false, null).File);

        g.Manual = new TechOverride { Apis = GraphicsApis.Dx9, Bits = 32 };
        var t = ReShadeNaming.Resolve(g.Effective, false, null);
        Assert.Equal("d3d9.dll", t.File);
        Assert.Equal(32, t.Bits);

        g.Manual = null;
        Assert.Equal("dxgi.dll", ReShadeNaming.Resolve(g.Effective, false, null).File);
    }

    [Fact]
    public void The_method_advisor_judges_the_set_values()
    {
        static MethodAdvice Advise(GameInfo g) => MethodAdvisor.Advise(new AdvisorInput(g, new GpuInfo(GpuVendor.Nvidia, RtxTier.Rtx40, "RTX 4070")));
        var detected = Game(GraphicsApis.Dx12);
        var g = Game(GraphicsApis.Dx12);
        g.Manual = new TechOverride { Apis = GraphicsApis.Dx9, Bits = 32 };
        var truth = Game(GraphicsApis.Dx9, 32);

        Assert.Equal(Advise(truth).Methods.Select(m => (m.Method, m.Availability)), Advise(g).Methods.Select(m => (m.Method, m.Availability)));
        Assert.NotEqual(Advise(detected).Methods.Select(m => (m.Method, m.Availability)), Advise(g).Methods.Select(m => (m.Method, m.Availability)));

        g.Manual = null;
        Assert.Equal(Advise(detected).Methods.Select(m => (m.Method, m.Availability)), Advise(g).Methods.Select(m => (m.Method, m.Availability)));
    }

    [Fact]
    public void The_Unreal_frame_cap_and_the_limiter_architecture_follow_the_set_engine_and_bitness()
    {
        var env = FrameLimitFixtures.Env(has32: false);
        var g = FrameLimitFixtures.Game("Plain", GameEngine.Unknown, 64, null);
        Assert.False(FrameLimitRules.Availability(LimitMethod.Unreal, g, env).Available);
        Assert.True(FrameLimitRules.Availability(LimitMethod.ReLimiter, g, env).Available);

        g.Manual = new TechOverride { Engine = GameEngine.Unreal, Bits = 32 };

        Assert.True(FrameLimitRules.Availability(LimitMethod.Unreal, g, env).Available);
        Assert.False(FrameLimitRules.Availability(LimitMethod.ReLimiter, g, env).Available);

        g.Manual = null;
        Assert.False(FrameLimitRules.Availability(LimitMethod.Unreal, g, env).Available);
        Assert.True(FrameLimitRules.Availability(LimitMethod.ReLimiter, g, env).Available);
    }

    [Fact]
    public void HDR_matching_uses_the_set_engine_and_bitness()
    {
        var picker = new HdrPicker(HdrFixtures.Renodx());
        var g = HdrFixtures.Unity("Some Unity Game", bits: 64);
        Assert.Equal(HdrKind.UnityGeneric, picker.Pick(g, null).Kind);

        g.Manual = new TechOverride { Engine = GameEngine.Unreal };
        Assert.Equal(HdrKind.UeExtended, picker.Pick(g, null).Kind);

        g.Manual = new TechOverride { Bits = 32 };
        var unity32 = picker.Pick(g, null);
        Assert.Equal(HdrKind.UnityGeneric, unity32.Kind);
        Assert.Equal("x86", unity32.File!.Arch);

        g.Manual = null;
        Assert.Equal("x64", picker.Pick(g, null).File!.Arch);
    }

    // ---------- a scan with a main exe set by hand ----------

    [Fact]
    public void A_scan_reads_API_and_bitness_from_the_main_exe_the_user_set_and_a_scan_without_it_does_not()
    {
        var root = Path.Combine(_tmp, "Game");
        var main = Path.Combine(root, "Game.exe");
        var launcher = Path.Combine(root, "tools", "Other.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(launcher)!);
        File.WriteAllBytes(main, SyntheticPe.Build(true, ["d3d12.dll"]));
        File.WriteAllBytes(launcher, SyntheticPe.Build(false, ["d3d9.dll"]));
        var entry = new GameEntry("Game", root, "Manual");

        var auto = GameScanner.Inspect(entry);
        Assert.Equal(main, auto.Exes[0], ignoreCase: true);
        Assert.Equal(64, auto.Tech.Bits);

        var set = GameScanner.Inspect(entry, new TechOverride { MainExe = launcher });
        Assert.Equal(launcher, set.Exes[0], ignoreCase: true);
        Assert.Equal(launcher, set.MainExe, ignoreCase: true);
        Assert.Equal(32, set.Tech.Bits);
        Assert.Equal(GraphicsApis.Dx9, set.Tech.PrimaryApi);

        var back = GameScanner.Inspect(entry, new TechOverride { MainExe = launcher } with { MainExe = null });
        Assert.Equal(64, back.Tech.Bits);
    }
}
