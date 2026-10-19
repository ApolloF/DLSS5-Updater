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
    public async Task MaintainedSource_UsesApiCache_AndFallsBackToCacheOffline()
    {
        var offline = false;
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
            if (url.StartsWith("https://api.github.com/")) return Json("[]");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var store = new ComponentStore(new GitHubClient(handler), () => true);
        await store.RefreshAsync(default);
        Assert.Equal("v0.9.51", store.Opti?.Tag);
        offline = true;
        await store.RefreshAsync(default);
        Assert.Equal("v0.9.51", store.Opti?.Tag);
        Assert.DoesNotContain(handler.Requests, r => r.Contains(ComponentStore.LegacyOptiRepo));
        Assert.DoesNotContain(handler.Requests, r => r.Contains("nvngx_dlssnr.dll"));
    }

    private const string F5Release =
        """[{"tag_name":"v0.1.32","prerelease":false,"assets":[""" +
        """{"name":"Optional-Vendor-Neutral-DLSS5-Forwarder.zip","browser_download_url":"https://example.invalid/fwd.zip"},""" +
        """{"name":"OptiScaler-DLSSNR-F5-0.1.32.zip","browser_download_url":"https://example.invalid/f5.zip"}]}]""";

    private static FakeGitHub F5Github(Func<bool> offline) => new(r =>
    {
        var url = r.RequestUri!.ToString();
        if (offline()) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        if (url.Contains(OptiSources.F5.Repo + "/releases")) return Json(F5Release);
        if (url.StartsWith("https://api.github.com/")) return Json("[]");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    [Fact]
    public async Task F5Source_PicksTheF5Zip_AndSuffixesTheTag()
    {
        var store = new ComponentStore(new GitHubClient(F5Github(() => false)), () => true) { OptiSourceRepo = () => OptiSources.F5.Repo };
        await store.RefreshAsync(default);
        Assert.Equal("v0.1.32-f5", store.Opti?.Tag);
        Assert.Equal("OptiScaler-DLSSNR-F5-0.1.32.zip", store.Opti!.Files[0].Name);
        Assert.Equal(new Version(0, 1, 32), store.Opti.Version);
    }

    [Fact]
    public void CompatFlagsManagedKeysWhoseDocumentationChanged()
    {
        const string tested = "[DlssNr]\n; 0 manual, 1 game exposure. Default 0.\nWhitePointSource=auto\n\n; Strength\nIntensity=auto\n; Unmanaged\nOther=auto\n";
        const string release = "[DlssNr]\r\n; 0 manual, 1 game exposure,\r\n;   2 scanned. Default 1.\r\nWhitePointSource=auto\r\n\r\n;   Strength  \r\nIntensity=auto\r\n; Unmanaged, reworded\r\nOther=auto\r\n";
        var then = OptiCompat.KeyDocs(tested);
        Assert.Equal("0 manual, 1 game exposure. Default 0.", then[("dlssnr", "whitepointsource")]);
        var changed = OptiCompat.ChangedDocs(then, OptiCompat.KeyDocs(release), [("DlssNr", "WhitePointSource"), ("DlssNr", "Intensity"), ("DlssNr", "Gone")]);
        Assert.Equal(["[DlssNr] WhitePointSource"], changed);
        var report = new CompatReport("v0.9.60", [], false, "v0.9.51") { ChangedKeys = changed };
        Assert.False(report.HasIssues);
        Assert.Contains("WhitePointSource", report.Summary);
    }

    [Fact]
    public void EveryOption_HasHelpAndAdvice_AndStaysInsideTheModsValidRange()
    {
        var all = DLSSUpdater.ViewModels.SettingsOptions.ReShade([]).Concat(DLSSUpdater.ViewModels.SettingsOptions.Mfg([]))
            .Concat(DLSSUpdater.ViewModels.SettingsOptions.RenoDx([])).Concat(DLSSUpdater.ViewModels.SettingsOptions.Lecram([])).Concat(DLSSUpdater.ViewModels.SettingsOptions.Nr([]))
            .Concat(DLSSUpdater.ViewModels.SettingsOptions.Tonemap([])).Concat(DLSSUpdater.ViewModels.SettingsOptions.HdrOutput([]));
        foreach (var o in all)
        {
            Assert.True(DLSSUpdater.ViewModels.HelpTopics.Get(o.Topic) is not null, $"{o.Key} has no help topic");
            Assert.True(DLSSUpdater.ViewModels.HelpTopics.AdviceFor(o.Topic!) is not null, $"{o.Key} has no advice");
        }
        int[] Values(string key) => DLSSUpdater.ViewModels.SettingsOptions.Mfg([]).Single(o => o.Key == key).Choices
            .Where(c => c.Value is not null).Select(c => int.Parse(c.Value!)).ToArray();
        // Ranges from mavismmg/MFGAdaUnlock-RenoDx addon.cpp: MaxCount 2..5, VRR headroom 50..300 bp, boundary mode 0..2.
        Assert.All(Values("MaxCount"), v => Assert.InRange(v, 2, 5));
        Assert.All(Values("ReflexVrrHeadroomBasisPoints"), v => Assert.InRange(v, 50, 300));
        Assert.All(Values("BoundaryArtifactMitigationMode"), v => Assert.InRange(v, 0, 2));
        Assert.All(Values("ForceMultiplier"), v => Assert.InRange(v, 2, 6));
    }

    [Fact]
    public void OptionVariants_FollowTheChosenBuild()
    {
        var overrides = new System.Collections.ObjectModel.ObservableCollection<IniOverride> { new("DlssNr", "ReversibleMode", "6") };
        var mode = DLSSUpdater.ViewModels.SettingsOptions.Tonemap(overrides).Single(o => o.Key == "ReversibleMode");
        var before = DLSSUpdater.ViewModels.SettingsOptions.Nr([]).Single(o => o.Key == "RunBeforeSR");
        var denoise = DLSSUpdater.ViewModels.SettingsOptions.Nr([]).Single(o => o.Key == "DenoiseFirst");
        Assert.Contains("custom", mode.Selected.Label);

        mode.ApplySource(OptiSources.F5); before.ApplySource(OptiSources.F5); denoise.ApplySource(OptiSources.F5);
        Assert.Equal("PQ composed", mode.Selected.Label);
        Assert.Contains("Ray Reconstruction", before.Hint);
        Assert.False(denoise.IsAvailable);

        mode.ApplySource(OptiSources.Default); before.ApplySource(OptiSources.Default); denoise.ApplySource(OptiSources.Default);
        Assert.Contains("custom", mode.Selected.Label);
        Assert.Equal(5, mode.Choices.Length - 1);
        Assert.DoesNotContain("Ray Reconstruction", before.Hint);
        Assert.True(denoise.IsAvailable);
        Assert.Equal("6", overrides.Single().Value);
    }

    [Fact]
    public void OptiSources_OfTagAndRawTag_RoundTrip_AndUnknownRepoIsDefault()
    {
        Assert.Same(OptiSources.F5, OptiSources.OfTag("v0.1.32-f5"));
        Assert.Same(OptiSources.Default, OptiSources.OfTag("v0.9.51"));
        Assert.Same(OptiSources.Default, OptiSources.OfTag(null));
        Assert.Equal("v0.1.32", OptiSources.RawTag("v0.1.32-f5"));
        Assert.Equal("v0.9.51", OptiSources.RawTag("v0.9.51"));
        Assert.Same(OptiSources.F5, OptiSources.For("JanBlade/OptiScaler-F5-DLSSNR-Multipass"));
        Assert.Same(OptiSources.Default, OptiSources.For("someone/else"));
        Assert.Same(OptiSources.Default, OptiSources.For(null));
    }

    [Fact]
    public async Task Offline_OnlyReturnsACachedReleaseOfTheCurrentSource()
    {
        ComponentStore.MarkComplete(Component.OptiScaler, new ReleaseInfo { Tag = "v0.9.51" });
        var offline = true;
        var repo = (string?)null;
        var store = new ComponentStore(new GitHubClient(F5Github(() => offline)), () => true) { OptiSourceRepo = () => repo };
        await store.RefreshAsync(default);
        Assert.Equal("v0.9.51", store.Opti?.Tag);

        repo = OptiSources.F5.Repo;
        await store.RefreshAsync(default);
        Assert.Null(store.Opti);

        ComponentStore.MarkComplete(Component.OptiScaler, new ReleaseInfo { Tag = "v0.1.32-f5" });
        await store.RefreshAsync(default);
        Assert.Equal("v0.1.32-f5", store.Opti?.Tag);
    }

    [Fact]
    public void SavedUnknownOptiSource_FallsBackToDefault()
    {
        FileUtil.AtomicWriteText(AppPaths.SettingsFile, """{"OptiSource":"someone/else"}""");
        Assert.Same(OptiSources.Default, OptiSources.For(AppSettings.Load().OptiSource));
    }

    [Fact]
    public async Task Refresh_CancelledByTheUser_StopsInsteadOfFallingBackToTheCache()
    {
        var handler = new FakeGitHub(_ => Json("[]"));
        var store = new ComponentStore(new GitHubClient(handler), () => true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RefreshAsync(cts.Token));
    }

    [Fact]
    public async Task ReShade_SiteTimeout_FallsBackToGitHubTags()
    {
        var handler = new FakeGitHub(r =>
        {
            var url = r.RequestUri!.ToString();
            if (url.StartsWith(ComponentStore.ReShadeSite)) throw new TaskCanceledException("timed out");
            if (url.Contains(ComponentStore.ReShadeRepo + "/tags")) return Json("""[{"name":"v6.4.1"},{"name":"v6.3.3"}]""");
            if (url.StartsWith("https://api.github.com/")) return Json("[]");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var store = new ComponentStore(new GitHubClient(handler), () => true);
        await store.RefreshAsync(default);
        Assert.Equal("6.4.1", store.ReShade?.Tag);
    }
}
