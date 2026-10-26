using System.Net;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// The Luma component in a temp game tree: download and unpack, the add-on (also into AddonPath), the Luma\ tree beside the exe, the files
/// of the zip that are never copied, the [Luma] keys, Engine.ini, remove, updates and the clash with the RenoDX HDR component.
/// </summary>
public class LumaInstallTests : IDisposable
{
    private sealed class FakeWeb(Func<string, byte[]?> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(body(request.RequestUri!.ToString()) is { } bytes
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-luma-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root, _target, _local, _docs;
    private readonly ComponentStore _store;
    private readonly LumaService _service;
    private byte[] _prey = LumaFixtures.PreyZip;

    public LumaInstallTests()
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
            var u when u.EndsWith("Luma-Prey.zip") => _prey,
            var u when u.EndsWith("Luma-Unreal_Engine.zip") => LumaFixtures.UnrealZip,
            _ => null,
        });
        _service = new LumaService(_store, new Installer(_store), new CatalogDownloader(new GitHubClient(web)));

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

    private GameInfo Game(string name, string version = "4.27.2")
    {
        var g = GameScanner.Inspect(new GameEntry(name, _root, "Manual"));
        g.Tech.Bits = 64;
        g.Tech.Engine = GameEngine.Unreal;
        g.Tech.EngineVersion = version;
        g.Tech.Apis = GraphicsApis.Dx11;
        return g;
    }

    private LumaChoice Choose(GameInfo g, string? manual = null) => LumaFixtures.Picker(_prey).Pick(g, manual);

    private Task<LumaInstallResult> Install(GameInfo g, LumaChoice c, bool? hdr = null, string? hint = null, bool clash = false) =>
        _service.InstallAsync(g, _target, c, hdr, hint, null, clash, null, default);

    private InstalledComponent? Record() => InstallManifest.Read(_target)?.Component(ComponentRegistry.Luma);

    // ---------- install and remove ----------

    [Fact]
    public async Task Install_downloads_the_zip_installs_ReShade_places_the_add_on_and_the_Luma_tree_and_records_the_hash()
    {
        var g = Game("Prey");

        var result = await Install(g, Choose(g));

        var sha = LumaFixtures.Sha(LumaFixtures.PreyZip);
        Assert.Equal(sha, result.Build.Id);
        var m = InstallManifest.Read(_target)!;
        Assert.True(m.ReShade);
        Assert.Equal("MZ luma addon v1", File.ReadAllText(T("Luma-Prey.addon64")));
        Assert.Equal("SHADER", File.ReadAllText(T(@"Luma\Shaders\main.hlsl")));
        Assert.Equal("MZ compiler", File.ReadAllText(T(@"Luma\d3dcompiler_47.dll")));
        var c = m.Component(ComponentRegistry.Luma)!;
        Assert.Equal(sha, c.BuildId);
        Assert.Equal("latest-755", c.Version);
        Assert.Equal(3, c.Files.Count);
        Assert.Contains("Luma", m.ModLabel());
        Assert.Equal("prey", c.Extras[LumaPlanner.ModKey]);
    }

    [Fact]
    public async Task The_ReShade_dll_ini_log_and_nvngx_dlss_of_the_zip_are_never_copied()
    {
        var g = Game("Prey");

        await Install(g, Choose(g));

        // dxgi.dll is the managed ReShade, not the zip's own.
        Assert.Equal("RESHADE", File.ReadAllText(T("dxgi.dll")));
        Assert.False(File.Exists(T("d3d11.dll")));
        Assert.False(File.Exists(T("nvngx_dlss.dll")));
        Assert.False(File.Exists(T("ReShade.log")));
        Assert.DoesNotContain("FromZip", File.Exists(ReShadeIni) ? File.ReadAllText(ReShadeIni) : "");
    }

    [Fact]
    public async Task The_add_on_goes_into_the_AddonPath_folder_while_the_Luma_tree_stays_beside_the_exe()
    {
        Write(ReShadeIni, "[ADDON]\nAddonPath=.\\addons\n");
        var g = Game("Prey");

        await Install(g, Choose(g));

        Assert.True(File.Exists(T(@"addons\Luma-Prey.addon64")));
        Assert.False(File.Exists(T("Luma-Prey.addon64")));
        Assert.True(File.Exists(T(@"Luma\Shaders\main.hlsl")));

        await _service.RemoveAsync(g, _target, default);

        Assert.False(File.Exists(T(@"addons\Luma-Prey.addon64")));
        Assert.Null(Record());
    }

    [Fact]
    public async Task Remove_takes_every_file_away_and_puts_back_a_game_file_the_mod_replaced()
    {
        Write(T(@"Luma\d3dcompiler_47.dll"), "GAME-OWN-COMPILER");
        var g = Game("Prey");
        await Install(g, Choose(g));
        Assert.Equal("MZ compiler", File.ReadAllText(T(@"Luma\d3dcompiler_47.dll")));

        var undo = await _service.RemoveAsync(g, _target, default);

        Assert.True(undo.Clean);
        Assert.Equal("GAME-OWN-COMPILER", File.ReadAllText(T(@"Luma\d3dcompiler_47.dll")));
        Assert.False(File.Exists(T("Luma-Prey.addon64")));
        Assert.False(File.Exists(T(@"Luma\Shaders\main.hlsl")));
        Assert.Null(Record());
    }

    [Fact]
    public async Task Nothing_is_installed_for_a_game_with_anti_cheat_or_without_an_offer()
    {
        var g = Game("Prey");
        g.AntiCheat = "BattlEye";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Install(g, Choose(g)));
        Assert.Contains("BattlEye", ex.Message);

        var none = Game("No Such Game");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Install(none, Choose(none)));
        Assert.Null(InstallManifest.Read(_target));
    }

    [Fact]
    public async Task OptiScaler_without_ReShade_is_not_given_a_second_loader()
    {
        new InstallManifest { Opti = true, OptiTag = "v1", Proxy = "dxgi.dll" }.Save(_target);
        var g = Game("Prey");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Install(g, Choose(g)));

        Assert.Contains("OptiScaler", ex.Message);
        Assert.False(File.Exists(T("Luma-Prey.addon64")));
    }

    // ---------- ini keys ----------

    [Fact]
    public async Task HDR_Auto_writes_no_Luma_keys_and_On_or_Off_write_both_and_remove_restores_the_ini()
    {
        Write(ReShadeIni, "[GENERAL]\nEffectSearchPaths=.\\x\n");
        var before = File.ReadAllText(ReShadeIni);
        var g = Game("Prey");

        await Install(g, Choose(g));
        Assert.Null(IniFile.Load(ReShadeIni).Get("Luma", "EnableHDR"));

        await Install(g, Choose(g), hdr: true);
        Assert.Equal("1", IniFile.Load(ReShadeIni).Get("Luma", "EnableHDR"));
        Assert.Equal("1", IniFile.Load(ReShadeIni).Get("Luma", "DisplayMode"));

        await Install(g, Choose(g), hdr: false);
        Assert.Equal("0", IniFile.Load(ReShadeIni).Get("Luma", "EnableHDR"));
        Assert.Equal("0", IniFile.Load(ReShadeIni).Get("Luma", "DisplayMode"));

        await _service.RemoveAsync(g, _target, default);

        Assert.Null(IniFile.Load(ReShadeIni).Get("Luma", "EnableHDR"));
        Assert.Contains("EffectSearchPaths", File.ReadAllText(ReShadeIni));
        Assert.Contains(before.TrimEnd(), File.ReadAllText(ReShadeIni));
    }

    [Fact]
    public async Task A_listed_Unreal_game_gets_the_wiki_Engine_ini_lines_locked_and_restored_on_remove()
    {
        var g = Game("Listed Game");
        var choice = Choose(g);
        Assert.Equal(LumaKind.Unreal, choice.Kind);

        var result = await Install(g, choice);

        Assert.Equal(EngineIni, result.Plan.EngineIni!.Path);
        Assert.Equal("1", IniFile.Load(EngineIni).Get("SystemSettings", "r.LumaTest"));
        Assert.True(new FileInfo(EngineIni).Attributes.HasFlag(FileAttributes.ReadOnly));
        Assert.Equal("MZ luma unreal", File.ReadAllText(T("Luma-Unreal_Engine.addon64")));

        await _service.RemoveAsync(g, _target, default);

        Assert.False(File.Exists(EngineIni));
    }

    [Fact]
    public async Task An_Engine_ini_value_the_user_set_is_kept()
    {
        Write(EngineIni, "[SystemSettings]\nr.LumaTest=7\n");
        var g = Game("Listed Game");

        await Install(g, Choose(g));

        Assert.Equal("7", IniFile.Load(EngineIni).Get("SystemSettings", "r.LumaTest"));
    }

    [Fact]
    public async Task An_unplaceable_Engine_ini_is_a_warning_not_a_failure()
    {
        var g = Game("Listed Game");
        g.Tech.UeProject = null;

        var result = await Install(g, Choose(g));

        Assert.Contains(result.Plan.Warnings, w => w.Contains("Engine.ini"));
        Assert.NotNull(Record());
    }

    // ---------- updates and pins ----------

    [Fact]
    public async Task Status_follows_the_record_the_HDR_setting_and_a_newer_catalog_zip()
    {
        var g = Game("Prey");
        var choice = Choose(g);
        Assert.Equal(HdrState.NotInstalled, LumaStatus.Of(choice, null, null).State);

        await Install(g, choice);

        Assert.Equal(HdrState.Current, LumaStatus.Of(choice, Record(), null).State);
        var changed = LumaStatus.Of(choice, Record(), true);
        Assert.True(changed.UpdateAvailable);
        Assert.Contains("settings changed", changed.Line);
        var newer = LumaFixtures.Picker(LumaFixtures.PreyZipV2).Pick(g);
        var update = LumaStatus.Of(newer, Record(), null);
        Assert.True(update.UpdateAvailable);
        Assert.Contains("update to", update.Line);
        Assert.Equal(HdrState.Orphaned, LumaStatus.Of(Choose(Game("Nobody")), Record(), null).State);
    }

    [Fact]
    public async Task A_newer_zip_replaces_the_files_and_a_pin_keeps_the_older_one()
    {
        var g = Game("Prey");
        var first = await Install(g, Choose(g));
        _prey = LumaFixtures.PreyZipV2;
        var newest = Choose(g);

        await Install(g, newest);
        Assert.Equal("MZ luma addon v2", File.ReadAllText(T("Luma-Prey.addon64")));
        Assert.NotEqual(first.Build.Id, Record()!.BuildId);

        var pinned = LumaPin.Apply(newest, first.Build.Id, _store.LumaBuilds);
        await Install(g, pinned);
        Assert.Equal("MZ luma addon v1", File.ReadAllText(T("Luma-Prey.addon64")));
        Assert.Equal(first.Build.Id, Record()!.BuildId);
    }

    [Fact]
    public async Task An_imported_zip_is_installed_as_it_is_from_the_library_copy()
    {
        var path = Path.Combine(_tmp, "in", "Luma-Mine.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, LumaFixtures.Zip("Luma-Mine.addon64", "MZ mine"));
        var build = _store.ImportLumaBuild(path);
        var g = Game("Whatever");

        await Install(g, LumaManual.CustomChoice(build, 64) with { Pinned = build });

        Assert.Equal("MZ mine", File.ReadAllText(T("Luma-Mine.addon64")));
        Assert.Equal(build.Id, Record()!.BuildId);
        Assert.Equal("custom", Record()!.Extras[LumaPlanner.ModKey]);
    }

    [Fact]
    public async Task A_second_Luma_add_on_in_the_folder_is_warned_about()
    {
        Write(T("Luma-Other.addon64"), "MZ other");
        var g = Game("Prey");

        var result = await Install(g, Choose(g));

        Assert.Contains(result.Plan.Warnings, w => w.Contains("Luma-Other.addon64"));
    }

    // ---------- the clash with RenoDX HDR ----------

    private void AddRenoDxRecord(bool confirmed = false)
    {
        var m = InstallManifest.Read(_target) ?? new InstallManifest();
        var c = new InstalledComponent { Id = ComponentRegistry.RenoDxHdr, BuildId = "abc", Version = "v" };
        if (confirmed) c.Extras[LumaConflict.ClashKey] = LumaConflict.ClashValue;
        m.Components.Add(c);
        m.Save(_target);
    }

    [Fact]
    public async Task Luma_next_to_a_RenoDX_HDR_mod_is_a_warning_the_user_confirms_and_the_confirmation_is_recorded()
    {
        AddRenoDxRecord();
        var m = InstallManifest.Read(_target);
        Assert.NotNull(LumaConflict.ForLuma(m));
        Assert.False(LumaConflict.Clashes(m));
        Assert.False(LumaConflict.Confirmed(m));

        var g = Game("Prey");
        await Install(g, Choose(g), clash: true);

        m = InstallManifest.Read(_target);
        Assert.NotNull(m!.Component(ComponentRegistry.RenoDxHdr));
        Assert.True(LumaConflict.Clashes(m));
        Assert.True(LumaConflict.Confirmed(m));
    }

    [Fact]
    public async Task An_unconfirmed_pair_stays_unconfirmed_and_a_confirmation_on_the_RenoDX_side_counts()
    {
        var g = Game("Prey");
        await Install(g, Choose(g));
        AddRenoDxRecord();
        var m = InstallManifest.Read(_target);

        Assert.True(LumaConflict.Clashes(m));
        Assert.False(LumaConflict.Confirmed(m));
        Assert.NotNull(LumaConflict.ForRenoDx(m));

        var other = InstallManifest.Read(_target)!;
        other.Components.Single(c => c.Id == ComponentRegistry.RenoDxHdr).Extras[LumaConflict.ClashKey] = LumaConflict.ClashValue;
        Assert.True(LumaConflict.Confirmed(other));
    }

    [Fact]
    public void Without_the_other_mod_there_is_nothing_to_warn_about()
    {
        Assert.Null(LumaConflict.ForLuma(null));
        Assert.Null(LumaConflict.ForRenoDx(new InstallManifest()));
        Assert.False(LumaConflict.Clashes(new InstallManifest()));
    }

    [Fact]
    public void The_clash_mark_is_only_added_when_confirmed()
    {
        var plain = new Dictionary<string, string> { ["a"] = "b" };

        Assert.Same(plain, LumaConflict.WithMark(plain, false));
        Assert.Equal(LumaConflict.ClashValue, LumaConflict.WithMark(plain, true)[LumaConflict.ClashKey]);
        Assert.Equal("b", LumaConflict.WithMark(plain, true)["a"]);
    }
}
