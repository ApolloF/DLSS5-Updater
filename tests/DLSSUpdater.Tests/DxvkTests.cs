using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// DXVK as a component: releases picked per channel, downloads checked against GitHub's digest, the library and its imports, the dlls placed per
/// graphics API and bitness, foreign dlls backed up and put back, dxvk.conf merged with the user's values, the Lilium presets, the NVIDIA present
/// method against a fake driver, and ReShade moving to its Vulkan layer. A fake web server stands in for GitHub; the games are temp folders.
/// </summary>
public sealed class DxvkTests : IDisposable
{
    // A real 7-Zip archive of stand-in files laid out like the Lilium release (normal\x32, normal\x64 and a preset file at the top).
    private const string LiliumSevenZ = "N3q8ryccAATyRYX9cAEAAAAAAAAiAAAAAAAAAB93y9bgAcwAXl0AEYgK5nnW6e6CHaNozG/v2Dq10XzrII9QDNASOrsuRQ+yo7TqDagFtJLtyeQYiJUMvps3PCN5hQvrC0FJq6GwX0yda0S3fXdjU6hLejS5BZqSKhlfYsrsgCJ/IjFoZgAAAIEzB64P091PfUDAkNL/fWlNjzyky2axQ1q1kUFpuP27157ju+R/7JQfjD/Ev5CKghWkjPtUsEZR+EVjFbtIcARKLac5nwioWIFQuVdzKo5ITob+b6i4ejUq0lZYZE8z9TD42BVl8w4uTQ6REra2W01Wvr2HsWy52q/gsIYZAxr+J1HO0c+ArsPdflToucyKv31nRbqtJnrEDPJ6c1oqvpeJal4RlmXp9pEO1G+ddHayLI0QF1zwfELKtX+mQR5YdZ0hfE+c6z4W5yUTfJywRHEFGOE5SNC4S2iFRiiK7onffV9dUBxYJdVpB+a1tZNGqSRnNe/R7m6hiR8qCuo56LB9NYf3eisMOhcGZgEJgQoABwsBAAEjAwEBBV0AEAAADIMmCgFWY7R/AAA=";

    private const string StableUrl = "https://github.com/doitsujin/dxvk/releases/download/v3.1.1/dxvk-3.1.1.tar.gz";
    private const string LiliumUrl = "https://github.com/EndlesslyFlowering/dxvk/releases/download/v3.1-HDR-mod-v0.3.4/dxvk_v3.1-HDR-mod-v0.3.4.7z";
    private const string DevName = "dxvk-master-d713ef8748a1eee3e90d4b6d5dc2e80f5be4b519";
    private const string DevUrl = "https://nightly.link/doitsujin/dxvk/actions/runs/39564889126/" + DevName + ".zip";

    private sealed class FakeWeb(Func<string, byte[]?> body) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            return Task.FromResult(body(url) is { } bytes
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-dxvk-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root, _target;
    private readonly FakeDrs _drs = new();
    private readonly GameOverride _over = new();
    private readonly byte[] _devZip, _liliumZ;
    private byte[] _stableTgz;
    private FakeWeb _web;
    private readonly ComponentStore _store;
    private int _saves;

    public DxvkTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, "bin");
        Directory.CreateDirectory(_target);
        File.WriteAllText(Path.Combine(_target, "Game.exe"), "EXE");
        _store = new ComponentStore(new GitHubClient(), () => true);
        _drs.Define(NvidiaProfileCatalog.PresentMethod, NvidiaProfileCatalog.PresentFlags);
        _stableTgz = StableTarGz("v1");
        _devZip = DevZip("dev");
        _liliumZ = Convert.FromBase64String(LiliumSevenZ);
        _web = Web();
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_tmp, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_tmp, true);
        }
        catch (IOException) { }
    }

    // ---------- fixtures ----------

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] StableTarGz(string variant, string prefix = "dxvk-3.1.1", string[]? skip = null)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gz, leaveOpen: true))
        {
            foreach (var arch in new[] { "x32", "x64" })
                foreach (var dll in DxvkDlls.All)
                {
                    if (skip?.Contains(arch + "/" + dll) == true) continue;
                    tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{prefix}/{arch}/{dll}") { DataStream = new MemoryStream(Encoding.ASCII.GetBytes($"{variant}:{arch}:{dll}")) });
                }
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{prefix}/native/libdxvk_d3d9.so") { DataStream = new MemoryStream("so"u8.ToArray()) });
        }
        return ms.ToArray();
    }

    private static byte[] DevZip(string variant)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.ASCII);
                w.Write(content);
            }
            Add("dxvk-native-master-d713ef8748a1eee3e90d4b6d5dc2e80f5be4b519.tar.gz", "native");
            foreach (var arch in new[] { "x32", "x64" })
                foreach (var dll in DxvkDlls.All) Add($"{arch}/{dll}", $"{variant}:{arch}:{dll}");
        }
        return ms.ToArray();
    }

    private static object Asset(string name, string url, byte[] content) => new { name, size = content.Length, browser_download_url = url, digest = "sha256:" + Sha(content) };

    private string StableReleases(string? digestOverride = null) => JsonSerializer.Serialize(new object[]
    {
        new { tag_name = "v3.1.1", prerelease = false, draft = false, published_at = "2026-10-01T00:00:00Z", assets = new object[]
        {
            digestOverride is null ? Asset("dxvk-3.1.1.tar.gz", StableUrl, _stableTgz)
                : new { name = "dxvk-3.1.1.tar.gz", size = 1, browser_download_url = StableUrl, digest = "sha256:" + digestOverride },
            Asset("dxvk-native-3.1.1-steamrt-sniper.tar.gz", "https://example.invalid/native.tar.gz", "native"u8.ToArray()),
        } },
    });

    private string LiliumReleases() => JsonSerializer.Serialize(new object[]
    {
        new { tag_name = "v3.1-HDR-mod-v0.3.4", prerelease = false, draft = false, published_at = "2026-09-07T00:00:00Z", assets = new object[]
        {
            Asset("dxvk_v3.1-HDR-mod-v0.3.4-gplasync-1.7z", "https://example.invalid/gplasync.7z", "gp"u8.ToArray()),
            Asset("dxvk_v3.1-HDR-mod-v0.3.4.7z", LiliumUrl, _liliumZ),
        } },
    });

    private FakeWeb Web(string? stable = null) => new(url => url switch
    {
        "https://api.github.com/repos/doitsujin/dxvk/releases?per_page=15" => Encoding.UTF8.GetBytes(stable ?? StableReleases()),
        "https://api.github.com/repos/EndlesslyFlowering/dxvk/releases?per_page=15" => Encoding.UTF8.GetBytes(LiliumReleases()),
        var u when u == DxvkLibrary.DevRunsUrl => Encoding.UTF8.GetBytes(
            """{"workflow_runs":[{"id":39564889126,"head_sha":"d713ef8748a1eee3e90d4b6d5dc2e80f5be4b519","created_at":"2026-10-21T08:00:00Z","artifacts_url":"https://api.github.com/repos/doitsujin/dxvk/actions/runs/39564889126/artifacts"}]}"""),
        "https://api.github.com/repos/doitsujin/dxvk/actions/runs/39564889126/artifacts" => Encoding.UTF8.GetBytes(
            $"{{\"artifacts\":[{{\"name\":\"{DevName}\",\"expired\":false,\"size_in_bytes\":{_devZip.Length},\"digest\":\"sha256:{Sha(_devZip)}\"}}]}}"),
        StableUrl => _stableTgz,
        LiliumUrl => _liliumZ,
        DevUrl => _devZip,
        _ => null,
    });

    private DxvkLibrary Library(FakeWeb? web = null) => new(new GitHubClient(_web = web ?? _web));

    /// <summary>A release folder of stand-in dlls (their text says which variant, bitness and dll), imported into the library.</summary>
    private LibraryBuild Stub(string variant, bool lilium = false)
    {
        var dir = Path.Combine(_tmp, "src", $"dxvk-{variant}-{Guid.NewGuid().ToString("N")[..4]}");
        foreach (var arch in new[] { "x32", "x64" })
            foreach (var dll in DxvkDlls.All)
            {
                Directory.CreateDirectory(Path.Combine(dir, arch));
                var signature = lilium && dll is "d3d11.dll" or "d3d9.dll" ? ":" + DxvkConf.LiliumSignature : "";
                File.WriteAllText(Path.Combine(dir, arch, dll), $"{variant}:{arch}:{dll}{signature}");
            }
        return DxvkLibrary.Import(dir);
    }

    private GameInfo Game(GraphicsApis apis = GraphicsApis.Dx11, int bits = 64, string? antiCheat = null)
    {
        var g = GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));
        g.Tech.Apis = apis;
        g.Tech.Bits = bits;
        g.AntiCheat = antiCheat;
        return g;
    }

    private DxvkService Service(VulkanLayerState layer = VulkanLayerState.NotInstalled, FakeDrs? drs = null) =>
        new(new Installer(_store) { VulkanHost = new ReShadeInstallTests.FakeLayer(layer) }, new GitHubClient(_web), new NvidiaProfile(drs ?? _drs));

    private DxvkRequest Req(GameInfo g, string? pick, string? tier = null, bool present = true, string? conf = null, InstallOptions? reshade = null, bool force = true, bool confirmed = false) =>
        new(g, _target, new NvidiaGame(FakeDrs.Exe, "Game", _over), pick, tier, present, conf, reshade, force, confirmed);

    private Task<DxvkResult> Apply(DxvkService s, GameInfo g, string? pick, string? tier = null, bool present = true, string? conf = null, InstallOptions? reshade = null,
        bool force = true, bool confirmed = false, string? defaultPick = null) =>
        s.ApplyAsync(Req(g, pick, tier, present, conf, reshade, force, confirmed), defaultPick, () => _saves++, null, default);

    private Task Remove(GameInfo g, DxvkService? s = null, InstallOptions? reShadeBack = null) =>
        (s ?? Service()).RemoveAsync(g, _target, new NvidiaGame(FakeDrs.Exe, "Game", _over), reShadeBack, () => { }, default);

    private string T(string rel) => Path.Combine(_target, rel);
    private InstalledComponent? Record() => InstallManifest.Read(_target)?.Component(ComponentRegistry.Dxvk);
    private string Conf => File.ReadAllText(T(DxvkConf.FileName));
    private IniFile ConfIni => IniFile.Load(T(DxvkConf.FileName));

    // ---------- releases ----------

    [Fact]
    public void Stable_is_the_plain_tarball_with_a_digest_and_the_native_one_is_ignored()
    {
        var releases = JsonSerializer.Deserialize<List<GhRelease>>(StableReleases())!;
        var pick = DxvkLibrary.PickStable(releases)!;
        Assert.Equal(("v3.1.1", "dxvk-3.1.1.tar.gz", DxvkChannel.Stable), (pick.Id, pick.AssetName, pick.Channel));
        Assert.Equal(Sha(_stableTgz), pick.Sha256);
        releases[0].Assets.RemoveAll(a => a.Name == "dxvk-3.1.1.tar.gz");
        Assert.Null(DxvkLibrary.PickStable(releases));
    }

    [Fact]
    public void Lilium_is_the_plain_7z_not_the_gplasync_variant()
    {
        var pick = DxvkLibrary.PickLilium(JsonSerializer.Deserialize<List<GhRelease>>(LiliumReleases())!)!;
        Assert.Equal(("lilium-v3.1-HDR-mod-v0.3.4", "dxvk_v3.1-HDR-mod-v0.3.4.7z"), (pick.Id, pick.AssetName));
        Assert.Equal("DXVK 3.1 · Lilium HDR 0.3.4", pick.Label);
    }

    [Fact]
    public async Task Development_comes_from_the_newest_master_run_through_nightly_link()
    {
        var latest = await Library().LatestAsync(DxvkChannel.Development, default);
        Assert.NotNull(latest);
        Assert.Equal(("dev-d713ef8748a1", DevUrl, Sha(_devZip)), (latest.Id, latest.Url, latest.Sha256));
    }

    [Fact]
    public async Task Development_without_a_digest_is_not_offered()
    {
        var web = new FakeWeb(url => url switch
        {
            var u when u == DxvkLibrary.DevRunsUrl => Encoding.UTF8.GetBytes("""{"workflow_runs":[{"id":1,"head_sha":"abcdef1234567","created_at":"2026-10-21T08:00:00Z","artifacts_url":"https://api.github.com/a"}]}"""),
            "https://api.github.com/a" => Encoding.UTF8.GetBytes("""{"artifacts":[{"name":"dxvk-master-abcdef1234567","expired":false,"size_in_bytes":5}]}"""),
            _ => null,
        });
        Assert.Null(await Library(web).LatestAsync(DxvkChannel.Development, default));
    }

    // ---------- downloads and the library ----------

    [Fact]
    public async Task A_stable_download_is_verified_extracted_and_kept_once()
    {
        var library = Library();
        var latest = (await library.LatestAsync(DxvkChannel.Stable, default))!;

        var build = await library.EnsureAsync(latest, null, default);

        Assert.Equal(("v3.1.1", "DXVK 3.1.1", "stable"), (build.Id, build.Label, build.Group));
        Assert.Equal("v1:x64:d3d11.dll", File.ReadAllText(DxvkLibrary.DllPath(build, "x64", "d3d11.dll")));
        Assert.Equal("v1:x32:dxgi.dll", File.ReadAllText(DxvkLibrary.DllPath(build, "x32", "dxgi.dll")));
        Assert.Empty(Directory.EnumerateFiles(build.Dir, "*.tar.gz", SearchOption.AllDirectories));
        var downloads = _web.Urls.Count(u => u == StableUrl);
        await library.EnsureAsync(latest, null, default);
        Assert.Equal(downloads, _web.Urls.Count(u => u == StableUrl));
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_digest_is_refused_and_leaves_nothing()
    {
        var library = Library(Web(StableReleases(digestOverride: new string('a', 64))));
        var latest = (await library.LatestAsync(DxvkChannel.Stable, default))!;

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => library.EnsureAsync(latest, null, default));

        Assert.Contains("SHA-256", ex.Message);
        Assert.Empty(DxvkLibrary.Builds());
        Assert.False(Directory.Exists(DxvkLibrary.Root) && Directory.EnumerateDirectories(DxvkLibrary.Root).Any());
    }

    [Fact]
    public async Task The_Lilium_7z_is_read_from_its_normal_folder_and_marked_as_Lilium()
    {
        var library = Library();
        var build = await library.EnsureAsync((await library.LatestAsync(DxvkChannel.Lilium, default))!, null, default);

        Assert.Equal(("lilium", true), (build.Group, DxvkLibrary.HasLiliumOptions(build)));
        Assert.Equal("lilium:x64:d3d9:" + DxvkConf.LiliumSignature, File.ReadAllText(DxvkLibrary.DllPath(build, "x64", "d3d9.dll")));
        Assert.False(File.Exists(Path.Combine(DxvkLibrary.PackageOf(build), "dxvk_d3d9_0_safest.conf")));
    }

    [Fact]
    public async Task A_development_zip_is_extracted_without_the_native_tarball()
    {
        var library = Library();
        var build = await library.EnsureAsync((await library.LatestAsync(DxvkChannel.Development, default))!, null, default);

        Assert.Equal(("dev-d713ef8748a1", "development"), (build.Id, build.Group));
        Assert.Equal("dev:x32:d3d8.dll", File.ReadAllText(DxvkLibrary.DllPath(build, "x32", "d3d8.dll")));
        Assert.Empty(Directory.EnumerateFiles(build.Dir, "*.tar.gz", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task An_archive_missing_a_dll_is_refused_whole()
    {
        _stableTgz = StableTarGz("v1", skip: ["x64/dxgi.dll"]);
        var library = Library(Web());
        var latest = (await library.LatestAsync(DxvkChannel.Stable, default))!;

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => library.EnsureAsync(latest, null, default));

        Assert.Contains("dxgi", ex.Message);
        Assert.Empty(DxvkLibrary.Builds());
    }

    [Fact]
    public void Import_takes_a_folder_a_tarball_and_a_7z_dedupes_and_tells_Lilium_apart()
    {
        var a = Stub("v1");
        var tgz = Path.Combine(_tmp, "src", "dxvk-3.0.2.tar.gz");
        File.WriteAllBytes(tgz, StableTarGz("v2", "dxvk-3.0.2"));
        var b = DxvkLibrary.Import(tgz);
        var again = DxvkLibrary.Import(tgz);
        var sz = Path.Combine(_tmp, "src", "lilium.7z");
        File.WriteAllBytes(sz, _liliumZ);
        var c = DxvkLibrary.Import(sz);

        Assert.Equal("stable", a.Group);
        Assert.Equal(b.Id, again.Id);
        Assert.Equal("DXVK 3.0.2 (imported)", b.Label);
        Assert.Equal("lilium", c.Group);
        Assert.Contains("Lilium HDR", c.Label);
        Assert.Equal(3, DxvkLibrary.Builds().Count);
        Assert.All(DxvkLibrary.Builds(), x => Assert.False(x.Downloaded));
        Assert.Throws<InvalidDataException>(() => DxvkLibrary.Import(Path.Combine(_tmp, "appdata")));
        Assert.Throws<FileNotFoundException>(() => DxvkLibrary.Import(Path.Combine(_tmp, "nothing.7z")));
    }

    [Fact]
    public void Choose_follows_the_channel_or_the_pinned_build_and_removing_a_build_unpins_it()
    {
        var stable = Stub("v1");
        var lilium = Stub("l1", lilium: true);
        var settings = new AppSettings();

        Assert.Equal(stable.Id, DxvkLibrary.Choose(null)!.Id);
        Assert.Equal(lilium.Id, DxvkLibrary.Choose("lilium")!.Id);
        Assert.Equal(lilium.Id, DxvkLibrary.Choose(null, lilium.Id)!.Id);
        Assert.Null(DxvkLibrary.Choose("development"));
        Assert.Equal(DxvkChannel.Lilium, DxvkLibrary.FollowedChannel("lilium", null));
        Assert.Null(DxvkLibrary.FollowedChannel(stable.Id, null));

        settings.SetDefaultBuild(ComponentRegistry.Dxvk, lilium.Id);
        settings.For("g").SetBuildPick(ComponentRegistry.Dxvk, lilium.Id);
        DxvkLibrary.Remove(lilium, settings);
        Assert.Null(settings.DefaultBuild(ComponentRegistry.Dxvk));
        Assert.Null(settings.For("g").BuildPick(ComponentRegistry.Dxvk));
        Assert.Single(DxvkLibrary.Builds());
    }

    [Fact]
    public void The_cleanup_protects_the_default_and_game_picks()
    {
        var a = Stub("v1");
        var b = Stub("v2");
        var c = Stub("v3");
        var settings = new AppSettings();
        settings.SetDefaultBuild(ComponentRegistry.Dxvk, a.Id);
        settings.For("g").SetBuildPick(ComponentRegistry.Dxvk, b.Id);

        var keep = _store.ProtectedBuilds(LibraryKind.Dxvk, settings, []);

        Assert.Contains(a.Id, keep);
        Assert.Contains(b.Id, keep);
        Assert.DoesNotContain(c.Id, keep);
        Assert.Equal("DXVK", ComponentStore.LibraryTitle(LibraryKind.Dxvk));
    }

    // ---------- install per API and bitness ----------

    [Theory]
    [InlineData(GraphicsApis.Dx8, 32, "d3d8.dll")]
    [InlineData(GraphicsApis.Dx9, 64, "d3d9.dll")]
    [InlineData(GraphicsApis.Dx10, 64, "d3d10core.dll,dxgi.dll")]
    [InlineData(GraphicsApis.Dx11, 32, "d3d11.dll,dxgi.dll")]
    public async Task The_dlls_of_the_games_API_go_in_from_the_folder_of_its_bitness_and_come_out_again(GraphicsApis api, int bits, string dlls)
    {
        var build = Stub("v1");
        var g = Game(api, bits);

        var result = await Apply(Service(), g, build.Id);

        var arch = bits == 32 ? "x32" : "x64";
        var names = dlls.Split(',');
        Assert.Equal(build.Id, result.Build.Id);
        foreach (var dll in names) Assert.Equal($"v1:{arch}:{dll}", File.ReadAllText(T(dll)));
        foreach (var other in DxvkDlls.All.Except(names)) Assert.False(File.Exists(T(other)));
        var c = Record()!;
        Assert.Equal((build.Id, names.Length), (c.BuildId, c.Files.Count(f => f.Path.EndsWith(".dll"))));
        Assert.Equal(arch, c.Extras[DxvkPlanner.ArchKey]);
        Assert.Equal("off", c.Extras[DxvkPlanner.PresentKey]);
        Assert.Equal(1, _saves);

        await Remove(g);
        foreach (var dll in names) Assert.False(File.Exists(T(dll)));
        Assert.False(File.Exists(T(DxvkConf.FileName)));
        Assert.Null(InstallManifest.Read(_target));
    }

    [Theory]
    [InlineData(GraphicsApis.Dx12)]
    [InlineData(GraphicsApis.Vulkan)]
    [InlineData(GraphicsApis.OpenGL)]
    [InlineData(GraphicsApis.None)]
    public async Task Games_that_are_not_Direct3D_8_to_11_have_nothing_for_DXVK_to_do(GraphicsApis api)
    {
        var build = Stub("v1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(Service(), Game(api), build.Id));

        Assert.NotEmpty(ex.Message);
        Assert.NotNull(DxvkRules.NotApplicable(Game(api)));
        Assert.Null(InstallManifest.Read(_target));
    }

    [Fact]
    public async Task A_manual_API_override_decides_the_dll_set()
    {
        var build = Stub("v1");
        var g = Game(GraphicsApis.Dx11 | GraphicsApis.Dx12);
        Assert.NotNull(DxvkRules.NotApplicable(g));

        g.Manual = new TechOverride { Apis = GraphicsApis.Dx9 };
        await Apply(Service(), g, build.Id);

        Assert.True(File.Exists(T("d3d9.dll")));
        Assert.False(File.Exists(T("d3d11.dll")));
    }

    [Fact]
    public async Task Changing_the_API_swaps_the_dlls_on_the_next_apply()
    {
        var build = Stub("v1");
        await Apply(Service(), Game(GraphicsApis.Dx9), build.Id);
        await Apply(Service(), Game(GraphicsApis.Dx11), build.Id);

        Assert.False(File.Exists(T("d3d9.dll")));
        Assert.True(File.Exists(T("d3d11.dll")) && File.Exists(T("dxgi.dll")));
        Assert.Equal(2, Record()!.Files.Count(f => f.Path.EndsWith(".dll")));
    }

    // ---------- foreign dlls and clashes ----------

    [Fact]
    public async Task A_foreign_dll_needs_a_confirmation_then_is_backed_up_and_put_back_byte_for_byte()
    {
        var build = Stub("v1");
        File.WriteAllText(T("d3d11.dll"), "SOMEONE ELSE'S");
        File.WriteAllText(T("dxgi.dll"), "ANOTHER");
        var g = Game();

        var ex = await Assert.ThrowsAsync<DxvkConfirmException>(() => Apply(Service(), g, build.Id));

        Assert.Equal(2, ex.Warnings.Count);
        Assert.Equal("SOMEONE ELSE'S", File.ReadAllText(T("d3d11.dll")));
        Assert.Null(InstallManifest.Read(_target));

        await Apply(Service(), g, build.Id, confirmed: true);
        Assert.Equal("v1:x64:d3d11.dll", File.ReadAllText(T("d3d11.dll")));
                Assert.Equal(2, Record()!.Backups.Count);

        await Remove(g);
        Assert.Equal("SOMEONE ELSE'S", File.ReadAllText(T("d3d11.dll")));
        Assert.Equal("ANOTHER", File.ReadAllText(T("dxgi.dll")));
    }

    [Fact]
    public async Task An_update_over_our_own_dlls_needs_no_confirmation_and_the_original_still_comes_back()
    {
        var a = Stub("v1");
        var b = Stub("v2");
        File.WriteAllText(T("d3d11.dll"), "FOREIGN");
        var g = Game();
        await Apply(Service(), g, a.Id, confirmed: true);

        await Apply(Service(), g, b.Id, force: false);

        Assert.Equal("v2:x64:d3d11.dll", File.ReadAllText(T("d3d11.dll")));
        await Remove(g);
        Assert.Equal("FOREIGN", File.ReadAllText(T("d3d11.dll")));
    }

    [Fact]
    public async Task Anti_cheat_is_a_warning_that_can_be_confirmed()
    {
        var build = Stub("v1");
        var g = Game(antiCheat: "EasyAntiCheat");

        var check = Service().Check(g, _target);
        var ex = await Assert.ThrowsAsync<DxvkConfirmException>(() => Apply(Service(), g, build.Id));
        await Apply(Service(), g, build.Id, confirmed: true);

        Assert.True(check.CanInstall && check.NeedsConfirm);
        Assert.Contains("EasyAntiCheat", ex.Warnings.Single());
        Assert.True(File.Exists(T("d3d11.dll")));
    }

    [Fact]
    public void A_dll_that_OptiScaler_owns_is_a_blocker_not_a_warning()
    {
        var m = new InstallManifest { Opti = true, Proxy = "dxgi.dll", Mode = InstallMode.OptiScaler };
        m.Files.Add(@"bin\dxgi.dll");

        var check = DxvkRules.Check(Game(), _target, m, new ReShadeInstallTests.FakeLayer(VulkanLayerState.Installed));

        Assert.False(check.CanInstall);
        Assert.Contains("OptiScaler", check.Blockers.Single());
    }

    // ---------- dxvk.conf ----------

    [Fact]
    public async Task A_fresh_dxvk_conf_gets_the_four_defaults_and_goes_with_DXVK()
    {
        var build = Stub("v1");
        await Apply(Service(), Game(GraphicsApis.Dx9), build.Id);

        var ini = ConfIni;
        Assert.Equal("True", ini.Get("", "dxgi.enableHDR"));
        Assert.Equal("False", ini.Get("", "dxvk.allowFse"));
        Assert.Equal("Auto", ini.Get("", "dxvk.latencySleep"));
        Assert.Equal("True", ini.Get("", "d3d9.dpiAware"));
        Assert.DoesNotContain("[", Conf);
        Assert.DoesNotContain("SwapChain", Conf);
    }

    [Fact]
    public async Task The_users_existing_values_win_over_our_defaults_and_the_file_comes_back_as_it_was()
    {
        const string original = "# my DXVK settings\r\ndxvk.allowFse = True\r\nd3d9.maxFrameRate = 60\r\n";
        File.WriteAllText(T(DxvkConf.FileName), original);
        var build = Stub("v1");
        var g = Game(GraphicsApis.Dx9);

        await Apply(Service(), g, build.Id, conf: "dxgi.enableHDR = False\n# comment\nd3d9.maxFrameRate = 30");

        var ini = ConfIni;
        Assert.Equal("True", ini.Get("", "dxvk.allowFse"));
        Assert.Equal("False", ini.Get("", "dxgi.enableHDR"));
        Assert.Equal("30", ini.Get("", "d3d9.maxFrameRate"));
        Assert.Equal("Auto", ini.Get("", "dxvk.latencySleep"));
        Assert.StartsWith("# my DXVK settings", Conf);

        await Remove(g);
        Assert.Equal(original, File.ReadAllText(T(DxvkConf.FileName)));
    }

    [Fact]
    public async Task An_update_leaves_a_value_the_user_changed_since_but_apply_writes_ours()
    {
        var lilium = Stub("l1", lilium: true);
        var g = Game(GraphicsApis.Dx11);
        const string key = "d3d11.upgradeBackBufferTo";
        await Apply(Service(), g, lilium.Id, tier: "slightly-unsafe");
        Assert.Equal("rgba16_sfloat", ConfIni.Get("", key));

        File.WriteAllText(T(DxvkConf.FileName), Conf.Replace("d3d11.upgradeBackBufferTo = rgba16_sfloat", "d3d11.upgradeBackBufferTo = rgba16_unorm"));
        await Apply(Service(), g, lilium.Id, tier: "slightly-unsafe", force: false);
        Assert.Equal("rgba16_unorm", ConfIni.Get("", key));

        await Apply(Service(), g, lilium.Id, tier: "slightly-unsafe", force: true);
        Assert.Equal("rgba16_sfloat", ConfIni.Get("", key));
    }

    [Fact]
    public void A_bad_extra_line_is_reported_and_dxvk_conf_has_no_sections()
    {
        var (items, errors) = DxvkConf.ParseLines("a.b = 1\n[Section]\nnonsense\n# c\n; d\na.b = 2\n");

        Assert.Equal([("a.b", "2")], items);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public async Task A_mistake_in_the_extra_lines_changes_nothing_in_the_game()
    {
        var build = Stub("v1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(Service(), Game(), build.Id, conf: "[Oops]"));

        Assert.Null(InstallManifest.Read(_target));
        Assert.False(File.Exists(T("d3d11.dll")));
    }

    // ---------- the Lilium presets ----------

    [Fact]
    public async Task The_safest_Lilium_tier_is_the_default_and_matches_the_authors_preset_per_API()
    {
        var lilium = Stub("l1", lilium: true);

        await Apply(Service(), Game(GraphicsApis.Dx11), lilium.Id);
        var ini = ConfIni;
        Assert.Equal(("true", "rgba16_sfloat", "scRGB"),
            (ini.Get("", "d3d11.enableSwapChainUpgrade"), ini.Get("", "d3d11.upgradeSwapChainFormatTo"), ini.Get("", "d3d11.upgradeSwapChainColorSpaceTo")));
        Assert.Null(ini.Get("", "d3d11.enableBackBufferUpgrade"));
        Assert.DoesNotContain("enableAsync", Conf);
        Assert.DoesNotContain("gplAsyncCache", Conf);
        Assert.Equal("safest", Record()!.Extras[DxvkPlanner.TierKey]);

        await Remove(Game(GraphicsApis.Dx11));
        await Apply(Service(), Game(GraphicsApis.Dx9), lilium.Id);
        ini = ConfIni;
        Assert.Equal(("true", "rgba16_sfloat", "scRGB", "disabled"),
            (ini.Get("", "d3d9.enableSwapChainUpgrade"), ini.Get("", "d3d9.upgradeSwapChainFormatTo"), ini.Get("", "d3d9.upgradeSwapChainColorSpaceTo"), ini.Get("", "d3d9.enforceWindowModeInternally")));
    }

    [Fact]
    public async Task A_higher_tier_adds_the_render_target_upgrades_and_off_writes_no_HDR_keys()
    {
        var lilium = Stub("l1", lilium: true);
        var g = Game(GraphicsApis.Dx11);

        await Apply(Service(), g, lilium.Id, tier: "unsafer");
        var ini = ConfIni;
        Assert.Equal("true", ini.Get("", "d3d11.enableRenderTargetUpgrades"));
        Assert.Equal("rgba16_unorm", ini.Get("", "d3d11.upgrade_RGBA8_UNORM_renderTargetTo"));
        Assert.Equal("rgba16_typeless", ini.Get("", "d3d11.upgrade_BGRA8_TYPELESS_renderTargetTo"));
        Assert.Equal("rgba16_unorm", ini.Get("", "d3d11.upgradeBackBufferTo"));

        await Remove(g);
        await Apply(Service(), g, lilium.Id, tier: DxvkConf.TierOff);
        Assert.DoesNotContain("SwapChain", Conf);
        Assert.DoesNotContain("renderTargetTo", Conf);
        Assert.Equal("True", ConfIni.Get("", "dxgi.enableHDR"));
    }

    [Fact]
    public void Every_tier_uses_keys_of_the_forks_documentation_for_its_API()
    {
        foreach (var d3d9 in new[] { true, false })
        {
            var prefix = d3d9 ? "d3d9." : "d3d11.";
            Assert.Equal(d3d9 ? 6 : 7, DxvkConf.Tiers(d3d9).Count);
            foreach (var t in DxvkConf.Tiers(d3d9))
            {
                Assert.All(t.Keys, k => Assert.StartsWith(prefix, k.Key));
                Assert.Contains((prefix + "upgradeSwapChainColorSpaceTo", "scRGB"), t.Keys);
                Assert.Equal(t.Keys.Count, t.Keys.Select(k => k.Key).Distinct().Count());
            }
        }
        Assert.Contains(("d3d9.enforceWindowModeInternally", "disabled"), DxvkConf.Tier(true, "safest")!.Keys);
    }

    [Fact]
    public async Task A_stock_build_never_gets_the_HDR_keys_even_when_a_tier_is_asked_for()
    {
        var stable = Stub("v1");

        await Apply(Service(), Game(), stable.Id, tier: "slightly-experimental");

        Assert.DoesNotContain("Upgrade", Conf, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("", Record()!.Extras[DxvkPlanner.TierKey]);
    }

    // ---------- the NVIDIA present method ----------

    [Fact]
    public async Task The_Lilium_build_sets_the_present_method_and_remove_puts_the_previous_values_back()
    {
        var lilium = Stub("l1", lilium: true);
        var profile = _drs.AddProfile(FakeDrs.Exe);
        profile.Values[NvidiaProfileCatalog.PresentMethod] = 0;
        var g = Game();

        var result = await Apply(Service(), g, lilium.Id);

        Assert.Equal(NvidiaProfileCatalog.PresentLayeredOnDxgi, _drs.Value(NvidiaProfileCatalog.PresentMethod));
        Assert.Equal(NvidiaProfileCatalog.PresentTreatDxvkAsNative, _drs.Value(NvidiaProfileCatalog.PresentFlags));
        Assert.Contains(result.Notes, n => n.Contains("present"));
        Assert.False(NvidiaRecord.Load(_over).IsEmpty);

        await Remove(g);
        Assert.Equal(0u, _drs.Value(NvidiaProfileCatalog.PresentMethod));
        Assert.Null(_drs.Value(NvidiaProfileCatalog.PresentFlags));
        Assert.True(NvidiaRecord.Load(_over).IsEmpty);
    }

    [Fact]
    public async Task The_present_method_is_left_alone_when_turned_off_for_a_stock_build_and_without_a_driver()
    {
        var lilium = Stub("l1", lilium: true);
        var stable = Stub("v1");
        _drs.AddProfile(FakeDrs.Exe);

        await Apply(Service(), Game(), lilium.Id, present: false);
        Assert.Null(_drs.Value(NvidiaProfileCatalog.PresentMethod));
        Assert.Equal("off", Record()!.Extras[DxvkPlanner.PresentKey]);

        await Apply(Service(), Game(), stable.Id);
        Assert.Null(_drs.Value(NvidiaProfileCatalog.PresentMethod));

        var none = new FakeDrs { Available = false };
        var result = await Apply(Service(drs: none), Game(), lilium.Id);
        Assert.Contains(result.Notes, n => n.Contains("No NVIDIA driver"));
    }

    [Fact]
    public async Task Switching_from_the_Lilium_build_to_a_stock_one_gives_the_driver_values_back()
    {
        var lilium = Stub("l1", lilium: true);
        var stable = Stub("v1");
        _drs.AddProfile(FakeDrs.Exe);
        var g = Game();
        await Apply(Service(), g, lilium.Id);
        Assert.NotNull(_drs.Value(NvidiaProfileCatalog.PresentMethod));

        await Apply(Service(), g, stable.Id);

        Assert.Null(_drs.Value(NvidiaProfileCatalog.PresentMethod));
        Assert.Null(_drs.Value(NvidiaProfileCatalog.PresentFlags));
    }

    [Fact]
    public async Task A_driver_that_does_not_keep_the_values_is_reported_and_DXVK_stays_installed()
    {
        var lilium = Stub("l1", lilium: true);
        _drs.AddProfile(FakeDrs.Exe);
        _drs.FailSet = (id, _) => id == NvidiaProfileCatalog.PresentFlags;

        var result = await Apply(Service(), Game(), lilium.Id);

        Assert.NotNull(Record());
        Assert.Contains(result.Notes, n => n.Contains("NVIDIA driver refused"));
        Assert.Null(_drs.Value(NvidiaProfileCatalog.PresentMethod));
    }

    // ---------- ReShade next to DXVK ----------

    private ReShadeBuild ImportReShade()
    {
        var rs = _store.ImportReShade(ReShadeFixtures.Folder(Path.Combine(_tmp, "rs"), "a", "6.8.0.2155"));
        _store.SelectedReShade = () => rs.Id;
        return rs;
    }

    private static InstallOptions ReShadeOpts(string proxy) => new() { Mode = InstallMode.ReShadeOnly, ReShade = true, Proxy = proxy, ReShadeTemplate = [] };

    [Fact]
    public async Task ReShade_on_the_dll_name_DXVK_needs_moves_to_its_Vulkan_layer_after_a_confirmation()
    {
        var rs = ImportReShade();
        var installer = new Installer(_store) { VulkanHost = new ReShadeInstallTests.FakeLayer(VulkanLayerState.Installed) };
        var g = Game(GraphicsApis.Dx9);
        await installer.InstallAsync(g, _target, ReShadeOpts("d3d9.dll"), null, default);
        Assert.Equal(rs.Sha64, FileUtil.Sha256(T("d3d9.dll")));
        var build = Stub("v1");
        var service = new DxvkService(installer, new GitHubClient(_web), new NvidiaProfile(_drs));

        var check = service.Check(g, _target);
        Assert.True(check.SwitchReShade && check.CanInstall);
        await Assert.ThrowsAsync<DxvkConfirmException>(() => service.ApplyAsync(Req(g, build.Id), null, () => { }, null, default));
        Assert.Equal(rs.Sha64, FileUtil.Sha256(T("d3d9.dll")));

        await service.ApplyAsync(Req(g, build.Id, reshade: ReShadeOpts("d3d9.dll"), confirmed: true), null, () => { }, null, default);

        var m = InstallManifest.Read(_target)!;
        Assert.True(m.ReShade && m.ReShadeVulkan);
        Assert.Null(m.Proxy);
        Assert.Equal("v1:x64:d3d9.dll", File.ReadAllText(T("d3d9.dll")));
        Assert.NotNull(m.Component(ComponentRegistry.Dxvk));
        Assert.Equal(["d3d9.dll"], DxvkDlls.InstalledIn(m));
        Assert.True(ReShadeNaming.Resolve(g.Effective, optiScalerLoads: false, null, DxvkDlls.InstalledIn(m)).VulkanLayer);
    }

    [Fact]
    public async Task Removing_DXVK_installs_ReShade_under_its_proxy_name_again_when_DXVK_had_moved_it_to_the_layer()
    {
        var rs = ImportReShade();
        var installer = new Installer(_store) { VulkanHost = new ReShadeInstallTests.FakeLayer(VulkanLayerState.Installed) };
        var g = Game(GraphicsApis.Dx9);
        await installer.InstallAsync(g, _target, ReShadeOpts("d3d9.dll"), null, default);
        var service = new DxvkService(installer, new GitHubClient(_web), new NvidiaProfile(_drs));
        await service.ApplyAsync(Req(g, Stub("v1").Id, reshade: ReShadeOpts("d3d9.dll"), confirmed: true), null, () => { }, null, default);
        Assert.True(InstallManifest.Read(_target)!.ReShadeVulkan);

        await Remove(g, service, ReShadeOpts("d3d9.dll") with { ReShadeVulkan = true });

        var m = InstallManifest.Read(_target)!;
        Assert.False(m.ReShadeVulkan);
        Assert.True(m.ReShade);
        Assert.Equal("d3d9.dll", m.Proxy);
        Assert.Equal(rs.Sha64, FileUtil.Sha256(T("d3d9.dll")));
        Assert.Null(m.Component(ComponentRegistry.Dxvk));
    }

    [Fact]
    public async Task Without_the_Vulkan_layer_the_move_is_a_blocker_and_nothing_changes()
    {
        var rs = ImportReShade();
        var g = Game(GraphicsApis.Dx9);
        await new Installer(_store) { VulkanHost = new ReShadeInstallTests.FakeLayer(VulkanLayerState.NotInstalled) }.InstallAsync(g, _target, ReShadeOpts("d3d9.dll"), null, default);
        var build = Stub("v1");
        var service = Service(VulkanLayerState.NotInstalled);

        var check = service.Check(g, _target);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(Req(g, build.Id, confirmed: true), null, () => { }, null, default));

        Assert.False(check.CanInstall);
        Assert.Contains("Vulkan layer", ex.Message);
        Assert.Equal(rs.Sha64, FileUtil.Sha256(T("d3d9.dll")));
    }

    [Fact]
    public void With_DXVK_installed_ReShade_takes_the_layer_unless_the_user_chose_a_name_DXVK_does_not_use()
    {
        var tech = Game(GraphicsApis.Dx11).Effective;

        Assert.True(ReShadeNaming.Resolve(tech, false, null, ["d3d11.dll", "dxgi.dll"]).VulkanLayer);
        Assert.True(ReShadeNaming.Resolve(tech, false, "dxgi.dll", ["d3d11.dll", "dxgi.dll"]).VulkanLayer);
        Assert.Equal("dinput8.dll", ReShadeNaming.Resolve(tech, false, "dinput8.dll", ["d3d11.dll", "dxgi.dll"]).File);
        Assert.Equal("dxgi.dll", ReShadeNaming.Resolve(tech, false, null, null).File);
        Assert.Equal(ComponentStore.ReShadeFile, ReShadeNaming.Resolve(tech, true, null, ["dxgi.dll"]).File);
    }

    // ---------- the source of a build ----------

    [Fact]
    public async Task Following_a_channel_installs_the_newest_download_and_offline_falls_back_to_the_library()
    {
        var g = Game();

        var online = await Apply(Service(), g, null);
        Assert.Equal("v3.1.1", online.Build.Id);
        Assert.Equal("v1:x64:d3d11.dll", File.ReadAllText(T("d3d11.dll")));

        _web = new FakeWeb(_ => null);
        var offline = await Apply(Service(), g, "stable", force: false);
        Assert.Equal("v3.1.1", offline.Build.Id);

        await Remove(g);
        Directory.Delete(DxvkLibrary.Root, true);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(Service(), g, null));
        Assert.Contains("library", ex.Message);
    }

    [Fact]
    public async Task A_pinned_build_that_left_the_library_is_not_replaced_by_another_one()
    {
        Stub("v1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(Service(), Game(), "imported-deadbeef0000"));

        Assert.Contains("no longer in the library", ex.Message);
        Assert.Null(InstallManifest.Read(_target));
    }

    // ---------- the method advisor ----------

    [Fact]
    public void The_advisor_offers_DXVK_as_advice_only_for_Direct3D_games_without_an_HDR_mod()
    {
        var dx11 = MethodAdvisor.Advise(new AdvisorInput(Game(GraphicsApis.Dx11), GpuInfo.Unknown));
        var dx12 = MethodAdvisor.Advise(new AdvisorInput(Game(GraphicsApis.Dx12), GpuInfo.Unknown));
        var ac = MethodAdvisor.Advise(new AdvisorInput(Game(GraphicsApis.Dx9, antiCheat: "BattlEye"), GpuInfo.Unknown));

        var option = dx11.Methods.Single(m => m.Method == Method.Dxvk);
        Assert.False(option.Selectable);
        Assert.Empty(option.Components);
        Assert.NotEqual(Method.Dxvk, dx11.Top?.Method);
        Assert.DoesNotContain(dx12.Methods, m => m.Method == Method.Dxvk);
        Assert.Contains("BattlEye", ac.Methods.Single(m => m.Method == Method.Dxvk).Warning);
    }

    // ---------- sectionless ini ----------

    [Fact]
    public void A_file_without_sections_takes_keys_at_the_top_and_gives_them_back()
    {
        var ini = IniFile.Parse("# comment\nb = 1\n", keepFinalNewlineAsIs: true);

        ini.Set("", "a.key", "x");
        ini.Set("", "b", "2");

        Assert.Equal("x", ini.Get("", "a.key"));
        Assert.Equal("2", ini.Get("", "b"));
        Assert.True(ini.HasSection(""));
        Assert.True(ini.Remove("", "a.key"));
        Assert.Equal("# comment\nb = 2\n", ini.ToString());
        var withSection = IniFile.Parse("[S]\nk=1\n");
        withSection.Set("", "top", "t");
        Assert.StartsWith("top = t", withSection.ToString());
        Assert.Equal("1", withSection.Get("S", "k"));
        Assert.Null(withSection.Get("", "k"));
    }
}
