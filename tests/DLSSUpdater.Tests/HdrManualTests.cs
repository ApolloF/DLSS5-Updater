using System.Net;
using System.Text;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// Manual control of the RenoDX HDR component: any catalog mod picked by hand, an add-on the user imports, the Engine.ini path typed by
/// the user and ini values that win over ours. Temp trees only.
/// </summary>
public sealed class HdrManualTests : IDisposable
{
    private sealed class FakeWeb(Func<string, string?> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(body(request.RequestUri!.ToString()) is { } text
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(text)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private const string OwnAddon = "MZ my own snapshot";

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-hdrm-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root, _target, _local, _docs;
    private readonly ComponentStore _store;
    private readonly HdrService _service;

    public HdrManualTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, @"Game\Binaries\Win64");
        _local = Path.Combine(_tmp, "LocalAppData");
        _docs = Path.Combine(_tmp, "Documents");
        InstallManifest.KnownFolder = tag => tag switch { "|localappdata|" => _local, "|documents|" => _docs, _ => null };
        _store = new ComponentStore(new GitHubClient(), () => true);
        var web = new FakeWeb(url => url switch
        {
            var u when u.EndsWith("renodx-ue-extended.addon64") => HdrFixtures.UeExtContent,
            var u when u.EndsWith("renodx-hades.addon64") => HdrFixtures.HadesContent,
            var u when u.EndsWith("renodx-unrealengine.addon64") => HdrFixtures.UeContent,
            _ => null,
        });
        _service = new HdrService(_store, new Installer(_store), new CatalogDownloader(new GitHubClient(web)));

        Write(Path.Combine(AppPaths.Components, ComponentStore.ReShadeFile), "RESHADE");
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

    private string ReShadeIni => Path.Combine(_target, "ReShade.ini");
    private string EngineIni => Path.Combine(_local, @"Game\Saved\Config\Windows\Engine.ini");

    private GameInfo Game()
    {
        var g = GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));
        g.Tech.Bits = 64;
        g.Tech.Engine = GameEngine.Unreal;
        g.Tech.EngineVersion = "5.3.2";
        g.Tech.Apis = GraphicsApis.Dx12;
        return g;
    }

    private static HdrPicker Picker() => new(HdrFixtures.Renodx());

    private InstalledComponent? Record() => InstallManifest.Read(_target)?.Component(ComponentRegistry.RenoDxHdr);

    private string Import(string content = OwnAddon, string name = "renodx-mine.addon64")
    {
        var src = Path.Combine(_tmp, "src", name);
        Write(src, content);
        return src;
    }

    // ---------- pick any catalog mod by hand ----------

    [Fact]
    public void A_catalog_mod_picked_by_hand_replaces_the_automatic_pick_and_reset_returns_to_it()
    {
        var g = HdrFixtures.Unity("Some Unity Game");
        var picker = Picker();
        var auto = picker.Pick(g, null);
        Assert.Equal(HdrKind.UnityGeneric, auto.Kind);

        var hand = picker.Pick(g, null, "hades");

        Assert.Equal(HdrKind.Named, hand.Kind);
        Assert.Equal("hades", hand.ModId);
        Assert.Contains("your pick", hand.Reason);
        Assert.Equal(CatalogFixtures.Sha(HdrFixtures.HadesContent), hand.File!.Sha256);
        Assert.Equal(auto.Kind, picker.Pick(g, null, null).Kind);
    }

    [Theory]
    [InlineData("ue-extended", HdrKind.UeExtended)]
    [InlineData("unreal", HdrKind.UnrealGeneric)]
    public void A_generic_add_on_picked_by_hand_is_used_whatever_the_engine_says(string id, HdrKind kind)
    {
        var g = HdrFixtures.Unreal("Whatever");
        g.Manual = new TechOverride { Engine = GameEngine.Unity };

        Assert.Equal(kind, Picker().Pick(g, null, id).Kind);
    }

    [Fact]
    public void A_hand_pick_without_a_build_for_the_game_says_why_and_anti_cheat_still_blocks()
    {
        var g = HdrFixtures.Unity("Some Unity Game", bits: 64);

        var wrongBits = Picker().Pick(g, null, "alien-isolation");
        Assert.False(wrongBits.IsOffered);
        Assert.Contains("64-bit", wrongBits.Reason);
        Assert.Contains("no download", Picker().Pick(g, null, "crashy").Reason);
        Assert.Contains("isn't in the catalog", Picker().Pick(g, null, "gone").Reason);

        g.AntiCheat = "EasyAntiCheat";
        Assert.Contains("anti-cheat", Picker().Pick(g, null, "hades").Reason);
    }

    [Fact]
    public void A_mod_that_uses_a_generic_add_on_resolves_to_it_when_picked_by_hand()
    {
        var g = HdrFixtures.Unity("Some Unity Game");

        var choice = Picker().Pick(g, null, "deaths-door");

        Assert.Equal(HdrKind.UnityGeneric, choice.Kind);
    }

    [Fact]
    public void The_list_to_search_holds_every_installable_mod_and_generic_add_on_by_name()
    {
        var list = Picker().ManualChoices();

        Assert.Contains(("hades", "Hades"), list);
        Assert.Contains(("ue-extended", "Unreal Engine Extended"), list);
        Assert.Contains(("deaths-door", "Death's Door"), list);
        Assert.DoesNotContain(list, c => c.Id is "nexus-only" or "crashy");
        Assert.Equal(list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => c.Id), list.Select(c => c.Id));
    }

    [Fact]
    public async Task A_mod_picked_by_hand_installs_like_an_automatic_one()
    {
        var g = Game();
        var choice = Picker().Pick(g, null, "hades");

        await _service.InstallAsync(g, _target, choice, null, null, null, null, default);

        Assert.Equal(HdrFixtures.HadesContent, File.ReadAllText(Path.Combine(_target, "renodx-hades.addon64")));
        Assert.Equal("hades", Record()!.Extras[HdrPlanner.ModKey]);
    }

    [Fact]
    public void The_hand_picked_mod_and_the_Engine_ini_path_round_trip_in_the_game_settings()
    {
        var o = new GameOverride();
        Assert.Null(o.HdrMod);

        o.SetHdrMod("hades");
        o.SetEngineIniPath("  C:\\x\\Engine.ini ");
        Assert.Equal("hades", o.HdrMod);
        Assert.Equal("C:\\x\\Engine.ini", o.EngineIniPath);

        o.SetHdrMod(null);
        o.SetEngineIniPath("");
        Assert.Null(o.HdrMod);
        Assert.Null(o.EngineIniPath);
        Assert.Null(o.Components);
    }

    // ---------- import your own add-on ----------

    [Fact]
    public void An_imported_add_on_lands_in_the_library_under_its_own_hash_and_importing_it_again_adds_nothing()
    {
        var build = _store.ImportHdrBuild(Import());

        Assert.Equal(CatalogFixtures.Sha(OwnAddon), build.Id);
        Assert.Equal(HdrManual.ImportedModId, build.ModId);
        Assert.Equal("x64", build.Arch);
        Assert.Equal(OwnAddon, File.ReadAllText(ComponentStore.HdrPathOf(build)));
        Assert.Single(_store.HdrBuilds);
        Assert.Equal(build.Id, _store.ImportHdrBuild(Import()).Id);
        Assert.Single(_store.HdrBuilds);
        Assert.Equal("x86", _store.ImportHdrBuild(Import("MZ other", "renodx-mine.addon32")).Arch);
    }

    [Theory]
    [InlineData("MZ x", "notanaddon.dll")]
    [InlineData("not a pe file", "renodx-mine.addon64")]
    public void A_file_that_is_not_an_add_on_is_refused(string content, string name)
    {
        Assert.Throws<InvalidDataException>(() => _store.ImportHdrBuild(Import(content, name)));
        Assert.Empty(_store.HdrBuilds);
    }

    [Fact]
    public async Task An_imported_add_on_installs_as_it_is_without_keys_of_ours_and_uninstalls()
    {
        var g = Game();
        var build = _store.ImportHdrBuild(Import());
        var choice = HdrManual.CustomChoice(build, 64);

        Assert.True(choice.IsOffered);
        Assert.Equal(HdrKind.Custom, choice.Kind);
        await _service.InstallAsync(g, _target, choice, null, null, null, null, default);

        Assert.Equal(OwnAddon, File.ReadAllText(Path.Combine(_target, "renodx-mine.addon64")));
        var rec = Record()!;
        Assert.Equal(build.Id, rec.BuildId);
        Assert.Equal(HdrState.Current, HdrStatus.Of(choice, rec, null).State);
        Assert.False(File.Exists(ReShadeIni) && IniFile.Load(ReShadeIni).Get("renodx", "SettingsMode") is not null);

        var undo = await _service.RemoveAsync(g, _target, default);
        Assert.True(undo.Clean);
        Assert.False(File.Exists(Path.Combine(_target, "renodx-mine.addon64")));
    }

    [Fact]
    public void An_imported_add_on_of_the_other_bitness_is_flagged_and_a_missing_one_says_so()
    {
        var build32 = _store.ImportHdrBuild(Import("MZ x32", "renodx-mine.addon32"));

        Assert.Contains("32-bit add-on and the game is 64-bit", Assert.Single(HdrManual.CustomChoice(build32, 64).Notes));
        Assert.Empty(HdrManual.CustomChoice(build32, 32).Notes);
        Assert.False(HdrManual.MissingCustom().IsOffered);
    }

    [Fact]
    public void An_imported_add_on_is_in_the_library_list_so_cleanup_can_see_it()
    {
        var build = _store.ImportHdrBuild(Import());

        Assert.Contains(_store.Library(LibraryKind.RenoDxHdr), b => b.Id == build.Id && b.Group == HdrManual.ImportedModId);
    }

    // ---------- Engine.ini path typed by the user ----------

    [Fact]
    public void The_typed_Engine_ini_path_beats_the_catalog_hint_and_empty_follows_it()
    {
        var o = new GameOverride();
        Assert.Equal("catalog", EngineIniLocator.HintFor(o, "catalog"));
        Assert.Equal("catalog", EngineIniLocator.HintFor(null, "catalog"));

        o.SetEngineIniPath(@"Mine\Engine.ini");
        Assert.Equal(@"Mine\Engine.ini", EngineIniLocator.HintFor(o, "catalog"));

        o.SetEngineIniPath(null);
        Assert.Equal("catalog", EngineIniLocator.HintFor(o, "catalog"));
    }

    [Fact]
    public void A_typed_path_outside_AppData_Documents_and_the_game_is_refused_with_a_reason()
    {
        var g = Game();

        Assert.NotNull(EngineIniLocator.Problem(g, @"C:\Windows\System32\Engine.ini", _local, _docs));
        Assert.Null(EngineIniLocator.Problem(g, Path.Combine(_local, "Elsewhere", "Engine.ini"), _local, _docs));
        Assert.Null(EngineIniLocator.Problem(g, "", _local, _docs));
    }

    [Fact]
    public async Task Hdr_keys_go_to_the_Engine_ini_the_user_named()
    {
        const string ini = """{ "engine": "ue-extended", "name": "Game", "compat": "working", "method": "ini" }""";
        var json = HdrFixtures.Json().Replace("\"engineGames\": [", "\"engineGames\": [\n" + ini + ",");
        var g = Game();
        var mine = Path.Combine(_local, @"Chosen\Config\Engine.ini");
        var o = new GameOverride();
        o.SetEngineIniPath(Path.Combine(_local, @"Chosen\Config"));
        var choice = new HdrPicker(HdrFixtures.Renodx(json)).Pick(g, null);

        await _service.InstallAsync(g, _target, choice, null, EngineIniLocator.HintFor(o, null), null, null, default);

        Assert.Equal("1", IniFile.Load(mine).Get("SystemSettings", "r.AllowHDR"));
        Assert.False(File.Exists(EngineIni));
    }

    // ---------- the user's own ini values ----------

    [Fact]
    public async Task Own_reshade_values_win_over_ours_and_over_what_the_file_holds()
    {
        Write(ReShadeIni, "[renodx]\nSettingsMode=9\nMyKey=old\n");
        var g = Game();
        var choice = new HdrPicker(HdrFixtures.Renodx()).Pick(g, false);
        var mine = new[]
        {
            new HdrIniEdit("reshade", "renodx", "SettingsMode", "1"),
            new HdrIniEdit("reshade", "renodx", "MyKey", "new"),
            new HdrIniEdit("reshade", "renodx-preset1", "ToneMapPeakNits", "900"),
        };

        await _service.InstallAsync(g, _target, choice, 1000, null, null, null, default, mine);

        var ini = IniFile.Load(ReShadeIni);
        Assert.Equal("1", ini.Get("renodx", "SettingsMode"));
        Assert.Equal("new", ini.Get("renodx", "MyKey"));
        Assert.Equal("900", ini.Get("renodx-preset1", "ToneMapPeakNits"));
        Assert.Equal("1000", ini.Get("renodx-preset2", "ToneMapPeakNits"));

        await _service.RemoveAsync(g, _target, default);
        Assert.Equal("old", IniFile.Load(ReShadeIni).Get("renodx", "MyKey"));
        Assert.Equal("9", IniFile.Load(ReShadeIni).Get("renodx", "SettingsMode"));
    }

    [Fact]
    public async Task Own_Engine_ini_values_are_written_and_restored_and_other_reshade_sections_are_ignored()
    {
        var original = "[SystemSettings]\nr.Foo=1\n";
        Write(EngineIni, original);
        Write(ReShadeIni, "[GENERAL]\nEffectSearchPaths=keep\n");
        var g = Game();
        var choice = new HdrPicker(HdrFixtures.Renodx()).Pick(g, false);
        var mine = new[]
        {
            new HdrIniEdit("engine", "SystemSettings", "r.Foo", "7"),
            new HdrIniEdit("engine", "SystemSettings", "r.Bar", "2"),
            new HdrIniEdit("reshade", "GENERAL", "EffectSearchPaths", "hijacked"),
        };

        await _service.InstallAsync(g, _target, choice, null, null, null, null, default, mine);

        var ini = IniFile.Load(EngineIni);
        Assert.Equal("7", ini.Get("SystemSettings", "r.Foo"));
        Assert.Equal("2", ini.Get("SystemSettings", "r.Bar"));
        Assert.Equal("keep", IniFile.Load(ReShadeIni).Get("GENERAL", "EffectSearchPaths"));

        await _service.RemoveAsync(g, _target, default);
        Assert.Equal(original, File.ReadAllText(EngineIni));
    }

    [Fact]
    public async Task Changing_an_own_value_shows_as_an_update_and_reinstalling_current_state_is_current_again()
    {
        var g = Game();
        var choice = new HdrPicker(HdrFixtures.Renodx()).Pick(g, false);
        var first = new[] { new HdrIniEdit("reshade", "renodx", "MyKey", "a") };
        await _service.InstallAsync(g, _target, choice, null, null, null, null, default, first);

        Assert.Equal(HdrState.Current, HdrStatus.Of(choice, Record(), null, first).State);
        Assert.Equal(HdrState.Update, HdrStatus.Of(choice, Record(), null, [new HdrIniEdit("reshade", "renodx", "MyKey", "b")]).State);
        Assert.Equal(HdrState.Update, HdrStatus.Of(choice, Record(), null, null).State);
    }

    [Fact]
    public void Invalid_edits_are_dropped_and_a_later_edit_of_a_key_replaces_an_earlier_one()
    {
        var clean = HdrUserEdits.Clean(
        [
            new HdrIniEdit("reshade", "renodx", "A", "1"),
            new HdrIniEdit("reshade", "RENODX", "a", "2"),
            new HdrIniEdit("reshade", "GENERAL", "B", "1"),
            new HdrIniEdit("engine", "SystemSettings", "r.X", "line\nbreak"),
            new HdrIniEdit("engine", "Bad[Section]", "r.X", "1"),
            new HdrIniEdit("elsewhere", "renodx", "A", "1"),
            new HdrIniEdit("engine", "SystemSettings", "", "1"),
        ]);

        var only = Assert.Single(clean);
        Assert.Equal("2", only.Value);
        Assert.Equal("", HdrUserEdits.Signature(null));
        Assert.NotEqual(HdrUserEdits.Signature([new HdrIniEdit("reshade", "renodx", "A", "1")]), HdrUserEdits.Signature([new HdrIniEdit("reshade", "renodx", "A", "2")]));
    }

    [Fact]
    public void A_game_without_own_values_keeps_the_signature_it_always_had()
    {
        var choice = Picker().Pick(HdrFixtures.Unity("Some Unity Game"), null);

        Assert.Equal(choice.Signature(null), choice.Signature(null, ""));
        Assert.EndsWith("|reshade/renodx/A=1", choice.Signature(null, "reshade/renodx/A=1"));
    }
}
