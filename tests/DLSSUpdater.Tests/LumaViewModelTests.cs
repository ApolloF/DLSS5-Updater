using System.Net;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>The Luma section of a game page: visibility, the pick and its manual override, the HDR choice, the clash warning and Update all's counting.</summary>
public class LumaViewModelTests : IDisposable
{
    private sealed class FakeWeb(Func<string, byte[]?> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(body(request.RequestUri!.ToString()) is { } bytes
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-lumavm-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root, _target;
    private readonly ComponentStore _store;
    private readonly AppSettings _settings = new();

    public LumaViewModelTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, @"Game\Binaries\Win64");
        _store = new ComponentStore(new GitHubClient(), () => true);
        Write(Path.Combine(AppPaths.Components, ComponentStore.ReShadeFile), "RESHADE");
        Write(Path.Combine(_target, "Game-Win64-Shipping.exe"), "EXE");
        Write(Path.Combine(_root, "Game.exe"), "BOOT");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); }
        catch (IOException) { }
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void SeedCatalog() =>
        File.WriteAllText(AppPaths.CatalogFile, $$"""{ "schema": 1, "repository": "ApolloF/DLSS5-Updater-catalog", "generated": "2026-10-21T12:00:00Z", "sections": { "luma": {{LumaFixtures.SectionJson()}} } }""");

    private GameInfo Game(string name = "Prey", Action<GameInfo>? tweak = null)
    {
        var g = GameScanner.Inspect(new GameEntry(name, _root, "Manual"));
        g.Tech.Bits = 64;
        g.Tech.Engine = GameEngine.Unknown;
        g.Tech.Apis = GraphicsApis.Dx11;
        tweak?.Invoke(g);
        return g;
    }

    private Services NewServices(bool luma = true)
    {
        var gh = new GitHubClient(new FakeWeb(url => url.EndsWith("Luma-Prey.zip") ? LumaFixtures.PreyZip : null));
        var installer = new Installer(_store);
        return new Services
        {
            Settings = _settings, Store = _store, Installer = installer, Catalog = new CatalogState(new CatalogClient(gh)),
            Luma = luma ? new LumaService(_store, installer, new CatalogDownloader(gh)) : null,
        };
    }

    private static GameViewModel NewVm(Services s, GameInfo info)
    {
        s.Catalog?.LoadCached();
        return new GameViewModel(info, s);
    }

    [Fact]
    public void Without_the_service_or_a_luma_section_the_section_is_off_and_nothing_throws()
    {
        SeedCatalog();
        var noService = NewVm(NewServices(luma: false), Game());
        Assert.False(noService.Luma.Visible);
        Assert.False(noService.NeedsLumaUpdate);
        Assert.False(noService.Luma.CanInstall);

        File.Delete(AppPaths.CatalogFile);
        var noCatalog = NewVm(NewServices(), Game());
        Assert.False(noCatalog.Luma.Visible);
        Assert.False(noCatalog.Luma.CanRemove);
    }

    [Fact]
    public void A_matched_game_shows_the_pick_the_latest_release_and_can_install()
    {
        SeedCatalog();
        var vm = NewVm(NewServices(), Game());

        Assert.True(vm.Luma.Visible);
        Assert.Contains("Prey", vm.Luma.PickedText);
        Assert.Equal("latest-755", vm.Luma.LatestVersion);
        Assert.Equal("—", vm.Luma.InstalledVersion);
        Assert.Equal("Install", vm.Luma.ActionText);
        Assert.True(vm.Luma.CanInstall);
        Assert.False(vm.Luma.CanRemove);
        Assert.True(vm.Luma.HasLink);
        Assert.False(vm.Luma.HasClashWarning);
    }

    [Fact]
    public void A_game_nobody_made_a_mod_for_says_so_and_cannot_install()
    {
        SeedCatalog();
        var vm = NewVm(NewServices(), Game("Some Game"));

        Assert.Contains("No Luma mod", vm.Luma.PickedText);
        Assert.False(vm.Luma.CanInstall);
        Assert.Equal("", vm.Luma.StatusLine);
        Assert.Equal("—", vm.Luma.LatestVersion);
    }

    [Fact]
    public void Picking_a_mod_by_hand_is_saved_per_game_and_reset_goes_back_to_auto()
    {
        SeedCatalog();
        var s = NewServices();
        var vm = NewVm(s, Game("Some Game"));

        vm.Luma.PickMod("prey");

        Assert.Equal("prey", _settings.Games[vm.Id].LumaMod);
        Assert.True(vm.Luma.CanInstall);
        Assert.Contains("your pick", vm.Luma.PickedText);
        Assert.True(vm.Luma.ModPick.IsSet);

        vm.Luma.ModPick.ResetCommand.Execute(null);

        Assert.Null(_settings.Games[vm.Id].LumaMod);
        Assert.False(vm.Luma.CanInstall);
    }

    [Fact]
    public void The_mod_list_has_auto_the_catalog_mods_and_imported_zips_and_the_search_shortens_it()
    {
        SeedCatalog();
        var path = Path.Combine(_tmp, "in", "Luma-Mine.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, LumaFixtures.Zip("Luma-Mine.addon64"));
        _store.ImportLumaBuild(path);
        var vm = NewVm(NewServices(), Game());

        var all = vm.Luma.ModPick.Choices.Select(c => c.Label).ToList();
        Assert.StartsWith("Auto (", all[0]);
        Assert.Contains("Borderlands 2", all);
        Assert.Contains(all, l => l.StartsWith("Imported: Luma-Mine.zip"));

        vm.Luma.ModSearch = "borderlands";

        Assert.Equal(["Borderlands 2"], vm.Luma.ModPick.Choices.Skip(1).Select(c => c.Label).ToList());
    }

    [Fact]
    public void The_HDR_choice_is_saved_per_game_and_changes_the_status_to_settings_changed_after_an_install()
    {
        SeedCatalog();
        var s = NewServices();
        var vm = NewVm(s, Game());
        Assert.Null(vm.Luma.HdrChoice!.Tag);

        vm.Luma.HdrChoice = vm.Luma.HdrChoices.Single(c => c.Tag == "on");

        Assert.True(_settings.Games[vm.Id].LumaHdr);
        Assert.Contains("[Luma]", vm.Luma.HdrHint);
    }

    [Fact]
    public void The_launch_arguments_and_Engine_ini_lines_of_a_listed_Unreal_game_are_shown()
    {
        SeedCatalog();
        var vm = NewVm(NewServices(), Game("Listed Game", g => { g.Tech.Engine = GameEngine.Unreal; g.Tech.EngineVersion = "4.27.2"; }));

        Assert.True(vm.Luma.HasLaunchArgs);
        Assert.Equal("-dx11", vm.Luma.LaunchArgsText);
        Assert.True(vm.Luma.HasEngineIni);
        Assert.Contains("r.LumaTest=1", vm.Luma.EngineIniText);
        Assert.Contains("Disable motion blur.", vm.Luma.Notes);
    }

    [Fact]
    public void A_dgVoodoo_mod_shows_the_requirement_and_the_official_link()
    {
        SeedCatalog();
        var vm = NewVm(NewServices(), Game("Borderlands 2", g => { g.Tech.Bits = 32; g.Tech.Apis = GraphicsApis.Dx9; }));

        Assert.True(vm.Luma.HasDgVoodoo);
        Assert.Contains("2.82.3", vm.Luma.DgVoodooText);
        Assert.Equal("https://github.com/dege-diosg/dgVoodoo2/releases", LumaViewModel.DgVoodooUrl);
    }

    [Fact]
    public async Task An_install_makes_the_section_current_and_a_newer_catalog_zip_counts_as_an_update_for_Update_all()
    {
        SeedCatalog();
        var s = NewServices();
        var info = Game();
        var vm = NewVm(s, info);

        await vm.Luma.InstallAsync(null, default);
        vm.Load(vm.Info);

        Assert.Equal(HdrState.Current, vm.Luma.Status!.State);
        Assert.True(vm.Luma.CanRemove);
        Assert.False(vm.NeedsLumaUpdate);
        Assert.False(MainViewModel.HasUpdate(vm));

        // The user changes the HDR choice: the record no longer matches, Update all picks the game up.
        vm.Luma.HdrChoice = vm.Luma.HdrChoices.Single(c => c.Tag == "off");

        Assert.True(vm.NeedsLumaUpdate);
        Assert.True(MainViewModel.HasUpdate(vm));
        Assert.Equal([vm], MainViewModel.UpdateTargets([vm]));
    }

    [Fact]
    public async Task The_clash_with_a_RenoDX_HDR_record_is_shown_and_Update_all_must_skip_it_until_it_is_confirmed()
    {
        SeedCatalog();
        var s = NewServices();
        var vm = NewVm(s, Game());
        await vm.Luma.InstallAsync(null, default);
        var m = InstallManifest.Read(vm.SelectedTarget!.Dir)!;
        m.Components.Add(new InstalledComponent { Id = ComponentRegistry.RenoDxHdr, BuildId = "abc", Version = "v" });
        m.Save(vm.SelectedTarget!.Dir);
        vm.Load(vm.Info);

        Assert.True(vm.Luma.HasClashWarning);
        Assert.Contains("RenoDX HDR", vm.Luma.ClashWarning);
        Assert.True(vm.Luma.ClashBlocksUpdate);

        m = InstallManifest.Read(vm.SelectedTarget!.Dir)!;
        m.Component(ComponentRegistry.Luma)!.Extras[LumaConflict.ClashKey] = LumaConflict.ClashValue;
        m.Save(vm.SelectedTarget!.Dir);
        vm.Load(vm.Info);

        Assert.True(vm.Luma.HasClashWarning);
        Assert.False(vm.Luma.ClashBlocksUpdate);
    }

    [Fact]
    public async Task The_install_needs_a_confirmation_only_while_RenoDX_HDR_is_there_and_not_yet_confirmed()
    {
        SeedCatalog();
        var vm = NewVm(NewServices(), Game());
        Assert.False(vm.Luma.NeedsClashConfirm);

        var m = InstallManifest.Read(vm.SelectedTarget!.Dir) ?? new InstallManifest();
        m.Components.Add(new InstalledComponent { Id = ComponentRegistry.RenoDxHdr, BuildId = "abc", Version = "v" });
        m.Save(vm.SelectedTarget!.Dir);
        vm.Load(vm.Info);

        Assert.True(vm.Luma.NeedsClashConfirm);
        Assert.True(vm.Luma.HasClashWarning);

        await vm.Luma.InstallAsync(null, default, clashConfirmed: true);
        vm.Load(vm.Info);

        Assert.False(vm.Luma.NeedsClashConfirm);
        Assert.False(vm.Luma.ClashBlocksUpdate);
    }
}
