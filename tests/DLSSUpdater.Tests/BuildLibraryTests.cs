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
    public void Prerelease_numbers_compare_by_value_inside_dash_and_dot_parts()
    {
        string[] labels = ["v8.5.0-rc9-model-sdr1", "v0.7.0-pre9", "v11.0.0-beta.5", "v8.5.0-rc10-model-sdr1", "v11.0.0-beta.48", "v0.7.0-pre10", "v11.0.0-beta.45"];
        var sorted = BuildOrder.NewestFirst(labels, l => l, _ => null, l => l);
        Assert.Equal(["v11.0.0-beta.48", "v11.0.0-beta.45", "v11.0.0-beta.5", "v8.5.0-rc10-model-sdr1", "v8.5.0-rc9-model-sdr1", "v0.7.0-pre10", "v0.7.0-pre9"], sorted);
        // The date only decides between labels that say the same.
        var dated = new[] { ("v8.5.0-rc9-x", new DateTime(2026, 6, 1)), ("v8.5.0-rc10-x", new DateTime(2026, 1, 1)) };
        Assert.Equal("v8.5.0-rc10-x", BuildOrder.NewestFirst(dated, d => d.Item1, d => d.Item2, d => d.Item1)[0].Item1);
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

        Assert.Equal(["Default (v0.9.51)", "OptiScaler-NR v0.9.51", "OptiScaler-NR v0.9.47"], vm.OptiChoices.Select(c => c.Label));
        Assert.Equal(["Default (v1.4.2)", "v1.4.2", "v1.3.0"], vm.UpliftChoices.Select(c => c.Label));
        Assert.Null(vm.OptiChoice?.Tag);

        _settings.OptiTag = "v0.9.47";
        vm.RebuildAddonChoices();
        Assert.Equal("Default (v0.9.47)", vm.OptiChoices[0].Label);
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

    [Fact]
    public void Migration_leftover_of_a_build_the_library_has_is_stale_and_an_unreadable_cache_is_logged()
    {
        Release(Component.OptiScaler, "v0.9.49", "library copy");
        var leftover = LegacyRelease(Component.OptiScaler, "v0.9.49");
        var notYet = LegacyRelease(Component.Uplift, "v1.3.0");

        _store.MigrateReleaseLibraries();

        Assert.True(Directory.Exists(leftover));
        var stale = _store.StaleDownloads(_settings, []).Select(i => i.Path).ToList();
        Assert.Contains(leftover, stale);
        Assert.DoesNotContain(notYet, stale);

        // A cache folder that can't be listed doesn't stop the start.
        var blocked = Path.Combine(AppPaths.Cache, "uplift");
        FileUtil.DeleteDirectory(blocked);
        Write(blocked, "a file where the folder should be");
        _store.MigrateReleaseLibraries();
    }

    [Fact]
    public void Cached_release_lists_skip_half_deleted_builds_and_the_newest_is_by_version()
    {
        Release(Component.Uplift, "v1.4.2", "U2", new DateTime(2026, 1, 1));
        Release(Component.Uplift, "v1.3.0", "U1", new DateTime(2026, 2, 1));
        var doomed = Release(Component.Uplift, "v9.0.0", "gone");
        Directory.Move(ComponentStore.TagDir(Component.Uplift, doomed.Tag), ComponentStore.TagDir(Component.Uplift, doomed.Tag) + ".dlssu-delete");
        // The older build's record is written last: its write time must not make it the newest.
        ComponentStore.MarkComplete(Component.Uplift, new ReleaseInfo { Tag = "v1.3.0", Published = new DateTime(2026, 2, 1) });
        Write(Path.Combine(ComponentStore.TagDir(Component.Dlss, "v310.1.0"), "nvngx_dlss.dll"), "x");
        ComponentStore.MarkComplete(Component.Dlss, new ReleaseInfo { Tag = "v310.1.0" });
        Directory.Move(ComponentStore.TagDir(Component.Dlss, "v310.1.0"), ComponentStore.TagDir(Component.Dlss, "v310.1.0") + ".dlssu-delete");

        Assert.Equal("v1.4.2", ComponentStore.NewestCached(Component.Uplift)?.Tag);
        Assert.Equal(["v1.4.2", "v1.3.0"], ComponentStore.CachedReleases(Component.Uplift).Select(r => r.Tag));
        Assert.Empty(ComponentStore.CachedReleases(Component.Dlss));
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

    // ---------- installs the scan can't see ----------

    private List<string> NrBuilds(int count)
    {
        var ids = Enumerable.Range(0, count).Select(i => new string((char)('a' + i), 64)).ToList();
        for (var i = 0; i < count; i++)
        {
            Write(Path.Combine(AppPaths.Components, "dlssnr", ids[i], ComponentStore.DlssNrFile), ids[i]);
            Write(Path.Combine(AppPaths.Components, "dlssnr", ids[i], "version.json"), JsonSerializer.Serialize(new NrVersion(ids[i], $"310.{9 - i}.0", ids[i]), JsonCtx.Default.NrVersion));
        }
        return ids;
    }

    private List<string> NrCandidates(CleanupRecords r) => _store.CleanupCandidates(LibraryKind.DlssNr, _settings, r.Records).Select(b => b.Id).ToList();

    private string GameWithRecord(string name, InstallManifest m)
    {
        var dir = Path.Combine(_tmp, "games", name);
        Directory.CreateDirectory(dir);
        m.Save(dir);
        return dir;
    }

    [Fact]
    public void Install_on_an_unplugged_drive_keeps_its_builds_and_the_automatic_cleanup_leaves_the_library_alone()
    {
        var ids = NrBuilds(5);
        _settings.DlssNrId = ids[0];
        // Installed: the oldest build, with the one before the switch to undo to.
        var game = GameWithRecord("usb", new InstallManifest { DlssNr = true, DlssNrId = ids[4], PreviousDlssNrId = ids[3] });
        var first = MainViewModel.ReadRecords([game]);
        Assert.False(first.Unknown);
        Assert.Empty(first.Records.Single().Files);
        Assert.Null(first.AutoSkipNote());

        // The drive is unplugged: the next scan doesn't list the game at all.
        Directory.Move(game, game + "-unplugged");
        var later = KnownInstalls.Gather([], InstallManifest.Read, _ => FolderState.Unplugged);

        Assert.False(later.Unknown);
        Assert.Contains(LibraryKind.DlssNr, later.Unsure);
        Assert.Equal(1, later.Unreachable);
        Assert.Equal("Automatic cleanup skips NR runtime: 1 game folder with installs can't be reached (drive not connected?).", later.AutoSkipNote());
        var manual = NrCandidates(later);
        Assert.Equal([ids[2]], manual);
        Assert.DoesNotContain(_store.PlanCleanup(_settings, later.Records, skip: later.Unsure), i => i.Path.Contains("dlssnr"));
        // Back again, with nothing changed: the remembered entry stays as long as the record does.
        Directory.Move(game + "-unplugged", game);
        Assert.Empty(MainViewModel.ReadRecords([]).Unsure);
        Assert.Single(MainViewModel.ReadRecords([]).Records);
    }

    [Fact]
    public void Folder_deleted_while_its_drive_is_there_is_forgotten_and_a_missing_drive_is_unplugged()
    {
        var ids = NrBuilds(5);
        _settings.DlssNrId = ids[0];
        var game = GameWithRecord("steam-uninstalled", new InstallManifest { DlssNr = true, DlssNrId = ids[4], PreviousDlssNrId = ids[3] });
        MainViewModel.ReadRecords([game]);

        Assert.Equal(FolderState.Here, KnownInstalls.Probe(game));
        Directory.Delete(game, true);
        Assert.Equal(FolderState.Gone, KnownInstalls.Probe(game));
        var free = Enumerable.Range('D', 23).Select(c => $"{(char)c}:\\").FirstOrDefault(d => !Directory.Exists(d));
        if (free is not null) Assert.Equal(FolderState.Unplugged, KnownInstalls.Probe(Path.Combine(free, "Games", "X")));

        // Deleted outside the app (a Steam uninstall): its builds no longer hold up the automatic cleanup.
        var records = MainViewModel.ReadRecords([]);
        Assert.Empty(records.Records);
        Assert.Empty(records.Unsure);
        Assert.Equal([ids[2], ids[3], ids[4]], NrCandidates(records));
        Assert.DoesNotContain("steam-uninstalled", File.ReadAllText(KnownInstalls.FilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unreadable_install_refs_stop_every_cleanup_and_are_never_overwritten(bool locked)
    {
        var ids = NrBuilds(4);
        _settings.DlssNrId = ids[0];
        var game = GameWithRecord("refs", new InstallManifest { DlssNr = true, DlssNrId = ids[3] });
        MainViewModel.ReadRecords([game]);
        if (!locked) File.WriteAllText(KnownInstalls.FilePath, "{ broken");
        var before = File.ReadAllBytes(KnownInstalls.FilePath);

        using (locked ? new FileStream(KnownInstalls.FilePath, FileMode.Open, FileAccess.Read, FileShare.None) : null)
        {
            var records = MainViewModel.ReadRecords([game]);
            Assert.True(records.Unknown);
            Assert.Empty(records.Records);
            KnownInstalls.Forget([game]);
        }

        Assert.Equal(before, File.ReadAllBytes(KnownInstalls.FilePath));
    }

    [Fact]
    public void First_run_without_install_refs_also_reads_the_hidden_games()
    {
        var ids = NrBuilds(4);
        _settings.DlssNrId = ids[0];
        var hidden = GameWithRecord("hidden-before-upgrade", new InstallManifest { DlssNr = true, DlssNrId = ids[3] });
        Assert.Equal([hidden], GameScanner.InstallDirs([new GameEntry("Hidden", hidden, "Library")]));
        var lookups = 0;
        IEnumerable<string> Hidden()
        {
            lookups++;
            return [hidden];
        }

        Assert.False(File.Exists(KnownInstalls.FilePath));
        var first = KnownInstalls.Gather([], InstallManifest.Read, KnownInstalls.Probe, Hidden);
        Assert.Equal([ids[2]], NrCandidates(first));
        Assert.True(File.Exists(KnownInstalls.FilePath));

        // Remembered from now on; the lookup runs only once.
        var later = KnownInstalls.Gather([], InstallManifest.Read, KnownInstalls.Probe, Hidden);
        Assert.Equal([ids[2]], NrCandidates(later));
        Assert.Equal(1, lookups);
    }

    [Fact]
    public void Hidden_game_keeps_its_builds_until_it_is_uninstalled_or_removed()
    {
        var ids = NrBuilds(5);
        _settings.DlssNrId = ids[0];
        var hidden = GameWithRecord("hidden", new InstallManifest { DlssNr = true, DlssNrId = ids[4], PreviousDlssNrId = ids[3] });
        MainViewModel.ReadRecords([hidden]);

        // Hidden from the list: the scan no longer names it, its record is still read from the remembered folder.
        var records = MainViewModel.ReadRecords([]);
        Assert.Empty(records.Unsure);
        Assert.Equal([ids[2]], NrCandidates(records));

        // Uninstalled outside the app: the folder is there and its record is gone.
        File.Delete(InstallManifest.PathFor(hidden));
        Assert.Equal([ids[2], ids[3], ids[4]], NrCandidates(MainViewModel.ReadRecords([])));

        // Removed from the app or uninstalled by it.
        var other = GameWithRecord("other", new InstallManifest { DlssNr = true, DlssNrId = ids[4] });
        MainViewModel.ReadRecords([other]);
        KnownInstalls.Forget([other]);
        Directory.Delete(other, true);
        Assert.Empty(MainViewModel.ReadRecords([]).Records);
    }

    [Fact]
    public void Unreadable_record_falls_back_to_what_it_named_and_stops_every_cleanup_when_nothing_is_known()
    {
        var ids = NrBuilds(4);
        _settings.DlssNrId = ids[0];
        var known = GameWithRecord("known", new InstallManifest { DlssNr = true, DlssNrId = ids[3] });
        MainViewModel.ReadRecords([known]);
        File.WriteAllText(InstallManifest.PathFor(known), "{ broken");

        var records = MainViewModel.ReadRecords([known]);
        Assert.False(records.Unknown);
        Assert.Contains(LibraryKind.DlssNr, records.Unsure);
        Assert.Equal(1, records.Unreadable);
        Assert.Equal("Automatic cleanup skips NR runtime: 1 install record can't be read.", records.AutoSkipNote());
        Assert.Equal([ids[2]], NrCandidates(records));

        var stranger = GameWithRecord("stranger", new InstallManifest());
        File.WriteAllText(InstallManifest.PathFor(stranger), "{ broken");
        Assert.True(MainViewModel.ReadRecords([known, stranger]).Unknown);
    }

    [Fact]
    public void Automatic_cleanup_never_deletes_an_import_only_the_manual_one_does()
    {
        var ids = NrBuilds(3);
        _settings.DlssNrId = ids[0];
        _settings.KeepBuilds[nameof(LibraryKind.DlssNr)] = 1;
        _store.TakeAdded();
        var source = Path.Combine(_tmp, "old-nr", ComponentStore.DlssNrFile);
        Write(source, "an old NR without a version resource");

        var imported = _store.RetainDlssNr(source);
        Assert.Empty(_store.TakeAdded());

        // Every later automatic pass leaves the NR runtime alone; the confirmed manual cleanup may still remove old ones.
        for (var pass = 0; pass < 2; pass++)
            Assert.DoesNotContain(_store.PlanCleanup(_settings, [], _store.TakeAdded(), downloadsOnly: true), i => i.Path.Contains("dlssnr"));
        var manual = _store.PlanCleanup(_settings, []).Select(i => Path.GetFileName(i.Path)).ToList();
        Assert.Contains(imported.Id, manual);
        Assert.Contains(ids[1], manual);
    }

    [Fact]
    public void Automatic_cleanup_deletes_old_downloads_and_counts_imports_toward_the_keep_count()
    {
        // Lecram: two builds from the channel, two imports (one from before the origin was recorded).
        string[] ids = [new('1', 64), new('2', 64), new('3', 64), new('4', 64)];
        bool?[] downloaded = [true, false, true, null];
        for (var i = 0; i < ids.Length; i++)
        {
            var build = new AddonBuild(ids[i], ComponentStore.Lecram11File, "", LecramGeneration.V11, $"v11.0.{9 - i}", null, DateTime.UtcNow, downloaded[i] == true);
            var json = JsonSerializer.Serialize(build, JsonCtx.Default.AddonBuild);
            if (downloaded[i] is null) json = System.Text.RegularExpressions.Regex.Replace(json, @",\s*""Downloaded"":\s*false", "");
            Write(Path.Combine(AppPaths.Components, "lecram-dlss5", ids[i], ComponentStore.Lecram11File), ids[i]);
            Write(Path.Combine(AppPaths.Components, "lecram-dlss5", ids[i], "version.json"), json);
        }
        Assert.DoesNotContain("Downloaded", File.ReadAllText(Path.Combine(AppPaths.Components, "lecram-dlss5", ids[3], "version.json")));
        _settings.LecramId = ids[0];
        _settings.KeepBuilds[nameof(LibraryKind.Lecram)] = 1;
        Release(Component.OptiScaler, "v0.7.9", "new");
        Release(Component.OptiScaler, "v0.7.8", "mid");
        Release(Component.OptiScaler, "v0.7.7", "old");
        _settings.KeepBuilds[nameof(LibraryKind.OptiScaler)] = 1;

        Assert.Equal([ids[1], ids[2], ids[3]], _store.CleanupCandidates(LibraryKind.Lecram, _settings, []).Select(b => b.Id));
        Assert.Equal([ids[2]], _store.CleanupCandidates(LibraryKind.Lecram, _settings, [], downloadsOnly: true).Select(b => b.Id));
        Assert.All(_store.Library(LibraryKind.OptiScaler), b => Assert.True(b.Downloaded));
        Assert.Equal(2, _store.CleanupCandidates(LibraryKind.OptiScaler, _settings, [], downloadsOnly: true).Count);
        // A download that just arrived stays through the pass that follows it.
        var auto = _store.PlanCleanup(_settings, [], new HashSet<string>(["v0.7.8"]), downloadsOnly: true).Select(i => Path.GetFileName(i.Path)).ToList();
        Assert.Contains("v0.7.7", auto);
        Assert.DoesNotContain("v0.7.8", auto);
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
