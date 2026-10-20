using System.Net;
using System.Security.Cryptography;
using System.Text;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Self-update of DLSS5 Updater: release picking, assets, checksums, throttle, offline, portable swap. No network, temp dirs only.</summary>
public sealed class AppUpdateTests : IDisposable
{
    private const string Download = "https://github.com/" + AppUpdate.Repo + "/releases/download/";
    private readonly string _dir;

    public AppUpdateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dlssu-selfupd-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.Root = Path.Combine(_dir, "data");
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static GhAsset Asset(string tag, string name, long size = 0, string? digest = null) =>
        new() { Name = name, Url = Download + tag + "/" + name, Size = size, Digest = digest };

    private static GhRelease Release(string tag, bool pre = false, bool draft = false, params GhAsset[] assets) =>
        new() { TagName = tag, Prerelease = pre, Draft = draft, Body = "## Notes\n- **Fixed** a [thing](https://x)", Assets = [.. assets] };

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    // ---------- private build ----------

    [Fact]
    public void CanSelfInstall_OnlyInThePublicBuild()
    {
        Assert.True(AppUpdate.CanSelfInstall(privateBuild: false));
        Assert.False(AppUpdate.CanSelfInstall(privateBuild: true));
    }

    [Fact]
    public void PrivateBuildNotice_NamesTheVersionAndWhereThePrivateBuildIs()
    {
        var r = AppUpdate.Newest([Release("v2.2.0")], new Version(2, 1, 0))!;
        var text = AppUpdate.PrivateBuildNotice(r);
        Assert.StartsWith("Private build: v2.2.0 is out.", text);
        Assert.Contains("private-build/", text);
        Assert.EndsWith("tools/build-private.ps1).", text);
        Assert.Equal("dlss5updater-drop/private-build/", AppUpdate.PrivateBuildFolder);
    }

    // ---------- versions ----------

    [Fact]
    public void Newest_ComparesVersionsNumerically_NotAsText()
    {
        var releases = new[] { Release("v1.9.0"), Release("v1.12.0"), Release("v1.10.3") };
        Assert.Equal(new Version(1, 12, 0), AppUpdate.Newest(releases, new Version(1, 9, 0))!.Version);
    }

    [Fact]
    public void Newest_SkipsPrereleasesDraftsAndSuffixedTags()
    {
        var releases = new[] { Release("v3.0.0", pre: true), Release("v2.9.0", draft: true), Release("v2.8.0-beta"), Release("v2.1.0"), Release("nightly") };
        Assert.Equal("v2.1.0", AppUpdate.Newest(releases, new Version(2, 0, 0))!.Tag);
    }

    [Fact]
    public void Newest_IsNull_WhenNothingIsNewer()
    {
        Assert.Null(AppUpdate.Newest([Release("v2.0.0"), Release("v1.12.0")], new Version(2, 0, 0)));
        Assert.Null(AppUpdate.Newest([], new Version(2, 0, 0)));
    }

    [Theory]
    [InlineData("v2.1", 2, 1, 0)]
    [InlineData("2.1.0.0", 2, 1, 0)]
    [InlineData("V1.12.3", 1, 12, 3)]
    public void ParseVersion_NormalizesToThreeParts(string tag, int major, int minor, int build) =>
        Assert.Equal(new Version(major, minor, build), AppUpdate.ParseVersion(tag));

    [Fact]
    public void Current_IsTheAssemblyVersion() =>
        Assert.Equal(3, AppUpdate.Current.ToString().Split('.').Length);

    // ---------- assets ----------

    [Fact]
    public void PackageAsset_PicksSetupForInstalled_AndExeForPortable()
    {
        var r = AppUpdate.Newest([Release("v2.1.0", false, false, Asset("v2.1.0", "DLSS5Updater.exe"), Asset("v2.1.0", "DLSS5Updater-Setup-2.1.0.exe"))], new Version(2, 0, 0))!;
        Assert.Equal("DLSS5Updater-Setup-2.1.0.exe", AppUpdate.PackageAsset(r, AppInstallKind.Installed)!.Name);
        Assert.Equal("DLSS5Updater.exe", AppUpdate.PackageAsset(r, AppInstallKind.Portable)!.Name);
    }

    [Theory]
    [InlineData("https://github.com/ApolloF/DLSS5-Updater/releases/download/v2.1.0/DLSS5Updater.exe")]
    [InlineData("https://github.com/ApolloF/dlss5updater/releases/download/v2.1.0/DLSS5Updater.exe")]
    public void PackageAsset_AcceptsTheRepoUnderItsCurrentAndFormerName(string url)
    {
        var rel = Release("v2.1.0", false, false, new GhAsset { Name = "DLSS5Updater.exe", Url = url });
        var r = AppUpdate.Newest([rel], new Version(2, 0, 0))!;
        Assert.NotNull(AppUpdate.PackageAsset(r, AppInstallKind.Portable));
    }

    [Theory]
    [InlineData("http://github.com/" + AppUpdate.Repo + "/releases/download/v2.1.0/DLSS5Updater.exe")]
    [InlineData("https://github.com/ApolloF/dlssupdater/releases/download/v2.1.0/DLSS5Updater.exe")]
    [InlineData("https://github.com/someone/fork/releases/download/v2.1.0/DLSS5Updater.exe")]
    [InlineData("https://evil.example/" + AppUpdate.Repo + "/releases/download/v2.1.0/DLSS5Updater.exe")]
    [InlineData("https://github.com/" + AppUpdate.Repo + "/releases/download/v2.0.0/DLSS5Updater.exe")]
    public void PackageAsset_RefusesUrlsThatAreNotThisReleasesOwn(string url)
    {
        var rel = Release("v2.1.0", false, false, new GhAsset { Name = "DLSS5Updater.exe", Url = url });
        var r = AppUpdate.Newest([rel], new Version(2, 0, 0))!;
        Assert.Null(AppUpdate.PackageAsset(r, AppInstallKind.Portable));
    }

    [Fact]
    public void SumsFile_IgnoresAHugeAsset()
    {
        var r = AppUpdate.Newest([Release("v2.1.0", false, false, Asset("v2.1.0", "SHA256SUMS", size: 10_000_000))], new Version(2, 0, 0))!;
        Assert.Null(AppUpdate.SumsFile(r));
    }

    // ---------- checksums ----------

    [Fact]
    public void ParseSums_ReadsTextAndBinaryMarkers()
    {
        var a = new string('a', 64);
        var b = new string('B', 64);
        var sums = AppUpdate.ParseSums($"{a}  DLSS5Updater.exe\r\n{b} *DLSS5Updater-Setup-2.1.0.exe\n\njunk line\n");
        Assert.Equal(a, sums["DLSS5Updater.exe"]);
        Assert.Equal(b.ToLowerInvariant(), sums["dlss5updater-setup-2.1.0.exe"]);
        Assert.Equal(2, sums.Count);
    }

    [Fact]
    public void Verify_AcceptsAMatchingHash()
    {
        var sha = new string('c', 64);
        Assert.Null(AppUpdate.Verify(Asset("v2.1.0", "DLSS5Updater.exe", 10, "sha256:" + sha), sha, 10, new Dictionary<string, string> { ["DLSS5Updater.exe"] = sha }));
    }

    [Fact]
    public void Verify_RefusesWrongMissingOrMismatchedChecksums()
    {
        var sha = new string('c', 64);
        var other = new string('d', 64);
        var sums = new Dictionary<string, string> { ["DLSS5Updater.exe"] = sha };
        Assert.Contains("doesn't match its published checksum", AppUpdate.Verify(Asset("v2.1.0", "DLSS5Updater.exe"), other, 10, sums));
        Assert.Contains("no checksum", AppUpdate.Verify(Asset("v2.1.0", "DLSS5Updater.exe"), sha, 10, new Dictionary<string, string>()));
        Assert.Contains("GitHub's checksum", AppUpdate.Verify(Asset("v2.1.0", "DLSS5Updater.exe", digest: "sha256:" + other), sha, 10, sums));
        Assert.Contains("wrong size", AppUpdate.Verify(Asset("v2.1.0", "DLSS5Updater.exe", size: 11), sha, 10, sums));
    }

    // ---------- throttle ----------

    [Fact]
    public void IsDue_ThrottlesToTheInterval()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(AppUpdate.IsDue(null, now));
        Assert.False(AppUpdate.IsDue(now.AddHours(-1), now));
        Assert.True(AppUpdate.IsDue(now - AppUpdate.CheckInterval, now));
        Assert.True(AppUpdate.IsDue(now.AddDays(2), now)); // clock went backwards
    }

    // ---------- check: online and offline ----------

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Urls) Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(string body, string? etag = "\"e1\"")
    {
        var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        if (etag is not null) res.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return res;
    }

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private const string ReleasesJson = """[{"tag_name":"v99.1.0","prerelease":false,"body":"new","assets":[]},{"tag_name":"v99.2.0","prerelease":true,"assets":[]}]""";

    [Fact]
    public async Task Check_FindsTheNewestStableRelease()
    {
        var gh = new GitHubClient(new FakeHttp(_ => Ok(ReleasesJson)));
        var result = await new AppUpdater(gh).CheckAsync(new Version(2, 0, 0), default);
        Assert.False(result.Failed);
        Assert.Equal("v99.1.0", result.Update!.Tag);
        Assert.Contains("v99.1.0 is available", result.Message);
    }

    [Fact]
    public async Task Check_Offline_WithoutCache_ReportsFailureInsteadOfThrowing()
    {
        var gh = new GitHubClient(new FakeHttp(_ => throw new HttpRequestException("no network")));
        var result = await new AppUpdater(gh).CheckAsync(new Version(2, 0, 0), default);
        Assert.True(result.Failed);
        Assert.Null(result.Update);
        Assert.Contains("Couldn't reach GitHub", result.Message);
    }

    [Fact]
    public async Task Check_Offline_FallsBackToTheCachedReleases()
    {
        await new GitHubClient(new FakeHttp(_ => Ok(ReleasesJson))).GetReleasesAsync(AppUpdate.Repo, 10, default);
        var gh = new GitHubClient(new FakeHttp(_ => throw new HttpRequestException("no network")));
        var result = await new AppUpdater(gh).CheckAsync(new Version(2, 0, 0), default);
        Assert.False(result.Failed);
        Assert.Equal("v99.1.0", result.Update!.Tag);
        Assert.Contains("from the last check", result.Message);
        Assert.Equal("v99.1.0", AppUpdate.Newest(gh.CachedReleases(AppUpdate.Repo), new Version(2, 0, 0))!.Tag);
    }

    [Fact]
    public async Task Check_RateLimited_FallsBackToTheCache()
    {
        await new GitHubClient(new FakeHttp(_ => Ok(ReleasesJson))).GetReleasesAsync(AppUpdate.Repo, 10, default);
        var gh = new GitHubClient(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("API rate limit exceeded") }));
        var result = await new AppUpdater(gh).CheckAsync(new Version(2, 0, 0), default);
        Assert.Equal("v99.1.0", result.Update!.Tag);
    }

    // ---------- download and verify ----------

    private (AppRelease Release, GitHubClient Gh, FakeHttp Http) Served(byte[] exe, string? sums)
    {
        const string tag = "v99.1.0";
        var assets = new List<GhAsset> { Asset(tag, "DLSS5Updater.exe", exe.Length) };
        if (sums is not null) assets.Add(Asset(tag, "SHA256SUMS", Encoding.UTF8.GetByteCount(sums)));
        var http = new FakeHttp(r => r.RequestUri!.AbsolutePath.EndsWith("/SHA256SUMS") ? Ok(sums!, null) : Ok(exe));
        var release = AppUpdate.Newest([new GhRelease { TagName = tag, Assets = assets }], new Version(2, 0, 0))!;
        return (release, new GitHubClient(http), http);
    }

    [Fact]
    public async Task Download_GoodChecksum_StagesTheExeNextToTheRunningOne()
    {
        var exe = Path.Combine(_dir, "app", "DLSS5Updater.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        var bytes = Encoding.UTF8.GetBytes("new build");
        var (r, gh, _) = Served(bytes, $"{Sha(bytes)}  DLSS5Updater.exe\n");

        var (path, sha) = await new AppUpdater(gh).DownloadAsync(r, AppInstallKind.Portable, exe, null, default);

        Assert.Equal(exe + ".new", path);
        Assert.Equal(Sha(bytes), sha);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task Download_BadChecksum_IsRefusedAndLeavesNoFile()
    {
        var exe = Path.Combine(_dir, "app", "DLSS5Updater.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        var (r, gh, _) = Served(Encoding.UTF8.GetBytes("tampered"), $"{new string('0', 64)}  DLSS5Updater.exe\n");

        var ex = await Assert.ThrowsAsync<AppUpdateException>(() => new AppUpdater(gh).DownloadAsync(r, AppInstallKind.Portable, exe, null, default));

        Assert.Contains("checksum", ex.Message);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(exe)!));
    }

    [Fact]
    public async Task Download_MissingChecksumFile_IsRefusedBeforeDownloading()
    {
        var exe = Path.Combine(_dir, "app", "DLSS5Updater.exe");
        var (r, gh, http) = Served(Encoding.UTF8.GetBytes("x"), sums: null);

        await Assert.ThrowsAsync<AppUpdateException>(() => new AppUpdater(gh).DownloadAsync(r, AppInstallKind.Portable, exe, null, default));
        Assert.Empty(http.Urls);
    }

    [Fact]
    public async Task Download_ChecksumFileWithoutThisAsset_IsRefusedBeforeDownloading()
    {
        var exe = Path.Combine(_dir, "app", "DLSS5Updater.exe");
        var (r, gh, http) = Served(Encoding.UTF8.GetBytes("x"), $"{new string('1', 64)}  Other.exe\n");

        await Assert.ThrowsAsync<AppUpdateException>(() => new AppUpdater(gh).DownloadAsync(r, AppInstallKind.Portable, exe, null, default));
        Assert.DoesNotContain(http.Urls, u => u.EndsWith("/DLSS5Updater.exe"));
    }

    [Fact]
    public async Task Download_Installed_StagesTheSetupInTheCache()
    {
        const string tag = "v99.1.0";
        var bytes = Encoding.UTF8.GetBytes("setup");
        var sums = $"{Sha(bytes)}  DLSS5Updater-Setup-99.1.0.exe\n";
        var http = new FakeHttp(r => r.RequestUri!.AbsolutePath.EndsWith("/SHA256SUMS") ? Ok(sums, null) : Ok(bytes));
        var r = AppUpdate.Newest([new GhRelease { TagName = tag, Assets = [Asset(tag, "DLSS5Updater-Setup-99.1.0.exe"), Asset(tag, "SHA256SUMS")] }], new Version(2, 0, 0))!;

        var (path, _) = await new AppUpdater(new GitHubClient(http)).DownloadAsync(r, AppInstallKind.Installed, Path.Combine(_dir, "x.exe"), null, default);

        Assert.Equal(AppUpdate.StagedSetup(new Version(99, 1, 0)), path);
        Assert.True(FileUtil.IsUnder(path, AppPaths.Cache));
    }

    // ---------- portable swap and cleanup ----------

    [Fact]
    public void SwapPortable_MovesTheOldExeAsideAndTheNewOneIn()
    {
        var exe = Path.Combine(_dir, "DLSS5Updater.exe");
        File.WriteAllText(exe, "old");
        File.WriteAllText(AppUpdate.StagedPortable(exe), "new");
        File.WriteAllText(AppUpdate.OldExe(exe), "older leftover");

        AppUpdate.SwapPortable(exe, AppUpdate.StagedPortable(exe));

        Assert.Equal("new", File.ReadAllText(exe));
        Assert.Equal("old", File.ReadAllText(AppUpdate.OldExe(exe)));
        Assert.False(File.Exists(AppUpdate.StagedPortable(exe)));
    }

    [Fact]
    public void SwapPortable_PutsTheOldExeBack_WhenTheNewOneCantMoveIn()
    {
        var exe = Path.Combine(_dir, "DLSS5Updater.exe");
        File.WriteAllText(exe, "old");

        Assert.Throws<FileNotFoundException>(() => AppUpdate.SwapPortable(exe, AppUpdate.StagedPortable(exe)));

        Assert.Equal("old", File.ReadAllText(exe));
        Assert.False(File.Exists(AppUpdate.OldExe(exe)));
    }

    [Fact]
    public void Cleanup_RemovesLeftovers_ButKeepsAPendingNewerUpdate()
    {
        var exe = Path.Combine(_dir, "DLSS5Updater.exe");
        File.WriteAllText(exe, "current");
        File.WriteAllText(AppUpdate.OldExe(exe), "previous");
        File.WriteAllText(AppUpdate.StagedPortable(exe), "pending");
        File.WriteAllText(AppUpdate.StagedPortable(exe) + ".part", "half");
        Directory.CreateDirectory(AppUpdate.SetupDir);
        var pending = AppUpdate.StagedSetup(new Version(99, 0, 0));
        File.WriteAllText(pending, "setup");
        File.WriteAllText(AppUpdate.StagedSetup(new Version(1, 0, 0)), "stale");

        AppUpdate.Cleanup(exe, keep: new Version(99, 0, 0));

        Assert.True(File.Exists(exe));
        Assert.False(File.Exists(AppUpdate.OldExe(exe)));
        Assert.False(File.Exists(AppUpdate.StagedPortable(exe) + ".part"));
        Assert.True(File.Exists(AppUpdate.StagedPortable(exe)));
        Assert.Equal([pending], Directory.GetFiles(AppUpdate.SetupDir));

        AppUpdate.Cleanup(exe, keep: null);

        Assert.False(File.Exists(AppUpdate.StagedPortable(exe)));
        Assert.Empty(Directory.GetFiles(AppUpdate.SetupDir));
        Assert.True(File.Exists(exe));
    }

    [Fact]
    public void Cleanup_DropsAStagedVersionThatIsNoLongerNewer()
    {
        var exe = Path.Combine(_dir, "DLSS5Updater.exe");
        File.WriteAllText(AppUpdate.StagedPortable(exe), "same as running");
        AppUpdate.Cleanup(exe, keep: AppUpdate.Current);
        Assert.False(File.Exists(AppUpdate.StagedPortable(exe)));
    }

    // ---------- install kind and pending installs ----------

    [Fact]
    public void IsInstalledAt_MatchesInnoInstallLocation()
    {
        var dir = Path.Combine(_dir, "DLSS5 Updater");
        Assert.True(AppUpdate.IsInstalledAt(dir, [null, dir.ToUpperInvariant() + "\\"]));
        Assert.False(AppUpdate.IsInstalledAt(dir, [Path.Combine(_dir, "elsewhere"), ""]));
    }

    [Fact]
    public void DetectKind_TreatsAFolderWithTheUninstallerAsInstalled()
    {
        var portable = Path.Combine(_dir, "p", "DLSS5Updater.exe");
        var installed = Path.Combine(_dir, "i", "DLSS5Updater.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(portable)!);
        Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
        File.WriteAllText(Path.Combine(_dir, "i", "unins000.exe"), "");

        Assert.Equal(AppInstallKind.Portable, AppUpdate.DetectKind(portable));
        Assert.Equal(AppInstallKind.Installed, AppUpdate.DetectKind(installed));
    }

    [Fact]
    public void SetupArgs_AreSilentAndRelaunchOnlyWhenAsked()
    {
        Assert.Contains("/VERYSILENT", AppUpdate.SetupArgs(false));
        Assert.Contains("/SUPPRESSMSGBOXES", AppUpdate.SetupArgs(false));
        Assert.DoesNotContain("/RELAUNCH", AppUpdate.SetupArgs(false));
        Assert.EndsWith("/RELAUNCH=1", AppUpdate.SetupArgs(true));
    }

    [Fact]
    public void ApplyPending_SkipsWhenOffNotNewerOrAlreadyTried()
    {
        var exe = Path.Combine(_dir, "DLSS5Updater.exe");
        File.WriteAllText(exe, "current");
        File.WriteAllText(AppUpdate.StagedPortable(exe), "new");
        var sha = FileUtil.Sha256(AppUpdate.StagedPortable(exe));

        var off = new AppSettings { AutoInstallAppUpdates = false, AppUpdateStaged = "v99.0.0", AppUpdateStagedSha256 = sha };
        var notNewer = new AppSettings { AutoInstallAppUpdates = true, AppUpdateStaged = "v" + AppUpdate.Current.ToString(3), AppUpdateStagedSha256 = sha };
        var tried = new AppSettings { AutoInstallAppUpdates = true, AppUpdateStaged = "v99.0.0", AppUpdateStagedSha256 = sha, AppUpdateTried = "v99.0.0" };

        Assert.False(AppUpdater.ApplyPending(off, exe, relaunch: false));
        Assert.False(AppUpdater.ApplyPending(notNewer, exe, relaunch: false));
        Assert.False(AppUpdater.ApplyPending(tried, exe, relaunch: false));
        Assert.Equal("current", File.ReadAllText(exe));
    }

    [Fact]
    public void ApplyPending_PortableSwapsOnce_AndNotAgain()
    {
        var exe = Path.Combine(_dir, "DLSS5Updater.exe");
        File.WriteAllText(exe, "current");
        File.WriteAllText(AppUpdate.StagedPortable(exe), "new");
        var s = new AppSettings { AutoInstallAppUpdates = true, AppUpdateStaged = "v99.0.0", AppUpdateStagedSha256 = FileUtil.Sha256(AppUpdate.StagedPortable(exe)) };

        Assert.True(AppUpdater.ApplyPending(s, exe, relaunch: false));

        Assert.Equal("new", File.ReadAllText(exe));
        Assert.Equal("v99.0.0", s.AppUpdateTried);
        Assert.False(AppUpdater.ApplyPending(s, exe, relaunch: false));
    }

    [Fact]
    public void Apply_RefusesAStagedFileThatChangedSinceItWasVerified()
    {
        var exe = Path.Combine(_dir, "DLSS5Updater.exe");
        File.WriteAllText(exe, "current");
        File.WriteAllText(AppUpdate.StagedPortable(exe), "swapped after the check");

        Assert.False(AppUpdater.Apply(AppInstallKind.Portable, exe, AppUpdate.StagedPortable(exe), new string('e', 64), relaunch: false));

        Assert.Equal("current", File.ReadAllText(exe));
        Assert.False(File.Exists(AppUpdate.StagedPortable(exe)));
    }

    [Fact]
    public void PlainNotes_ReducesMarkdown()
    {
        var text = AppUpdate.PlainNotes("## What's new\n- **Bold** [link](https://x)\n<img src=x>\n\n\n\nEnd");
        Assert.Equal("What's new\n• Bold link\n\nEnd", text.Replace("\r", ""));
        Assert.Equal("This release has no notes.", AppUpdate.PlainNotes(null));
        Assert.EndsWith("…", AppUpdate.PlainNotes(new string('x', 5000), 100));
    }
}
