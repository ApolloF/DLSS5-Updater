using System.Net;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Download checks against a fake GitHub: no request leaves the machine.</summary>
public class ComponentStoreTests
{
    private sealed class FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add($"{request.Method} {request.RequestUri}");
            return Task.FromResult(respond(request));
        }
    }

    public ComponentStoreTests()
    {
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "dlssu-store-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.Ensure();
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    /// <summary>MFG Unlock's repo answers with <paramref name="mfgReleases"/>, every other API call with an empty list.</summary>
    private static FakeGitHub Releases(string mfgReleases, string dlssReleases = "[]", HttpStatusCode head = HttpStatusCode.OK) => new(r =>
    {
        var url = r.RequestUri!.ToString();
        if (r.Method == HttpMethod.Head) return new HttpResponseMessage(head);
        if (url.Contains(ComponentStore.MfgRepo + "/releases")) return Json(mfgReleases);
        if (url.Contains(ComponentStore.DlssRepo + "/releases")) return Json(dlssReleases);
        if (url.StartsWith("https://api.github.com/")) return Json("[]");
        if (url.EndsWith(".addon64")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    [Fact]
    public async Task Mfg_AnyOtherAddonInTheRelease_IsNotAccepted()
    {
        var gh = Releases("""[{"tag_name":"2.0","assets":[{"name":"something-else.addon64","browser_download_url":"https://example.invalid/something-else.addon64","digest":null}]}]""");
        var store = new ComponentStore(new GitHubClient(gh), () => true);
        await store.RefreshAsync(default);
        Assert.Null(store.Mfg);
    }

    [Fact]
    public async Task Mfg_AssetWithoutDigest_IsRefused()
    {
        var gh = Releases($$"""[{"tag_name":"2.0","assets":[{"name":"{{ComponentStore.MfgFile}}","browser_download_url":"https://example.invalid/{{ComponentStore.MfgFile}}"}]}]""");
        var store = new ComponentStore(new GitHubClient(gh), () => true);
        await store.RefreshAsync(default);
        Assert.Equal("2.0", store.Mfg?.Tag);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureMfgAsync(null, null, default));
        Assert.False(ComponentStore.IsCached(Component.MfgUnlock, "2.0"));
        Assert.False(File.Exists(Path.Combine(ComponentStore.TagDir(Component.MfgUnlock, "2.0"), ComponentStore.MfgFile)));
    }

    [Fact]
    public async Task Dlss_PinnedVersion_RateLimitedCheckFails_InsteadOfSkippingFiles()
    {
        var store = new ComponentStore(new GitHubClient(Releases("[]", head: HttpStatusCode.TooManyRequests)), () => true);
        await Assert.ThrowsAsync<HttpRequestException>(() => store.EnsureDlssAsync("v310.1.0", null, default));
        Assert.False(ComponentStore.IsCached(Component.Dlss, "v310.1.0"));
    }

    [Fact]
    public async Task Dlss_Latest_RateLimitedCheck_NeverFallsBackToMain()
    {
        var gh = Releases("[]", dlssReleases: """[{"tag_name":"v310.2.0","assets":[]}]""", head: HttpStatusCode.ServiceUnavailable);
        var store = new ComponentStore(new GitHubClient(gh), () => true);
        await store.RefreshAsync(default);
        Assert.Equal("v310.2.0", store.Dlss?.Tag);

        await Assert.ThrowsAsync<HttpRequestException>(() => store.EnsureDlssAsync(null, null, default));
        Assert.DoesNotContain(gh.Requests, r => r.Contains("/main/"));
        Assert.False(ComponentStore.IsCached(Component.Dlss, "v310.2.0"));
    }

    [Fact]
    public async Task MaintainedSourceAndRenoDx_UseApiCache_KeepMutableSnapshotsSeparate()
    {
        var offline = false;
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 })).ToLowerInvariant();
        var handler = new FakeGitHub(r =>
        {
            var url = r.RequestUri!.ToString();
            if (offline) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (url.Contains(ComponentStore.OptiRepo + "/releases"))
            {
                var result = Json("""[{"tag_name":"v0.9.51","assets":[{"name":"OptiScaler-NR-v0.9.51.zip","browser_download_url":"https://example.invalid/package.zip"}]}]""");
                result.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"opti\"");
                return result;
            }
            if (url.Contains(ComponentStore.RenoDxRepo + "/releases"))
            {
                var result = Json($$"""[{"tag_name":"snapshot","assets":[{"name":"{{ComponentStore.RenoDxFile}}","browser_download_url":"https://example.invalid/renodx.addon64","digest":"sha256:{{sha}}"}]}]""");
                result.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"reno\"");
                return result;
            }
            if (url.EndsWith("renodx.addon64")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            if (url.StartsWith("https://api.github.com/")) return Json("[]");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var store = new ComponentStore(new GitHubClient(handler), () => true);
        await store.RefreshAsync(default);
        Assert.Equal("v0.9.51", store.Opti?.Tag);
        Assert.Equal("snapshot-" + sha, store.RenoDx?.Tag);
        var addon = await store.EnsureRenoDxAsync(null, default);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(addon));
        offline = true;
        await store.RefreshAsync(default);
        Assert.Equal("v0.9.51", store.Opti?.Tag);
        Assert.Equal(addon, await store.EnsureRenoDxAsync(null, default));
        Assert.DoesNotContain(handler.Requests, r => r.Contains(ComponentStore.LegacyOptiRepo));
        Assert.DoesNotContain(handler.Requests, r => r.Contains("nvngx_dlssnr.dll"));
    }

    [Fact]
    public void RenoDxParser_RejectsOtherAssetsAndDrafts_RecognizesBothNamesAndChangedDigest()
    {
        GhRelease Release(string name, string tag, string digest, bool draft = false) => new()
        {
            TagName = tag, Draft = draft,
            Assets = [new GhAsset { Name = name, Url = "https://example.invalid/addon", Digest = "sha256:" + digest }],
        };
        var sha = new string('a', 64);
        Assert.Null(ComponentStore.ParseRenoDxRelease([Release("renodx-dlssfix.addon32", "snapshot", sha)]));
        Assert.Null(ComponentStore.ParseRenoDxRelease([Release(ComponentStore.RenoDxFile, "unrelated", sha)]));
        Assert.Null(ComponentStore.ParseRenoDxRelease([Release(ComponentStore.RenoDxFile, "snapshot", sha, true)]));
        Assert.Null(ComponentStore.ParseRenoDxRelease([Release(ComponentStore.RenoDxFile, "snapshot", "")]));
        var first = ComponentStore.ParseRenoDxRelease([Release(ComponentStore.RenoDxFile, "snapshot", sha)])!;
        var second = ComponentStore.ParseRenoDxRelease([Release("renodx-dlss.addon64", "snapshot", new string('b', 64))])!;
        Assert.NotEqual(first.Tag, second.Tag);
        Assert.Equal(ComponentStore.RenoDxFile, second.Files[0].Name);
        Assert.NotNull(ComponentStore.ParseRenoDxRelease([Release(ComponentStore.RenoDxFile, "nightly-20261017", sha)]));
    }
}
