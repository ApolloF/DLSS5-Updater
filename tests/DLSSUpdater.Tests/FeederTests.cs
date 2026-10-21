using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// DLSS5-Feeder as a component: the release is chosen from the pinned repository's releases, verified against the digest GitHub
/// publishes, kept in its own library, installed per its README (64-bit and 32-bit with the host64 helper) and removed again.
/// A fake web server stands in for GitHub; the games are temp folders.
/// </summary>
public sealed class FeederTests : IDisposable
{
    private const string Tag = "v1.19.0";
    private const string Asset = "DLSS5-Feeder-1.19.0.zip";
    private const string ZipUrl = "https://github.com/jlrouzies-fr/DLSS5-Feeder/releases/download/v1.19.0/DLSS5-Feeder-1.19.0.zip";

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-feeder-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly string _target;
    private readonly byte[] _zip;

    private sealed class FakeWeb(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request.RequestUri!.ToString()));
        }
    }

    public FeederTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = _root;
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "game.exe"), "EXE");
        _zip = BuildZip();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    /// <summary>The release zip as DLSS5-Feeder 1.19.0 lays it out (listed from the real release), with stand-in contents.</summary>
    private static byte[] BuildZip(bool withHost = true)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            Add("dlss5-feed.addon64", "ADDON64");
            Add("dlss5-feed.addon32", "ADDON32");
            Add("dlss5-feed-helper.addon64", "HELPER");
            if (withHost) Add("host64/dlss5-feed-host64.exe", "HOST64");
            Add("reshade-shaders/Shaders/DLSS5_Feed.fx", "FX");
            Add("layer-x64/VkLayer_feed_vk.dll", "LAYER");
            Add("READ-ME-FIRST.txt", "DLSS5-Feeder 1.19.0");
            Add("Verify-DLSS5Feeder.ps1", "# script");
        }
        return ms.ToArray();
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static GhRelease Release(string tag, string? digest, bool prerelease = false, string? assetName = null, DateTime? published = null)
    {
        var version = tag.TrimStart('v');
        return new GhRelease
        {
            TagName = tag, Prerelease = prerelease, PublishedAt = published ?? new DateTime(2026, 10, 11, 5, 56, 10, DateTimeKind.Utc),
            Assets = [new GhAsset { Name = assetName ?? $"DLSS5-Feeder-{version}.zip", Url = $"https://github.com/jlrouzies-fr/DLSS5-Feeder/releases/download/{tag}/DLSS5-Feeder-{version}.zip", Digest = digest }],
        };
    }

    private static string Digest(byte[] bytes) => "sha256:" + Sha(bytes);

    private FeederRelease Known() => FeederLibrary.Pick([Release(Tag, Digest(_zip))], false)!;

    private static HttpResponseMessage Body(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static GameInfo GameOf(string root, int bits) =>
        new() { Name = "Test", Root = root, Exes = [Path.Combine(root, "game.exe")], Tech = new GameTech { Bits = bits } };

    // ---------- which release ----------

    [Fact]
    public void Pick_TakesTheNewestStableReleaseWithAVerifiableZip()
    {
        var older = Release("v1.17.0", "sha256:" + new string('1', 64), published: new DateTime(2026, 9, 27));
        var beta = Release("v1.18.0-beta.2", "sha256:" + new string('2', 64), published: new DateTime(2026, 10, 6));
        var stable = Release("v1.19.0", "sha256:" + new string('3', 64), published: new DateTime(2026, 10, 11));

        Assert.Equal("v1.19.0", FeederLibrary.Pick([older, beta, stable], false)!.Tag);
        Assert.Equal("v1.17.0", FeederLibrary.Pick([older, beta], false)!.Tag);
        var withBetas = FeederLibrary.Pick([older, beta], true)!;
        Assert.Equal("v1.18.0-beta.2", withBetas.Tag);
        Assert.True(withBetas.Beta);
    }

    [Fact]
    public void Pick_SkipsAReleaseWithoutADigest_AndOneWhoseZipHasAnotherName()
    {
        Assert.Null(FeederLibrary.Pick([Release("v1.19.0", null)], false));
        Assert.Null(FeederLibrary.Pick([Release("v1.19.0", "sha256:" + new string('a', 64), assetName: "DLSS5-Feeder-Setup.zip")], false));
        Assert.Null(FeederLibrary.Pick([Release("v1.19.0", "md5:abc")], false));
    }

    [Fact]
    public void Pick_IgnoresDraftsAndPrereleaseFlags()
    {
        var draft = Release("v1.20.0", "sha256:" + new string('a', 64));
        draft.Draft = true;
        var pre = Release("v1.21.0", "sha256:" + new string('b', 64), prerelease: true);

        Assert.Null(FeederLibrary.Pick([draft, pre], true));
    }

    [Fact]
    public async Task TheRepositoryIsPinnedToTheAuthorsOwnAndOnlyThatOneIsAsked()
    {
        Assert.Equal("jlrouzies-fr/DLSS5-Feeder", FeederLibrary.Repo);
        var web = new FakeWeb(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var gh = new GitHubClient(web);

        _ = await new FeederLibrary(gh).LatestAsync(false, default);

        Assert.All(web.Urls, u => Assert.Contains("/repos/jlrouzies-fr/DLSS5-Feeder/", u));
    }

    [Fact]
    public async Task LatestAsync_OffLineFallsBackToTheCachedReleaseList_AndNeverThrows()
    {
        var releases = JsonSerializer.Serialize(new List<GhRelease> { Release(Tag, Digest(_zip)) }, JsonCtx.Default.ListGhRelease);
        var online = true;
        var web = new FakeWeb(_ => online ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releases, Encoding.UTF8, "application/json"), Headers = { ETag = new("\"e1\"") } }
                                          : throw new HttpRequestException("offline"));
        var library = new FeederLibrary(new GitHubClient(web));

        Assert.Equal(Tag, (await library.LatestAsync(false, default))!.Tag);
        online = false;
        Assert.Equal(Tag, (await library.LatestAsync(false, default))!.Tag);
    }

    // ---------- download and library ----------

    [Fact]
    public async Task Ensure_VerifiesTheDigest_KeepsOnlyThePackageFiles_AndListsTheBuild()
    {
        var web = new FakeWeb(_ => Body(_zip));
        var library = new FeederLibrary(new GitHubClient(web));

        var build = await library.EnsureAsync(Known(), null, default);

        Assert.Equal(Tag, build.Id);
        Assert.Equal("1.19.0", build.Label);
        Assert.Equal(LibraryKind.Feeder, build.Kind);
        Assert.Equal(ZipUrl, Assert.Single(web.Urls));
        var pkg = FeederLibrary.PackageOf(build);
        Assert.Equal(new[] { "dlss5-feed.addon32", "dlss5-feed.addon64", "DLSS5_Feed.fx", "dlss5-feed-host64.exe" }.Order(),
            Directory.EnumerateFiles(pkg, "*", SearchOption.AllDirectories).Select(Path.GetFileName).Order());
        Assert.False(File.Exists(Path.Combine(build.Dir, Asset)));
        Assert.Equal(Tag, Assert.Single(FeederLibrary.Builds()).Id);
    }

    [Fact]
    public async Task Ensure_ADownloadThatDoesntMatchTheDigest_IsRefused_AndLeavesNothing()
    {
        var release = Known();
        var tampered = BuildZip();
        tampered[^30] ^= 0xFF;
        var library = new FeederLibrary(new GitHubClient(new FakeWeb(_ => Body(tampered))));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => library.EnsureAsync(release, null, default));

        Assert.Contains("SHA-256", ex.Message);
        Assert.Empty(FeederLibrary.Builds());
        Assert.False(Directory.Exists(Path.Combine(FeederLibrary.Root, Tag)));
    }

    [Fact]
    public async Task Ensure_AZipMissingAFile_IsRefusedWhole()
    {
        var odd = BuildZip(withHost: false);
        var release = FeederLibrary.Pick([Release(Tag, Digest(odd))], false)!;
        var library = new FeederLibrary(new GitHubClient(new FakeWeb(_ => Body(odd))));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => library.EnsureAsync(release, null, default));

        Assert.Contains("host64/dlss5-feed-host64.exe", ex.Message);
        Assert.Empty(FeederLibrary.Builds());
    }

    [Fact]
    public async Task Ensure_ABuildAlreadyInTheLibrary_IsNotDownloadedAgain()
    {
        var web = new FakeWeb(_ => Body(_zip));
        var library = new FeederLibrary(new GitHubClient(web));
        await library.EnsureAsync(Known(), null, default);

        await library.EnsureAsync(Known(), null, default);

        Assert.Single(web.Urls);
    }

    [Fact]
    public async Task Library_OrdersNewestFirst_AndChoosePrefersThePick()
    {
        foreach (var tag in new[] { "v1.17.0", "v1.19.0", "v1.18.0-beta.2" })
        {
            var dir = Path.Combine(FeederLibrary.Root, tag);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ".complete"), JsonSerializer.Serialize(new CachedComponent { Tag = tag, Fetched = DateTime.UtcNow }, JsonCtx.Default.CachedComponent));
        }

        Assert.Equal(["v1.19.0", "v1.18.0-beta.2", "v1.17.0"], FeederLibrary.Builds().Select(b => b.Id));
        Assert.Equal("v1.18.0-beta.2 beta".Replace("v", ""), FeederLibrary.Builds()[1].Label);
        Assert.Equal("v1.17.0", FeederLibrary.Choose("v1.17.0")!.Id);
        Assert.Equal("v1.19.0", FeederLibrary.Choose(null)!.Id);
        Assert.Equal("v1.19.0", FeederLibrary.Choose("gone")!.Id);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Store_EnsureFeeder_FollowsTheNewest_ButAPickMustBeInTheLibrary()
    {
        var releases = JsonSerializer.Serialize(new List<GhRelease> { Release(Tag, Digest(_zip)) }, JsonCtx.Default.ListGhRelease);
        var web = new FakeWeb(u => u.Contains("/releases?") ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releases, Encoding.UTF8, "application/json") } : Body(_zip));
        var store = new ComponentStore(new GitHubClient(web), () => false);

        var build = await store.EnsureFeederAsync(null, null, default);

        Assert.Equal(Tag, build.Id);
        Assert.Equal(Tag, (await store.EnsureFeederAsync(Tag, null, default)).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.EnsureFeederAsync("v9.9.9", null, default));
    }

    [Fact]
    public async Task Store_EnsureFeeder_OfflineUsesTheLibrary_AndExplainsWhenItIsEmpty()
    {
        var store = new ComponentStore(new GitHubClient(new FakeWeb(_ => throw new HttpRequestException("offline"))), () => false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.EnsureFeederAsync(null, null, default));
        Assert.Contains("GitHub can't be reached", ex.Message);

        var dir = Path.Combine(FeederLibrary.Root, Tag);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".complete"), JsonSerializer.Serialize(new CachedComponent { Tag = Tag }, JsonCtx.Default.CachedComponent));
        Assert.Equal(Tag, (await store.EnsureFeederAsync(null, null, default)).Id);
    }

    // ---------- the registration ----------

    [Fact]
    public void ItIsRegisteredOnceInTheGenericWay()
    {
        var def = ComponentRegistry.Find(ComponentRegistry.Feeder)!;

        Assert.Equal("DLSS5-Feeder", def.Name);
        Assert.Equal(LibraryKind.Feeder, def.Library);
        Assert.False(def.Legacy);
        Assert.True(def.OwnsFile("dlss5-feed.addon32"));
        Assert.True(def.OwnsFile(@"host64\dlss5-feed-host64.exe"));
        Assert.True(def.OwnsFile(@"reshade-shaders\Shaders\DLSS5_Feed.fx"));
        Assert.False(def.OwnsFile("deep-fried-chicken.addon64"));
        Assert.Contains(def, ComponentRegistry.OfLibrary(LibraryKind.Feeder));
        Assert.Equal("Feeder", def.Label);
    }

    // ---------- install and uninstall ----------

    private async Task<LibraryBuild> Cached() =>
        await new FeederLibrary(new GitHubClient(new FakeWeb(_ => Body(_zip)))).EnsureAsync(Known(), null, default);

    [Fact]
    public async Task Install_64Bit_PlacesTheAddonAndTheShader_AndRecordsThem()
    {
        var installer = new Installer(new ComponentStore(new GitHubClient(), () => false));
        var build = await Cached();
        var game = GameOf(_root, 64);

        await installer.InstallComponentAsync(game, _target, FeederLibrary.Plan(build, game, _target), default);

        Assert.Equal("ADDON64", File.ReadAllText(Path.Combine(_target, "dlss5-feed.addon64")));
        Assert.Equal("FX", File.ReadAllText(Path.Combine(_target, @"reshade-shaders\Shaders\DLSS5_Feed.fx")));
        Assert.False(File.Exists(Path.Combine(_target, "dlss5-feed.addon32")));
        Assert.False(Directory.Exists(Path.Combine(_target, "host64")));
        var c = InstallManifest.Read(_target)!.Component(ComponentRegistry.Feeder)!;
        Assert.Equal(Tag, c.BuildId);
        Assert.Equal("1.19.0", c.Version);
        Assert.Equal("64", c.Extras["bits"]);
        Assert.Equal(2, c.Files.Count);
        Assert.All(c.Files, f => Assert.Equal(FileUtil.Sha256(Path.Combine(_root, f.Path)), f.Sha256));
        Assert.Equal("Feeder", InstallManifest.Read(_target)!.ModLabel());
    }

    [Fact]
    public async Task Install_32Bit_PlacesAddon32_TheShader_AndTheHost64Helper_AndSaysWhatIsLeftToDoByHand()
    {
        var installer = new Installer(new ComponentStore(new GitHubClient(), () => false));
        var build = await Cached();
        var game = GameOf(_root, 32);

        await installer.InstallComponentAsync(game, _target, FeederLibrary.Plan(build, game, _target), default);

        Assert.Equal("ADDON32", File.ReadAllText(Path.Combine(_target, "dlss5-feed.addon32")));
        Assert.Equal("HOST64", File.ReadAllText(Path.Combine(_target, @"host64\dlss5-feed-host64.exe")));
        Assert.Equal("FX", File.ReadAllText(Path.Combine(_target, @"reshade-shaders\Shaders\DLSS5_Feed.fx")));
        Assert.False(File.Exists(Path.Combine(_target, "dlss5-feed.addon64")));
        var c = InstallManifest.Read(_target)!.Component(ComponentRegistry.Feeder)!;
        Assert.Equal("32", c.Extras["bits"]);
        Assert.Contains("64-bit ReShade", c.Extras["manual"]);
        Assert.Equal(3, c.Files.Count);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(32)]
    public async Task Uninstall_LeavesTheGameAsItWas_OriginalsBack(int bits)
    {
        var installer = new Installer(new ComponentStore(new GitHubClient(), () => false));
        var build = await Cached();
        var game = GameOf(_root, bits);
        // A copy of the add-on the user put there by hand, and the shader folder that was already there.
        File.WriteAllText(Path.Combine(_target, bits == 64 ? "dlss5-feed.addon64" : "dlss5-feed.addon32"), "USERS-OWN");
        Directory.CreateDirectory(Path.Combine(_target, @"reshade-shaders\Shaders"));
        File.WriteAllText(Path.Combine(_target, @"reshade-shaders\Shaders\Other.fx"), "OTHER");
        var before = Directory.EnumerateFiles(_target, "*", SearchOption.AllDirectories).Where(f => !f.Contains(InstallManifest.DirName)).Order().ToList();

        await installer.InstallComponentAsync(game, _target, FeederLibrary.Plan(build, game, _target), default);
        Assert.NotEqual("USERS-OWN", File.ReadAllText(Path.Combine(_target, bits == 64 ? "dlss5-feed.addon64" : "dlss5-feed.addon32")));
        var undo = await installer.RemoveComponentAsync(game, _target, ComponentRegistry.Feeder, default);

        Assert.True(undo.Clean);
        Assert.Equal(before, Directory.EnumerateFiles(_target, "*", SearchOption.AllDirectories).Where(f => !f.Contains(InstallManifest.DirName)).Order());
        Assert.Equal("USERS-OWN", File.ReadAllText(Path.Combine(_target, bits == 64 ? "dlss5-feed.addon64" : "dlss5-feed.addon32")));
        Assert.Null(InstallManifest.Read(_target));
    }

    [Fact]
    public async Task Update_ToANewBuild_ReplacesTheFilesInPlace()
    {
        var installer = new Installer(new ComponentStore(new GitHubClient(), () => false));
        var build = await Cached();
        var game = GameOf(_root, 64);
        await installer.InstallComponentAsync(game, _target, FeederLibrary.Plan(build, game, _target), default);

        var newer = BuildZipWith("ADDON64-NEW");
        var release = FeederLibrary.Pick([Release("v1.20.0", Digest(newer))], false)!;
        var second = await new FeederLibrary(new GitHubClient(new FakeWeb(_ => Body(newer)))).EnsureAsync(release, null, default);
        await installer.InstallComponentAsync(game, _target, FeederLibrary.Plan(second, game, _target), default);

        Assert.Equal("ADDON64-NEW", File.ReadAllText(Path.Combine(_target, "dlss5-feed.addon64")));
        Assert.Equal("v1.20.0", InstallManifest.Read(_target)!.Component(ComponentRegistry.Feeder)!.BuildId);
    }

    private static byte[] BuildZipWith(string addon64)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, content) in new[]
            {
                ("dlss5-feed.addon64", addon64), ("dlss5-feed.addon32", "A32"), ("host64/dlss5-feed-host64.exe", "H"), ("reshade-shaders/Shaders/DLSS5_Feed.fx", "FX"),
            })
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        return ms.ToArray();
    }

    [Fact]
    public void Plan_NamesTheBuildAndEveryFileTheComponentOwns()
    {
        var build = new LibraryBuild(LibraryKind.Feeder, Tag, "1.19.0", Tag, null, Path.Combine(_tmp, "b"), Downloaded: true);
        var def = ComponentRegistry.Find(ComponentRegistry.Feeder)!;

        foreach (var bits in new[] { 64, 32 })
        {
            var plan = FeederLibrary.Plan(build, GameOf(_root, bits), _target);
            Assert.Equal(Tag, plan.BuildId);
            Assert.All(plan.Files, f => Assert.True(def.OwnsFile(Path.GetRelativePath(_target, f.Dest))));
        }
    }
}
