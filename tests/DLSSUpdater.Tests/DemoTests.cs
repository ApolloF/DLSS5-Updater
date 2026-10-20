using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

public class DemoTests : IDisposable
{
    private readonly string _appRoot = AppPaths.Root;
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-demo-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _game;
    private readonly ComponentStore _store;
    private readonly AppSettings _settings = new();

    public DemoTests()
    {
        _game = Path.Combine(_tmp, "game");
        Demo.Enable(Path.Combine(_tmp, "data"));
        _store = new ComponentStore(new GitHubClient(), () => false);
        Demo.Seed(_store, _settings);
    }

    public void Dispose()
    {
        Demo.Active = false;
        AppPaths.Root = _appRoot;
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private static string[] Files(string dir) =>
        Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Select(p => p + "|" + (File.Exists(p) ? new FileInfo(p).Length : -1)).Order().ToArray();

    [Fact]
    public void Enable_uses_a_throwaway_data_folder_and_refuses_others()
    {
        Assert.StartsWith(_tmp, AppPaths.Root, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => Demo.Enable(@"C:\dlssu-not-temp"));
    }

    [Fact]
    public void Enable_withLockedLeftoverFile_failsWithAClearMessage()
    {
        var data = Path.Combine(_tmp, "data");
        var locked = Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).First();
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        var ex = Assert.Throws<IOException>(() => Demo.Enable(data));

        Assert.Contains("still in use", ex.Message);
    }

    [Fact]
    public void Screenshot_folder_comes_from_the_argument()
    {
        Assert.Equal(@"C:\x", Demo.ScreenshotArg(["--demo", "--screenshots", @"C:\x"]));
        Assert.Null(Demo.ScreenshotArg(["--demo"]));
        Assert.Null(Demo.ScreenshotArg(["--demo", "--screenshots"]));
    }

    [Fact]
    public void Library_covers_the_states_the_app_shows()
    {
        var games = Demo.Games;
        Assert.Equal(12, games.Count);
        Assert.All(games, g => Assert.StartsWith(@"D:\Games\", g.Root));
        var manifests = games.ToDictionary(g => g.Name, g => g.Installs.Select(Demo.ManifestAt).FirstOrDefault());

        Assert.Contains(games, g => g.AntiCheat is not null);
        Assert.Contains(games, g => g.Dlss.Count == 0);
        Assert.Contains(games, g => g.Streamline.Count > 0);
        Assert.Contains(manifests.Values, m => m is null);
        Assert.Contains(manifests.Values, m => m is { Mode: InstallMode.OptiScaler, Opti: true });
        Assert.Contains(manifests.Values, m => m is { Mode: InstallMode.ReShadeOnly, ReShade: true, Opti: false });
        Assert.Contains(manifests.Values, m => m is { RenoDx: true, Lecram: true });
        Assert.Contains(manifests.Values, m => m is { Dfc: true });
        Assert.Contains(manifests.Values, m => m is { Uplift: true });
        Assert.Contains(manifests.Values, m => m is { Mfg: true });
        Assert.Contains(manifests.Values, m => m is { Dlss: true, Opti: false, ReShade: false });
        Assert.Contains(games, g => g.Source == "Manual");

        var latest = new Version(310, 10, 0);
        var current = games.Where(g => g.Installs.Count > 0 && g.Dlss.Count > 0 && g.Dlss.All(d => d.Version >= latest)).Select(g => g.Name).ToList();
        var outdated = games.Where(g => g.Installs.Count > 0 && g.Dlss.Any(d => d.Version < latest)).Select(g => g.Name).ToList();
        Assert.True(current.Count >= 3);
        Assert.Contains("Ember Crown", outdated);
        Assert.Contains("Iron Veil", outdated);
        Assert.Contains("Wicker & Ash", outdated);
    }

    [Fact]
    public void Placeholders_pose_as_components_without_being_binaries()
    {
        Assert.Equal(2, _store.DlssNrVersions.Count);
        Assert.Single(_store.RenoDxBuilds);
        Assert.Equal(["v11.0.1", "v11.0.0-beta.48", "v11.0.0-beta.45", "v8.5.0-rc10-model-sdr1"], _store.LecramBuilds.Select(b => b.DetectedLabel));
        Assert.Equal(LecramGeneration.V11, _store.LecramBuilds[0].Generation);
        Assert.True(_store.DfcImported);
        Assert.Equal("4.0.0", _store.DfcVersion);
        Assert.Equal(DfcCompatTag(), Demo.ManifestAt(Demo.Games.First(g => g.Name == "Hollow Tide").Installs[0])!.DfcTag);
        Assert.All(Directory.EnumerateFiles(AppPaths.Components, "*", SearchOption.AllDirectories).Where(f => Path.GetExtension(f) is not (".json" or ".cfg")), f => Assert.True(new FileInfo(f).Length < 200));
    }

    private string? DfcCompatTag() => _store.DfcTag;

    [Fact]
    public async Task Install_and_uninstall_never_touch_the_game_folder()
    {
        var dll = Path.Combine(_game, "nvngx_dlss.dll");
        Directory.CreateDirectory(_game);
        File.WriteAllText(dll, "ORIGINAL");
        var before = Files(_game);
        var game = new GameInfo
        {
            Id = GameInfo.MakeId(_game), Name = "Real game", Root = _game,
            Dlss = [new DlssDll { Path = dll, Name = "nvngx_dlss.dll", Version = new Version(3, 7, 10, 0) }],
        };
        var installer = new Installer(_store);

        await installer.InstallAsync(game, _game, new InstallOptions { Dlss = true, Opti = true, ReShade = true }, null, CancellationToken.None);

        Assert.Equal(new Version(310, 10, 0, 0), game.Dlss[0].Version);
        Assert.Equal(Demo.LatestDlss, Demo.ManifestAt(_game)?.DlssTag);
        Assert.Equal(Demo.LatestOpti, Demo.ManifestAt(_game)?.OptiTag);
        Assert.Equal(before, Files(_game));

        await installer.UninstallAsync(game, _game, CancellationToken.None);

        Assert.Equal(new Version(3, 7, 10, 0), game.Dlss[0].Version);
        Assert.Null(Demo.ManifestAt(_game));
        Assert.Equal(before, Files(_game));
        Assert.Equal("ORIGINAL", File.ReadAllText(dll));
    }

    [Fact]
    public async Task Restore_keeps_the_mod_and_updating_a_demo_game_clears_its_update()
    {
        var game = Demo.Games.First(g => g.Name == "Ember Crown");
        var dir = game.Installs[0];
        var installer = new Installer(_store);

        await installer.InstallAsync(game, dir, new InstallOptions { Opti = true, ReShade = true, Mfg = true, DlssNr = true, Dlss = true, Streamline = true }, null, CancellationToken.None);
        Assert.All(game.Dlss, d => Assert.Equal(new Version(310, 10, 0, 0), d.Version));
        Assert.Equal(Demo.LatestMfg, Demo.ManifestAt(dir)!.MfgTag);

        await installer.RestoreDlssAsync(game, dir, CancellationToken.None);
        Assert.All(game.Dlss, d => Assert.Equal(new Version(310, 2, 1, 0), d.Version));
        Assert.True(Demo.ManifestAt(dir)!.Opti);
    }

    [Fact]
    public void Nothing_outside_the_demo_folder_is_written_by_enabling()
    {
        Assert.True(Directory.Exists(AppPaths.Root));
        Assert.DoesNotContain(Directory.EnumerateFiles(AppPaths.Root, "*", SearchOption.AllDirectories), f => !FileUtil.IsUnder(f, _tmp));
    }
}
