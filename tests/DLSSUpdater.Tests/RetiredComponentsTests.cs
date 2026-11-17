using System.Text.Json.Nodes;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// OptiScaler-NR, Deep Fried Chicken and DLSS5-Feeder left the app. Settings that named them move on, and games an older version
/// set up with them still uninstall to exactly what they were, while their next install replaces OptiScaler-NR with F5 and takes
/// DFC and Feeder out. Fake files only.
/// </summary>
public sealed class RetiredComponentsTests : IDisposable
{
    private const string NrTag = "v0.9.72";
    private const string F5Tag = "v0.1.38-f5";
    private const string ReleaseIni = "[Menu]\nShortcutKey=auto\n[Plugins]\nLoadReshade=auto\n[DlssNr]\nEnabled=auto\n";

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-retired-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly string _target;
    private readonly ComponentStore _store;

    public RetiredComponentsTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, @"Game\Binaries\Win64");
        _store = new ComponentStore(new GitHubClient(), () => true);
        Write(Path.Combine(AppPaths.Components, ComponentStore.DlssNrFile), "NR");
        Write(Path.Combine(AppPaths.Components, ComponentStore.ReShadeFile), "RESHADE");

        // A game with another mod's dxgi.dll and files of its own where the retired components went.
        Write(Path.Combine(_target, "Game-Win64-Shipping.exe"), "EXE");
        Write(Path.Combine(_target, "dxgi.dll"), "OTHER-MOD");
        Write(Path.Combine(_target, ComponentStore.DfcConfigFile), "arm=1\nmine=1\n");
        Write(Path.Combine(_target, "dlss5-feed.addon64"), "FOREIGN-FEEDER");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); }
        catch (IOException) { }
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string G(string rel) => Path.Combine(_target, rel);

    private Dictionary<string, string> Snapshot() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(_root, f), File.ReadAllText, StringComparer.OrdinalIgnoreCase);

    private GameInfo Game() => GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));

    private static InstallOptions ReShadeOnly => new() { Mode = InstallMode.ReShadeOnly, ReShade = true, DlssNr = true, Proxy = "dxgi.dll" };

    private string Rel(string full) => InstallManifest.Entry(full, _target, _root);

    /// <summary>
    /// What 2.x-3.1 left in a game with Deep Fried Chicken 4.x: its set beside the exe and in its folders (recorded in DfcFiles),
    /// NVIDIA's SR runtime it needed (DfcRuntimes), and the game's own cfg backed up before the app wrote arm=0 into it.
    /// </summary>
    private void PlantLegacyDfc()
    {
        var m = InstallManifest.Read(_target)!;
        string[] set = [ComponentStore.DfcFile, "deep-fried-chicken-nvngx.dll", "deep-fried-chicken-present-support.dll",
            @"deep-fried-chicken-neural\model.onnx", Path.Combine(ComponentStore.ShaderDir, "DFC_OutputState.addonfx")];
        foreach (var rel in set)
        {
            Write(G(rel), "DFC-" + rel);
            m.AddFile(Rel(G(rel)));
            m.DfcFiles.Add(Rel(G(rel)));
        }
        Write(G("nvngx_dlss.dll"), "SR-FOR-DFC");
        m.AddFile(Rel(G("nvngx_dlss.dll")));
        m.DfcRuntimes.Add(Rel(G("nvngx_dlss.dll")));

        var cfg = Rel(G(ComponentStore.DfcConfigFile));
        var backup = Path.Combine(InstallManifest.BackupDirFor(_target), cfg);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(G(ComponentStore.DfcConfigFile), backup);
        m.Backups.Add(new BackupEntry { Original = cfg, Backup = cfg, Kind = "file" });
        File.WriteAllText(G(ComponentStore.DfcConfigFile), "arm=0\nmine=1\n");
        m.DfcCfg["DFC/arm"] = "0";

        m.Dfc = true;
        m.DfcTag = "4.0.6-0123456789ab";
        m.DfcId = "0123456789abcdef";
        m.Save(_target);
    }

    [Fact]
    public async Task LegacyDfcInstall_UninstallLeavesTheGameAsItWas()
    {
        var before = Snapshot();
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, ReShadeOnly, null, default);
        PlantLegacyDfc();
        Assert.Contains(InstallManifest.Read(_target)!.InstalledComponents(), c => c.Id == ComponentRegistry.Dfc);

        await installer.UninstallAsync(Game(), _target, default);

        Assert.Equal(before, Snapshot());
        Assert.False(Directory.Exists(G("deep-fried-chicken-neural")));
    }

    [Fact]
    public async Task LegacyDfcInstall_NextInstallTakesItOut_AndUninstallStillRestoresEverything()
    {
        var before = Snapshot();
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, ReShadeOnly, null, default);
        PlantLegacyDfc();

        await installer.InstallAsync(Game(), _target, ReShadeOnly, null, default);

        var m = InstallManifest.Read(_target)!;
        Assert.False(m.Dfc);
        Assert.Empty(m.DfcFiles);
        Assert.Empty(m.DfcRuntimes);
        Assert.Empty(m.DfcCfg);
        Assert.DoesNotContain(m.InstalledComponents(), c => c.Id == ComponentRegistry.Dfc);
        Assert.False(File.Exists(G(ComponentStore.DfcFile)));
        Assert.False(File.Exists(G("nvngx_dlss.dll")));
        Assert.False(Directory.Exists(G("deep-fried-chicken-neural")));
        Assert.Equal("arm=1\nmine=1\n", File.ReadAllText(G(ComponentStore.DfcConfigFile)));
        Assert.Equal("RESHADE", File.ReadAllText(G("dxgi.dll")));
        Assert.Equal("NR", File.ReadAllText(G(ComponentStore.DlssNrFile)));

        await installer.UninstallAsync(Game(), _target, default);
        Assert.Equal(before, Snapshot());
    }

    /// <summary>What 3.0-3.1 recorded for DLSS5-Feeder: a generic component placed over a copy that wasn't ours.</summary>
    private async Task PlantLegacyFeeder(Installer installer)
    {
        var src = Path.Combine(_tmp, "feeder-pkg");
        Write(Path.Combine(src, "dlss5-feed.addon64"), "FEEDER-64");
        Write(Path.Combine(src, "DLSS5_Feed.fx"), "FEEDER-FX");
        await installer.InstallComponentAsync(Game(), _target, new ComponentPlan(ComponentRegistry.Feeder)
        {
            BuildId = "v1.19.0", Version = "1.19.0",
            Files = [(Path.Combine(src, "dlss5-feed.addon64"), G("dlss5-feed.addon64")), (Path.Combine(src, "DLSS5_Feed.fx"), G(Path.Combine(ComponentStore.ShaderDir, "DLSS5_Feed.fx")))],
        }, default);
        Assert.Equal("FEEDER-64", File.ReadAllText(G("dlss5-feed.addon64")));
    }

    [Fact]
    public async Task LegacyFeeder_NextInstallTakesItOut_AndPutsBackWhatItReplaced()
    {
        var before = Snapshot();
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, ReShadeOnly, null, default);
        await PlantLegacyFeeder(installer);

        await installer.InstallAsync(Game(), _target, ReShadeOnly, null, default);

        var m = InstallManifest.Read(_target)!;
        Assert.Null(m.Component(ComponentRegistry.Feeder));
        Assert.Equal("FOREIGN-FEEDER", File.ReadAllText(G("dlss5-feed.addon64")));
        Assert.False(File.Exists(G(Path.Combine(ComponentStore.ShaderDir, "DLSS5_Feed.fx"))));
        Assert.Equal("RESHADE", File.ReadAllText(G("dxgi.dll")));

        await installer.UninstallAsync(Game(), _target, default);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task LegacyFeederAndDfc_UninstallLeavesTheGameAsItWas()
    {
        var before = Snapshot();
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, ReShadeOnly, null, default);
        PlantLegacyDfc();
        await PlantLegacyFeeder(installer);
        Assert.Equal("ReShade + DFC + Feeder", InstallManifest.Read(_target)!.ModLabel());

        await installer.UninstallAsync(Game(), _target, default);

        Assert.Equal(before, Snapshot());
    }

    private void SeedOpti(string tag, bool forwarder)
    {
        var pkg = Path.Combine(ComponentStore.TagDir(Component.OptiScaler, tag), "pkg");
        Write(Path.Combine(pkg, "OptiScaler.dll"), "OPTI-" + tag);
        Write(Path.Combine(pkg, "OptiScaler.ini"), ReleaseIni);
        Write(Path.Combine(pkg, @"OptiScaler\libxess.dll"), "XESS-" + tag);
        if (forwarder) Write(Path.Combine(pkg, "nvngx.dll_dlssnr.dll"), "FORWARDER");
        ComponentStore.MarkComplete(Component.OptiScaler, new ReleaseInfo { Tag = tag });
    }

    [Fact]
    public async Task LegacyOptiScalerNr_IsNeverListedOrKept_AndItsUpdateInstallsF5_AndUninstallRestoresTheGame()
    {
        var before = Snapshot();
        SeedOpti(NrTag, forwarder: false);
        SeedOpti(F5Tag, forwarder: true);
        var installer = new Installer(_store);
        // An older version installed OptiScaler-NR: the record holds its suffix-less tag.
        await installer.InstallAsync(Game(), _target, new InstallOptions { Opti = true, ReShade = true, DlssNr = true, Proxy = "dxgi.dll", OptiTag = NrTag }, null, default);
        var m = InstallManifest.Read(_target)!;
        Assert.Equal(NrTag, m.OptiTag);
        Assert.Equal(OptiSources.LegacyNrName, OptiSources.NameOf(m.OptiTag));

        // The NR download stays on disk but is no build of the library and never the newest.
        Assert.True(Directory.Exists(ComponentStore.TagDir(Component.OptiScaler, NrTag)));
        Assert.Equal(new[] { F5Tag }, _store.Library(LibraryKind.OptiScaler).Select(b => b.Id));
        Assert.Equal(F5Tag, ComponentStore.NewestCached(Component.OptiScaler, r => OptiSources.IsF5Tag(r.Tag))?.Tag);

        // An automatic reinstall that keeps installed versions moves NR to the newest F5 instead of putting it back.
        var keep = new InstallOptions { Opti = true, ReShade = true, DlssNr = true, Proxy = "dxgi.dll" }.KeepingInstalledVersions(m);
        Assert.True(keep.Opti);
        Assert.Null(keep.OptiTag);

        _store.Opti = new ReleaseInfo { Tag = F5Tag };
        await installer.InstallAsync(Game(), _target, keep, null, default);

        m = InstallManifest.Read(_target)!;
        Assert.Equal(F5Tag, m.OptiTag);
        Assert.Equal("OPTI-" + F5Tag, File.ReadAllText(G("dxgi.dll")));
        Assert.Equal("FORWARDER", File.ReadAllText(G("nvngx.dll_dlssnr.dll")));
        Assert.Equal("XESS-" + F5Tag, File.ReadAllText(G(@"OptiScaler\libxess.dll")));

        await installer.UninstallAsync(Game(), _target, default);
        Assert.Equal(before, Snapshot());
    }

    // ---------- settings ----------

    private static readonly string LegacySettings = """
        {
          "OptiSource": "ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass",
          "OptiTag": "v0.9.72",
          "DfcId": "dfc-default",
          "LecramNewestMigrated": true,
          "Components": { "dlss5-feeder": { "Build": "v1.19.0" } },
          "ReShadeOverrides": [
            { "Section": "DFC", "Key": "arm", "Value": "1" },
            { "Section": "OVERLAY", "Key": "TutorialProgress", "Value": "4" }
          ],
          "Presets": [ { "Name": "Mine", "Opti": [], "ReShade": [ { "Section": "DFC", "Key": "passes", "Value": "2.0" } ] } ],
          "Games": {
            "nr-game": {
              "OptiTag": "v0.9.51",
              "DfcId": "dfc-pick",
              "DfcFrameGen": true,
              "Components": { "DLSS5-Feeder": { "Build": "v1.18.0" }, "luma": { "Options": { "hdr": "on" } } },
              "ReShade": [ { "Section": "dfc", "Key": "passes", "Value": "3.0" }, { "Section": "Uplift", "Key": "Intensity", "Value": "0.5" } ]
            },
            "f5-game": { "OptiTag": "v0.1.37.2-f5" }
          }
        }
        """;

    [Fact]
    public void Load_MovesOptiScalerNrToF5_DropsDfcAndFeeder_SavesAndLogsOnce()
    {
        File.WriteAllText(AppPaths.SettingsFile, LegacySettings);
        var lines = new List<string>();
        void Collect(string l)
        {
            if (l.Contains("OptiScaler-NR is no longer offered", StringComparison.Ordinal)) lock (lines) lines.Add(l);
        }
        Log.Line += Collect;
        try
        {
            var s = AppSettings.Load();

            Assert.Null(s.OptiTag);
            Assert.Null(s.Games["nr-game"].OptiTag);
            Assert.Equal("v0.1.37.2-f5", s.Games["f5-game"].OptiTag);
            Assert.Null(s.Components);
            Assert.Null(s.Games["nr-game"].BuildPick(ComponentRegistry.Feeder));
            Assert.True(s.Games["nr-game"].LumaHdr);
            Assert.DoesNotContain(s.ReShadeOverrides, o => o.Section == "DFC");
            Assert.Contains(s.ReShadeOverrides, o => o.Section == "OVERLAY");
            Assert.Empty(s.Presets.Single().ReShade);
            Assert.Equal(new[] { "Uplift" }, s.Games["nr-game"].ReShade!.Select(o => o.Section));
            Assert.Single(lines);

            // Written back without what the app no longer knows.
            var saved = JsonNode.Parse(File.ReadAllText(AppPaths.SettingsFile))!.AsObject();
            Assert.False(saved.ContainsKey("OptiSource"));
            Assert.False(saved.ContainsKey("DfcId"));
            Assert.False(saved.ContainsKey("OptiTag"));
            var game = saved["Games"]!["nr-game"]!.AsObject();
            Assert.False(game.ContainsKey("DfcId"));
            Assert.False(game.ContainsKey("DfcFrameGen"));
            Assert.False(game.ContainsKey("OptiTag"));
            Assert.DoesNotContain("feeder", game["Components"]!.ToJsonString(), StringComparison.OrdinalIgnoreCase);

            // The next start finds nothing left to move.
            var text = File.ReadAllText(AppPaths.SettingsFile);
            AppSettings.Load();
            Assert.Single(lines);
            Assert.Equal(text, File.ReadAllText(AppPaths.SettingsFile));
        }
        finally { Log.Line -= Collect; }
    }

    [Theory]
    [InlineData("wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass")]
    [InlineData("ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass")]
    public void Load_AnOptiScalerNrSourceAlone_IsMigratedToo(string repo)
    {
        File.WriteAllText(AppPaths.SettingsFile, $$"""{ "OptiSource": "{{repo}}", "LecramNewestMigrated": true }""");

        AppSettings.Load();

        Assert.DoesNotContain("OptiSource", File.ReadAllText(AppPaths.SettingsFile), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_F5SettingsStayAsTheyAre()
    {
        var json = """{ "OptiSource": "janblade/OptiScaler-F5-DLSSNR-Multipass", "OptiTag": "v0.1.38-f5", "LecramNewestMigrated": true }""";
        File.WriteAllText(AppPaths.SettingsFile, json);

        var s = AppSettings.Load();

        Assert.Equal("v0.1.38-f5", s.OptiTag);
        Assert.Equal(json, File.ReadAllText(AppPaths.SettingsFile));
    }
}
