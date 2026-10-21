using System.Net;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>What the rate-limit notice is built on: GitHubClient.RateLimit follows the answers of a fake handler.</summary>
public sealed class GitHubRateLimitTests : IDisposable
{
    private const string Repo = "someone/tool";
    private const string ReleasesJson = """[{"tag_name":"v2.0","assets":[]}]""";

    private sealed class FakeApi(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond());
    }

    public GitHubRateLimitTests()
    {
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "dlssu-ghrl-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(AppPaths.Root, true); } catch (IOException) { }
    }

    private static HttpResponseMessage Ok(string etag = "\"v1\"")
    {
        var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReleasesJson) };
        res.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return res;
    }

    private static async Task SeedCache() => await new GitHubClient(new FakeApi(() => Ok())).GetReleasesAsync(Repo, 10, default);

    [Fact]
    public async Task Forbidden_WithNoRequestsLeft_IsALimit_ResettingAtTheHeaderTime()
    {
        await SeedCache();
        var reset = DateTimeOffset.UtcNow.AddMinutes(20);
        var gh = new GitHubClient(new FakeApi(() =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.Forbidden);
            res.Headers.Add("X-RateLimit-Remaining", "0");
            res.Headers.Add("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString());
            return res;
        }));
        var raised = 0;
        gh.RateLimitChanged += () => raised++;

        var releases = await gh.GetReleasesAsync(Repo, 10, default);

        Assert.True(releases.Single().Stale);
        Assert.Equal(new GitHubRateLimit(DateTimeOffset.FromUnixTimeSeconds(reset.ToUnixTimeSeconds()), false), gh.RateLimit);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task TooManyRequests_WithRetryAfter_ResetsThatManySecondsFromNow_AndNotesTheToken()
    {
        await SeedCache();
        var gh = new GitHubClient(new FakeApi(() =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            res.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return res;
        })) { Token = "ghp_test" };
        var before = DateTimeOffset.UtcNow;

        await gh.GetReleasesAsync(Repo, 10, default);

        var limit = gh.RateLimit!.Value;
        Assert.True(limit.TokenUsed);
        Assert.InRange(limit.ResetAt!.Value, before.AddSeconds(120), DateTimeOffset.UtcNow.AddSeconds(120));
    }

    [Fact]
    public async Task ForbiddenThatIsNotARateLimit_ShowsNoNotice()
    {
        await SeedCache();
        var gh = new GitHubClient(new FakeApi(() => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"message":"Resource not accessible by personal access token"}"""),
        }));

        await gh.GetReleasesAsync(Repo, 10, default);

        Assert.Null(gh.RateLimit);
    }

    [Fact]
    public async Task ASuccessfulCall_ClearsTheLimit()
    {
        await SeedCache();
        var limited = true;
        var gh = new GitHubClient(new FakeApi(() =>
        {
            if (!limited) return Ok("\"v2\"");
            var res = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            res.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return res;
        }));
        await gh.GetReleasesAsync(Repo, 10, default);
        Assert.NotNull(gh.RateLimit);
        var raised = 0;
        gh.RateLimitChanged += () => raised++;

        limited = false;
        await gh.GetReleasesAsync(Repo, 10, default);

        Assert.Null(gh.RateLimit);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Describe_MentionsTheResetAndAnExistingToken()
    {
        var at = new DateTimeOffset(2026, 10, 21, 12, 32, 0, TimeSpan.Zero);

        Assert.Equal($"GitHub is limiting requests until {at.ToLocalTime():t}, showing cached versions.", new GitHubRateLimit(at, false).Describe());
        Assert.Contains("even with your token", new GitHubRateLimit(at, true).Describe());
        Assert.Equal("GitHub is limiting requests, showing cached versions.", new GitHubRateLimit(null, false).Describe());
    }
}
