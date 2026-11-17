using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

public sealed class OptiBuildNameTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-buildname-" + Guid.NewGuid().ToString("N"));

    public OptiBuildNameTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
    }

    public void Dispose() => Directory.Delete(_tmp, true);

    private GameViewModel NewGame(AppSettings settings, Action<string>? seedGameDir = null)
    {
        var root = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(root);
        seedGameDir?.Invoke(root);
        var store = new ComponentStore(new GitHubClient(), () => true);
        return new GameViewModel(new GameInfo { Id = root, Root = root, Name = "Build game", Installs = [root] },
            new Services { Settings = settings, Store = store, Installer = new Installer(store) });
    }

    private static string OptiRowName(GameViewModel vm) => vm.Components.First(c => c.Key == "opti").Name;

    [Fact]
    public void ProductNames_FollowTheTag_AndALegacyNrTagKeepsItsOldName()
    {
        Assert.Equal("OptiScaler F5", OptiSources.F5.Name);
        Assert.Equal(OptiSources.F5.Name, OptiSources.NameOf("v0.1.32-f5"));
        Assert.Equal(OptiSources.F5.Name, OptiSources.NameOf(null));
        Assert.Equal("OptiScaler-NR", OptiSources.NameOf("v0.9.51"));
    }

    [Fact]
    public void ModeChoices_NameF5_AndKeepTheModeValues()
    {
        var choices = ModeChoice.All;
        Assert.Equal(InstallMode.OptiScaler, choices[0].Mode);
        Assert.Equal("OptiScaler F5", choices[0].Label);
        Assert.StartsWith("OptiScaler F5 loads", choices[0].Description);
        Assert.Equal(InstallMode.ReShadeOnly, choices[1].Mode);
    }

    [Fact]
    public void GameViewModel_NamesTheOptiScalerRowAndModeAfterF5()
    {
        var vm = NewGame(new AppSettings());

        Assert.Equal("OptiScaler F5", OptiRowName(vm));
        Assert.Equal("OptiScaler F5", vm.ModChoices.First(c => c.Mod == Mod.OptiScalerNr).Label);
        Assert.Equal("OptiScaler F5", vm.ModSelection.Label);
    }

    [Fact]
    public void ComponentRow_NamesTheBuildActuallyInstalled_ALegacyNrOneStaysNr()
    {
        var nrGame = NewGame(new AppSettings(), root =>
        {
            var m = new InstallManifest { Opti = true, OptiTag = "v0.9.51", Mode = InstallMode.OptiScaler };
            m.Save(root);
        });
        Assert.Equal("OptiScaler-NR", OptiRowName(nrGame));

        var f5Game = NewGame(new AppSettings(), root =>
        {
            var m = new InstallManifest { Opti = true, OptiTag = "v0.1.32-f5", Mode = InstallMode.OptiScaler };
            m.Save(root);
        });
        Assert.Equal("OptiScaler F5", OptiRowName(f5Game));
    }

    [Fact]
    public void CompatSummary_NamesTheBuild()
    {
        var f5 = new CompatReport("v0.1.32-f5", [], false, "v0.1.32") { Name = OptiSources.F5.Name };
        Assert.Equal("OptiScaler F5 v0.1.32-f5: config compatible.", f5.Summary);
        Assert.StartsWith("OptiScaler F5 v0.1.32:", new CompatReport("v0.1.32", [], false, "v0.1.32").Summary);
    }
}
