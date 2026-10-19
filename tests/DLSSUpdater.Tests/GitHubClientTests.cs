using System.Net;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>GitHub API answers that aren't a plain 200: refused tokens, rate limits, outages. Fake handler, no network.</summary>
public sealed class GitHubClientTests : IDisposable
{
    private const string Repo = "someone/tool";
    private const string ReleasesJson = """[{"tag_name":"v2.0","assets":[]}]""";

    private sealed class FakeApi(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string?> Auth { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Auth) Auth.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(respond(request));
        }
    }

    private readonly List<string> _log = [];
    private readonly Action<string> _onLog;

    public GitHubClientTests()
    {
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "dlssu-gh-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.Ensure();
        _onLog = line => { lock (_log) _log.Add(line); };
        Log.Line += _onLog;
    }

    public void Dispose()
    {
        Log.Line -= _onLog;
        try { Directory.Delete(AppPaths.Root, true); } catch (IOException) { }
    }

    private static HttpResponseMessage Ok(string body, string etag = "\"v2\"")
    {
        var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        res.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return res;
    }

    /// <summary>Puts an older release list into the ETag cache, as an earlier successful check would have.</summary>
    private static async Task SeedCache()
    {
        var seed = new GitHubClient(new FakeApi(_ => Ok("""[{"tag_name":"v1.0","assets":[]}]""", "\"v1\"")));
        await seed.GetReleasesAsync(Repo, 10, default);
    }

    [Fact]
    public async Task ExpiredToken_IsDroppedWithOneWarning_AndNewReleasesStillArrive()
    {
        await SeedCache();
        var api = new FakeApi(r => r.Headers.Authorization is null ? Ok(ReleasesJson) : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var gh = new GitHubClient(api) { Token = "expired" };

        var first = await gh.GetReleasesAsync(Repo, 10, default);
        var second = await gh.GetReleasesAsync(Repo, 10, default);

        Assert.Equal("v2.0", Assert.Single(first).TagName);
        Assert.False(first[0].Stale);
        Assert.Equal("v2.0", Assert.Single(second).TagName);
        Assert.Equal(["expired", null, null], api.Auth);
        Assert.Single(_log, l => l.Contains("WARNING") && l.Contains("token"));
    }

    [Fact]
    public async Task ForbiddenWithoutRateLimitSigns_CountsAsRefusedToken()
    {
        var api = new FakeApi(r => r.Headers.Authorization is null ? Ok(ReleasesJson) : new HttpResponseMessage(HttpStatusCode.Forbidden));
        var gh = new GitHubClient(api) { Token = "no-access" };
        Assert.Equal("v2.0", Assert.Single(await gh.GetReleasesAsync(Repo, 10, default)).TagName);
        Assert.Equal(["no-access", null], api.Auth);
    }

    [Fact]
    public async Task NewToken_IsTriedAgain()
    {
        var api = new FakeApi(r => r.Headers.Authorization?.Parameter == "good" || r.Headers.Authorization is null
            ? Ok(ReleasesJson) : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var gh = new GitHubClient(api) { Token = "bad" };
        await gh.GetReleasesAsync(Repo, 10, default);
        gh.Token = "good";
        await gh.GetReleasesAsync(Repo, 10, default);
        Assert.Equal(["bad", null, "good"], api.Auth);
    }

    [Fact]
    public async Task RateLimit_KeepsTheToken_AndServesTheCacheAsStale()
    {
        await SeedCache();
        var api = new FakeApi(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.Forbidden);
            res.Headers.Add("X-RateLimit-Remaining", "0");
            return res;
        });
        var gh = new GitHubClient(api) { Token = "fine" };

        var releases = await gh.GetReleasesAsync(Repo, 10, default);

        Assert.Equal("v1.0", Assert.Single(releases).TagName);
        Assert.True(releases[0].Stale);
        Assert.Equal(["fine"], api.Auth);
        Assert.DoesNotContain(_log, l => l.Contains("WARNING"));
    }

    [Fact]
    public async Task NotModified_IsFresh()
    {
        await SeedCache();
        var gh = new GitHubClient(new FakeApi(_ => new HttpResponseMessage(HttpStatusCode.NotModified)));
        var releases = await gh.GetReleasesAsync(Repo, 10, default);
        Assert.Equal("v1.0", Assert.Single(releases).TagName);
        Assert.False(releases[0].Stale);
    }

    [Fact]
    public async Task ServerError_ServesTheCacheAsStale_AndTheStoreIsNotOnline()
    {
        var offline = false;
        var gh = new GitHubClient(new FakeApi(r =>
        {
            var url = r.RequestUri!.ToString();
            if (offline) return new HttpResponseMessage(HttpStatusCode.BadGateway);
            if (url.Contains(ComponentStore.MfgRepo + "/releases"))
                return Ok($$"""[{"tag_name":"2.0","assets":[{"name":"{{ComponentStore.MfgFile}}","browser_download_url":"https://example.invalid/a"}]}]""");
            return url.StartsWith("https://api.github.com/") ? Ok("[]") : new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var store = new ComponentStore(gh, () => true);
        await store.RefreshAsync(default);
        Assert.False(store.Mfg!.Stale);

        offline = true;
        await store.RefreshAsync(default);
        Assert.Equal("2.0", store.Mfg!.Tag);
        Assert.True(store.Mfg.Stale);
        Assert.False(store.Mfg.FromCache);
        Assert.Single(store.Mfg.Files);
    }
}

/// <summary>
/// Answers every request with a sign-in page, the way a captive portal does: nothing usable arrives, so the store falls
/// back to what it downloaded before.
/// </summary>
internal sealed class CaptivePortal : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Sign in to continue</html>") });
}
