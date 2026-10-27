using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>
/// The manual side of DLSS5-Feeder: a user-supplied release (.zip, folder or the add-on file) goes into the library byte for byte
/// and is never cleaned up by itself; "Auto" follows the newest build, a pick wins over it per game and as the default, and
/// "Reset to auto" goes back. No network.
/// </summary>
public sealed class FeederImportTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-feedimp-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _releases;

    public FeederImportTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _releases = Path.Combine(_tmp, "releases");
        Directory.CreateDirectory(_releases);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private static readonly string[] Files =
        ["dlss5-feed.addon64", "dlss5-feed.addon32", "host64/dlss5-feed-host64.exe", "reshade-shaders/Shaders/DLSS5_Feed.fx", "READ-ME-FIRST.txt", "Verify-DLSS5Feeder.ps1"];

    private static string Content(string version, string file) =>
        file == "READ-ME-FIRST.txt" ? $"DLSS5-Feeder {version}\n===\nrest" : $"{file}@{version}";

    /// <summary>An extracted release folder, as the zip lays it out.</summary>
    private string Folder(string version, string? inner = null, string[]? skip = null)
    {
        var root = Path.Combine(_releases, "folder-" + version + (inner is null ? "" : "-wrapped"));
        var dir = inner is null ? root : Path.Combine(root, inner);
        foreach (var f in Files.Where(f => skip?.Contains(f) != true))
        {
            var path = Path.Combine(dir, f.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Content(version, f));
        }
        return root;
    }

    private string Zip(string version, string? prefix = null, string[]? skip = null)
    {
        var path = Path.Combine(_releases, $"DLSS5-Feeder-{version}{(prefix is null ? "" : "-wrapped")}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var f in Files.Where(f => skip?.Contains(f) != true))
        {
            using var w = new StreamWriter(zip.CreateEntry(prefix + f).Open(), new UTF8Encoding(false));
            w.Write(Content(version, f));
        }
        return path;
    }

    private static void SeedDownloaded(string tag, DateTime fetched)
    {
        var dir = Path.Combine(FeederLibrary.Root, tag);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".complete"), JsonSerializer.Serialize(new CachedComponent { Tag = tag, Fetched = fetched }, JsonCtx.Default.CachedComponent));
    }

    // ---------- import ----------

    [Theory]
    [InlineData("zip")]
    [InlineData("wrapped-zip")]
    [InlineData("folder")]
    [InlineData("wrapped-folder")]
    [InlineData("addon-file")]
    public void Import_AcceptsAZipAFolderOrTheAddonFile_AndCopiesByteForByte(string how)
    {
        var source = how switch
        {
            "zip" => Zip("1.19.0"),
            "wrapped-zip" => Zip("1.19.0", "DLSS5-Feeder-1.19.0/"),
            "folder" => Folder("1.19.0"),
            "wrapped-folder" => Folder("1.19.0", "DLSS5-Feeder-1.19.0"),
            _ => Path.Combine(Folder("1.19.0"), "dlss5-feed.addon64"),
        };

        var build = FeederLibrary.Import(source);

        Assert.Equal(LibraryKind.Feeder, build.Kind);
        Assert.Equal("1.19.0", build.Version);
        Assert.Equal("1.19.0 (imported)", build.Label);
        Assert.False(build.Downloaded);
        Assert.Equal(64, build.Id.Length);
        var pkg = FeederLibrary.PackageOf(build);
        Assert.Equal("dlss5-feed.addon64@1.19.0", File.ReadAllText(Path.Combine(pkg, "dlss5-feed.addon64")));
        Assert.Equal("dlss5-feed.addon32@1.19.0", File.ReadAllText(Path.Combine(pkg, "dlss5-feed.addon32")));
        Assert.Equal("host64/dlss5-feed-host64.exe@1.19.0", File.ReadAllText(Path.Combine(pkg, "host64", "dlss5-feed-host64.exe")));
        Assert.Equal("reshade-shaders/Shaders/DLSS5_Feed.fx@1.19.0", File.ReadAllText(Path.Combine(pkg, "reshade-shaders", "Shaders", "DLSS5_Feed.fx")));
        Assert.Equal(FileUtil.Sha256(Path.Combine(pkg, "dlss5-feed.addon64")), build.Id);
        Assert.Equal(build.Id, Assert.Single(FeederLibrary.Builds()).Id);
        Assert.False(Directory.Exists(FeederLibrary.Root + ".new"));
    }

    [Fact]
    public void Import_WithoutAReadme_StillWorks_AndNamesTheBuildByItsHash()
    {
        var build = FeederLibrary.Import(Folder("1.19.0", skip: ["READ-ME-FIRST.txt"]));

        Assert.StartsWith("imported ", build.Version);
        Assert.Equal(build.Id[..8], build.Version["imported ".Length..]);
    }

    [Fact]
    public void Import_TheSameAddonAgain_ReplacesThatBuildsSet()
    {
        var first = FeederLibrary.Import(Folder("1.19.0"));
        File.WriteAllText(Path.Combine(FeederLibrary.PackageOf(first), "stale.txt"), "left over");

        var second = FeederLibrary.Import(Zip("1.19.0"));

        Assert.Equal(first.Id, second.Id);
        Assert.Single(FeederLibrary.Builds());
        Assert.False(File.Exists(Path.Combine(FeederLibrary.PackageOf(second), "stale.txt")));
    }

    [Theory]
    [InlineData("dlss5-feed.addon64", "isn't in")]
    [InlineData("host64/dlss5-feed-host64.exe", "host64/dlss5-feed-host64.exe is missing")]
    [InlineData("reshade-shaders/Shaders/DLSS5_Feed.fx", "DLSS5_Feed.fx is missing")]
    public void Import_AReleaseMissingAFile_IsRefusedWhole(string missing, string message)
    {
        var zip = Assert.Throws<InvalidDataException>(() => FeederLibrary.Import(Zip("1.19.0", skip: [missing])));
        var folder = Assert.Throws<InvalidDataException>(() => FeederLibrary.Import(Folder("1.19.0", skip: [missing])));

        Assert.Contains(message.Replace("isn't in", "not found in"), folder.Message);
        Assert.Contains(message, zip.Message);
        Assert.Empty(FeederLibrary.Builds());
        Assert.False(Directory.Exists(FeederLibrary.Root + ".new"));
    }

    [Fact]
    public void Import_SomethingThatIsNotAFeederRelease_IsRefused()
    {
        var empty = Path.Combine(_releases, "empty");
        Directory.CreateDirectory(empty);

        Assert.Throws<InvalidDataException>(() => FeederLibrary.Import(empty));
        Assert.Throws<InvalidDataException>(() => FeederLibrary.Import(Path.Combine(_releases, "nope")));
    }

    [Fact]
    public void ImportedBuilds_AreNeverInTheAutomaticCleanup_ButCountForTheManualOne()
    {
        var store = new ComponentStore(new GitHubClient(), () => false);
        var settings = new AppSettings();
        FeederLibrary.Import(Folder("1.17.0"));
        FeederLibrary.Import(Folder("1.18.0"));
        FeederLibrary.Import(Folder("1.19.0"));
        SeedDownloaded("v1.16.0", DateTime.UtcNow.AddDays(-30));
        SeedDownloaded("v1.15.0", DateTime.UtcNow.AddDays(-40));
        settings.KeepBuilds[LibraryKind.Feeder.ToString()] = 1;

        var automatic = store.CleanupCandidates(LibraryKind.Feeder, settings, [], downloadsOnly: true);
        var manual = store.CleanupCandidates(LibraryKind.Feeder, settings, []);

        Assert.All(automatic, b => Assert.True(b.Downloaded));
        Assert.Equal(["v1.15.0", "v1.16.0"], automatic.Select(b => b.Id).Order());
        Assert.Equal(4, manual.Count);
        Assert.Contains(manual, b => !b.Downloaded);
    }

    [Fact]
    public void ImportedAndDownloadedBuilds_SortTogetherByVersion()
    {
        SeedDownloaded("v1.17.0", DateTime.UtcNow);
        FeederLibrary.Import(Folder("1.19.0"));
        SeedDownloaded("v1.18.0-beta.2", DateTime.UtcNow);

        Assert.Equal(["1.19.0", "1.18.0-beta.2", "1.17.0"], FeederLibrary.Builds().Select(b => b.Version));
    }

    [Fact]
    public async Task AnImportedBuild_InstallsLikeADownloadedOne_AndIsRecordedByItsHash()
    {
        var root = Path.Combine(_tmp, "Game");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "game.exe"), "EXE");
        var build = FeederLibrary.Import(Folder("1.19.0"));
        var game = new GameInfo { Name = "Test", Root = root, Exes = [Path.Combine(root, "game.exe")], Tech = new GameTech { Bits = 64 } };
        var installer = new Installer(new ComponentStore(new GitHubClient(), () => false));

        await installer.InstallComponentAsync(game, root, FeederLibrary.Plan(build, game, root), default);

        Assert.Equal("dlss5-feed.addon64@1.19.0", File.ReadAllText(Path.Combine(root, "dlss5-feed.addon64")));
        var c = InstallManifest.Read(root)!.Component(ComponentRegistry.Feeder)!;
        Assert.Equal(build.Id, c.BuildId);
        Assert.Equal("1.19.0 (imported)", c.Version);
    }

    // ---------- Auto, the manual pick, reset to auto ----------

    [Fact]
    public void Auto_FollowsTheNewestBuild_UntilAPinnedDefaultIsChosen()
    {
        FeederLibrary.Import(Folder("1.17.0"));
        var newest = FeederLibrary.Import(Folder("1.19.0"));

        Assert.Equal("Auto (1.19.0 (imported))", FeederLibrary.AutoLabel(null));
        Assert.Equal(newest.Id, FeederLibrary.Choose(null)!.Id);
        var old = FeederLibrary.Builds().Single(b => b.Version == "1.17.0");
        Assert.Equal("Auto (1.17.0 (imported))", FeederLibrary.AutoLabel(old.Id));
        Assert.Equal(old.Id, FeederLibrary.Choose(old.Id)!.Id);
    }

    [Fact]
    public void Auto_WithAnEmptyLibrary_SaysItDownloadsTheNewest()
    {
        Assert.Equal("Auto (newest, downloaded on install)", FeederLibrary.AutoLabel(null));
    }

    private (GameViewModel Vm, AppSettings Settings, string GameId) Game()
    {
        var settings = new AppSettings { DefaultMode = InstallMode.ReShadeOnly };
        var store = new ComponentStore(new GitHubClient(), () => false);
        var services = new Services { Settings = settings, Store = store, Installer = new Installer(store) };
        var root = Path.Combine(_tmp, "pick-game");
        Directory.CreateDirectory(root);
        var vm = new GameViewModel(new GameInfo { Id = root, Root = root, Name = "Pick game" }, services);
        return (vm, settings, vm.Id);
    }

    [Fact]
    public void AGamePick_WinsOverTheDefaultAndAuto_AndResetGoesBackToAuto()
    {
        var a = FeederLibrary.Import(Folder("1.17.0"));
        var b = FeederLibrary.Import(Folder("1.19.0"));
        var (vm, settings, id) = Game();
        vm.RebuildAddonChoices();
        Assert.Equal([null, a.Id], vm.FeederChoices.Select(c => c.Tag));
        Assert.StartsWith("Newest · ", vm.FeederChoices[0].Label);
        Assert.Null(vm.EffectiveFeederId);
        Assert.False(vm.FeederIsManual);

        settings.SetDefaultBuild(ComponentRegistry.Feeder, a.Id);
        Assert.Equal(a.Id, vm.EffectiveFeederId);
        vm.RebuildAddonChoices();
        Assert.Equal([null, b.Id], vm.FeederChoices.Select(c => c.Tag));
        Assert.StartsWith("Default · ", vm.FeederChoices[0].Label);
        Assert.EndsWith(" · newest", vm.FeederChoices[1].Label);

        vm.FeederChoice = vm.FeederChoices.Single(c => c.Tag == b.Id);
        Assert.Equal(b.Id, settings.Games[id].BuildPick(ComponentRegistry.Feeder));
        Assert.Equal(b.Id, vm.EffectiveFeederId);
        Assert.Equal(b.Id, vm.BuildOptions(false, false).FeederId);
        Assert.True(vm.FeederIsManual);

        vm.ResetFeederToAutoCommand.Execute(null);
        Assert.Null(settings.Games[id].BuildPick(ComponentRegistry.Feeder));
        Assert.Equal(a.Id, vm.EffectiveFeederId);
        Assert.False(vm.FeederIsManual);
    }

    [Fact]
    public void ThePickSurvivesAReload_AndALostPickStaysVisible()
    {
        var a = FeederLibrary.Import(Folder("1.17.0"));
        FeederLibrary.Import(Folder("1.19.0"));
        var (vm, settings, id) = Game();
        vm.RebuildAddonChoices();
        vm.FeederChoice = vm.FeederChoices.Single(c => c.Tag == a.Id);

        vm.RebuildAddonChoices();
        Assert.Equal(a.Id, vm.FeederChoice!.Tag);

        FeederLibrary.Remove(a, new AppSettings());
        vm.RebuildAddonChoices();
        Assert.Contains("not in the library", vm.FeederChoice!.Label);
        Assert.Equal(a.Id, settings.Games[id].BuildPick(ComponentRegistry.Feeder));
    }

    [Fact]
    public void Remove_TakesTheBuildOut_AndSendsPinsOnItBackToAuto_NotToAnotherBuild()
    {
        var a = FeederLibrary.Import(Folder("1.17.0"));
        var b = FeederLibrary.Import(Folder("1.19.0"));
        var settings = new AppSettings();
        settings.SetDefaultBuild(ComponentRegistry.Feeder, a.Id);
        settings.For("game-a").SetBuildPick(ComponentRegistry.Feeder, a.Id);
        settings.For("game-b").SetBuildPick(ComponentRegistry.Feeder, b.Id);

        FeederLibrary.Remove(a, settings);

        Assert.False(Directory.Exists(a.Dir));
        Assert.Equal(b.Id, Assert.Single(FeederLibrary.Builds()).Id);
        Assert.Null(settings.DefaultBuild(ComponentRegistry.Feeder));
        Assert.Null(settings.Games["game-a"].BuildPick(ComponentRegistry.Feeder));
        Assert.Equal(b.Id, settings.Games["game-b"].BuildPick(ComponentRegistry.Feeder));
    }

    [Fact]
    public async Task TheInstallerUsesThePickOverTheNewest_WithoutAskingGitHub()
    {
        var a = FeederLibrary.Import(Folder("1.17.0"));
        FeederLibrary.Import(Folder("1.19.0"));
        var store = new ComponentStore(new GitHubClient(new ThrowingHandler()), () => false);

        var chosen = await store.EnsureFeederAsync(a.Id, null, default);

        Assert.Equal(a.Id, chosen.Id);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("a pinned build needs no network");
    }
}
