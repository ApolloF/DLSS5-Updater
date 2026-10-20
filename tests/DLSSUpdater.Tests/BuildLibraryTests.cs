using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>
/// The build libraries of OptiScaler and GITC Uplift, the newest-first order of every build list, and the cleanup of old
/// builds. Temp folders only; the fixtures are text files posing as packages.
/// </summary>
public sealed class BuildLibraryTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-builds-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly AppSettings _settings = new();
    private readonly ComponentStore _store = new(new GitHubClient(), () => true);

    public BuildLibraryTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
        _store.OptiSourceRepo = () => _settings.OptiSource;
        _store.SelectedDlssNr = () => _settings.DlssNrId;
        _store.SelectedRenoDx = () => _settings.RenoDxId;
        _store.SelectedLecram = () => _settings.LecramId;
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

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static ReleaseInfo Release(Component c, string tag, string content, DateTime? published = null)
    {
        var file = c == Component.OptiScaler ? "OptiScaler.dll" : ComponentStore.UpliftFile;
        Write(Path.Combine(ComponentStore.TagDir(c, tag), "pkg", file), content);
        if (c == Component.OptiScaler) Write(Path.Combine(ComponentStore.TagDir(c, tag), "pkg", "OptiScaler.ini"), "[DlssNr]\nEnabled=auto\n");
        var r = new ReleaseInfo { Tag = tag, Published = published };
        ComponentStore.MarkComplete(c, r);
        return r;
    }

    /// <summary>A complete download in the cache layout of 2.0 and older.</summary>
    private static string LegacyRelease(Component c, string tag, bool complete = true)
    {
        var dir = Path.Combine(AppPaths.Cache, c.ToString().ToLowerInvariant(), tag);
        foreach (var file in c == Component.OptiScaler ? ["OptiScaler.dll"] : ComponentStore.UpliftFiles)
            Write(Path.Combine(dir, "pkg", file), $"{c} {tag}");
        if (complete)
            Write(Path.Combine(dir, ".complete"), JsonSerializer.Serialize(new CachedComponent { Tag = tag, Fetched = DateTime.UtcNow }, JsonCtx.Default.CachedComponent));
        return dir;
    }

    private static LibraryBuild Build(string id, string group = "") => new(LibraryKind.Lecram, id, id, id, null, id, group);

    // ---------- newest first ----------

    [Fact]
    public void Lecram_labels_sort_newest_first_with_prereleases_before_their_release()
    {
        string[] labels = ["v8.5.0-rc10-model-sdr1", "v11.0.0-beta.22", "v11.0.0-beta.45", "v11.0.0", "v11.0.0-beta.48", "v11.0.0-rc.1"];
        var sorted = BuildOrder.NewestFirst(labels, l => l, _ => null, l => l);
        Assert.Equal(["v11.0.0", "v11.0.0-rc.1", "v11.0.0-beta.48", "v11.0.0-beta.45", "v11.0.0-beta.22", "v8.5.0-rc10-model-sdr1"], sorted);
    }

    [Fact]
    public void Same_version_falls_back_to_the_later_date_and_versions_compare_by_value()
    {
        var items = new[] { ("a", new DateTime(2026, 1, 1)), ("b", new DateTime(2026, 3, 1)) };
        Assert.Equal(["b", "a"], BuildOrder.NewestFirst(items, _ => "v1.0", i => i.Item2, i => i.Item1).Select(i => i.Item1));
        Assert.Equal(["310.10.0", "310.9.16", "310.9"], BuildOrder.NewestFirst(["310.9", "310.10.0", "310.9.16"], v => v, _ => null, v => v));
        Assert.Equal(BuildOrder.Key("v1.2"), BuildOrder.Key("1.2.0.0"));
    }

    [Fact]
    public void Opti_library_lists_each_source_newest_first_and_uplift_newest_first()
    {
        Release(Component.OptiScaler, "v0.9.47", "A");
        Release(Component.OptiScaler, "v0.1.30-f5", "B");
        Release(Component.OptiScaler, "v0.9.51", "C");
        Release(Component.OptiScaler, "v0.9.9", "D");
        Release(Component.OptiScaler, "v0.1.32-f5", "E");
        Release(Component.Uplift, "v1.2.0", "U1");
        Release(Component.Uplift, "v1.10.0", "U2");

        Assert.Equal(["v0.9.51", "v0.9.47", "v0.9.9", "v0.1.32-f5", "v0.1.30-f5"], _store.Library(LibraryKind.OptiScaler).Select(b => b.Id));
        Assert.Equal("OptiScaler F5 v0.1.32", _store.Library(LibraryKind.OptiScaler)[3].Label);
        Assert.Equal(["v1.10.0", "v1.2.0"], _store.Library(LibraryKind.Uplift).Select(b => b.Id));
    }

    [Fact]
    public void Game_dropdowns_list_default_first_then_builds_newest_first()
    {
        Release(Component.OptiScaler, "v0.9.47", "A");
        _store.Opti = Release(Component.OptiScaler, "v0.9.51", "B");
        Release(Component.Uplift, "v1.3.0", "U1");
        _store.Uplift = Release(Component.Uplift, "v1.4.2", "U2");
        var vm = NewGame();

        Assert.Equal(["Default (newest: OptiScaler-NR v0.9.51)", "OptiScaler-NR v0.9.51", "OptiScaler-NR v0.9.47"], vm.OptiChoices.Select(c => c.Label));
        Assert.Equal(["Default (newest: v1.4.2)", "v1.4.2", "v1.3.0"], vm.UpliftChoices.Select(c => c.Label));
        Assert.Null(vm.OptiChoice?.Tag);

        _settings.OptiTag = "v0.9.47";
        vm.RebuildAddonChoices();
        Assert.Equal("Default (OptiScaler-NR v0.9.47)", vm.OptiChoices[0].Label);
    }

    // ---------- migration ----------

    [Fact]
    public async Task Migration_moves_downloaded_builds_into_the_library_without_downloading()
    {
        var nr = LegacyRelease(Component.OptiScaler, "v0.9.49");
        LegacyRelease(Component.OptiScaler, "v0.1.32-f5");
        LegacyRelease(Component.Uplift, "v1.3.0");
        var unfinished = LegacyRelease(Component.OptiScaler, "v0.9.51", complete: false);

        _store.MigrateReleaseLibraries();

        Assert.Equal(["v0.9.49", "v0.1.32-f5"], _store.Library(LibraryKind.OptiScaler).Select(b => b.Id));
        Assert.Equal(["v1.3.0"], _store.Library(LibraryKind.Uplift).Select(b => b.Id));
        Assert.False(Directory.Exists(nr));
        Assert.True(Directory.Exists(unfinished));
        // GitHub is never asked: the library build installs as it is.
        var pkg = await _store.EnsureOptiAsync("v0.9.49", null, default);
        Assert.Equal("OptiScaler v0.9.49", File.ReadAllText(Path.Combine(pkg, "OptiScaler.dll")));
        Assert.StartsWith(AppPaths.Components, pkg);
        var uplift = await _store.EnsureUpliftAsync("v1.3.0", null, default);
        Assert.StartsWith(AppPaths.Components, uplift);

        _store.MigrateReleaseLibraries();
        Assert.Equal(2, _store.Library(LibraryKind.OptiScaler).Count);
        Assert.Contains(_store.StaleDownloads(_settings, []), i => i.Path == unfinished);
    }

    // ---------- per-game choice ----------

    [Fact]
    public async Task Per_game_opti_build_reaches_the_installer_with_its_source_extras()
    {
        _store.Opti = Release(Component.OptiScaler, "v0.9.51", "OPTI-NEW");
        Release(Component.OptiScaler, "v0.1.32-f5", "OPTI-F5");
        Write(Path.Combine(ComponentStore.TagDir(Component.OptiScaler, "v0.1.32-f5"), "pkg", "nvngx.dll_dlssnr.dll"), "FORWARDER");
        var root = Path.Combine(_tmp, "Game");
        Write(Path.Combine(root, "Game.exe"), "EXE");
        var info = GameScanner.Inspect(new GameEntry("Game", root, "Manual"));
        var vm = new GameViewModel(info, new Services { Settings = _settings, Store = _store, Installer = new Installer(_store) });

        vm.OptiChoice = vm.OptiChoices.First(c => c.Tag == "v0.1.32-f5");
        Assert.Equal("v0.1.32-f5", _settings.Games[vm.Id].OptiTag);
        var o = vm.BuildOptions(false, false) with { ReShade = false, Mfg = false, DlssNr = false, Dlss = false, AddMissingDlss = false, Streamline = false };
        Assert.True(o.Opti);
        Assert.Equal("v0.1.32-f5", o.OptiTag);

        await new Installer(_store).InstallAsync(info, root, o, null, default);
        Assert.Equal("OPTI-F5", File.ReadAllText(Path.Combine(root, "dxgi.dll")));
        Assert.Equal("FORWARDER", File.ReadAllText(Path.Combine(root, "nvngx.dll_dlssnr.dll")));
        Assert.Equal("v0.1.32-f5", InstallManifest.Load(root)!.OptiTag);

        // Back to Default: the newest release of the selected source.
        vm.OptiChoice = vm.OptiChoices[0];
        Assert.Null(vm.BuildOptions(false, false).OptiTag);
        _settings.OptiTag = "v0.9.51";
        Assert.Equal("v0.9.51", vm.BuildOptions(false, false).OptiTag);
    }

    [Fact]
    public async Task Per_game_uplift_build_reaches_the_install_options_and_installs_from_the_library()
    {
        _store.Uplift = Release(Component.Uplift, "v1.4.2", "NEW");
        Release(Component.Uplift, "v1.3.0", "OLD");
        foreach (var f in ComponentStore.UpliftFiles) Write(Path.Combine(ComponentStore.TagDir(Component.Uplift, "v1.3.0"), "pkg", f), "OLD");
        var vm = NewGame();

        vm.UpliftChoice = vm.UpliftChoices.First(c => c.Tag == "v1.3.0");
        Assert.Equal("v1.3.0", vm.BuildOptions(false, false).UpliftTag);
        Assert.Equal("v1.3.0", vm.UpdateOptions(new InstallManifest { Uplift = true }).UpliftTag);
        var pkg = await _store.EnsureUpliftAsync(vm.BuildOptions(false, false).UpliftTag, null, default);
        Assert.Equal("OLD", File.ReadAllText(Path.Combine(pkg, ComponentStore.UpliftFile)));
    }

    // ---------- cleanup ----------

    [Fact]
    public void Automatic_cleanup_is_off_and_two_builds_are_kept_by_default()
    {
        var s = new AppSettings();
        Assert.False(s.AutoCleanBuilds);
        Assert.All(Enum.GetValues<LibraryKind>(), k => Assert.Equal(2, s.KeepBuildsFor(k)));
        s.KeepBuilds[nameof(LibraryKind.Lecram)] = 0;
        Assert.Equal(1, s.KeepBuildsFor(LibraryKind.Lecram));
        var loaded = JsonSerializer.Deserialize("{}", JsonCtx.Default.AppSettings)!;
        Assert.False(loaded.AutoCleanBuilds);
    }

    [Fact]
    public void Selection_keeps_the_newest_per_group_and_every_protected_build()
    {
        LibraryBuild[] builds = [Build("n1", "nr"), Build("n2", "nr"), Build("n3", "nr"), Build("n4", "nr"), Build("f1", "f5"), Build("f2", "f5")];

        Assert.Equal(["n3", "n4"], BuildCleanup.Select(builds, 2, new HashSet<string>()).Select(b => b.Id));
        Assert.Equal(["n4"], BuildCleanup.Select(builds, 2, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "N3" }).Select(b => b.Id));
        Assert.Equal(["n2", "n3", "n4", "f2"], BuildCleanup.Select(builds, 0, new HashSet<string>()).Select(b => b.Id));
        Assert.Empty(BuildCleanup.Select(builds, 2, builds.Select(b => b.Id).ToHashSet()));
    }

    [Fact]
    public void Cleanup_never_plans_protected_builds()
    {
        _store.Opti = Release(Component.OptiScaler, "v0.9.51", "A");
        foreach (var t in new[] { "v0.9.49", "v0.9.47", "v0.9.45", "v0.9.43", "v0.9.41" }) Release(Component.OptiScaler, t, t);
        Release(Component.OptiScaler, "v0.1.32-f5", "F");
        Release(Component.OptiScaler, "v0.1.30-f5", "F");
        Release(Component.OptiScaler, "v0.1.28-f5", "F");
        _settings.OptiTag = "v0.9.41";
        _settings.For("game-a").OptiTag = "v0.9.43";
        InstallManifest[] records = [new() { Opti = true, OptiTag = "v0.9.45" }];

        var planned = _store.PlanCleanup(_settings, records).Select(i => Path.GetFileName(i.Path)).ToList();

        Assert.Equal(["v0.9.47", "v0.1.28-f5"], planned);
    }

    [Fact]
    public void Nr_cleanup_keeps_what_games_and_the_default_can_switch_back_to()
    {
        var ids = Enumerable.Range(0, 6).Select(i => (Id: new string((char)('a' + i), 64), Version: $"310.{9 - i}.0")).ToList();
        foreach (var (id, version) in ids)
        {
            Write(Path.Combine(AppPaths.Components, "dlssnr", id, ComponentStore.DlssNrFile), id);
            Write(Path.Combine(AppPaths.Components, "dlssnr", id, "version.json"), JsonSerializer.Serialize(new NrVersion(id, version, id), JsonCtx.Default.NrVersion));
        }
        _settings.DlssNrId = ids[0].Id;
        _settings.PreviousDlssNrId = ids[2].Id;
        _settings.For("g").DlssNrId = ids[3].Id;
        InstallManifest[] records = [new() { DlssNr = true, DlssNrId = ids[0].Id, PreviousDlssNrId = ids[4].Id }];

        var planned = _store.CleanupCandidates(LibraryKind.DlssNr, _settings, records);

        Assert.Equal([ids[5].Id], planned.Select(b => b.Id));
    }

    [Fact]
    public void Addon_cleanup_keeps_the_default_game_picks_and_installed_builds_by_tag()
    {
        string[] ids = [new('1', 64), new('2', 64), new('3', 64), new('4', 64), new('5', 64)];
        for (var i = 0; i < ids.Length; i++)
        {
            var build = new AddonBuild(ids[i], ComponentStore.Lecram11File, "", LecramGeneration.V11, $"v11.0.{9 - i}", null, DateTime.UtcNow);
            Write(Path.Combine(AppPaths.Components, "lecram-dlss5", ids[i], ComponentStore.Lecram11File), ids[i]);
            Write(Path.Combine(AppPaths.Components, "lecram-dlss5", ids[i], "version.json"), JsonSerializer.Serialize(build, JsonCtx.Default.AddonBuild));
        }
        _settings.LecramId = ids[2];
        // A record from before the library: only the tag names the build.
        InstallManifest[] records = [new() { Lecram = true, LecramTag = $"v11.0.6-{ids[3][..12]}" }];

        Assert.Equal([ids[4]], _store.CleanupCandidates(LibraryKind.Lecram, _settings, records).Select(b => b.Id));
        _settings.KeepBuilds[nameof(LibraryKind.Lecram)] = 1;
        Assert.Equal([ids[1], ids[4]], _store.CleanupCandidates(LibraryKind.Lecram, _settings, records).Select(b => b.Id));
    }

    [Fact]
    public void Stale_downloads_keep_current_chosen_installed_and_imported_releases()
    {
        foreach (var t in new[] { "v310.1.0", "v310.2.0", "v310.3.0", "v310.4.0", "v310.5.0-local-abcdef123456" })
        {
            Write(Path.Combine(ComponentStore.TagDir(Component.Dlss, t), "nvngx_dlss.dll"), t);
            ComponentStore.MarkComplete(Component.Dlss, new ReleaseInfo { Tag = t });
        }
        _store.Dlss = new ReleaseInfo { Tag = "v310.4.0" };
        _settings.DlssTag = "v310.1.0";
        Write(Path.Combine(ComponentStore.TagDir(Component.MfgUnlock, "1.0"), ComponentStore.MfgFile), "half");
        var temp = Path.Combine(AppPaths.Cache, "lecram-download-0123");
        Write(Path.Combine(temp, "package"), "x");
        Write(Path.Combine(AppPaths.Cache, "api", "x.json"), "{}");
        InstallManifest[] records = [new() { Dlss = true, DlssTag = "v310.2.0" }];

        var stale = _store.StaleDownloads(_settings, records).Select(i => i.Path).ToList();

        Assert.Equal(new[] { temp, ComponentStore.TagDir(Component.Dlss, "v310.3.0"), ComponentStore.TagDir(Component.MfgUnlock, "1.0") }.Order(), stale.Order());
    }

    [Fact]
    public void Run_deletes_for_good_logs_freed_bytes_and_skips_what_is_locked_or_outside()
    {
        var old = Release(Component.OptiScaler, "v0.9.1", "0123456789");
        var locked = Release(Component.OptiScaler, "v0.9.2", "locked");
        var outside = Path.Combine(_tmp, "elsewhere");
        Write(Path.Combine(outside, "keep.txt"), "x");
        var lockedFile = Path.Combine(ComponentStore.TagDir(Component.OptiScaler, locked.Tag), "pkg", "OptiScaler.dll");
        var plan = new[]
        {
            new CleanupItem("old", ComponentStore.TagDir(Component.OptiScaler, old.Tag), BuildCleanup.SizeOf(ComponentStore.TagDir(Component.OptiScaler, old.Tag))),
            new CleanupItem("locked", ComponentStore.TagDir(Component.OptiScaler, locked.Tag), 6),
            new CleanupItem("outside", outside, 1),
        };
        Assert.Equal(2, _store.Library(LibraryKind.OptiScaler).Count);

        long freed;
        using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            freed = _store.RunCleanup(plan);

        Assert.Equal(plan[0].Bytes, freed);
        Assert.False(Directory.Exists(plan[0].Path));
        Assert.True(File.Exists(lockedFile));
        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
        Assert.Equal(["v0.9.2"], _store.Library(LibraryKind.OptiScaler).Select(b => b.Id));
    }

    private GameViewModel NewGame()
    {
        var root = Path.Combine(_tmp, "vm-game");
        Directory.CreateDirectory(root);
        return new GameViewModel(new GameInfo { Id = root, Root = root, Name = "VM game" }, new Services { Settings = _settings, Store = _store, Installer = new Installer(_store) });
    }
}
