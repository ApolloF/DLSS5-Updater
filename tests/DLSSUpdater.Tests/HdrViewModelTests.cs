using System.Net;
using System.Text;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>
/// The HDR (RenoDX) section of a game page: what it shows for each state, the Unreal Extended switch, update detection and the
/// counting Update all uses. Everything runs on a temp game tree and a seeded catalog copy; nothing touches a real game.
/// </summary>
public class HdrViewModelTests : IDisposable
{
    private sealed class FakeWeb(Func<string, string?> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(body(request.RequestUri!.ToString()) is { } text
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(text)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-hdrvm-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root, _target, _local, _docs;
    private readonly ComponentStore _store;
    private readonly AppSettings _settings = new();
    private string _ueExtContent = HdrFixtures.UeExtContent;

    public HdrViewModelTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, @"Game\Binaries\Win64");
        _local = Path.Combine(_tmp, "LocalAppData");
        _docs = Path.Combine(_tmp, "Documents");
        InstallManifest.KnownFolder = tag => tag switch { "|localappdata|" => _local, "|documents|" => _docs, _ => null };
        _store = new ComponentStore(new GitHubClient(), () => true);
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

    private void SeedCatalog(string? json = null) => File.WriteAllText(AppPaths.CatalogFile, json ?? HdrFixtures.Json());

    private GameInfo Game(Action<GameInfo>? tweak = null)
    {
        var g = GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));
        g.Tech.Bits = 64;
        g.Tech.Engine = GameEngine.Unreal;
        g.Tech.EngineVersion = "5.3.2";
        g.Tech.Apis = GraphicsApis.Dx12;
        tweak?.Invoke(g);
        return g;
    }

    private Services NewServices(bool catalog = true)
    {
        var web = new FakeWeb(url => url switch
        {
            var u when u.EndsWith("renodx-ue-extended.addon64") => _ueExtContent,
            var u when u.EndsWith("renodx-unrealengine.addon64") => HdrFixtures.UeContent,
            _ => null,
        });
        var gh = new GitHubClient(web);
        var installer = new Installer(_store);
        var services = new Services { Settings = _settings, Store = _store, Installer = installer };
        return catalog
            ? new Services
            {
                Settings = _settings, Store = _store, Installer = installer,
                Catalog = new CatalogState(new CatalogClient(gh)), Hdr = new HdrService(_store, installer, new CatalogDownloader(gh)),
            }
            : services;
    }

    private GameViewModel NewVm(Services s, GameInfo? info = null)
    {
        s.Catalog?.LoadCached();
        return new GameViewModel(info ?? Game(), s);
    }

    // ---------- states ----------

    [Fact]
    public void Services_without_the_catalog_leave_the_section_off_and_nothing_throws()
    {
        var vm = NewVm(NewServices(catalog: false));

        Assert.False(vm.Hdr.Visible);
        Assert.False(vm.NeedsHdrUpdate);
        Assert.False(vm.Hdr.CanInstall);
        Assert.False(vm.Hdr.CanRemove);
        Assert.False(vm.Hdr.HasStatusLine);
        Assert.False(vm.Hdr.ShowUeToggle);
        Assert.Equal("Install", vm.Hdr.ActionText);
    }

    [Fact]
    public void A_missing_catalog_shows_the_problem_and_offers_nothing()
    {
        var s = NewServices();
        var vm = NewVm(s);

        Assert.True(vm.Hdr.Visible);
        Assert.NotNull(s.Catalog!.Problem);
        Assert.Equal(s.Catalog.Problem, vm.Hdr.StatusLine);
        Assert.False(vm.Hdr.HasPickedText);
        Assert.False(vm.Hdr.CanInstall);
        Assert.False(vm.NeedsHdrUpdate);
    }

    [Fact]
    public void A_game_with_nothing_offered_says_why_and_cannot_install()
    {
        SeedCatalog();
        var vm = NewVm(NewServices(), Game(g => g.AntiCheat = "BattlEye"));

        Assert.Contains("BattlEye", vm.Hdr.PickedText);
        Assert.False(vm.Hdr.CanInstall);
        Assert.False(vm.Hdr.CanRemove);
        Assert.False(vm.Hdr.ShowUeToggle);
        Assert.False(vm.Hdr.HasStatusLine);
        Assert.False(vm.NeedsHdrUpdate);
        Assert.Equal("—", vm.Hdr.LatestVersion);
    }

    [Fact]
    public void An_offered_mod_not_installed_shows_the_pick_the_latest_build_and_the_toggle()
    {
        SeedCatalog();
        var vm = NewVm(NewServices());

        Assert.Contains("Unreal Engine Extended", vm.Hdr.PickedText);
        Assert.Contains("Unreal Extended is the default", vm.Hdr.PickedText);
        Assert.True(vm.Hdr.CanInstall);
        Assert.False(vm.Hdr.CanRemove);
        Assert.Equal("Install", vm.Hdr.ActionText);
        Assert.Equal("—", vm.Hdr.InstalledVersion);
        Assert.Equal("2026-10-21-aaaaaaa", vm.Hdr.LatestVersion);
        Assert.StartsWith("Not installed", vm.Hdr.StatusLine);
        Assert.True(vm.Hdr.ShowUeToggle);
        Assert.True(vm.Hdr.UeExtended);
        Assert.True(vm.Hdr.HasLink);
        Assert.Equal("https://github.com/marat569/renodx", vm.Hdr.Link);
        Assert.False(vm.NeedsHdrUpdate);
    }

    [Fact]
    public void A_named_mod_has_no_toggle_and_its_notes_show()
    {
        SeedCatalog();
        var vm = NewVm(NewServices(), Game(g => { g.SteamAppId = 214490; g.Tech.Bits = 32; g.Tech.Engine = GameEngine.Unknown; }));

        Assert.Contains("Alien: Isolation", vm.Hdr.PickedText);
        Assert.False(vm.Hdr.ShowUeToggle);
        Assert.True(vm.Hdr.HasNotes);
        Assert.Contains("in progress", vm.Hdr.Notes);
    }

    // ---------- install, update, remove ----------

    [Fact]
    public async Task An_installed_current_mod_offers_reinstall_and_remove()
    {
        SeedCatalog();
        var vm = NewVm(NewServices());

        await vm.Hdr.InstallAsync(null, default);
        vm.Load(vm.Info);

        Assert.Equal("Reinstall", vm.Hdr.ActionText);
        Assert.True(vm.Hdr.CanRemove);
        Assert.Equal("2026-10-21-aaaaaaa", vm.Hdr.InstalledVersion);
        Assert.Contains("up to date", vm.Hdr.StatusLine);
        Assert.False(vm.NeedsHdrUpdate);
        Assert.NotNull(vm.Manifest?.Component(ComponentRegistry.RenoDxHdr));
    }

    [Fact]
    public async Task A_newer_catalog_build_makes_the_game_need_an_update()
    {
        SeedCatalog();
        var s = NewServices();
        var vm = NewVm(s);
        await vm.Hdr.InstallAsync(null, default);
        vm.Load(vm.Info);
        var changed = 0;
        vm.Hdr.Changed += () => changed++;

        _ueExtContent = "MZ ue-extended build 2";
        SeedCatalog(HdrFixtures.Json(_ueExtContent, "2026-10-22-bbbbbbb"));
        s.Catalog!.LoadCached();
        vm.RefreshHdr();

        Assert.True(changed > 0);
        Assert.True(vm.NeedsHdrUpdate);
        Assert.Equal("Update", vm.Hdr.ActionText);
        Assert.Equal("2026-10-21-aaaaaaa", vm.Hdr.InstalledVersion);
        Assert.Equal("2026-10-22-bbbbbbb", vm.Hdr.LatestVersion);
        Assert.Contains("update to 2026-10-22-bbbbbbb", vm.Hdr.StatusLine);

        await vm.Hdr.InstallAsync(null, default);
        vm.Load(vm.Info);

        Assert.False(vm.NeedsHdrUpdate);
        Assert.Equal("2026-10-22-bbbbbbb", vm.Hdr.InstalledVersion);
    }

    [Fact]
    public async Task Changing_the_peak_brightness_counts_as_an_update()
    {
        SeedCatalog();
        var vm = NewVm(NewServices());
        await vm.Hdr.InstallAsync(null, default);
        vm.Load(vm.Info);
        Assert.False(vm.NeedsHdrUpdate);

        _settings.HdrPeakNits = 1000;
        vm.RefreshStatus();

        Assert.True(vm.NeedsHdrUpdate);
        Assert.Contains("settings changed", vm.Hdr.StatusLine);
    }

    [Fact]
    public async Task Remove_takes_the_record_out_and_the_section_goes_back_to_install()
    {
        SeedCatalog();
        var vm = NewVm(NewServices());
        await vm.Hdr.InstallAsync(null, default);
        vm.Load(vm.Info);
        Assert.True(vm.Hdr.CanRemove);

        await vm.Hdr.RemoveAsync(default);
        vm.Load(vm.Info);

        Assert.False(vm.Hdr.CanRemove);
        Assert.Equal("Install", vm.Hdr.ActionText);
        Assert.Null(vm.Manifest?.Component(ComponentRegistry.RenoDxHdr));
    }

    [Fact]
    public async Task Installing_for_a_game_with_nothing_offered_throws_the_reason()
    {
        SeedCatalog();
        var vm = NewVm(NewServices(), Game(g => g.AntiCheat = "BattlEye"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.Hdr.InstallAsync(null, default));

        Assert.Contains("BattlEye", ex.Message);
    }

    // ---------- Unreal Extended switch ----------

    [Fact]
    public void The_switch_stores_the_choice_per_game_changes_the_pick_and_is_not_written_before()
    {
        SeedCatalog();
        var vm = NewVm(NewServices());
        Assert.Null(_settings.Games.GetValueOrDefault(vm.Id)?.HdrUeExtended);

        vm.Hdr.UeExtended = false;

        Assert.False(_settings.Games[vm.Id].HdrUeExtended);
        Assert.Equal(HdrKind.UnrealGeneric, vm.Hdr.Choice!.Kind);
        Assert.Contains("generic Unreal", vm.Hdr.PickedText);
        Assert.False(vm.Hdr.UeExtended);
        Assert.True(vm.Hdr.ShowUeToggle);

        vm.Hdr.UeExtended = true;

        Assert.True(_settings.Games[vm.Id].HdrUeExtended);
        Assert.Equal(HdrKind.UeExtended, vm.Hdr.Choice!.Kind);
        Assert.True(vm.Hdr.UeExtended);
    }

    [Fact]
    public void Setting_the_switch_to_what_it_already_shows_writes_nothing()
    {
        SeedCatalog();
        var vm = NewVm(NewServices());

        vm.Hdr.UeExtended = true;

        Assert.Null(_settings.Games.GetValueOrDefault(vm.Id)?.HdrUeExtended);
    }

    [Fact]
    public async Task Switching_an_installed_game_to_the_other_add_on_is_an_update()
    {
        SeedCatalog();
        var vm = NewVm(NewServices());
        await vm.Hdr.InstallAsync(null, default);
        vm.Load(vm.Info);

        vm.Hdr.UeExtended = false;

        Assert.True(vm.NeedsHdrUpdate);
        Assert.Contains("another mod", vm.Hdr.StatusLine);
    }

    // ---------- Update all / counting ----------

    [Fact]
    public void Update_counting_includes_games_with_only_an_HDR_update_and_keeps_main_updates_as_they_were()
    {
        var s = NewServices(catalog: false);
        var mainOnly = NewVm(s, Game());
        mainOnly.NeedsUpdate = true;
        var none = NewVm(s, Game());

        Assert.True(MainViewModel.HasUpdate(mainOnly));
        Assert.False(MainViewModel.HasUpdate(none));
        // A main update without an install record is counted but not worked through, as before.
        Assert.Empty(MainViewModel.UpdateTargets([mainOnly, none]));

        mainOnly.Load(mainOnly.Info);
        Assert.False(MainViewModel.HasUpdate(mainOnly));
    }

    [Fact]
    public async Task Update_targets_list_a_game_whose_HDR_mod_has_an_update()
    {
        SeedCatalog();
        var s = NewServices();
        var vm = NewVm(s);
        await vm.Hdr.InstallAsync(null, default);
        vm.Load(vm.Info);
        Assert.False(vm.NeedsHdrUpdate);

        SeedCatalog(HdrFixtures.Json("MZ ue-extended build 2", "2026-10-22-bbbbbbb"));
        s.Catalog!.LoadCached();
        vm.RefreshHdr();

        Assert.True(MainViewModel.HasUpdate(vm));
        Assert.Equal([vm], MainViewModel.UpdateTargets([vm]));
    }

    // ---------- Settings ----------

    [Theory]
    [InlineData("", true, null)]
    [InlineData("   ", true, null)]
    [InlineData("1000", true, 1000)]
    [InlineData(" 800 ", true, 800)]
    [InlineData("20", true, 80)]
    [InlineData("99999", true, 10000)]
    [InlineData("abc", false, null)]
    [InlineData("-5", false, null)]
    [InlineData("1e3", false, null)]
    [InlineData("12.5", false, null)]
    public void Peak_brightness_text_is_empty_a_clamped_number_or_rejected(string text, bool ok, int? expected)
    {
        Assert.Equal(ok, SettingsViewModel.TryParsePeak(text, out var nits));
        Assert.Equal(expected, nits);
    }

    [Fact]
    public void The_HDR_help_topics_exist_with_advice()
    {
        foreach (var id in new[] { "hdr-renodx", "hdr-peak", "hdr-catalog-addon" })
        {
            Assert.NotNull(HelpTopics.Get(id));
            Assert.NotNull(HelpTopics.AdviceFor(id));
        }
    }
}
