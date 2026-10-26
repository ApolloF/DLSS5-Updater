using System.Text;
using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>
/// The libraries of ShortFuse's and Lecram's add-ons: storage, migration, labels, default and per-game choice, installs of the
/// chosen build and the options that follow a build's generation. Fixtures only: a copy of the test assembly with a
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
        _store.PairTests = () => _settings.AddonPairTests;
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
        ConfirmedConflict = renoDx && lecram ? Installer.ConflictKey(NrAddons.Conflicts([NrAddon.RenoDx, NrAddon.Lecram])) : null,
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
        // The string layout of v11 beta.18 / beta.22 / beta.29 (docs/addons/lecram-dlss5.md): the add-on description ends in "(v11.0.0-beta.NN)".
        Assert.Equal("v11.0.0-beta.22", Label(
            "v11.0.0-H\0dlss5-nr11.0.0-beta.22nr_inputs\0v11.0.0-beta.22NR onNR offbytessha256\0" +
            "opens the DLSS 5 NR menu; Shift+key opens ReShade (v11.0.0-beta.22)\0v11.0.0-beta.22\0"));
        Assert.Equal("v11.0.0-beta.29", Label(
            "v11.0.0-H\0dlss5-nr11.0.0-beta.29nr_inputs\0v11.0.0-beta.29NR onNR offbytessha256\0" +
            "opens the DLSS 5 NR menu; Shift+key opens ReShade (v11.0.0-beta.29)\0v11.0.0-beta.29\0"));
        Assert.Equal("v11.0.0-beta.18", Label(
            "v11.0.0-H\0dlss5-nr11.0.0-beta.18nr_inputs\0v11.0.0-beta.18NR onNR offbytessha256\0" +
            "opens the DLSS 5 NR menu; Shift+key opens ReShade (v11.0.0-beta.18)\0v11.0.0-beta.18\0"));
        // beta.45 (Patch 10) and beta.48 (Patch 11) dropped the "dlss5-nr11.0.0-beta.NN" description string; the label stays the same.
        Assert.Equal("v11.0.0-beta.48", Label(
            "v11.0.0-H\0v11.0.0-beta.48NR onNR offbytessha256Offline St\0" +
            "opens the DLSS 5 NR menu; Shift+key opens ReShade (v11.0.0-beta.48)\0v11.0.0-beta.48\0"));
        Assert.Equal("v11.0.0-beta.45", Label(
            "v11.0.0-H\0v11.0.0-beta.45NR onNR offbytessha256Offline St\0" +
            "opens the DLSS 5 NR menu; Shift+key opens ReShade (v11.0.0-beta.45)\0v11.0.0-beta.45\0"));
        var build = LecramV11("v11.0.0-beta.22NR on\0v11.0.0-H\0v11.0.0-beta.22\0v11.0.0-beta.22");
        Assert.Equal(LecramGeneration.V11, build.Generation);
        Assert.Equal("v11.0.0-beta.22", build.DetectedLabel);
    }

    [Fact]
    public void Label_ReadsTheV11_9AndV12Layouts()
    {
        // v11.9.0 and v12.0.0 carry their version once glued to "NR" and once bare (docs/addons/lecram-dlss5.md); no beta suffix.
        Assert.Equal("v12.0.0", Label("v12.0.0NR onNR offbytessha256\0v12.0.0\0"));
        Assert.Equal("v11.9.0", Label("v11.9.0NR onNR offbytessha256\0v11.9.0\0"));
        var build = LecramV11("v12.0.0NR on\0v12.0.0", frameworkNoise: false);
        Assert.Equal(LecramGeneration.V11, build.Generation);
        Assert.Equal("v12.0.0", build.DetectedLabel);
    }

    [Fact]
    public void Label_IsNeverTheDlssSdkVersion()
    {
        // Older v10 builds hold no label of their own, only the DLSS SDK's v310.8.0.
        Assert.Null(Label("DLSS SDK v310.8.0\0"));
        Assert.Equal("v8.5.0-rc10", Label("v310.8.0\0v8.5.0-rc10\0v6.1.2\0"));
        Assert.True(ComponentStore.IsSdkLabel("v310.8.0"));
        Assert.True(ComponentStore.IsSdkLabel(" V310.10.0 "));
        Assert.False(ComponentStore.IsSdkLabel("v12.0.0"));
        Assert.False(ComponentStore.IsSdkLabel("310.8.0"));
        Assert.False(ComponentStore.IsSdkLabel("v99.0.0"));
        Assert.True(ComponentStore.IsLecramVersionLabel("v7.0.0-rc8"));
        Assert.False(ComponentStore.IsLecramVersionLabel("v310.8.0"));
        Assert.False(ComponentStore.IsLecramVersionLabel("0.2026.923.307"));
        Assert.False(ComponentStore.IsLecramVersionLabel(""));
        Assert.False(ComponentStore.IsLecramVersionLabel(null));
    }

    [Fact]
    public void ALecramBuildLabelledByTheSdkString_IsRelabelledOnLoad_AndRanksBelowLabelledBuilds()
    {
        var old = LecramV10(new Version(0, 2026, 828, 517));
        var rc10 = LecramV11("v8.5.0-rc10", frameworkNoise: false);
        // What an earlier version of the app stored for a v10 binary whose only version string is the SDK's. The fixture (a copy of
        // the test assembly) carries other vX.Y.Z strings, so the stored file is replaced by bytes with only the SDK's.
        var file = ComponentStore.AddonPathOf(AddonKind.Lecram, old);
        File.WriteAllBytes(file, [(byte)'M', (byte)'Z', .. Encoding.ASCII.GetBytes("\0DLSS SDK v310.8.0\0")]);
        var record = Path.Combine(Path.GetDirectoryName(file)!, "version.json");
        File.WriteAllText(record, JsonSerializer.Serialize(old with { DetectedLabel = "v310.8.0" }, JsonCtx.Default.AddonBuild));

        var reloaded = new ComponentStore(new GitHubClient(), () => true);
        var builds = reloaded.LecramBuilds;

        Assert.Equal([rc10.Id, old.Id], builds.Select(b => b.Id));
        Assert.Equal("0.2026.828.517", builds[1].DetectedLabel);
        Assert.Equal("0.2026.828.517", JsonSerializer.Deserialize(File.ReadAllText(record), JsonCtx.Default.AddonBuild)!.DetectedLabel);
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
    public void Label_OfAnImportedBuild_ComesFromItsBinary_AndSfWithoutABuildTimeUsesItsFileVersion()
    {
        var lecram = LecramV11("v8.5.0-rc10-model-sdr1");
        Assert.Equal("v8.5.0-rc10-model-sdr1", lecram.DetectedLabel);
        var sf = Sf(new Version(0, 2026, 928, 306));
        Assert.Equal("0.2026.928.306", sf.FileVersion);
        Assert.Equal("26.0928.0306", sf.DetectedLabel);
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
        Assert.Equal("26.0928.0306", json.GetProperty("DetectedLabel").GetString());
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
        Assert.Equal("26.0928.0306", sf.Label);
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
    public void Builds_AreListedNewestFirst_ByVersion_NotByImportTime()
    {
        var newer = Sf(new Version(0, 2026, 1016, 1704));
        var older = Sf(new Version(0, 2026, 928, 306));
        Assert.Equal([newer.Id, older.Id], _store.RenoDxBuilds.Select(b => b.Id));

        var release = LecramV11("v11.0.0", frameworkNoise: false);
        var beta = LecramV11("v11.0.0-beta.6");
        var old = LecramV11("v8.5.0-rc10-model-sdr1");
        Assert.Equal([release.Id, beta.Id, old.Id], _store.LecramBuilds.Select(b => b.Id));
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

    [Fact]
    public void LecramDefault_FollowsTheNewestBuild_WithoutAnySettingsWrite()
    {
        Assert.False(_store.EnsureAddonDefaults(_settings));
        var old = LecramV11("v11.0.0-beta.6");
        Assert.False(_store.EnsureAddonDefaults(_settings));
        Assert.False(_store.AdoptImportedAddon(_settings, AddonKind.Lecram, old));
        Assert.Null(_settings.LecramId);
        Assert.Equal(old.Id, _store.LecramBuildFor(null)!.Id);

        var newer = LecramV11("v11.0.0-beta.22");
        Assert.False(_store.AdoptImportedAddon(_settings, AddonKind.Lecram, newer));
        Assert.Null(_settings.LecramId);
        Assert.Equal(newer.Id, _store.LecramBuildFor(null)!.Id);

        var older = LecramV11("v11.0.0-beta.3");
        Assert.False(_store.AdoptImportedAddon(_settings, AddonKind.Lecram, older));
        Assert.Equal(newer.Id, _store.LecramBuildFor(null)!.Id);
    }

    [Fact]
    public void LecramDefault_APinStaysPinned_EvenForANewerImport_ARemovedPinGoesBackToNewest()
    {
        var pinned = LecramV11("v11.0.0-beta.22");
        _settings.LecramId = pinned.Id;
        Assert.False(_store.EnsureAddonDefaults(_settings));
        Assert.False(_store.AdoptImportedAddon(_settings, AddonKind.Lecram, LecramV11("v11.0.0-beta.6")));
        Assert.Equal(pinned.Id, _settings.LecramId);

        var newer = LecramV11("v11.0.0-beta.45");
        Assert.False(_store.AdoptImportedAddon(_settings, AddonKind.Lecram, newer));
        Assert.Equal(pinned.Id, _settings.LecramId);

        _store.RemoveAddon(AddonKind.Lecram, pinned.Id);
        Assert.True(_store.EnsureAddonDefaults(_settings));
        Assert.Null(_settings.LecramId);
        Assert.Equal(newer.Id, _store.LecramBuildFor(null)!.Id);
    }

    [Fact]
    public void LecramNewestMigration_ResetsThePinOnce_AndLeavesGamePicksAndRenoDxAlone()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{"LecramId":"old-auto-pin","RenoDxId":"sf","Games":{"game":{"LecramId":"lec-game"}}}""");
        var first = AppSettings.Load();
        Assert.Null(first.LecramId);
        Assert.True(first.LecramNewestMigrated);
        Assert.Equal("sf", first.RenoDxId);
        Assert.Equal("lec-game", first.Games["game"].LecramId);

        first.LecramId = "user-pick";
        first.Save();
        Assert.Equal("user-pick", AppSettings.Load().LecramId);
    }

    [Fact]
    public void LecramNewestMigration_FreshInstallsAreAlreadyMigrated()
    {
        Assert.True(AppSettings.Load().LecramNewestMigrated);
        var s = AppSettings.Load();
        s.LecramId = "pick";
        s.Save();
        Assert.Equal("pick", AppSettings.Load().LecramId);
    }

    [Fact]
    public void RenoDxDefault_IsStillPinnedById()
    {
        var sf = Sf(new Version(0, 2026, 928, 306));
        Assert.True(_store.EnsureAddonDefaults(_settings));
        Assert.Equal(sf.Id, _settings.RenoDxId);
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
        Assert.Null(_settings.LecramId);
        Assert.Equal(lecramBuild.Id, _store.LecramBuildFor(null)!.Id);

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

        Assert.Equal([LecramGeneration.V10, LecramGeneration.V11], _store.LecramBuilds.Select(b => b.Generation!.Value));
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

    private GameViewModel NewGameFor(string root) =>
        new(new GameInfo { Id = root, Root = root, Name = "VM game" }, new Services { Settings = _settings, Store = _store, Installer = new Installer(_store) });

    [Fact]
    public void GameDropdowns_OfferUseDefaultWithTheDefaultsLabel_AndFlagAMissingPin()
    {
        var sf = Sf(new Version(0, 2026, 928, 306));
        _settings.RenoDxId = sf.Id;
        var vm = NewGame();
        Assert.Equal($"Default · {sf.Name}", vm.RenoDxChoices[0].Label);
        Assert.Equal("Default · none imported", vm.LecramChoices[0].Label);
        Assert.Equal(new string?[] { null, sf.Id }, vm.RenoDxChoices.Select(c => c.Tag));

        _settings.For(vm.Id).RenoDxId = new string('e', 64);
        vm.RebuildAddonChoices();
        Assert.Equal("Missing add-on file · import again", vm.RenoDxChoice!.Label);
    }

    // ---------- compat warning and options per game ----------

    [Fact]
    public void PairStatus_FollowsBothGameBuilds_AndMarkCommandsPersistTheResolvedDefaults()
    {
        var sf = Sf(new Version(0, 2026, 928, 306));
        var newer = Sf(new Version(0, 2026, 1016, 1704));
        var lecram = LecramV10();
        var otherLecram = LecramV11();
        _settings.RenoDxId = sf.Id;
        _settings.LecramId = lecram.Id;
        var vm = NewGame();
        vm.Components.First(c => c.Key == "renodx").Enabled = true;
        vm.Components.First(c => c.Key == "lecram").Enabled = true;
        Assert.Equal(AddonPairStatus.Untested, vm.PairStatus);
        Assert.Null(vm.SfLecramMessage);
        Assert.False(vm.IsPairCrashed);
        Assert.True(vm.MarkPairWorkingCommand.CanExecute(null));
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.MarkPairWorkingCommand.Execute(null);
        Assert.Null(vm.SfLecramMessage);
        Assert.Equal(AddonPairStatus.Works, vm.PairStatus);
        Assert.Contains(nameof(vm.PairStatus), changed);
        var saved = Assert.Single(AppSettings.Load().AddonPairTests);
        Assert.Equal((sf.Id, lecram.Id, AddonPairStatus.Works), (saved.RenoDxId, saved.LecramId, saved.Status));
        Assert.NotNull(saved.Marked);

        vm.RenoDxChoice = vm.RenoDxChoices.Single(c => c.Tag == newer.Id);
        Assert.Null(vm.SfLecramMessage);
        vm.MarkPairCrashedCommand.Execute(null);
        Assert.True(vm.IsPairCrashed);
        Assert.Contains("crashed before (marked ", vm.SfLecramMessage);
        Assert.Contains(_store.PairTestFor(newer.Id, lecram.Id)!.Marked!.Value.ToLocalTime().ToString("yyyy-MM-dd"), vm.SfLecramMessage);
        Assert.Equal(2, AppSettings.Load().AddonPairTests.Count);

        vm.LecramChoice = vm.LecramChoices.Single(c => c.Tag == otherLecram.Id);
        Assert.False(vm.IsPairCrashed);
        Assert.Equal(AddonPairStatus.Untested, vm.PairStatus);
        vm.MarkPairWorkingCommand.Execute(null);
        Assert.Null(vm.SfLecramMessage); // A newer SF build can be known working too.
        Assert.Equal(AddonPairStatus.Crashed, _store.PairTestFor(newer.Id, lecram.Id)!.Status);
        vm.Components.First(c => c.Key == "lecram").Enabled = false;
        Assert.Null(vm.SfLecramMessage);
        Assert.False(vm.MarkPairCrashedCommand.CanExecute(null));
    }

    [Fact]
    public void Settings_LoadLegacyCombosAndKeepSeparateGameBuildChoices()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{"AddonCombos":[{"Name":"Pair","RenoDxId":"sf","LecramId":"lec"}],"TestedComboSeeded":true,"Games":{"game":{"RenoDxId":"sf-game","LecramId":"lec-game"}}}""");
        var old = AppSettings.Load();
        Assert.Empty(old.AddonPairTests);
        Assert.Equal("sf-game", old.Games["game"].RenoDxId);
        Assert.Equal("lec-game", old.Games["game"].LecramId);
        Assert.DoesNotContain("AddonCombos", JsonSerializer.Serialize(old, JsonCtx.Default.AppSettings));

        Assert.Equal(AddonPairStatus.Untested, new AddonPairTest("sf", "lec").Status);
        var sf = Sf(new Version(0, 2026, 928, 306));
        var lecram = LecramV11("v8.5.0-rc10-model-sdr1");
        Assert.True(_store.SeedTestedAddonPair(old));
        Assert.Equal(AddonPairStatus.Works, Assert.Single(old.AddonPairTests).Status);
        Assert.False(_store.SeedTestedAddonPair(old));
        _store.MarkAddonPair(old, sf.Id, lecram.Id, AddonPairStatus.Crashed);
        Assert.False(_store.SeedTestedAddonPair(old));
        Assert.Equal(AddonPairStatus.Crashed, Assert.Single(old.AddonPairTests).Status);
    }

    [Fact]
    public void PairStatus_ReplacesOnlyTheExactPair_AndSurvivesBuildRename()
    {
        var sf = Sf(new Version(0, 2026, 1016, 1704));
        var lecram = LecramV10();
        _store.MarkAddonPair(_settings, sf.Id, lecram.Id, AddonPairStatus.Crashed);
        _store.MarkAddonPair(_settings, sf.Id.ToUpperInvariant(), lecram.Id.ToUpperInvariant(), AddonPairStatus.Works);
        Assert.Single(_settings.AddonPairTests);
        _settings.Save();
        _store.RenameAddon(AddonKind.RenoDx, sf.Id, "My SF");
        _store.RenameAddon(AddonKind.Lecram, lecram.Id, "My Lecram");
        Assert.Contains("ShortFuse My SF + Lecram My Lecram", _store.KnownWorkingPairs);
        Assert.Equal(AddonPairStatus.Works, Assert.Single(AppSettings.Load().AddonPairTests).Status);
        Assert.Throws<FileNotFoundException>(() => _store.MarkAddonPair(_settings, new string('f', 64), lecram.Id, AddonPairStatus.Works));
        Assert.Throws<ArgumentOutOfRangeException>(() => _store.MarkAddonPair(_settings, sf.Id, lecram.Id, (AddonPairStatus)100));
    }

    [Fact]
    public void PairStatus_FailedSaveKeepsTheOldResult_AndDoesNotPublishAChange()
    {
        var sf = Sf(new Version(0, 2026, 1016, 1704));
        var lecram = LecramV10();
        _store.MarkAddonPair(_settings, sf.Id, lecram.Id, AddonPairStatus.Works);
        var previous = Assert.Single(_settings.AddonPairTests);
        var notified = false;
        _store.PairStatusChanged += () => notified = true;
        File.Delete(AppPaths.SettingsFile);
        Directory.CreateDirectory(AppPaths.SettingsFile);
        Assert.Throws<UnauthorizedAccessException>(() => _store.MarkAddonPair(_settings, sf.Id, lecram.Id, AddonPairStatus.Crashed));
        Assert.Equal(previous, Assert.Single(_settings.AddonPairTests));
        Assert.False(notified);
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

    // ---------- one NR add-on per game ----------

    private static bool On(GameViewModel vm, string key) => vm.Components.First(c => c.Key == key).Enabled;
    private static void Turn(GameViewModel vm, string key, bool on) => vm.Components.First(c => c.Key == key).Enabled = on;

    [Fact]
    public void GameToggles_AllowAnyMix_AndWarnAboutTheClash()
    {
        var vm = NewGame();
        Turn(vm, "renodx", true);
        Turn(vm, "lecram", true);
        Assert.True(On(vm, "renodx") && On(vm, "lecram"));
        Assert.Contains("NR runs twice", vm.NrAddonWarning);
        Turn(vm, "dfc", true);
        Assert.True(On(vm, "renodx") && On(vm, "lecram") && On(vm, "dfc"));
        Turn(vm, "uplift", true);
        Assert.True(On(vm, "uplift") && On(vm, "renodx"));
        Turn(vm, "dfc", false);
        Turn(vm, "uplift", false);
        Turn(vm, "lecram", false);
        Assert.Null(vm.NrAddonWarning);
    }

    [Fact]
    public void GameWithBothInstalled_ShowsBoth()
    {
        var root = Path.Combine(_tmp, "both-game");
        Directory.CreateDirectory(root);
        new InstallManifest { Mode = InstallMode.ReShadeOnly, ReShade = true, DlssNr = true, RenoDx = true, Lecram = true }.Save(root);
        var vm = NewGameFor(root);
        Assert.True(On(vm, "renodx") && On(vm, "lecram"));
        Assert.NotNull(vm.NrAddonWarning);
    }

    [Fact]
    public void EveryComponentRow_HasAHelpTopic()
    {
        var vm = NewGame();
        Assert.All(vm.Components, c => Assert.NotNull(HelpTopics.Get(c.HelpTopic)));
    }
}
