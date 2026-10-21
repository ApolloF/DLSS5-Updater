using System.Net;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>The ReShade library: import, order, picks, migration of the old single file and cache, and the downloads of every channel.</summary>
public sealed class ReShadeLibraryTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-rslib-" + Guid.NewGuid().ToString("N")[..8]);

    public ReShadeLibraryTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); }
        catch (IOException) { }
    }

    private ComponentStore Store(ReShadeFixtures.FakeNet? net = null) => new(new GitHubClient(net ?? new ReShadeFixtures.FakeNet(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), () => true);

    private string Src(string name, string version, string tag = "", bool only64 = false) => ReShadeFixtures.Folder(Path.Combine(_tmp, "src"), name, version, tag, only64);

    [Fact]
    public void Import_AFolder_KeepsBothDlls_AsACustomBuild()
    {
        var store = Store();
        var build = store.ImportReShade(Src("a", "6.8.0.2155"));
        Assert.Equal(ReShadeChannel.Custom, build.Channel);
        Assert.Equal("6.8.0", build.Version);
        Assert.True(build.Has64 && build.Has32);
        Assert.Equal(build.Sha64, build.Id);
        Assert.Equal(ReShadeVerification.Imported, build.Verification);
        Assert.True(File.Exists(ComponentStore.ReShadeFilePath(build, 64)));
        Assert.True(File.Exists(ComponentStore.ReShadeFilePath(build, 32)));
        Assert.Equal([build.Id], store.ReShadeBuilds.Select(b => b.Id));
        Assert.True(store.ReShadeImported);
    }

    [Fact]
    public void Import_OneDll_TakesTheOtherBitnessFromItsFolder_AndDedupes()
    {
        var store = Store();
        var dir = Src("a", "6.8.0.2155");
        var first = store.ImportReShade(Path.Combine(dir, ReShadeBuild.File32));
        Assert.True(first.Has64 && first.Has32);
        var again = store.ImportReShade(Path.Combine(dir, ReShadeBuild.File64));
        Assert.Equal(first.Id, again.Id);
        Assert.Single(store.ReShadeBuilds);
    }

    [Fact]
    public void Import_A64BitOnlyDll_HasNo32BitFile()
    {
        var store = Store();
        var build = store.ImportReShade(Path.Combine(Src("a", "6.8.0.2155", only64: true), ReShadeBuild.File64));
        Assert.True(build.Has64);
        Assert.False(build.Has32);
        Assert.False(build.HasBits(32));
    }

    [Fact]
    public void Import_ASetupAndAZip_Work_AndSomethingElseIsRefused()
    {
        var store = Store();
        var dir = Src("a", "6.8.0.2155");
        var setup = Path.Combine(_tmp, "ReShade_Setup_6.8.0_Addon.exe");
        File.WriteAllBytes(setup, ReShadeFixtures.Setup(dir));
        Assert.True(store.ImportReShade(setup).Has32);

        var bad = Path.Combine(_tmp, "ReShade64.dll");
        File.WriteAllText(bad, "<html>an error page</html>");
        Assert.Throws<InvalidDataException>(() => store.ImportReShade(bad));
        Assert.Throws<FileNotFoundException>(() => store.ImportReShade(Path.Combine(_tmp, "nope.dll")));
        Assert.Single(store.ReShadeBuilds);
    }

    [Fact]
    public void Builds_AreListedNewestFirst_RenamedAndRemoved()
    {
        var store = Store();
        var old = store.ImportReShade(Src("a", "6.5.1.1"));
        var newer = store.ImportReShade(Src("b", "6.8.0.1"));
        Assert.Equal([newer.Id, old.Id], store.ReShadeBuilds.Select(b => b.Id));

        store.RenameReShade(old.Id, "Max Payne 3");
        Assert.Equal("Max Payne 3", store.ReShadeBuilds.Single(b => b.Id == old.Id).Label);
        store.RenameReShade(old.Id, " ");
        Assert.StartsWith("6.5.1 · Custom", store.ReShadeBuilds.Single(b => b.Id == old.Id).Label);

        store.RemoveReShade(old.Id);
        Assert.Equal([newer.Id], store.ReShadeBuilds.Select(b => b.Id));
        Assert.Throws<InvalidDataException>(() => store.RemoveReShade("../escape"));
    }

    [Fact]
    public void TheDefault_IsTheNewestStableAddonBuild_NotAnImportOrANightly()
    {
        var store = Store();
        store.ImportReShade(Src("custom", "6.9.0.1"));
        Assert.Null(store.ReShadeBuildFor(null)); // nothing Stable yet
        Assert.Equal("channel:stable", store.EffectiveReShadePick(null));
    }

    [Fact]
    public async Task Picks_AChannelFollowsItsNewestBuild_AnIdPinsOne_ARemovedPinFallsBackToTheDefaultNotToAnotherPin()
    {
        var net = SiteAndSetups(out _);
        var store = Store(net);
        await store.RefreshAsync(default);
        var stable = (await store.EnsureReShadeAsync(null, 64, null, default)).Build;
        var custom = store.ImportReShade(Src("custom", "6.5.1.1"));

        Assert.Equal(stable.Id, store.ReShadeBuildFor(null)!.Id);
        Assert.Equal(custom.Id, store.ReShadeBuildFor(custom.Id)!.Id);
        Assert.Equal(custom.Id, (await store.EnsureReShadeAsync(custom.Id, 64, null, default)).Build.Id);

        store.SelectedReShade = () => custom.Id;
        Assert.Equal(custom.Id, store.ReShadeBuildFor(null)!.Id);   // the settings' default
        Assert.Equal(stable.Id, store.ReShadeBuildFor(ReShadePick.ForChannel(ReShadeChannel.Stable))!.Id);   // a game's own pick wins

        store.RemoveReShade(custom.Id);
        Assert.Equal(stable.Id, store.ReShadeBuildFor(null)!.Id);
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.EnsureReShadeAsync(custom.Id, 64, null, default));
    }

    [Fact]
    public async Task AutomaticDownloadOff_UsesImportsOnly_ForTheDefault()
    {
        var store = Store();
        store.AutoReShade = () => false;
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.EnsureReShadeAsync(null, 64, null, default));
        var custom = store.ImportReShade(Src("a", "6.5.1.1"));
        Assert.Equal(custom.Id, (await store.EnsureReShadeAsync(null, 64, null, default)).Build.Id);
        Assert.Equal(ComponentStore.ReShadeFilePath(custom, 64), store.CurrentReShadePath);
    }

    [Fact]
    public async Task Ensure_ABuildWithoutTheBitnessAsked_SaysSo()
    {
        var store = Store();
        var build = store.ImportReShade(Path.Combine(Src("a", "6.8.0.1", only64: true), ReShadeBuild.File64));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.EnsureReShadeAsync(build.Id, 32, null, default));
        Assert.Contains("32-bit", ex.Message);
    }

    [Fact]
    public async Task Ensure_AChangedStoredFile_IsRefused()
    {
        var store = Store();
        var build = store.ImportReShade(Src("a", "6.8.0.1"));
        File.WriteAllText(ComponentStore.ReShadeFilePath(build, 64), "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureReShadeAsync(build.Id, 64, null, default));
    }

    // ---- migration ----

    [Fact]
    public async Task Migration_TheOldImportedFile_BecomesACustomBuild_AndStaysWhatInstallsUse()
    {
        var store = Store();
        Directory.CreateDirectory(AppPaths.Components);
        File.WriteAllText(store.ReShadePath, "RESHADE-IMPORTED");   // not even a PE: the old import never checked
        store.MigrateReShade();

        Assert.False(File.Exists(store.ReShadePath));
        var build = Assert.Single(store.ReShadeBuilds);
        Assert.Equal(ReShadeChannel.Custom, build.Channel);
        Assert.Equal("RESHADE-IMPORTED", File.ReadAllText(ComponentStore.ReShadeFilePath(build, 64)));
        // Still the default for a user who had imported it, without anyone pinning it in the settings.
        Assert.Equal(build.Id, store.ReShadeBuildFor(null)!.Id);
        Assert.Equal(build.Id, (await store.EnsureReShadeAsync(null, 64, null, default)).Build.Id);
        Assert.Equal(build.Id, store.TakeLegacyReShadePin());
        Assert.Null(store.TakeLegacyReShadePin());
        store.MigrateReShade(); // again: nothing to do
        Assert.Single(store.ReShadeBuilds);
    }

    [Fact]
    public void Migration_TheDownloadCache_BecomesStableBuilds()
    {
        var store = Store();
        var cached = Path.Combine(AppPaths.Cache, "reshade", "6.7.3");
        Directory.CreateDirectory(cached);
        File.WriteAllText(Path.Combine(cached, "ReShade64.dll"), "RS-673");
        ComponentStore.MarkComplete(Component.ReShade, new ReleaseInfo { Tag = "6.7.3" });
        var unfinished = Path.Combine(AppPaths.Cache, "reshade", "6.9.9");
        Directory.CreateDirectory(unfinished);
        File.WriteAllText(Path.Combine(unfinished, "ReShade64.dll"), "half");

        store.MigrateReShade();
        var build = Assert.Single(store.ReShadeBuilds);
        Assert.Equal(ReShadeChannel.Stable, build.Channel);
        Assert.Equal("6.7.3", build.Version);
        Assert.Equal(ReShadeVerification.RecordedAtDownload, build.Verification);
        Assert.False(build.Has32);
        Assert.False(Directory.Exists(cached));
        Assert.True(Directory.Exists(unfinished)); // not complete: left for the cleanup
    }

    // ---- downloads ----

    private ReShadeFixtures.FakeNet SiteAndSetups(out string srcDir, string version = "6.8.0.2155")
    {
        var dir = srcDir = Src("site", version);
        var addon = ReShadeFixtures.Setup(dir);
        var plainDir = Src("plain", version, "plain");
        var plain = ReShadeFixtures.Setup(plainDir);
        return new ReShadeFixtures.FakeNet(r =>
        {
            var url = r.RequestUri!.AbsoluteUri;
            if (url == "https://reshade.me/") return ReShadeFixtures.Text(ReShadeFixtures.SitePage);
            if (url.EndsWith("_Addon.exe")) return ReShadeFixtures.Bytes(addon);
            if (url.EndsWith(".exe")) return ReShadeFixtures.Bytes(plain);
            if (url.Contains("/tags")) return ReShadeFixtures.Text("""[{"name":"v6.8.0"},{"name":"v6.7.3"},{"name":"v5.9.2"}]""");
            if (url.StartsWith("https://api.github.com/")) return ReShadeFixtures.Text("[]");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task Stable_IsDownloadedOnce_WithBothDlls_AndItsHashRecorded()
    {
        var net = SiteAndSetups(out var src);
        var store = Store(net);
        await store.RefreshAsync(default);
        Assert.Equal("6.8.0", store.ReShade!.Tag);
        Assert.Equal("6.8.0", store.ReShadeNoAddonsVersion);

        var file = await store.EnsureReShadeAsync(null, 64, null, default);
        Assert.Equal(ReShadeChannel.Stable, file.Build.Channel);
        Assert.True(file.Build.Has32);
        Assert.Equal(FileUtil.Sha256(Path.Combine(src, ReShadeBuild.File64)), file.Build.Id);
        Assert.Equal(ReShadeVerification.RecordedAtDownload, file.Build.Verification);
        Assert.Contains("publishes no digest", file.Build.VerificationText);
        Assert.Equal("https://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe", file.Build.Origin);

        var before = net.Requests.Count(r => r.EndsWith("_Addon.exe"));
        await store.EnsureReShadeAsync(null, 32, null, default);
        Assert.Equal(before, net.Requests.Count(r => r.EndsWith("_Addon.exe"))); // no second download
        Assert.Equal(1, before);
    }

    [Fact]
    public async Task StableWithoutAddons_IsItsOwnBuild_FromThePlainSetup()
    {
        var net = SiteAndSetups(out _);
        var store = Store(net);
        await store.RefreshAsync(default);
        var file = await store.EnsureReShadeAsync(ReShadePick.ForChannel(ReShadeChannel.StableNoAddons), 64, null, default);
        Assert.Equal(ReShadeChannel.StableNoAddons, file.Build.Channel);
        Assert.False(file.Build.Addon);
        Assert.EndsWith("ReShade_Setup_6.8.0.exe", file.Build.Origin);
        Assert.Contains(net.Requests, r => r.EndsWith("ReShade_Setup_6.8.0.exe"));
    }

    [Fact]
    public async Task Legacy_ADownloadWhoseDllIsAnotherVersion_IsRefused()
    {
        var net = SiteAndSetups(out _);   // the site serves 6.8.0 whatever is asked for
        var store = Store(net);
        var versions = await store.ReShadeLegacyVersionsAsync(default);
        Assert.Equal(["6.8.0", "6.7.3"], versions);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.DownloadReShadeLegacyAsync("6.7.3", null, default));
        Assert.Empty(store.ReShadeBuilds);
        var ok = await store.DownloadReShadeLegacyAsync("6.8.0", null, default);
        Assert.Equal(ReShadeChannel.Legacy, ok.Channel);
    }

    [Fact]
    public async Task Offline_TheNewestBuildOfTheChannelInTheLibraryIsUsed_AndNothingKnownIsAClearError()
    {
        var offline = new ReShadeFixtures.FakeNet(_ => throw new HttpRequestException("offline"));
        var store = Store(offline);
        await store.RefreshAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.EnsureReShadeAsync(null, 64, null, default));

        var cached = Path.Combine(AppPaths.Cache, "reshade", "6.7.3");
        Directory.CreateDirectory(cached);
        File.WriteAllText(Path.Combine(cached, "ReShade64.dll"), "RS-673");
        ComponentStore.MarkComplete(Component.ReShade, new ReleaseInfo { Tag = "6.7.3" });
        Assert.Equal("6.7.3", (await store.EnsureReShadeAsync(null, 64, null, default)).Build.Version);
    }

    [Fact]
    public async Task Migrated64BitBuild_GetsItsMissing32BitDll_FromTheSameVersion()
    {
        var net = SiteAndSetups(out var src);
        var store = Store(net);
        await store.RefreshAsync(default);
        // The cache of 2.x held ReShade64.dll only; the same bytes the site serves.
        var cached = Path.Combine(AppPaths.Cache, "reshade", "6.8.0");
        Directory.CreateDirectory(cached);
        File.Copy(Path.Combine(src, ReShadeBuild.File64), Path.Combine(cached, ReShadeBuild.File64));
        ComponentStore.MarkComplete(Component.ReShade, new ReleaseInfo { Tag = "6.8.0" });

        var file = await store.EnsureReShadeAsync(null, 32, null, default);
        Assert.True(file.Build.Has32);
        Assert.Equal(FileUtil.Sha256(Path.Combine(src, ReShadeBuild.File32)), FileUtil.Sha256(file.Path));
        Assert.Single(store.ReShadeBuilds);
    }

    // ---- nightly ----

    private ReShadeFixtures.FakeNet NightlyNet(string dir, bool digests = true, string? wrongDigest64 = null)
    {
        var zip64 = ReShadeFixtures.Zip(dir, ReShadeBuild.File64);
        var zip32 = ReShadeFixtures.Zip(dir, ReShadeBuild.File32);
        var d64 = digests ? $"\"sha256:{wrongDigest64 ?? ReShadeFixtures.Sha(zip64)}\"" : "null";
        var d32 = digests ? $"\"sha256:{ReShadeFixtures.Sha(zip32)}\"" : "null";
        return new ReShadeFixtures.FakeNet(r =>
        {
            var url = r.RequestUri!.AbsoluteUri;
            if (url.Contains("/actions/workflows/build.yml/runs"))
                return ReShadeFixtures.Text("""{"workflow_runs":[{"id":7,"head_sha":"7bf9de8b33bcc76c3177007e65d73c72dd0f34c0","created_at":"2026-09-24T11:24:08Z","artifacts_url":"https://api.github.com/repos/crosire/reshade/actions/runs/7/artifacts"}]}""");
            if (url.EndsWith("/runs/7/artifacts"))
                return ReShadeFixtures.Text($$"""{"artifacts":[{"name":"ReShade (64-bit)","size_in_bytes":1,"expired":false,"digest":{{d64}}},{"name":"ReShade (32-bit)","size_in_bytes":1,"expired":false,"digest":{{d32}}}]}""");
            if (url == "https://nightly.link/crosire/reshade/workflows/build/main/ReShade%20%2864-bit%29.zip") return ReShadeFixtures.Bytes(zip64);
            if (url == "https://nightly.link/crosire/reshade/workflows/build/main/ReShade%20%2832-bit%29.zip") return ReShadeFixtures.Bytes(zip32);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task Nightly_IsVerifiedAgainstGitHubsArtifactDigest()
    {
        var dir = Src("nightly", "6.8.1.2200");
        var store = Store(NightlyNet(dir));
        var file = await store.EnsureReShadeAsync(ReShadePick.ForChannel(ReShadeChannel.Nightly), 64, null, default);
        Assert.Equal(ReShadeChannel.Nightly, file.Build.Channel);
        Assert.Equal("6.8.1.2200", file.Build.Version);
        Assert.Equal(ReShadeVerification.PublishedDigest, file.Build.Verification);
        Assert.Equal("7bf9de8b33bcc76c3177007e65d73c72dd0f34c0", file.Build.Commit);
        Assert.Contains("6.8.1.2200 · Nightly 7bf9de8", file.Build.Label);
        Assert.True(file.Build.Has32);
        // The same commit is not downloaded twice.
        await store.EnsureReShadeAsync(ReShadePick.ForChannel(ReShadeChannel.Nightly), 32, null, default);
        Assert.Single(store.ReShadeBuilds);
    }

    [Fact]
    public async Task Nightly_ADigestThatDoesNotMatch_InstallsNothing()
    {
        var dir = Src("nightly", "6.8.1.2200");
        var store = Store(NightlyNet(dir, wrongDigest64: new string('a', 64)));
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureReShadeAsync(ReShadePick.ForChannel(ReShadeChannel.Nightly), 64, null, default));
        Assert.Contains("digest", ex.Message);
        Assert.Empty(store.ReShadeBuilds);
        Assert.False(Directory.EnumerateDirectories(AppPaths.Cache, "reshade-download-*").Any());
    }

    [Fact]
    public async Task Nightly_WithoutAPublishedDigest_IsMarkedAnUnverifiedSource()
    {
        var dir = Src("nightly", "6.8.1.2200");
        var store = Store(NightlyNet(dir, digests: false));
        var file = await store.EnsureReShadeAsync(ReShadePick.ForChannel(ReShadeChannel.Nightly), 64, null, default);
        Assert.Equal(ReShadeVerification.RecordedAtDownload, file.Build.Verification);
        Assert.Contains("Unverified source", file.Build.VerificationText);
    }

    // ---- library kind: cleanup and settings ----

    [Fact]
    public void TheCleanup_KeepsTheDefaultPicksAndInstalledBuilds_AndAskingForEachByName()
    {
        var store = Store();
        var builds = new[] { "6.5.1.1", "6.6.0.1", "6.7.0.1", "6.8.0.1" }.Select((v, i) => store.ImportReShade(Src("b" + i, v))).ToList();
        var settings = new AppSettings { ReShadeId = builds[0].Id };
        settings.KeepBuilds[LibraryKind.ReShade.ToString()] = 1;
        settings.For("g").ReShadeId = builds[1].Id;
        var installed = new InstallManifest { ReShadeId = builds[2].Id };

        var delete = store.CleanupCandidates(LibraryKind.ReShade, settings, [installed]);
        Assert.Empty(delete);   // newest is kept by the count; the other three are default, picked and installed
        var reasons = store.ProtectionReasons(LibraryKind.ReShade, settings, [installed], null).ToList();
        Assert.Contains(reasons, r => r.Id == builds[0].Id && r.Reason == "default");
        Assert.Contains(reasons, r => r.Id == builds[1].Id && r.Reason.StartsWith("picked for"));
        Assert.Contains(reasons, r => r.Id == builds[2].Id && r.Reason.StartsWith("installed in"));
        Assert.Equal("ReShade", ComponentStore.LibraryTitle(LibraryKind.ReShade));

        settings.ReShadeId = null;
        settings.For("g").ReShadeId = null;
        Assert.Single(store.CleanupCandidates(LibraryKind.ReShade, settings, [installed]), b => b.Id == builds[1].Id);
    }

    [Fact]
    public void TheDefaultAndPerGamePick_AreReadAndWrittenByComponentId()
    {
        var s = new AppSettings();
        s.For("g").SetBuildPick(ComponentRegistry.ReShade, "channel:nightly");
        Assert.Equal("channel:nightly", s.Games["g"].ReShadeId);
        Assert.Equal("channel:nightly", s.Games["g"].BuildPick(ComponentRegistry.ReShade));
        s.ReShadeId = "abc";
        Assert.Equal("abc", s.DefaultBuild(ComponentRegistry.ReShade));
        Assert.Equal(LibraryKind.ReShade, ComponentRegistry.Find(ComponentRegistry.ReShade)!.Library);
        Assert.Equal("channel:nightly", ReShadePick.ForChannel(ReShadeChannel.Nightly));
        Assert.Equal(ReShadeChannel.Nightly, ReShadePick.ChannelOf("channel:nightly"));
        Assert.Null(ReShadePick.ChannelOf("channel:custom"));
        Assert.Null(ReShadePick.ChannelOf(new string('a', 64)));
    }
}
