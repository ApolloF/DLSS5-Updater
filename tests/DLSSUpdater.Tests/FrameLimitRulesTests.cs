using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>Which methods a game may use, what is picked automatically, the settings round trip, the ini lines and the hint.</summary>
public class FrameLimitRulesTests
{
    private static GameInfo Unreal(string? antiCheat = null, int bits = 64) => FrameLimitFixtures.Game("UeGame", GameEngine.Unreal, bits, antiCheat);
    private static GameInfo Plain(string? antiCheat = null, int bits = 64) => FrameLimitFixtures.Game("Plain", GameEngine.Unknown, bits, antiCheat);

    // ---------- availability ----------

    [Fact]
    public void The_driver_limiter_needs_an_NVIDIA_card_and_is_allowed_with_anti_cheat()
    {
        Assert.True(FrameLimitRules.Availability(LimitMethod.Driver, Plain("EasyAntiCheat"), FrameLimitFixtures.Env()).Available);
        var no = FrameLimitRules.Availability(LimitMethod.Driver, Plain(), FrameLimitFixtures.Env(nvidia: false));
        Assert.False(no.Available);
        Assert.Contains("NVIDIA", no.Reason);
    }

    [Theory]
    [InlineData(LimitMethod.ReLimiter)]
    [InlineData(LimitMethod.DisplayCommander)]
    public void Add_on_limiters_with_anti_cheat_are_a_warning_to_confirm_not_a_block(LimitMethod method)
    {
        var a = FrameLimitRules.Availability(method, Plain("BattlEye"), FrameLimitFixtures.Env());

        Assert.True(a.Available);
        Assert.Contains("BattlEye", a.Warning);
        Assert.Contains("driver limiter", a.Warning);
        Assert.NotEqual(method, FrameLimitRules.Auto(Plain("BattlEye"), FrameLimitFixtures.Env()));
    }

    [Theory]
    [InlineData(LimitMethod.ReLimiter)]
    [InlineData(LimitMethod.DisplayCommander)]
    public void A_32_bit_game_needs_a_32_bit_build(LimitMethod method)
    {
        Assert.True(FrameLimitRules.Availability(method, Plain(bits: 32), FrameLimitFixtures.Env(has32: true)).Available);
        var refused = FrameLimitRules.Availability(method, Plain(bits: 32), FrameLimitFixtures.Env(has32: false));
        Assert.False(refused.Available);
        Assert.Contains("no 32-bit build", refused.Reason);
        Assert.True(FrameLimitRules.Availability(method, Plain(bits: 64), FrameLimitFixtures.Env(has32: false)).Available);
    }

    [Fact]
    public void Without_any_build_an_add_on_is_unavailable_and_the_reason_names_import()
    {
        var a = FrameLimitRules.Availability(LimitMethod.ReLimiter, Plain(), FrameLimitFixtures.Env(has64: false, has32: false));

        Assert.False(a.Available);
        Assert.Contains("Import", a.Reason);
    }

    [Fact]
    public void The_Unreal_cap_is_for_Unreal_games_with_a_known_Engine_ini_and_stays_allowed_with_anti_cheat()
    {
        Assert.True(FrameLimitRules.Availability(LimitMethod.Unreal, Unreal("EasyAntiCheat"), FrameLimitFixtures.Env()).Available);
        Assert.Contains("Only for Unreal Engine games", FrameLimitRules.Availability(LimitMethod.Unreal, Plain(), FrameLimitFixtures.Env()).Reason);
        Assert.Contains("Engine.ini", FrameLimitRules.Availability(LimitMethod.Unreal, Unreal(), FrameLimitFixtures.Env(engineIni: false)).Reason);
    }

    // ---------- automatic pick ----------

    [Fact]
    public void Auto_picks_the_driver_on_NVIDIA_even_in_Unreal_games()
    {
        Assert.Equal(LimitMethod.Driver, FrameLimitRules.Auto(Unreal(), FrameLimitFixtures.Env()));
        Assert.Equal(LimitMethod.Driver, FrameLimitRules.Auto(Plain("EasyAntiCheat"), FrameLimitFixtures.Env()));
    }

    [Fact]
    public void Auto_without_NVIDIA_takes_the_Unreal_cap_then_Display_Commander_then_nothing()
    {
        var amd = FrameLimitFixtures.Env(nvidia: false);

        Assert.Equal(LimitMethod.Unreal, FrameLimitRules.Auto(Unreal(), amd));
        Assert.Equal(LimitMethod.DisplayCommander, FrameLimitRules.Auto(Plain(), amd));
        Assert.Equal(LimitMethod.None, FrameLimitRules.Auto(Plain("EasyAntiCheat"), amd));
    }

    // ---------- effective choice: the manual choice wins ----------

    [Fact]
    public void A_game_without_a_choice_follows_the_Settings_default_and_the_automatic_pick()
    {
        var settings = new AppSettings();
        var game = Plain();

        var auto = FrameLimitRules.Effective(game, FrameLimitFixtures.Env(), null, settings);
        Assert.Equal((LimitMethod.Driver, FrameLimitRules.DefaultFps, false), (auto.Method, auto.Fps, auto.Manual));

        FrameLimitSettings.SetMethod(settings, LimitMethod.DisplayCommander);
        FrameLimitSettings.SetFps(settings, 90);
        var def = FrameLimitRules.Effective(game, FrameLimitFixtures.Env(), null, settings);
        Assert.Equal((LimitMethod.DisplayCommander, 90, false), (def.Method, def.Fps, def.Manual));
    }

    [Fact]
    public void The_games_own_choice_wins_over_the_default_and_over_auto()
    {
        var settings = new AppSettings();
        FrameLimitSettings.SetMethod(settings, LimitMethod.DisplayCommander);
        FrameLimitSettings.SetFps(settings, 90);
        var over = new GameOverride();
        FrameLimitSettings.SetMethod(over, LimitMethod.ReLimiter);
        FrameLimitSettings.SetFps(over, 141);

        var e = FrameLimitRules.Effective(Plain(), FrameLimitFixtures.Env(), over, settings);

        Assert.Equal((LimitMethod.ReLimiter, 141, true, LimitMethod.Driver), (e.Method, e.Fps, e.Manual, e.AutoMethod));
    }

    [Fact]
    public void A_chosen_method_the_game_cannot_use_falls_back_to_auto_and_the_note_says_why()
    {
        var over = new GameOverride();
        FrameLimitSettings.SetMethod(over, LimitMethod.Unreal);

        var e = FrameLimitRules.Effective(Plain(), FrameLimitFixtures.Env(), over, new AppSettings());

        Assert.Equal(LimitMethod.Driver, e.Method);
        Assert.False(e.Manual);
        Assert.Contains("can't be used here", e.Note);
        Assert.Contains("Unreal", e.Note);
    }

    [Fact]
    public void A_chosen_add_on_in_an_anti_cheat_game_is_kept_with_the_warning_as_its_note()
    {
        var over = new GameOverride();
        FrameLimitSettings.SetMethod(over, LimitMethod.ReLimiter);

        var e = FrameLimitRules.Effective(Plain("EasyAntiCheat"), FrameLimitFixtures.Env(), over, new AppSettings());

        Assert.Equal(LimitMethod.ReLimiter, e.Method);
        Assert.True(e.Manual);
        Assert.Contains("EasyAntiCheat", e.Note);
    }

    [Fact]
    public void Reset_to_auto_removes_the_choice_and_leaves_no_empty_entry()
    {
        var over = new GameOverride();
        FrameLimitSettings.SetMethod(over, LimitMethod.Unreal);
        FrameLimitSettings.SetFps(over, 60);

        FrameLimitSettings.SetMethod(over, null);
        FrameLimitSettings.SetFps(over, null);

        Assert.Null(over.Components);
    }

    [Fact]
    public void Settings_round_trip_through_the_generic_component_picks()
    {
        var over = new GameOverride();
        FrameLimitSettings.SetMethod(over, LimitMethod.DisplayCommander);
        FrameLimitSettings.SetFps(over, 72);
        FrameLimitSettings.SetOsd(over, true);
        FrameLimitSettings.SetIniText(over, "  background_fps=30  ");
        FrameLimitSettings.SetDriverCap(over, 100);

        var pick = over.PickOf(ComponentRegistry.FrameLimit);
        Assert.Equal(LimitMethod.DisplayCommander, FrameLimitSettings.Method(pick));
        Assert.Equal(72, FrameLimitSettings.Fps(pick));
        Assert.True(FrameLimitSettings.Osd(pick));
        Assert.Equal("background_fps=30", FrameLimitSettings.IniText(pick));
        Assert.Equal(100, FrameLimitSettings.DriverCap(pick));
        FrameLimitSettings.SetOsd(over, null);
        Assert.Null(FrameLimitSettings.Osd(over.PickOf(ComponentRegistry.FrameLimit)));
    }

    [Fact]
    public void An_out_of_range_or_unknown_stored_value_is_ignored()
    {
        var over = new GameOverride();
        over.SetPickOptions(ComponentRegistry.FrameLimit, new() { ["method"] = "nonsense", ["fps"] = "5" });

        var pick = over.PickOf(ComponentRegistry.FrameLimit);
        Assert.Null(FrameLimitSettings.Method(pick));
        Assert.Null(FrameLimitSettings.Fps(pick));
    }

    // ---------- ini keys ----------

    [Fact]
    public void Only_documented_keys_are_written()
    {
        Assert.Equal([("FrameLimiter", "target_fps", "117")], FrameLimitIni.Keys(LimitMethod.ReLimiter, 117));
        Assert.Equal([("FrameLimiter", "target_fps", "117"), ("FrameLimiter", "osd_enabled", "true")], FrameLimitIni.Keys(LimitMethod.ReLimiter, 117, osd: true));
        Assert.Equal([("DisplayCommander", "fps_limit", "60"), ("DisplayCommander", "fps_limiter_enabled", "true")], FrameLimitIni.Keys(LimitMethod.DisplayCommander, 60));
        Assert.Equal([("SystemSettings", "t.MaxFPS", "60")], FrameLimitIni.Keys(LimitMethod.Unreal, 60));
        Assert.Empty(FrameLimitIni.Keys(LimitMethod.Driver, 60));
    }

    [Fact]
    public void User_ini_lines_parse_with_section_headers_comments_and_the_default_section()
    {
        var (items, errors) = FrameLimitIni.ParseOverrides("""
            ; my tweaks
            background_fps = 30
            [Other]
            osd_scale=1.5
            # again
            background_fps=45
            """, "FrameLimiter");

        Assert.Empty(errors);
        Assert.Equal([("FrameLimiter", "background_fps", "30"), ("Other", "osd_scale", "1.5"), ("Other", "background_fps", "45")], items);
    }

    [Fact]
    public void A_user_line_that_is_neither_key_value_nor_a_header_is_an_error()
    {
        var (items, errors) = FrameLimitIni.ParseOverrides("just words\n=novalue\n", "FrameLimiter");

        Assert.Empty(items);
        Assert.Equal(2, errors.Count);
    }

    // ---------- the hint ----------

    [Fact]
    public void The_hint_is_refresh_divided_by_the_frame_generation_factor()
    {
        Assert.Equal(72, FrameLimitHint.Recommend(144, 2));
        Assert.Equal(60, FrameLimitHint.Recommend(240, 4));
        Assert.Equal(30, FrameLimitHint.Recommend(60, 2));
        Assert.Null(FrameLimitHint.Recommend(0, 2));
        Assert.Null(FrameLimitHint.Recommend(144, 1));
    }

    [Fact]
    public void The_hint_text_appears_only_with_frame_generation_and_says_it_is_a_suggestion()
    {
        var fg = Plain();
        fg.Dlss.Add(new DlssDll { Name = "nvngx_dlssg.dll", Path = @"C:\x\nvngx_dlssg.dll" });

        var text = FrameLimitHint.Text(fg, 144)!;

        Assert.Contains("72 FPS", text);
        Assert.Contains("144 Hz", text);
        Assert.Contains("nothing is set for you", text);
        Assert.Null(FrameLimitHint.Text(Plain(), 144));
    }

    [Fact]
    public void FSR_and_XeSS_frame_generation_count_too()
    {
        var fsr = Plain();
        fsr.Tech.Upscalers = UpscalerSignals.FsrFrameGen;
        var xess = Plain();
        xess.Tech.Upscalers = UpscalerSignals.XeSSFrameGen;

        Assert.Equal("FSR frame generation", FrameLimitHint.FrameGenerationSource(fsr));
        Assert.Equal("XeSS frame generation", FrameLimitHint.FrameGenerationSource(xess));
        Assert.Null(FrameLimitHint.FrameGenerationSource(Plain()));
    }

    [Fact]
    public void The_method_ids_round_trip_and_unknown_ids_are_null()
    {
        foreach (var m in LimitMethods.All) Assert.Equal(m, LimitMethods.Parse(LimitMethods.Id(m)));
        Assert.Null(LimitMethods.Parse("rtss"));
        Assert.Null(LimitMethods.Parse(null));
    }

    // ---------- registration ----------

    [Fact]
    public void The_limiters_are_registered_in_one_library_and_the_Engine_ini_cap_has_none()
    {
        Assert.Equal(LibraryKind.FrameLimiter, ComponentRegistry.Find(ComponentRegistry.ReLimiter)!.Library);
        Assert.Equal(LibraryKind.FrameLimiter, ComponentRegistry.Find(ComponentRegistry.DisplayCommander)!.Library);
        Assert.Null(ComponentRegistry.Find(ComponentRegistry.UnrealFrameCap)!.Library);
        Assert.True(ComponentRegistry.Find(ComponentRegistry.ReLimiter)!.OwnsFile("relimiter.addon32"));
        Assert.True(ComponentRegistry.Find(ComponentRegistry.DisplayCommander)!.OwnsFile("zzz_display_commander.addon64"));
    }

    [Fact]
    public void The_catalog_model_reads_the_frameLimiters_section_and_labels_versions()
    {
        var section = FrameLimitFixtures.Section();

        var re = section.For("relimiter")!;
        Assert.Equal("3.3.5", re.VersionOf(re.File("x64")!));
        Assert.NotNull(re.File("x86"));
        var dc = section.For("display-commander")!;
        Assert.Equal("2026-10-21-ccccccc", dc.VersionOf(dc.File("x64")!));
        Assert.Null(FrameLimitFixtures.Section(FrameLimitFixtures.Json(relimiter32: false)).For("relimiter")!.File("x86"));
    }
}
