using System.Net;
using System.Net.Http.Headers;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>CatalogClient: parsing a catalog.json, the ETag-cached fetch through GitHubClient, and falling back to the last good copy. Fake handler, no network.</summary>
public sealed class CatalogTests : IDisposable
{
    private sealed class FakeApi(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private readonly string _previousRoot = AppPaths.Root;

    public CatalogTests()
    {
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "dlssu-catalog-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        var root = AppPaths.Root;
        AppPaths.Root = _previousRoot;
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private static HttpResponseMessage Ok(string body, string etag = "\"c1\"")
    {
        var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        res.Headers.ETag = new EntityTagHeaderValue(etag);
        return res;
    }

    private static CatalogClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond, out FakeApi api)
    {
        api = new FakeApi(respond);
        return new CatalogClient(new GitHubClient(api));
    }

    [Fact]
    public void Fixture_parses_into_the_renodx_model()
    {
        var r = CatalogFixtures.Renodx();

        Assert.Equal(6, r.Mods.Count);
        var hades = r.Mods.Single(m => m.Id == "hades");
        Assert.Equal([1145360], hades.SteamAppIds);
        Assert.Equal("working", hades.Compat);
        var file = Assert.Single(hades.Files);
        Assert.Equal(new string('d', 64), file.Sha256);
        Assert.Equal(7, file.Size);
        Assert.Equal("2026-10-20-ddddddd", file.Version);
        Assert.Equal("2026-10-01-1111111", Assert.Single(file.History).Version);
        Assert.Equal("https://x.test/hades", file.DownloadUrl);
        Assert.True(file.IsInstallable);
    }

    [Fact]
    public void A_mirrored_file_downloads_from_the_mirror_and_a_pinned_one_from_upstream()
    {
        var r = CatalogFixtures.Renodx();

        var generic = r.GenericFor("unreal")!.Files.Single();
        Assert.StartsWith("https://github.com/ApolloF/DLSS5-Updater-catalog/releases/download/mirror-2026-10/", generic.DownloadUrl);
        Assert.Equal("https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-unrealengine.addon64", generic.Url);
        Assert.Equal(["x64", "x86"], r.GenericFor("unity")!.Files.Select(f => f.Arch).ToArray());
    }

    [Fact]
    public void Generic_marker_blocked_flag_engine_games_and_overrides_are_read()
    {
        var r = CatalogFixtures.Renodx();

        Assert.Equal("unity", r.Mods.Single(m => m.Id == "deaths-door").Generic);
        var crashy = r.Mods.Single(m => m.Id == "crashy");
        Assert.True(crashy.Blocked);
        Assert.Equal("crashes on start", crashy.BlockReason);
        Assert.Equal("Engine.ini", Assert.Single(r.EngineGames).Notes);
        Assert.True(r.Overrides.OptsOutOfUeExtended(4242, "whatever"));
        Assert.True(r.Overrides.OptsOutOfUeExtended(null, "SOME game"));
        Assert.False(r.Overrides.OptsOutOfUeExtended(1, "Other"));
        Assert.Equal("Game\\Saved\\Config\\Windows\\Engine.ini", r.Overrides.EngineIniPath(4242, "x"));
        Assert.Null(r.Overrides.EngineIniPath(1, "x"));
    }

    [Fact]
    public void An_entry_without_a_hash_is_not_installable()
    {
        var file = new CatalogBinary { Name = "a.addon64", Url = "https://x.test/a" };

        Assert.False(file.IsInstallable);
        Assert.False(new CatalogBinary { Url = "https://x.test/a", Sha256 = "abc" }.IsInstallable);
    }

    [Fact]
    public async Task Online_load_returns_the_catalog_and_keeps_a_copy_under_app_paths()
    {
        var client = Client(r => Ok(CatalogFixtures.Json()), out var api);

        var load = await client.LoadAsync(default);

        Assert.Equal(CatalogOrigin.Online, load.Origin);
        Assert.Null(load.Note);
        Assert.Equal(6, load.Document!.Sections.Renodx!.Mods.Count);
        Assert.Equal(CatalogFixtures.Json(), File.ReadAllText(AppPaths.CatalogFile));
        var req = Assert.Single(api.Requests);
        Assert.Equal("https://api.github.com/repos/ApolloF/DLSS5-Updater-catalog/contents/catalog.json", req.RequestUri!.ToString());
        Assert.Contains("application/vnd.github.raw+json", req.Headers.Accept.ToString());
    }

    [Fact]
    public async Task A_second_load_is_conditional_and_a_304_still_gives_the_catalog()
    {
        var client = Client(r => r.Headers.IfNoneMatch.Count > 0 ? new HttpResponseMessage(HttpStatusCode.NotModified) : Ok(CatalogFixtures.Json()), out var api);
        await client.LoadAsync(default);

        var again = await client.LoadAsync(default);

        Assert.Equal(CatalogOrigin.Online, again.Origin);
        Assert.Equal(6, again.Document!.Sections.Renodx!.Mods.Count);
        Assert.Equal("\"c1\"", api.Requests[1].Headers.IfNoneMatch.Single().ToString());
    }

    [Fact]
    public async Task Offline_after_an_earlier_success_serves_the_last_good_copy()
    {
        var up = true;
        var client = Client(r => up ? Ok(CatalogFixtures.Json()) : throw new HttpRequestException("no network"), out _);
        await client.LoadAsync(default);
        up = false;
        foreach (var f in Directory.GetFiles(AppPaths.ApiCacheDir)) File.Delete(f);

        var load = await client.LoadAsync(default);

        Assert.Equal(CatalogOrigin.Cached, load.Origin);
        Assert.Contains("unreachable", load.Note);
        Assert.Equal(6, load.Document!.Sections.Renodx!.Mods.Count);
    }

    [Fact]
    public async Task Offline_with_only_the_etag_cache_is_marked_cached()
    {
        var up = true;
        var client = Client(r => up ? Ok(CatalogFixtures.Json()) : throw new HttpRequestException("no network"), out _);
        await client.LoadAsync(default);
        up = false;

        var load = await client.LoadAsync(default);

        Assert.Equal(CatalogOrigin.Cached, load.Origin);
        Assert.NotNull(load.Document);
    }

    [Fact]
    public async Task Offline_without_any_copy_returns_nothing_and_does_not_throw()
    {
        var client = Client(r => throw new HttpRequestException("no network"), out _);

        var load = await client.LoadAsync(default);

        Assert.Equal(CatalogOrigin.None, load.Origin);
        Assert.Null(load.Document);
        Assert.Contains("no earlier copy", load.Note);
    }

    [Fact]
    public async Task A_rate_limited_or_missing_repo_without_a_copy_does_not_throw()
    {
        var limited = Client(r => new HttpResponseMessage(HttpStatusCode.Forbidden), out _);
        var missing = Client(r => new HttpResponseMessage(HttpStatusCode.NotFound), out _);

        Assert.Equal(CatalogOrigin.None, (await limited.LoadAsync(default)).Origin);
        Assert.Equal(CatalogOrigin.None, (await missing.LoadAsync(default)).Origin);
    }

    [Fact]
    public async Task A_damaged_online_catalog_keeps_the_last_good_copy()
    {
        var body = CatalogFixtures.Json();
        var client = Client(r => Ok(body, "\"c" + Guid.NewGuid().ToString("N")[..4] + "\""), out _);
        await client.LoadAsync(default);
        body = "{ this is not json";

        var load = await client.LoadAsync(default);

        Assert.Equal(CatalogOrigin.Cached, load.Origin);
        Assert.Contains("unusable", load.Note);
        Assert.Equal(6, load.Document!.Sections.Renodx!.Mods.Count);
        Assert.Equal(CatalogFixtures.Json(), File.ReadAllText(AppPaths.CatalogFile));
    }

    [Fact]
    public async Task A_catalog_with_a_newer_schema_is_refused_and_the_copy_stays()
    {
        var body = CatalogFixtures.Json();
        var client = Client(r => Ok(body, "\"c" + Guid.NewGuid().ToString("N")[..4] + "\""), out _);
        await client.LoadAsync(default);
        body = CatalogFixtures.Json("2");

        var load = await client.LoadAsync(default);

        Assert.Equal(CatalogOrigin.Cached, load.Origin);
        Assert.Contains("newer", load.Note);
        Assert.Equal(1, load.Document!.Schema);
    }

    [Fact]
    public async Task A_catalog_with_no_schema_version_is_unusable()
    {
        var client = Client(r => Ok("""{ "sections": {} }"""), out _);

        var load = await client.LoadAsync(default);

        Assert.Equal(CatalogOrigin.None, load.Origin);
        Assert.False(File.Exists(AppPaths.CatalogFile));
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var client = Client(r => Ok(CatalogFixtures.Json()), out _);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.LoadAsync(cts.Token));
    }

    [Fact]
    public async Task LoadCached_reads_the_saved_copy_without_a_request()
    {
        var client = Client(r => Ok(CatalogFixtures.Json()), out var api);
        await client.LoadAsync(default);
        var offline = new CatalogClient(new GitHubClient(new FakeApi(_ => throw new InvalidOperationException("no request expected"))));

        var load = offline.LoadCached();

        Assert.Equal(CatalogOrigin.Cached, load.Origin);
        Assert.Equal(6, load.Document!.Sections.Renodx!.Mods.Count);
        Assert.Single(api.Requests);
    }

    [Fact]
    public void LoadCached_without_a_copy_or_with_a_damaged_one_is_none()
    {
        var client = new CatalogClient(new GitHubClient(new FakeApi(_ => throw new InvalidOperationException())));
        Assert.Equal(CatalogOrigin.None, client.LoadCached().Origin);

        File.WriteAllText(AppPaths.CatalogFile, "garbage");

        Assert.Equal(CatalogOrigin.None, client.LoadCached().Origin);
    }

    [Fact]
    public async Task Sections_from_later_workstreams_do_not_break_parsing()
    {
        var client = Client(r => Ok(CatalogFixtures.Json()), out _);

        var load = await client.LoadAsync(default);

        Assert.NotNull(load.Document!.Sections.Renodx);
    }
}
