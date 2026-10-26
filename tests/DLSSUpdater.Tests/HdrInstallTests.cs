using System.Net;
using System.Text;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// The RenoDX HDR component in a temp game tree: library download, ReShade, the add-on (also into AddonPath), the [renodx] keys that
/// are only written when missing, Engine.ini (found, appended, locked read-only, restored exactly), the peak brightness and updates.
/// </summary>
public class HdrInstallTests : IDisposable
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

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-hdr-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root, _target, _local, _docs;
    private readonly ComponentStore _store;
    private readonly HdrService _service;
    private readonly FakeWeb _web;
    private string _ueExtContent = HdrFixtures.UeExtContent;

    public HdrInstallTests()
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
            var u when u.EndsWith("renodx-ue-extended.addon64") => _ueExtContent,
            var u when u.EndsWith("renodx-unrealengine.addon64") => HdrFixtures.UeContent,
            var u when u.EndsWith("renodx-unityengine.addon64") => HdrFixtures.Unity64Content,
            var u when u.EndsWith("renodx-hades.addon64") => HdrFixtures.HadesContent,
            _ => null,
        });
        _service = new HdrService(_store, new Installer(_store), new CatalogDownloader(new GitHubClient(_web)));

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

    private string T(string rel) => Path.Combine(_target, rel);
    private string ReShadeIni => T("ReShade.ini");
    private string EngineIni => Path.Combine(_local, @"Game\Saved\Config\Windows\Engine.ini");

    private GameInfo Game(string version = "5.3.2")
    {
        var g = GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));
        g.Tech.Bits = 64;
        g.Tech.Engine = GameEngine.Unreal;
        g.Tech.EngineVersion = version;
        g.Tech.Apis = GraphicsApis.Dx12;
        return g;
    }

    private HdrPicker Picker(string? json = null) => new(HdrFixtures.Renodx(json));

    private HdrChoice Choose(GameInfo g, bool? ue = null, string? json = null) => Picker(json).Pick(g, ue);

    private string CatalogWith(string gameEntry) => HdrFixtures.Json().Replace("\"engineGames\": [", "\"engineGames\": [\n" + gameEntry + ",");

    private const string IniGameEntry = """{ "engine": "ue-extended", "name": "Game", "compat": "working", "method": "ini" }""";

    private Task<HdrInstallResult> Install(GameInfo g, HdrChoice c, int? peak = null, string? hint = null) =>
        _service.InstallAsync(g, _target, c, peak, hint, null, null, default);

    private InstalledComponent? Record() => InstallManifest.Read(_target)?.Component(ComponentRegistry.RenoDxHdr);

    // ---------- install and uninstall ----------

    [Fact]
    public async Task Install_downloads_the_build_into_the_library_installs_ReShade_and_places_the_add_on_with_its_hash()
    {
        var g = Game();
        var choice = Choose(g, json: CatalogWith(IniGameEntry));

        var result = await Install(g, choice);

        var sha = CatalogFixtures.Sha(HdrFixtures.UeExtContent);
        Assert.Equal(sha, result.Build.Id);
        Assert.Equal(HdrFixtures.UeExtContent, File.ReadAllText(ComponentStore.HdrPathOf(result.Build)));
        Assert.Equal("RESHADE", File.ReadAllText(T("dxgi.dll")));
        var m = InstallManifest.Read(_target)!;
        Assert.True(m.ReShade);
        Assert.Equal(InstallMode.ReShadeOnly, m.Mode);
        Assert.Equal(HdrFixtures.UeExtContent, File.ReadAllText(T("renodx-ue-extended.addon64")));
        var c = m.Component(ComponentRegistry.RenoDxHdr)!;
        Assert.Equal(sha, c.BuildId);
        Assert.Equal("2026-10-21-aaaaaaa", c.Version);
        Assert.Equal(sha, c.Files.Single().Sha256, ignoreCase: true);
        Assert.Contains("RenoDX HDR", m.ModLabel());
    }

    [Fact]
    public async Task The_add_on_goes_into_the_AddonPath_folder_when_reshade_ini_sets_one()
    {
        Write(ReShadeIni, "[ADDON]\nAddonPath=.\\addons\n");
        var g = Game();

        await Install(g, Choose(g));

        Assert.True(File.Exists(T(@"addons\renodx-ue-extended.addon64")));
        Assert.False(File.Exists(T("renodx-ue-extended.addon64")));
        Assert.Equal(".\\addons", IniFile.Load(ReShadeIni).Get("ADDON", "AddonPath"));

        await _service.RemoveAsync(g, _target, default);

        Assert.False(File.Exists(T(@"addons\renodx-ue-extended.addon64")));
        Assert.Null(Record());
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(@".\addons\**", "addons")]
    [InlineData(@"addons;other", "addons")]
    [InlineData(@"C:\Elsewhere\NotTheGame", "")]
    [InlineData(@"..\..\..\..\..\outside", "")]
    public void AddonPath_resolves_relative_to_ReShade_and_never_leaves_the_game(string? value, string expectedRel)
    {
        var resolved = ReShadeAddonPath.FromValue(value, _target, _root);

        Assert.Equal(FileUtil.Normalize(Path.Combine(_target, expectedRel)), resolved);
    }

    [Fact]
    public async Task Uninstall_puts_everything_back_including_a_game_file_the_add_on_replaced()
    {
        Write(T("renodx-ue-extended.addon64"), "GAME-OWN-COPY");
        Write(ReShadeIni, "[GENERAL]\nEffectSearchPaths=.\\x\n");
        var g = Game();
        var before = File.ReadAllText(ReShadeIni);
        await Install(g, Choose(g, json: CatalogWith(IniGameEntry)));
        Assert.Equal(HdrFixtures.UeExtContent, File.ReadAllText(T("renodx-ue-extended.addon64")));

        var undo = await _service.RemoveAsync(g, _target, default);

        Assert.True(undo.Clean);
        Assert.Equal("GAME-OWN-COPY", File.ReadAllText(T("renodx-ue-extended.addon64")));
        Assert.Null(Record());
        Assert.False(File.Exists(EngineIni));
        Assert.DoesNotContain("renodx", File.ReadAllText(ReShadeIni));
        Assert.Contains("EffectSearchPaths", File.ReadAllText(ReShadeIni));
        Assert.Contains(before.TrimEnd(), File.ReadAllText(ReShadeIni));
    }

    [Fact]
    public async Task Nothing_is_installed_for_a_game_with_anti_cheat_or_without_an_offer()
    {
        var g = Game();
        g.AntiCheat = "BattlEye";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Install(g, Choose(g)));

        Assert.Contains("BattlEye", ex.Message);
        Assert.Null(InstallManifest.Read(_target));
    }

    [Fact]
    public async Task OptiScaler_without_ReShade_is_not_given_a_second_loader()
    {
        new InstallManifest { Opti = true, OptiTag = "v1", Proxy = "dxgi.dll" }.Save(_target);
        var g = Game();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Install(g, Choose(g)));

        Assert.Contains("OptiScaler", ex.Message);
        Assert.Null(Record());
    }

    [Fact]
    public async Task A_download_that_does_not_match_the_catalog_hash_installs_nothing()
    {
        _ueExtContent = "MZ something else entirely";
        var g = Game();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Install(g, Choose(g)));

        Assert.Contains("refused", ex.Message);
        Assert.Empty(_store.HdrBuilds);
        Assert.False(File.Exists(T("renodx-ue-extended.addon64")));
    }

    // ---------- reshade.ini [renodx] ----------

    [Fact]
    public async Task Renodx_keys_are_written_only_when_missing_and_a_users_value_is_never_overwritten()
    {
        Write(ReShadeIni, "[renodx]\nForceBorderless=0\nSet_Path=1\nUpgrade_B8G8R8A8_UNORM=2\n[GENERAL]\nx=1\n");
        var g = Game();

        await Install(g, Choose(g, json: CatalogWith(IniGameEntry)));

        var ini = IniFile.Load(ReShadeIni);
        Assert.Equal("0", ini.Get("renodx", "ForceBorderless"));
        Assert.Equal("1", ini.Get("renodx", "Set_Path"));
        Assert.Equal("2", ini.Get("renodx", "Upgrade_B8G8R8A8_UNORM"));
        Assert.Equal("1", ini.Get("renodx", "PreventFullscreen"));
        Assert.Equal("2", ini.Get("renodx", "SettingsMode"));
        Assert.Equal("1", ini.Get("renodx", "FxUpgradeRender"));
        Assert.Equal("", ini.Get("renodx", "Upgrade_R11G11B10_FLOAT"));

        await _service.RemoveAsync(g, _target, default);

        var after = IniFile.Load(ReShadeIni);
        Assert.Equal("0", after.Get("renodx", "ForceBorderless"));
        Assert.Equal("1", after.Get("renodx", "Set_Path"));
        Assert.Equal("2", after.Get("renodx", "Upgrade_B8G8R8A8_UNORM"));
        Assert.Null(after.Get("renodx", "PreventFullscreen"));
        Assert.Null(after.Get("renodx", "Upgrade_R11G11B10_FLOAT"));
        Assert.Equal("1", after.Get("GENERAL", "x"));
    }

    [Theory]
    [InlineData("Native Game", "0", null)]
    [InlineData("Ini Game", "0", null)]
    [InlineData("Upgrade Game", "1", "3")]
    public async Task Set_Path_follows_the_method_and_the_upgrade_method_forces_its_one_format_key(string wikiName, string setPath, string? upgrade)
    {
        var g = Game();
        g.Name = wikiName;

        await Install(g, Choose(g));

        var ini = IniFile.Load(ReShadeIni);
        Assert.Equal(setPath, ini.Get("renodx", "Set_Path"));
        Assert.Equal(upgrade ?? "", ini.Get("renodx", "Upgrade_B8G8R8A8_TYPELESS"));
        if (upgrade is not null) Assert.Equal("0", ini.Get("renodx", "Upgrade_CopyDestinations"));
    }

    [Fact]
    public async Task Generic_unreal_and_unity_only_get_their_placeholder_keys()
    {
        var g = Game();

        await Install(g, Choose(g, ue: false));

        var ini = IniFile.Load(ReShadeIni);
        Assert.Equal("", ini.Get("renodx", "ForceBorderless"));
        Assert.Equal("", ini.Get("renodx", "DumpLUTShaders"));
        Assert.Null(ini.Get("renodx", "Set_Path"));
        Assert.Null(ini.Get("renodx", "Use_Swapchain_Proxy"));
        Assert.False(File.Exists(EngineIni));
        Assert.Equal(HdrFixtures.UeContent, File.ReadAllText(T("renodx-unrealengine.addon64")));
        // Switching to Unreal Extended removes the plain add-on and puts the new keys in.
        await Install(g, Choose(g, ue: true));
        Assert.False(File.Exists(T("renodx-unrealengine.addon64")));
        Assert.True(File.Exists(T("renodx-ue-extended.addon64")));
        Assert.Equal("0", IniFile.Load(ReShadeIni).Get("renodx", "Set_Path"));
    }

    [Fact]
    public async Task A_named_mod_gets_no_renodx_section_keys_at_all()
    {
        var g = Game();
        g.SteamAppId = 1145360;
        g.Name = "Hades";

        await Install(g, Choose(g));

        Assert.Equal(HdrFixtures.HadesContent, File.ReadAllText(T("renodx-hades.addon64")));
        var ini = File.Exists(ReShadeIni) ? IniFile.Load(ReShadeIni) : null;
        Assert.Null(ini?.Get("renodx", "Set_Path"));
        Assert.DoesNotContain(ini?.Entries() ?? [], e => e.Section.Equals("renodx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_key_the_user_changed_after_install_is_released_on_update_and_left_alone_on_uninstall()
    {
        var g = Game();
        await Install(g, Choose(g, json: CatalogWith(IniGameEntry)));
        var ini = IniFile.Load(ReShadeIni);
        ini.Set("renodx", "SettingsMode", "5");
        ini.Save(ReShadeIni);

        await Install(g, Choose(g, json: CatalogWith(IniGameEntry)), peak: 800);

        Assert.Equal("5", IniFile.Load(ReShadeIni).Get("renodx", "SettingsMode"));
        Assert.Null(Record()!.EditOf("|install|\\ReShade.ini", "renodx", "SettingsMode") ?? Record()!.ConfigEdits.FirstOrDefault(e => e.Key == "SettingsMode"));
        await _service.RemoveAsync(g, _target, default);
        Assert.Equal("5", IniFile.Load(ReShadeIni).Get("renodx", "SettingsMode"));
    }

    // ---------- peak brightness ----------

    [Fact]
    public async Task Peak_nits_go_into_all_three_presets_replace_what_was_there_and_come_back_off_on_uninstall()
    {
        Write(ReShadeIni, "[renodx-preset2]\nToneMapPeakNits=600\nOther=1\n");
        var g = Game();

        await Install(g, Choose(g), peak: 1000);

        var ini = IniFile.Load(ReShadeIni);
        foreach (var preset in new[] { "renodx-preset1", "renodx-preset2", "renodx-preset3" })
            Assert.Equal("1000", ini.Get(preset, "ToneMapPeakNits"));

        // Changing the setting and updating re-applies it; clearing it hands the old values back.
        await Install(g, Choose(g), peak: 1500);
        Assert.Equal("1500", IniFile.Load(ReShadeIni).Get("renodx-preset3", "ToneMapPeakNits"));
        await Install(g, Choose(g), peak: null);
        ini = IniFile.Load(ReShadeIni);
        Assert.Equal("600", ini.Get("renodx-preset2", "ToneMapPeakNits"));
        Assert.Null(ini.Get("renodx-preset1", "ToneMapPeakNits"));
        Assert.Equal("1", ini.Get("renodx-preset2", "Other"));
    }

    [Theory]
    [InlineData(10, 80)]
    [InlineData(1000, 1000)]
    [InlineData(99999, 10000)]
    public void Peak_nits_are_clamped_to_a_sane_range(int given, int expected) => Assert.Equal(expected, HdrIni.ClampPeak(given));

    // ---------- Engine.ini ----------

    [Fact]
    public async Task Engine_ini_gets_the_missing_hdr_keys_appended_keeps_duplicates_goes_read_only_and_is_restored_exactly()
    {
        var original = "[Section]\r\n; a comment\r\nr.Foo=1\r\nr.Foo=2\r\n\r\n[SystemSettings]\r\nr.HDR.EnableHDROutput=0\r\nr.Dup=1\r\nr.Dup=1\r\n";
        Write(EngineIni, original);
        var g = Game();

        await Install(g, Choose(g, json: CatalogWith(IniGameEntry)));

        var text = File.ReadAllText(EngineIni);
        var ini = IniFile.Parse(text);
        Assert.Equal("1", ini.Get("SystemSettings", "r.AllowHDR"));
        Assert.Equal("3", ini.Get("SystemSettings", "r.HDR.Display.OutputDevice"));
        Assert.Equal("2", ini.Get("SystemSettings", "r.HDR.Display.ColorGamut"));
        Assert.Equal("1", ini.Get("SystemSettings", "r.HDR.UI.CompositeMode"));
        Assert.Equal("1", ini.Get("/Script/Engine.RendererSettings", "r.LUT.UpdateEveryFrame"));
        // A value the user already set stays; duplicates and comments are untouched.
        Assert.Equal("0", ini.Get("SystemSettings", "r.HDR.EnableHDROutput"));
        Assert.Contains("r.Foo=1\r\nr.Foo=2", text);
        Assert.Equal(2, ini.Entries().Count(e => e.Key == "r.Dup"));
        Assert.True(new FileInfo(EngineIni).IsReadOnly);
        Assert.NotEmpty(Record()!.Attributes);

        var undo = await _service.RemoveAsync(g, _target, default);

        Assert.True(undo.Clean);
        Assert.Equal(original, File.ReadAllText(EngineIni));
        Assert.False(new FileInfo(EngineIni).IsReadOnly);
    }

    [Fact]
    public async Task Engine_ini_that_was_already_read_only_stays_read_only_and_a_file_without_a_final_newline_or_with_a_BOM_comes_back_byte_for_byte()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("[SystemSettings]\nr.A=1")).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(EngineIni)!);
        File.WriteAllBytes(EngineIni, bytes);
        File.SetAttributes(EngineIni, FileAttributes.ReadOnly);
        var g = Game();

        await Install(g, Choose(g, json: CatalogWith(IniGameEntry)));

        Assert.True(new FileInfo(EngineIni).IsReadOnly);
        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(EngineIni).Take(3));
        Assert.Equal("1", IniFile.Load(EngineIni).Get("SystemSettings", "r.AllowHDR"));

        await _service.RemoveAsync(g, _target, default);

        Assert.Equal(bytes, File.ReadAllBytes(EngineIni));
        Assert.True(new FileInfo(EngineIni).IsReadOnly);
    }

    [Fact]
    public async Task A_new_engine_ini_is_created_and_removed_again()
    {
        var g = Game();

        await Install(g, Choose(g, json: CatalogWith(IniGameEntry)));

        Assert.True(File.Exists(EngineIni));

        await _service.RemoveAsync(g, _target, default);

        Assert.False(File.Exists(EngineIni));
    }

    [Fact]
    public async Task The_upgrade_and_native_methods_leave_engine_ini_alone()
    {
        var g = Game();
        g.Name = "Upgrade Game";

        await Install(g, Choose(g));

        Assert.False(File.Exists(EngineIni));
        Assert.Empty(Record()!.Attributes);
    }

    [Fact]
    public async Task A_catalog_engine_ini_key_list_replaces_the_standard_keys()
    {
        var g = Game();
        g.Name = "Wukong Like";

        await Install(g, Choose(g));

        var ini = IniFile.Load(EngineIni);
        Assert.Equal("1", ini.Get("SystemSettings", "r.HDR.EnableHDROutput"));
        Assert.Null(ini.Get("SystemSettings", "r.AllowHDR"));
    }

    [Fact]
    public async Task The_catalogs_path_hint_wins_over_the_search()
    {
        var hinted = Path.Combine(_local, @"Hinted\Saved\Config\Windows\Engine.ini");
        Write(EngineIni, "[SystemSettings]\n");
        var g = Game();
        g.SteamAppId = 4343;

        await Install(g, Choose(g, json: CatalogWith(IniGameEntry)), hint: HdrFixtures.Renodx().Overrides.EngineIniPath(4343, "Game"));

        Assert.True(File.Exists(hinted));
        Assert.Equal("1", IniFile.Load(hinted).Get("SystemSettings", "r.AllowHDR"));
        Assert.Null(IniFile.Load(EngineIni).Get("SystemSettings", "r.AllowHDR"));
    }

    [Fact]
    public void Engine_ini_is_found_in_the_documented_order()
    {
        var g = Game();

        // Nothing yet: the game would create it in AppData.
        var fresh = EngineIniLocator.Resolve(g, null, _local, _docs)!;
        Assert.Equal(EngineIni, fresh.Path);
        Assert.False(fresh.Exists);

        // Inside the game folder.
        Directory.CreateDirectory(Path.Combine(_root, @"Game\Saved\Config\WindowsNoEditor"));
        Assert.Equal(Path.Combine(_root, @"Game\Saved\Config\WindowsNoEditor\Engine.ini"), EngineIniLocator.Resolve(g, null, _local, _docs)!.Path);

        // Documents beats the game folder.
        Directory.CreateDirectory(Path.Combine(_docs, @"My Games\Game\Saved\Config\Windows"));
        Assert.Equal(Path.Combine(_docs, @"My Games\Game\Saved\Config\Windows\Engine.ini"), EngineIniLocator.Resolve(g, null, _local, _docs)!.Path);

        // AppData beats Documents, and WinGDK beats Windows there.
        Directory.CreateDirectory(Path.Combine(_local, @"Game\Saved\Config\Windows"));
        Directory.CreateDirectory(Path.Combine(_local, @"Game\Saved\Config\WinGDK"));
        var found = EngineIniLocator.Resolve(g, null, _local, _docs)!;
        Assert.Equal(Path.Combine(_local, @"Game\Saved\Config\WinGDK\Engine.ini"), found.Path);
        Assert.Equal("AppData", found.Source);
    }

    [Fact]
    public void A_game_pass_build_creates_its_engine_ini_under_WinGDK_and_a_hint_outside_the_allowed_places_is_ignored()
    {
        var g = Game();
        g.Tech.ModFolder = Path.Combine(_root, @"Game\Binaries\WinGDK");

        Assert.Equal(Path.Combine(_local, @"Game\Saved\Config\WinGDK\Engine.ini"), EngineIniLocator.Resolve(g, null, _local, _docs)!.Path);
        Assert.Equal(Path.Combine(_local, @"Game\Saved\Config\WinGDK\Engine.ini"), EngineIniLocator.Resolve(g, @"C:\Windows\System32\Engine.ini", _local, _docs)!.Path);
    }

    // ---------- updates ----------

    [Fact]
    public async Task A_newer_catalog_build_is_an_update_that_replaces_the_add_on_and_the_old_build_stays_in_the_library()
    {
        var g = Game();
        var first = await Install(g, Choose(g));
        Assert.Equal(HdrState.Current, HdrStatus.Of(Choose(g), Record(), null).State);

        _ueExtContent = "MZ ue-extended build 2";
        var newer = Choose(g, json: HdrFixtures.Json(_ueExtContent, "2026-10-22-bbbbbbb"));
        var status = HdrStatus.Of(newer, Record(), null);
        Assert.Equal(HdrState.Update, status.State);
        Assert.True(status.UpdateAvailable);
        Assert.Equal("2026-10-21-aaaaaaa", status.Installed);
        Assert.Equal("2026-10-22-bbbbbbb", status.Latest);
        Assert.Contains("update to 2026-10-22-bbbbbbb", status.Line);

        var second = await Install(g, newer);

        Assert.NotEqual(first.Build.Id, second.Build.Id);
        Assert.Equal("MZ ue-extended build 2", File.ReadAllText(T("renodx-ue-extended.addon64")));
        Assert.Equal(HdrState.Current, HdrStatus.Of(newer, Record(), null).State);
        Assert.Equal(2, _store.HdrBuilds.Count);
        Assert.Equal(second.Build.Id, _store.HdrBuilds[0].Id);
        Assert.Single(Record()!.Files);
    }

    [Fact]
    public async Task A_changed_peak_brightness_counts_as_an_update_and_offline_status_needs_no_network()
    {
        var g = Game();
        await Install(g, Choose(g), peak: 1000);
        var urls = _web.Urls.Count;

        Assert.Equal(HdrState.Current, HdrStatus.Of(Choose(g), Record(), 1000).State);
        var changed = HdrStatus.Of(Choose(g), Record(), 1200);

        Assert.Equal(HdrState.Update, changed.State);
        Assert.Contains("settings changed", changed.Line);
        Assert.Equal(urls, _web.Urls.Count);
    }

    [Fact]
    public async Task Status_covers_not_installed_unavailable_and_an_install_the_catalog_no_longer_offers()
    {
        var g = Game();
        Assert.Equal(HdrState.NotInstalled, HdrStatus.Of(Choose(g), null, null).State);
        var anti = Game();
        anti.AntiCheat = "EAC";
        var none = Choose(anti);
        var unavailable = HdrStatus.Of(none, null, null);
        Assert.Equal(HdrState.Unavailable, unavailable.State);
        Assert.Contains("EAC", unavailable.Line);

        await Install(g, Choose(g));
        var orphan = HdrStatus.Of(none, Record(), null);
        Assert.Equal(HdrState.Orphaned, orphan.State);
        Assert.False(orphan.UpdateAvailable);
    }

    // ---------- library and registry ----------

    [Fact]
    public async Task The_library_lists_builds_by_mod_and_the_cleanup_protects_the_installed_one()
    {
        var g = Game();
        await Install(g, Choose(g));
        _ueExtContent = "MZ ue-extended build 2";
        await Install(g, Choose(g, json: HdrFixtures.Json(_ueExtContent, "2026-10-22-bbbbbbb")));
        await Install(Game(), Choose(Game(), ue: false));

        var builds = _store.Library(LibraryKind.RenoDxHdr);
        Assert.Equal(3, builds.Count);
        Assert.Contains(builds, b => b.Group == "ue-extended");
        Assert.Contains(builds, b => b.Group == "unreal");
        var manifests = new[] { InstallManifest.Read(_target)! };
        var protectedIds = _store.ProtectedBuilds(LibraryKind.RenoDxHdr, new AppSettings { KeepBuilds = { ["RenoDxHdr"] = 1 } }, manifests);
        Assert.Contains(CatalogFixtures.Sha(HdrFixtures.UeContent), protectedIds);
        Assert.DoesNotContain(CatalogFixtures.Sha(HdrFixtures.UeExtContent), protectedIds);
        Assert.Contains(manifests, m => m.Component(ComponentRegistry.RenoDxHdr) is not null);
    }

    [Fact]
    public void The_component_is_registered_apart_from_the_ShortFuse_DLSS_add_on_and_both_can_be_listed_in_one_game()
    {
        var hdr = ComponentRegistry.Find(ComponentRegistry.RenoDxHdr)!;
        var dlss = ComponentRegistry.Find(ComponentRegistry.RenoDx)!;

        Assert.NotEqual(hdr.Id, dlss.Id);
        Assert.False(hdr.Legacy);
        Assert.Equal(LibraryKind.RenoDxHdr, hdr.Library);
        Assert.True(hdr.OwnsFile("renodx-hades.addon64"));
        var m = new InstallManifest { RenoDx = true, ReShade = true, Mode = InstallMode.ReShadeOnly };
        m.Components.Add(new InstalledComponent { Id = hdr.Id });
        Assert.Equal("ReShade + ShortFuse + RenoDX HDR", m.ModLabel());
    }

    [Fact]
    public async Task Another_renodx_add_on_in_the_folder_is_warned_about_but_the_DLSS_add_on_is_not()
    {
        Write(T("renodx-other.addon64"), "X");
        Write(T("renodx-dlss.addon64"), "Y");
        var g = Game();
        var choice = Choose(g);
        var build = await _store.EnsureHdrBuildAsync(choice.File!, choice.ModId!, choice.Title, new CatalogDownloader(new GitHubClient(_web)), null, default);

        var plan = HdrPlanner.Build(g, _target, choice, build, ComponentStore.HdrPathOf(build), null, null, null);

        Assert.Single(plan.Warnings);
        Assert.Contains("renodx-other.addon64", plan.Warnings[0]);
    }
}
