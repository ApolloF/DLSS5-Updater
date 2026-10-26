using System.Net;
using System.Text;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>
/// The RenoDX HDR component with the managed ReShade install: the game's build, 32-bit games (ReShade32 + .addon32), the Vulkan layer
/// route, and the per-game HDR build pick. Temp game trees and a fake web only; the Vulkan layer host never writes.
/// </summary>
public class HdrReShadeTests : IDisposable
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

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-hdrrs-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root, _target;
    private readonly ComponentStore _store;
    private readonly ReShadeBuild _build;
    private readonly FakeWeb _web;
    private readonly AppSettings _settings = new();
    private string _ueExtContent = HdrFixtures.UeExtContent;

    public HdrReShadeTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, "bin");
        Directory.CreateDirectory(_target);
        File.WriteAllText(Path.Combine(_target, "Game.exe"), "EXE");
        InstallManifest.KnownFolder = tag => tag switch { "|localappdata|" => Path.Combine(_tmp, "Local"), "|documents|" => Path.Combine(_tmp, "Docs"), _ => null };
        _store = new ComponentStore(new GitHubClient(), () => true);
        _build = _store.ImportReShade(ReShadeFixtures.Folder(Path.Combine(_tmp, "src"), "a", "6.8.0.2155"));
        _store.SelectedReShade = () => _build.Id;
        _web = new FakeWeb(url => url switch
        {
            var u when u.EndsWith("renodx-ue-extended.addon64") => _ueExtContent,
            var u when u.EndsWith("renodx-unityengine.addon64") => HdrFixtures.Unity64Content,
            var u when u.EndsWith("renodx-unityengine.addon32") => HdrFixtures.Unity32Content,
            _ => null,
        });
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

    private HdrService Service(VulkanLayerState layer = VulkanLayerState.NotInstalled) =>
        new(_store, new Installer(_store) { VulkanHost = new ReShadeInstallTests.FakeLayer(layer) }, new CatalogDownloader(new GitHubClient(_web)));

    private GameInfo Unity(int bits, GraphicsApis apis)
    {
        var g = GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));
        g.Tech.Engine = GameEngine.Unity;
        g.Tech.Bits = bits;
        g.Tech.Apis = apis;
        return g;
    }

    private static HdrChoice Choose(GameInfo g, string? json = null) => new HdrPicker(HdrFixtures.Renodx(json)).Pick(g, null);

    private string T(string name) => Path.Combine(_target, name);

    private InstalledComponent? Record() => InstallManifest.Read(_target)?.Component(ComponentRegistry.RenoDxHdr);

    // ---------- ReShade through the managed install ----------

    [Fact]
    public async Task A_64_bit_game_gets_the_chosen_library_build_under_the_API_name_and_the_ini_template()
    {
        var other = _store.ImportReShade(ReShadeFixtures.Folder(Path.Combine(_tmp, "src"), "b", "6.5.1.1"));
        var g = Unity(64, GraphicsApis.Dx9);
        var template = new[] { new IniOverride("GENERAL", "EffectSearchPaths", ".\\reshade-shaders") };

        await Service().InstallAsync(g, _target, Choose(g), null, null, new HdrReShade(other.Id, null, [], template), null, default);

        Assert.Equal(other.Sha64, FileUtil.Sha256(T("d3d9.dll")));
        Assert.False(File.Exists(T("dxgi.dll")));
        var m = InstallManifest.Read(_target)!;
        Assert.Equal(other.Id, m.ReShadeId);
        Assert.Equal("d3d9.dll", m.Proxy);
        Assert.Equal(HdrFixtures.Unity64Content, File.ReadAllText(T("renodx-unityengine.addon64")));
        Assert.Contains("reshade-shaders", File.ReadAllText(T("ReShade.ini")));
    }

    [Fact]
    public async Task A_32_bit_game_gets_ReShade32_as_its_proxy_and_the_32_bit_add_on()
    {
        var g = Unity(32, GraphicsApis.Dx11);
        var choice = Choose(g);
        Assert.Equal("renodx-unityengine.addon32", choice.File!.Name);

        await Service().InstallAsync(g, _target, choice, null, null, null, null, default);

        Assert.Equal(_build.Sha32, FileUtil.Sha256(T("dxgi.dll")));
        Assert.Equal(HdrFixtures.Unity32Content, File.ReadAllText(T("renodx-unityengine.addon32")));
        Assert.False(File.Exists(T("renodx-unityengine.addon64")));
        Assert.Equal(_build.Id, InstallManifest.Read(_target)!.ReShadeId);
        Assert.EndsWith("renodx-unityengine.addon32", Record()!.Files.Single().Path);
    }

    [Fact]
    public async Task The_games_saved_proxy_name_wins_over_the_API()
    {
        var g = Unity(64, GraphicsApis.Dx11);

        await Service().InstallAsync(g, _target, Choose(g), null, null, new HdrReShade(Proxy: "opengl32.dll"), null, default);

        Assert.Equal(_build.Sha64, FileUtil.Sha256(T("opengl32.dll")));
        Assert.False(File.Exists(T("dxgi.dll")));
    }

    [Fact]
    public async Task A_Vulkan_game_goes_through_the_layer_when_it_is_installed_and_gets_no_dll()
    {
        var g = Unity(64, GraphicsApis.Vulkan);

        await Service(VulkanLayerState.Installed).InstallAsync(g, _target, Choose(g), null, null, null, null, default);

        var m = InstallManifest.Read(_target)!;
        Assert.True(m.ReShade);
        Assert.True(m.ReShadeVulkan);
        Assert.Null(m.Proxy);
        Assert.False(File.Exists(T("dxgi.dll")));
        Assert.True(File.Exists(T("ReShade.ini")));
        Assert.Equal(HdrFixtures.Unity64Content, File.ReadAllText(T("renodx-unityengine.addon64")));
    }

    [Fact]
    public async Task A_Vulkan_game_without_the_layer_is_refused_with_a_pointer_to_Settings_and_nothing_is_written()
    {
        var g = Unity(64, GraphicsApis.Vulkan);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(VulkanLayerState.NotInstalled).InstallAsync(g, _target, Choose(g), null, null, null, null, default));

        Assert.Contains("Settings → ReShade", ex.Message);
        Assert.Null(InstallManifest.Read(_target));
        Assert.False(File.Exists(T("renodx-unityengine.addon64")));
        Assert.False(File.Exists(T("ReShade.ini")));
    }

    [Fact]
    public async Task A_game_that_already_has_ReShade_keeps_its_build()
    {
        var g = Unity(64, GraphicsApis.Dx11);
        var svc = Service();
        await svc.InstallAsync(g, _target, Choose(g), null, null, null, null, default);
        var newer = _store.ImportReShade(ReShadeFixtures.Folder(Path.Combine(_tmp, "src"), "c", "6.9.0.1"));

        await svc.InstallAsync(g, _target, Choose(g), null, null, new HdrReShade(newer.Id), null, default);

        Assert.Equal(_build.Id, InstallManifest.Read(_target)!.ReShadeId);
        Assert.Equal(_build.Sha64, FileUtil.Sha256(T("dxgi.dll")));
    }

    // ---------- per-game HDR build pick ----------

    private GameInfo Unreal() => new()
    {
        Id = "Game", Name = "Game", Root = _root,
        Tech = new GameTech { Engine = GameEngine.Unreal, EngineVersion = "5.3.2", Bits = 64, Apis = GraphicsApis.Dx12, UeProject = "Proj", ModFolder = _target },
    };

    private async Task<(HdrBuild Old, HdrBuild New)> TwoBuilds(HdrService svc, GameInfo g)
    {
        var old = (await svc.InstallAsync(g, _target, Choose(g), null, null, null, null, default)).Build;
        await svc.RemoveAsync(g, _target, default);
        _ueExtContent = "MZ ue-extended build 2";
        var newer = (await svc.InstallAsync(g, _target, Choose(g, HdrFixtures.Json(_ueExtContent, "2026-10-22-bbbbbbb")), null, null, null, null, default)).Build;
        await svc.RemoveAsync(g, _target, default);
        return (old, newer);
    }

    [Fact]
    public async Task A_pinned_build_is_installed_from_the_library_without_a_download_and_counts_as_current()
    {
        var g = Unreal();
        var svc = Service();
        var (old, newer) = await TwoBuilds(svc, g);
        Assert.Equal(2, _store.HdrBuilds.Count);
        var latest = Choose(g, HdrFixtures.Json(_ueExtContent, "2026-10-22-bbbbbbb"));
        _web.Urls.Clear();

        var pinned = HdrPin.Apply(latest, old.Id, _store.HdrBuilds);
        Assert.Equal(old.Id, pinned.Pinned!.Id);
        Assert.Equal(old.Id, pinned.File!.Sha256);
        await svc.InstallAsync(g, _target, pinned, null, null, null, null, default);

        Assert.Empty(_web.Urls);
        Assert.Equal(HdrFixtures.UeExtContent, File.ReadAllText(T("renodx-ue-extended.addon64")));
        Assert.Equal(old.Id, Record()!.BuildId);
        Assert.Equal(HdrState.Current, HdrStatus.Of(pinned, Record(), null).State);
        // Without the pin the same game is one update behind.
        Assert.Equal(HdrState.Update, HdrStatus.Of(latest, Record(), null).State);
        Assert.NotEqual(old.Id, newer.Id);
    }

    [Fact]
    public async Task Updating_with_a_pin_keeps_the_pinned_build_and_unpinning_moves_to_the_catalog_build()
    {
        var g = Unreal();
        var svc = Service();
        var (old, newer) = await TwoBuilds(svc, g);
        var latest = Choose(g, HdrFixtures.Json(_ueExtContent, "2026-10-22-bbbbbbb"));
        await svc.InstallAsync(g, _target, latest, null, null, null, null, default);
        Assert.Equal(newer.Id, Record()!.BuildId);

        var pinned = HdrPin.Apply(latest, old.Id, _store.HdrBuilds);
        Assert.Equal(HdrState.Update, HdrStatus.Of(pinned, Record(), null).State);
        await svc.InstallAsync(g, _target, pinned, null, null, null, null, default);
        Assert.Equal(old.Id, Record()!.BuildId);
        Assert.Equal(HdrFixtures.UeExtContent, File.ReadAllText(T("renodx-ue-extended.addon64")));

        await svc.InstallAsync(g, _target, HdrPin.Apply(latest, null, _store.HdrBuilds), null, null, null, null, default);
        Assert.Equal(newer.Id, Record()!.BuildId);
    }

    [Fact]
    public async Task A_pin_that_does_not_fit_is_ignored()
    {
        var g = Unreal();
        var svc = Service();
        var (old, _) = await TwoBuilds(svc, g);
        var choice = Choose(g);

        // A build of another mod is not a candidate for this game.
        var unity = Unity(64, GraphicsApis.Dx11);
        await svc.InstallAsync(unity, _target, Choose(unity), null, null, null, null, default);
        var unityBuild = _store.HdrBuilds.Single(b => b.ModId == "unity");
        Assert.Same(choice, HdrPin.Apply(choice, unityBuild.Id, _store.HdrBuilds));
        Assert.DoesNotContain(unityBuild, HdrPin.Candidates(choice, _store.HdrBuilds));

        Assert.Same(choice, HdrPin.Apply(choice, "not-a-build", _store.HdrBuilds));
        File.Delete(ComponentStore.HdrPathOf(old));
        Assert.Same(choice, HdrPin.Apply(choice, old.Id, _store.HdrBuilds));
    }

    [Fact]
    public async Task A_pinned_file_that_changed_on_disk_is_not_installed()
    {
        var g = Unreal();
        var svc = Service();
        var (old, _) = await TwoBuilds(svc, g);
        var pinned = HdrPin.Apply(Choose(g), old.Id, _store.HdrBuilds);
        File.WriteAllText(ComponentStore.HdrPathOf(old), "tampered");

        await Assert.ThrowsAsync<InvalidDataException>(() => svc.InstallAsync(g, _target, pinned, null, null, null, null, default));
        Assert.Null(Record());
    }

    // ---------- the game page picker ----------

    private GameViewModel Vm(out Services s)
    {
        File.WriteAllText(AppPaths.CatalogFile, HdrFixtures.Json());
        var gh = new GitHubClient(_web);
        var installer = new Installer(_store) { VulkanHost = new ReShadeInstallTests.FakeLayer(VulkanLayerState.NotInstalled) };
        s = new Services
        {
            Settings = _settings, Store = _store, Installer = installer,
            Catalog = new CatalogState(new CatalogClient(gh)), Hdr = new HdrService(_store, installer, new CatalogDownloader(gh)),
        };
        s.Catalog.LoadCached();
        return new GameViewModel(Unreal(), s);
    }

    [Fact]
    public async Task The_picker_lists_Default_and_the_library_builds_of_the_mod_and_stores_the_pick_per_game()
    {
        var vm = Vm(out var s);
        Assert.Equal(["Default · 2026-10-21-aaaaaaa"], vm.Hdr.BuildChoices.Select(c => c.Label));
        Assert.True(vm.Hdr.ShowBuildChoices);
        await vm.Hdr.InstallAsync(null, default);
        var id = CatalogFixtures.Sha(HdrFixtures.UeExtContent);

        vm.RefreshHdr();
        Assert.Equal(["Default · 2026-10-21-aaaaaaa", "2026-10-21-aaaaaaa"], vm.Hdr.BuildChoices.Select(c => c.Label));
        Assert.Null(vm.Hdr.BuildChoice!.Tag);

        vm.Hdr.BuildChoice = vm.Hdr.BuildChoices[1];

        Assert.Equal(id, _settings.Games[vm.Id].BuildPick(ComponentRegistry.RenoDxHdr));
        Assert.Equal(id, vm.Hdr.Choice!.Pinned!.Id);
        Assert.Contains("You pinned", vm.Hdr.PickedText);

        vm.Hdr.BuildChoice = vm.Hdr.BuildChoices[0];

        Assert.Null(_settings.Games[vm.Id].BuildPick(ComponentRegistry.RenoDxHdr));
        Assert.Null(vm.Hdr.Choice!.Pinned);
        Assert.Equal(id, s.Store.HdrBuilds.Single().Id);
    }

    [Fact]
    public async Task A_pin_on_the_older_build_clears_the_update_and_the_status_follows_it()
    {
        var vm = Vm(out var s);
        await vm.Hdr.InstallAsync(null, default);
        vm.Load(vm.Info);
        _ueExtContent = "MZ ue-extended build 2";
        File.WriteAllText(AppPaths.CatalogFile, HdrFixtures.Json(_ueExtContent, "2026-10-22-bbbbbbb"));
        s.Catalog!.LoadCached();
        vm.RefreshHdr();
        Assert.True(vm.NeedsHdrUpdate);

        vm.Hdr.BuildChoice = vm.Hdr.BuildChoices.Single(c => c.Tag is not null);

        Assert.False(vm.NeedsHdrUpdate);
        Assert.Equal("2026-10-21-aaaaaaa", vm.Hdr.LatestVersion);
    }
}
