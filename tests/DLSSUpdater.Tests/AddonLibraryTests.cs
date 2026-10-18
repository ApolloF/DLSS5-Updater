using System.Text;
using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>
/// The libraries of ShortFuse's and Lecram's add-ons: storage, migration, labels, default and per-game choice, installs of the
/// chosen build, combos and the options that follow a build's generation. Fixtures only: a copy of the test assembly with a
/// version resource, or synthetic bytes; never real add-on or NVIDIA binaries.
/// </summary>
public sealed class AddonLibraryTests : IDisposable
{
    private const string V10File = ComponentStore.Lecram10File;
    private const string V11File = ComponentStore.Lecram11File;

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-addons-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly AppSettings _settings = new() { DefaultMode = InstallMode.ReShadeOnly };
    private readonly ComponentStore _store = new(new GitHubClient(), () => true);
    private readonly NrVersion _nr;
    private readonly GameInfo _game;

    public AddonLibraryTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(_root);
        _store.SelectedRenoDx = () => _settings.RenoDxId;
        _store.SelectedLecram = () => _settings.LecramId;
        FakeVersionDll.Write(_store.ReShadePath, new Version(6, 8, 0, 0), ComponentStore.ReShadeFile);
        var nr = Path.Combine(_tmp, "nr.dll");
        FakeVersionDll.Write(nr, new Version(310, 9, 16, 0), ComponentStore.DlssNrFile);
        _nr = _store.ImportDlssNr(nr);
        _game = new GameInfo { Id = _root, Root = _root, Name = "Addon game", Dlss = [new DlssDll { Name = "nvngx_dlss.dll" }] };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    // ---------- fixtures ----------

    private string Src(string fileName)
    {
        var path = Path.Combine(_tmp, "src-" + Guid.NewGuid().ToString("N")[..6], fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private AddonBuild Sf(Version version)
    {
        var path = Src(ComponentStore.RenoDxFile);
        FakeVersionDll.Write(path, version, ComponentStore.RenoDxFile);
        return _store.ImportRenoDx(path);
    }

    /// <summary>A v10-style add-on: it names itself in the version resource (the fixture is a copy of the test assembly).</summary>
    private AddonBuild LecramV10(Version? version = null)
    {
        var path = Src(V10File);
        FakeVersionDll.Write(path, version ?? new Version(0, 2026, 1013, 940), V10File);
        return _store.ImportLecram(path);
    }

    private static byte[] SyntheticBytes(string label, bool frameworkNoise = true)
    {
        var bytes = new byte[0x200];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        var text = $"RenoDX.DLSS5\0NR-SETTINGS event=effective\0NR-COMPAT v1 coinstalled=0\0{label}\0{(frameworkNoise ? "v6.1.2\0v6.1.0\0" : "")}";
        return [.. bytes, .. Encoding.ASCII.GetBytes(text)];
    }

    /// <summary>A PE-like file without a version resource whose strings name the build: recognised as v11, labelled by <paramref name="label"/>.</summary>
    private AddonBuild LecramV11(string label = "v11.0.0-beta.6", bool frameworkNoise = true)
    {
        var path = Src(V11File);
        File.WriteAllBytes(path, SyntheticBytes(label, frameworkNoise));
        return _store.ImportLecram(path);
    }

    private string G(string name) => Path.Combine(_root, name);

    private InstallOptions Options(string? renoDxId = null, string? lecramId = null, bool renoDx = false, bool lecram = false) => new()
    {
        Mode = InstallMode.ReShadeOnly, ReShade = true, RenoDx = renoDx, Lecram = lecram, DlssNr = renoDx || lecram, DlssNrId = _nr.Id,
        Proxy = "dxgi.dll", RenoDxId = renoDxId, LecramId = lecramId,
    };

    private GameViewModel NewGame(string? name = null)
    {
        var services = new Services { Settings = _settings, Store = _store, Installer = new Installer(_store) };
        var root = Path.Combine(_tmp, name ?? "vm-game");
        Directory.CreateDirectory(root);
        return new GameViewModel(new GameInfo { Id = root, Root = root, Name = "VM game" }, services);
    }

    // ---------- build label ----------

    private static string? Label(string text) => ComponentStore.FindBuildLabel(Encoding.ASCII.GetBytes(text));

    [Fact]
    public void Label_TheHighestVersionWins_SoTheRenoDxFrameworkStringsAreIgnored()
    {
        Assert.Equal("v8.5.0-rc10-model-sdr1", Label("v6.1.2\0v6.1.0\0v6.1.2\0v8.5.0-rc10-model-sdr1\0v6.1.0"));
        Assert.Equal("v7.5.0-rc2", Label("v6.1.2\0v7.5.0-rc2\0v6.1.0\0"));
        Assert.Equal("v10.7.0", Label("v10.1.0-rc3\0v10.1.0-rc3\0v10.0.0\0v10.7.0\0v6.1.2"));
        Assert.Equal("v6.1.2", Label("v6.1.0\0v6.1.2\0"));
    }

    [Fact]
    public void Label_OnATieThePreReleaseWins_ThenTheMostFrequent()
    {
        Assert.Equal("v11.0.0-beta.6", Label("v11.0.0\0v11.0.0\0v11.0.0\0v11.0.0-beta.6\0"));
        Assert.Equal("v11.0.0-beta.6", Label("v11.0.0-H\0v11.0.0-beta.6NR on\0v11.0.0-beta.6\0v11.0.0-beta.6\0"));
        Assert.Equal("v11.0.0-beta.6", Label("v11.0.0-\0v11.0.0-beta.6NR on\0v11.0.0-beta.6\0v11.0.0-beta.6\0"));
    }

    [Fact]
    public void Label_ReadsTheV11Beta18AndBeta22Layouts()
    {
        // The string layout of v11 beta.18 / beta.22 (docs/addons/lecram-dlss5.md): the add-on description ends in "(v11.0.0-beta.NN)".
        Assert.Equal("v11.0.0-beta.22", Label(
            "v11.0.0-H\0dlss5-nr11.0.0-beta.22nr_inputs\0v11.0.0-beta.22NR onNR offbytessha256\0" +
            "opens the DLSS 5 NR menu; Shift+key opens ReShade (v11.0.0-beta.22)\0v11.0.0-beta.22\0"));
        Assert.Equal("v11.0.0-beta.18", Label(
            "v11.0.0-H\0dlss5-nr11.0.0-beta.18nr_inputs\0v11.0.0-beta.18NR onNR offbytessha256\0" +
            "opens the DLSS 5 NR menu; Shift+key opens ReShade (v11.0.0-beta.18)\0v11.0.0-beta.18\0"));
        var build = LecramV11("v11.0.0-beta.22NR on\0v11.0.0-H\0v11.0.0-beta.22\0v11.0.0-beta.22");
        Assert.Equal(LecramGeneration.V11, build.Generation);
        Assert.Equal("v11.0.0-beta.22", build.DetectedLabel);
    }

    [Fact]
    public void Label_NeedsAFullThreePartVersion_TrimsPunctuation_AndIsNullWithoutOne()
    {
        Assert.Null(Label(""));
        Assert.Null(Label("v11 and v11.x and v1.2 and 1.2.3 and version 4.5.6"));
        Assert.Equal("v7.5.0-rc2", Label("built as v7.5.0-rc2."));
        Assert.Equal("v1.2.3", Label("v1.2.3-\0"));
    }

    [Fact]
    public void Label_OfAnImportedBuild_ComesFromItsBinary_AndSfUsesItsFileVersion()
    {
        var lecram = LecramV11("v8.5.0-rc10-model-sdr1");
        Assert.Equal("v8.5.0-rc10-model-sdr1", lecram.DetectedLabel);
        var sf = Sf(new Version(0, 2026, 928, 306));
        Assert.Equal("0.2026.928.306", sf.FileVersion);
        Assert.Equal("0.2026.928.306", sf.DetectedLabel);
        Assert.Equal("v11", LecramV11("no version here", frameworkNoise: false).DetectedLabel);
    }

    // ---------- library ----------

    [Fact]
    public void Import_StoresTheBuildUnderItsHash_WithAVersionJson_AndDedupes()
    {
        var path = Src(ComponentStore.RenoDxFile);
        FakeVersionDll.Write(path, new Version(0, 2026, 928, 306), ComponentStore.RenoDxFile);
        var sha = FileUtil.Sha256(path);

        var first = _store.ImportRenoDx(path);
        var dir = Path.Combine(AppPaths.Components, "renodx-dlss", sha);
        Assert.Equal(sha, first.Id);
        Assert.Equal(ComponentStore.RenoDxFile, first.FileName);
        Assert.Equal(sha, FileUtil.Sha256(Path.Combine(dir, ComponentStore.RenoDxFile)));
        var json = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(dir, "version.json")));
        Assert.Equal(sha, json.GetProperty("Id").GetString());
        Assert.Equal("0.2026.928.306", json.GetProperty("FileVersion").GetString());
        Assert.Equal("0.2026.928.306", json.GetProperty("DetectedLabel").GetString());
        Assert.True(json.TryGetProperty("Imported", out _));

        _store.RenameAddon(AddonKind.RenoDx, sha, "my label");
        var again = _store.ImportRenoDx(path);
        Assert.Equal("my label", again.UserLabel);
        Assert.Equal(first.Imported, again.Imported);
        Assert.Single(_store.RenoDxBuilds);
    }

    [Fact]
    public void Import_NeverDeletesOtherBuilds_AndRemoveDeletesExactlyOne()
    {
        var a = Sf(new Version(0, 2026, 928, 306));
        var b = Sf(new Version(0, 2026, 1016, 1704));
        var l10 = LecramV10();
        var l11 = LecramV11();
        Assert.Equal(2, _store.RenoDxBuilds.Count);
        Assert.Equal(2, _store.LecramBuilds.Count);

        _store.RemoveAddon(AddonKind.RenoDx, a.Id);
        Assert.Equal([b.Id], _store.RenoDxBuilds.Select(x => x.Id));
        Assert.False(Directory.Exists(Path.Combine(AppPaths.Components, "renodx-dlss", a.Id)));
        Assert.True(File.Exists(ComponentStore.AddonPathOf(AddonKind.RenoDx, b)));
        Assert.Equal(2, _store.LecramBuilds.Count);

        _store.RemoveAddon(AddonKind.Lecram, l10.Id);
        Assert.Equal([l11.Id], _store.LecramBuilds.Select(x => x.Id));
        Assert.Throws<InvalidDataException>(() => _store.RemoveAddon(AddonKind.Lecram, "../escape"));
    }

    [Fact]
    public void Label_ShowsTheUsersNameOrTheDetectedOne_AndRenameCanClearIt()
    {
        var build = LecramV11("v8.5.0-rc10-model-sdr1");
        Assert.Matches(@"^v8\.5\.0-rc10-model-sdr1 \(\d{4}-\d{2}-\d{2}\)$", build.Label);
        var sf = Sf(new Version(0, 2026, 928, 306));
        Assert.Matches(@"^0\.2026\.928\.306 \(\d{4}-\d{2}-\d{2}\)$", sf.Label);
        var v10 = LecramV10();
        Assert.Contains($" · {v10.FileVersion} (", v10.Label);

        var named = _store.RenameAddon(AddonKind.Lecram, build.Id, "  v10.1  ");
        Assert.Equal("v10.1", named.Label);
        Assert.Equal("v10.1", _store.LecramBuildFor(build.Id)!.Label);
        Assert.Equal(build.DetectedLabel, named.DetectedLabel);
        Assert.Equal(build.Label, _store.RenameAddon(AddonKind.Lecram, build.Id, "  ").Label);
        Assert.Null(_store.LecramBuildFor(build.Id)!.UserLabel);
    }

    [Fact]
    public void Build_MatchesItsManifestRecord_ByIdOrByTheTagOfOlderRecords()
    {
        var build = Sf(new Version(0, 2026, 928, 306));
        Assert.True(build.Matches(build.Id, null));
        Assert.True(build.Matches(null, "0.2026.928.306-" + build.Id[..12]));
        Assert.False(build.Matches(null, "0.2026.928.306-aaaaaaaaaaaa"));
        Assert.False(build.Matches(new string('a', 64), build.Tag));
        Assert.False(build.Matches(null, null));
    }

    [Fact]
    public void BuildFor_ExplicitIdIsStrict_NoIdFollowsTheDefaultThenTheNewest()
    {
        var older = Sf(new Version(0, 2026, 928, 306));
        var newer = Sf(new Version(0, 2026, 1016, 1704));
        Assert.Equal(newer.Id, _store.RenoDxBuildFor(null)!.Id);
        _settings.RenoDxId = older.Id;
        Assert.Equal(older.Id, _store.RenoDxBuildFor(null)!.Id);
        Assert.Equal(newer.Id, _store.RenoDxBuildFor(newer.Id)!.Id);
        Assert.Null(_store.RenoDxBuildFor(new string('a', 64)));
        _settings.RenoDxId = new string('b', 64);
        Assert.Equal(newer.Id, _store.RenoDxBuildFor(null)!.Id);
    }

    [Fact]
    public void EnsureDefaults_FillsAnUnsetOrRemovedDefault_AndLeavesAValidOneAlone()
    {
        Assert.False(_store.EnsureAddonDefaults(_settings));
        var a = Sf(new Version(0, 2026, 928, 306));
        var b = Sf(new Version(0, 2026, 1016, 1704));
        Assert.True(_store.EnsureAddonDefaults(_settings));
        Assert.Equal(b.Id, _settings.RenoDxId);
        Assert.Null(_settings.LecramId);

        _settings.RenoDxId = a.Id;
        Assert.False(_store.EnsureAddonDefaults(_settings));
        Assert.Equal(a.Id, _settings.RenoDxId);

        _store.RemoveAddon(AddonKind.RenoDx, a.Id);
        Assert.True(_store.EnsureAddonDefaults(_settings));
        Assert.Equal(b.Id, _settings.RenoDxId);
    }

    [Fact]
    public void Builds_AreListedOldestFirst_ByVersion_NotByImportTime()
    {
        var newer = Sf(new Version(0, 2026, 1016, 1704));
        var older = Sf(new Version(0, 2026, 928, 306));
        Assert.Equal([older.Id, newer.Id], _store.RenoDxBuilds.Select(b => b.Id));

        var release = LecramV11("v11.0.0", frameworkNoise: false);
        var beta = LecramV11("v11.0.0-beta.6");
        var old = LecramV11("v8.5.0-rc10-model-sdr1");
        Assert.Equal([old.Id, beta.Id, release.Id], _store.LecramBuilds.Select(b => b.Id));
    }

    [Fact]
    public void Import_BecomesTheDefaultOnlyWhenNewerThanTheCurrentDefault()
    {
        var newer = Sf(new Version(0, 2026, 1016, 1704));
        Assert.True(_store.AdoptImportedAddon(_settings, AddonKind.RenoDx, newer));
        var older = Sf(new Version(0, 2026, 928, 306));
        Assert.False(_store.AdoptImportedAddon(_settings, AddonKind.RenoDx, older));
        Assert.Equal(newer.Id, _settings.RenoDxId);

        _settings.RenoDxId = older.Id;
        Assert.True(_store.AdoptImportedAddon(_settings, AddonKind.RenoDx, newer));
        Assert.Equal(newer.Id, _settings.RenoDxId);
    }

    // ---------- migration ----------

    [Fact]
    public void Migration_MovesTheOldSingleFilesIntoTheLibrary_AndTheyBecomeTheDefaults()
    {
        var sf = Src(ComponentStore.RenoDxFile);
        FakeVersionDll.Write(sf, new Version(0, 2026, 928, 306), ComponentStore.RenoDxFile);
        var lecram = Src(V11File);
        File.WriteAllBytes(lecram, SyntheticBytes("v11.0.0-beta.6"));
        File.Copy(sf, Path.Combine(AppPaths.Components, ComponentStore.RenoDxFile));
        File.Copy(lecram, Path.Combine(AppPaths.Components, V11File));

        _store.MigrateAddons();

        Assert.False(File.Exists(Path.Combine(AppPaths.Components, ComponentStore.RenoDxFile)));
        Assert.False(File.Exists(Path.Combine(AppPaths.Components, V11File)));
        var sfBuild = Assert.Single(_store.RenoDxBuilds);
        var lecramBuild = Assert.Single(_store.LecramBuilds);
        Assert.Equal(FileUtil.Sha256(sf), sfBuild.Id);
        Assert.Equal(FileUtil.Sha256(lecram), lecramBuild.Id);
        Assert.Equal(LecramGeneration.V11, lecramBuild.Generation);
        Assert.True(File.Exists(ComponentStore.AddonPathOf(AddonKind.RenoDx, sfBuild)));
        Assert.True(File.Exists(ComponentStore.AddonPathOf(AddonKind.Lecram, lecramBuild)));

        Assert.True(_store.EnsureAddonDefaults(_settings));
        Assert.Equal(sfBuild.Id, _settings.RenoDxId);
        Assert.Equal(lecramBuild.Id, _settings.LecramId);

        _store.MigrateAddons();
        Assert.Single(_store.RenoDxBuilds);
        Assert.Single(_store.LecramBuilds);
    }

    [Fact]
    public void Migration_TakesBothLecramGenerations_AndLeavesAnUnreadableFileWhereItIs()
    {
        var v10 = Src(V10File);
        FakeVersionDll.Write(v10, new Version(0, 2026, 1013, 940), V10File);
        File.Copy(v10, Path.Combine(AppPaths.Components, V10File));
        File.WriteAllBytes(Path.Combine(AppPaths.Components, V11File), SyntheticBytes("v11.0.0-beta.6"));
        File.WriteAllText(Path.Combine(AppPaths.Components, ComponentStore.RenoDxFile), "not an add-on");

        _store.MigrateAddons();

        Assert.Equal([LecramGeneration.V11, LecramGeneration.V10], _store.LecramBuilds.Select(b => b.Generation!.Value));
        Assert.False(File.Exists(Path.Combine(AppPaths.Components, V10File)));
        Assert.False(File.Exists(Path.Combine(AppPaths.Components, V11File)));
        Assert.Empty(_store.RenoDxBuilds);
        Assert.True(File.Exists(Path.Combine(AppPaths.Components, ComponentStore.RenoDxFile)));
    }

    // ---------- install of the chosen build ----------

    [Fact]
    public async Task Install_PlacesTheChosenBuild_RecordsItsId_AndFollowsTheDefaultWhenNoneIsGiven()
    {
        var sfOld = Sf(new Version(0, 2026, 928, 306));
        var sfNew = Sf(new Version(0, 2026, 1016, 1704));
        var installer = new Installer(_store);
        _settings.RenoDxId = sfOld.Id;

        await installer.InstallAsync(_game, _root, Options(renoDx: true), null, default);
        Assert.Equal(sfOld.Id, FileUtil.Sha256(G(ComponentStore.RenoDxFile)));
        var m = InstallManifest.Read(_root)!;
        Assert.Equal(sfOld.Id, m.RenoDxId);
        Assert.Equal(sfOld.Tag, m.RenoDxTag);

        await installer.InstallAsync(_game, _root, Options(renoDxId: sfNew.Id, renoDx: true), null, default);
        Assert.Equal(sfNew.Id, FileUtil.Sha256(G(ComponentStore.RenoDxFile)));
        Assert.Equal(sfNew.Id, InstallManifest.Read(_root)!.RenoDxId);

        var missing = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            installer.InstallAsync(_game, _root, Options(renoDxId: new string('c', 64), renoDx: true), null, default));
        Assert.Contains("no longer in the library", missing.Message);
        Assert.Equal(sfNew.Id, FileUtil.Sha256(G(ComponentStore.RenoDxFile)));

        await installer.UninstallAsync(_game, _root, default);
        Assert.False(File.Exists(G(ComponentStore.RenoDxFile)));
    }

    [Fact]
    public async Task Install_LecramGenerationAndFileNameComeFromTheChosenBuild()
    {
        var v10 = LecramV10();
        var v11 = LecramV11();
        var installer = new Installer(_store);

        await installer.InstallAsync(_game, _root, Options(lecramId: v10.Id, lecram: true), null, default);
        Assert.True(File.Exists(G(V10File)));
        Assert.False(File.Exists(G(V11File)));
        Assert.Equal(v10.Id, InstallManifest.Read(_root)!.LecramId);

        await installer.InstallAsync(_game, _root, Options(lecramId: v11.Id, lecram: true), null, default);
        Assert.False(File.Exists(G(V10File)));
        Assert.Equal(v11.Id, FileUtil.Sha256(G(V11File)));
        var m = InstallManifest.Read(_root)!;
        Assert.Equal(V11File, m.LecramFile);
        Assert.Equal(v11.Id, m.LecramId);

        await installer.InstallAsync(_game, _root, Options(lecramId: v10.Id, lecram: true), null, default);
        Assert.True(File.Exists(G(V10File)));
        Assert.False(File.Exists(G(V11File)));
    }

    [Fact]
    public async Task Install_UsesTheDefaultLecramBuild_WhenTheGameHasNoOwn()
    {
        var v10 = LecramV10();
        var v11 = LecramV11();
        _settings.LecramId = v10.Id;
        await new Installer(_store).InstallAsync(_game, _root, Options(lecram: true), null, default);
        Assert.True(File.Exists(G(V10File)));

        _settings.LecramId = v11.Id;
        await new Installer(_store).InstallAsync(_game, _root, Options(lecram: true), null, default);
        Assert.True(File.Exists(G(V11File)));
        Assert.False(File.Exists(G(V10File)));
    }

    [Fact]
    public async Task KeepingInstalledVersions_HoldsTheInstalledAddonBuilds()
    {
        var sfOld = Sf(new Version(0, 2026, 928, 306));
        Sf(new Version(0, 2026, 1016, 1704));
        var v10 = LecramV10();
        LecramV11();
        await new Installer(_store).InstallAsync(_game, _root, Options(sfOld.Id, v10.Id, renoDx: true, lecram: true), null, default);
        var m = InstallManifest.Read(_root)!;

        var kept = Options(new string('1', 64), new string('2', 64), renoDx: true, lecram: true).KeepingInstalledVersions(m);
        Assert.Equal(sfOld.Id, kept.RenoDxId);
        Assert.Equal(v10.Id, kept.LecramId);

        var fromOlderRecord = Options("sf-choice", "lecram-choice").KeepingInstalledVersions(new InstallManifest());
        Assert.Equal("sf-choice", fromOlderRecord.RenoDxId);
        Assert.Equal("lecram-choice", fromOlderRecord.LecramId);
    }

    // ---------- combos ----------

    [Fact]
    public void Combo_IsSeededOnceWhenTheTestedSfAndLecramBuildsAreBothInTheLibrary()
    {
        Assert.False(_store.SeedAddonCombos(_settings));
        var sf = Sf(new Version(0, 2026, 928, 306));
        Sf(new Version(0, 2026, 1016, 1704));
        Assert.False(_store.SeedAddonCombos(_settings));
        LecramV11("v7.5.0-rc2");
        Assert.False(_store.SeedAddonCombos(_settings));
        Assert.Empty(_settings.AddonCombos);

        var lecram = LecramV11("v8.5.0-rc10-model-sdr1");
        Assert.True(_store.SeedAddonCombos(_settings));
        var combo = Assert.Single(_settings.AddonCombos);
        Assert.Equal("SF 0928 + Lecram 0928 (tested)", combo.Name);
        Assert.Equal(sf.Id, combo.RenoDxId);
        Assert.Equal(lecram.Id, combo.LecramId);

        Assert.False(_store.SeedAddonCombos(_settings));
        _settings.AddonCombos.Clear();
        Assert.False(_store.SeedAddonCombos(_settings));
        Assert.Empty(_settings.AddonCombos);
    }

    [Fact]
    public void Settings_KeepTheDefaultsPerGameChoicesAndCombosThroughJson()
    {
        var s = new AppSettings { RenoDxId = "sf", LecramId = "lec", TestedComboSeeded = true };
        s.AddonCombos.Add(new AddonCombo { Name = "Pair", RenoDxId = "sf", LecramId = "lec" });
        s.For("game").RenoDxId = "sf2";
        s.For("game").LecramId = "lec2";
        var back = JsonSerializer.Deserialize(JsonSerializer.Serialize(s, JsonCtx.Default.AppSettings), JsonCtx.Default.AppSettings)!;
        Assert.Equal("sf", back.RenoDxId);
        Assert.Equal("lec", back.LecramId);
        Assert.True(back.TestedComboSeeded);
        var combo = Assert.Single(back.AddonCombos);
        Assert.Equal(("Pair", "sf", "lec"), (combo.Name, combo.RenoDxId, combo.LecramId));
        Assert.Equal("sf2", back.Games["game"].RenoDxId);
        Assert.Equal("lec2", back.Games["game"].LecramId);
    }

    [Fact]
    public void GameCombo_SetsBothVersions_AndChangingEitherByHandClearsTheDisplay()
    {
        var sfA = Sf(new Version(0, 2026, 928, 306));
        var sfB = Sf(new Version(0, 2026, 1016, 1704));
        var lecA = LecramV11("v8.5.0-rc10-model-sdr1");
        var lecB = LecramV10();
        _settings.RenoDxId = sfB.Id;
        _settings.LecramId = lecB.Id;
        _settings.AddonCombos.Add(new AddonCombo { Name = "Pair", RenoDxId = sfA.Id, LecramId = lecA.Id });
        _settings.AddonCombos.Add(new AddonCombo { Name = "Dangling", RenoDxId = sfA.Id, LecramId = new string('d', 64) });
        var vm = NewGame();

        Assert.Equal(["No combo", "Pair"], vm.ComboChoices.Select(c => c.Label));
        Assert.Null(vm.ComboChoice!.Tag);
        Assert.Equal(sfB.Id, vm.EffectiveRenoDxId);
        Assert.Equal(lecB.Id, vm.EffectiveLecramId);

        vm.ComboChoice = vm.ComboChoices.Single(c => c.Tag == "Pair");
        Assert.Equal(sfA.Id, _settings.Games[vm.Id].RenoDxId);
        Assert.Equal(lecA.Id, _settings.Games[vm.Id].LecramId);
        Assert.Equal(sfA.Id, vm.EffectiveRenoDxId);
        Assert.Equal(lecA.Id, vm.EffectiveLecramId);
        Assert.Equal(sfA.Id, vm.RenoDxChoice!.Tag);
        Assert.Equal("Pair", vm.ComboChoice!.Tag);
        Assert.Equal("Pair", NewGameFor(vm.Id).ComboChoice!.Tag);

        vm.RenoDxChoice = vm.RenoDxChoices.Single(c => c.Tag == sfB.Id);
        Assert.Null(vm.ComboChoice!.Tag);
        Assert.Equal(lecA.Id, _settings.Games[vm.Id].LecramId);
        Assert.Equal(sfB.Id, _settings.Games[vm.Id].RenoDxId);

        vm.ComboChoice = vm.ComboChoices.Single(c => c.Tag == "Pair");
        vm.LecramChoice = vm.LecramChoices.Single(c => c.Tag == lecB.Id);
        Assert.Null(vm.ComboChoice!.Tag);

        var options = vm.BuildOptions(false, false);
        Assert.Equal(vm.EffectiveRenoDxId, options.RenoDxId);
        Assert.Equal(lecB.Id, options.LecramId);

        vm.ComboChoice = vm.ComboChoices.Single(c => c.Tag == "Pair");
        vm.ComboChoice = vm.ComboChoices[0];
        Assert.Null(_settings.Games[vm.Id].RenoDxId);
        Assert.Null(_settings.Games[vm.Id].LecramId);
        Assert.Equal(sfB.Id, vm.EffectiveRenoDxId);
    }

    private GameViewModel NewGameFor(string root) =>
        new(new GameInfo { Id = root, Root = root, Name = "VM game" }, new Services { Settings = _settings, Store = _store, Installer = new Installer(_store) });

    [Fact]
    public void GameDropdowns_OfferUseDefaultWithTheDefaultsLabel_AndFlagAMissingPin()
    {
        var sf = Sf(new Version(0, 2026, 928, 306));
        _settings.RenoDxId = sf.Id;
        var vm = NewGame();
        Assert.Equal($"Use default ({sf.Label})", vm.RenoDxChoices[0].Label);
        Assert.Equal("Use default (none imported)", vm.LecramChoices[0].Label);
        Assert.Equal(new string?[] { null, sf.Id }, vm.RenoDxChoices.Select(c => c.Tag));

        _settings.For(vm.Id).RenoDxId = new string('e', 64);
        vm.RebuildAddonChoices();
        Assert.Equal("Missing add-on file · import again", vm.RenoDxChoice!.Label);
    }

    // ---------- compat warning and options per game ----------

    [Fact]
    public void SfWarning_UsesTheBuildSelectedForTheGame_NotJustTheDefault()
    {
        var tested = Sf(new Version(0, 2026, 928, 306));
        var newer = Sf(new Version(0, 2026, 1016, 1704));
        LecramV10();
        _settings.RenoDxId = tested.Id;
        var vm = NewGame();
        vm.Components.First(c => c.Key == "renodx").Enabled = true;
        vm.Components.First(c => c.Key == "lecram").Enabled = true;
        Assert.True(vm.IsReShadeOnly);
        Assert.Null(vm.SfLecramWarning);
        Assert.Null(_store.SfLecramWarning(true, true, tested.Id));

        vm.RenoDxChoice = vm.RenoDxChoices.Single(c => c.Tag == newer.Id);
        Assert.NotNull(vm.SfLecramWarning);
        Assert.True(vm.HasSfLecramWarning);
        Assert.Contains("0.2026.1016.1704", vm.SfLecramWarning);
        Assert.NotNull(_store.SfLecramWarning(true, true, newer.Id));

        vm.Components.First(c => c.Key == "lecram").Enabled = false;
        Assert.Null(vm.SfLecramWarning);
        Assert.Null(_store.SfLecramWarning(true, false, newer.Id));

        _settings.RenoDxId = newer.Id;
        var other = NewGame("other-game");
        other.Components.First(c => c.Key == "renodx").Enabled = true;
        other.Components.First(c => c.Key == "lecram").Enabled = true;
        Assert.NotNull(other.SfLecramWarning);
        other.RenoDxChoice = other.RenoDxChoices.Single(c => c.Tag == tested.Id);
        Assert.Null(other.SfLecramWarning);
    }

    [Fact]
    public void GameLecramOptions_FollowTheGenerationOfTheGamesOwnBuild()
    {
        var v10 = LecramV10();
        var v11 = LecramV11();
        _settings.LecramId = v10.Id;
        var vm = NewGame();
        vm.DetailTab = "Config";
        GameOptionViewModel Row(string key) => vm.LecramRows.Single(r => r.Label == key);
        const string mask = "Character mask";
        const string uplift = "Neural rendering";
        const string style = "Style";

        Assert.Equal(LecramGeneration.V10, vm.EffectiveLecramGeneration);
        Assert.True(Row(mask).IsAvailable);
        Assert.False(Row(uplift).IsAvailable);
        Assert.DoesNotContain(Row(style).DisplayChoices, c => c.Label == "Model A");

        vm.LecramChoice = vm.LecramChoices.Single(c => c.Tag == v11.Id);
        Assert.Equal(LecramGeneration.V11, vm.EffectiveLecramGeneration);
        Assert.False(Row(mask).IsAvailable);
        Assert.True(Row(uplift).IsAvailable);
        Assert.Contains(Row(style).DisplayChoices, c => c.Label == "Model A");

        // Another game keeps following the default build, and the shared Settings definitions stay as the default's generation set them.
        var other = NewGame("other-game");
        other.DetailTab = "Config";
        Assert.True(other.LecramRows.Single(r => r.Label == mask).IsAvailable);
        Assert.False(other.LecramRows.Single(r => r.Label == uplift).IsAvailable);

        vm.LecramChoice = vm.LecramChoices[0];
        Assert.Equal(LecramGeneration.V10, vm.EffectiveLecramGeneration);
        Assert.True(Row(mask).IsAvailable);
    }

    [Fact]
    public void GameRows_ShowTheChosenBuildAsLatest_AndUpdateWhenTheInstalledOneDiffers()
    {
        var a = Sf(new Version(0, 2026, 928, 306));
        var b = Sf(new Version(0, 2026, 1016, 1704));
        _settings.RenoDxId = a.Id;
        var vm = NewGame();
        var row = vm.Components.First(c => c.Key == "renodx");
        Assert.Equal(a.Name, row.Latest);

        vm.RenoDxChoice = vm.RenoDxChoices.Single(c => c.Tag == b.Id);
        Assert.Equal(b.Name, row.Latest);

        _settings.For(vm.Id).RenoDxId = null;
        vm.RebuildAddonChoices();
        vm.RefreshStatus();
        Assert.Equal(a.Name, row.Latest);
    }
}
