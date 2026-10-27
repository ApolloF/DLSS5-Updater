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
    public void EachBuildHasItsOwnProductName()
    {
        Assert.Equal("OptiScaler-NR", OptiSources.Default.Name);
        Assert.Equal("OptiScaler F5", OptiSources.F5.Name);
        Assert.Equal(OptiSources.F5.Name, OptiSources.OfTag("v0.1.32-f5").Name);
        Assert.Equal(OptiSources.Default.Name, OptiSources.OfTag("v0.9.51").Name);
        Assert.Equal(OptiSources.Default.Name, OptiSources.For("unknown/repo").Name);
    }

    [Fact]
    public void ModeChoices_NameTheSelectedBuild_AndKeepTheModeValues()
    {
        var f5 = ModeChoice.For(OptiSources.F5);
        Assert.Equal(InstallMode.OptiScaler, f5[0].Mode);
        Assert.Equal("OptiScaler F5", f5[0].Label);
        Assert.StartsWith("OptiScaler F5 loads", f5[0].Description);
        Assert.Equal("OptiScaler-NR", ModeChoice.For(OptiSources.Default)[0].Label);
        Assert.Equal(InstallMode.ReShadeOnly, f5[1].Mode);
    }

    [Fact]
    public void GameViewModel_RenamesLabelsWhenTheBuildIsSwitched()
    {
        var settings = new AppSettings { OptiSource = ComponentStore.OptiRepo };
        var vm = NewGame(settings);
        Assert.Equal("OptiScaler-NR", OptiRowName(vm));

        settings.OptiSource = OptiSources.F5.Repo;
        vm.RefreshBuildName();

        Assert.Equal("OptiScaler F5", OptiRowName(vm));
        Assert.Equal("OptiScaler F5", vm.ModChoices.First(c => c.Mod == Mod.OptiScalerNr).Label);
        Assert.Equal("OptiScaler F5", vm.ModSelection.Label);
    }

    [Fact]
    public void ComponentRow_NamesTheBuildActuallyInstalled_NotTheSelectedOne()
    {
        var settings = new AppSettings { OptiSource = OptiSources.F5.Repo };
        var vm = NewGame(settings, root =>
        {
            var m = new InstallManifest { Opti = true, OptiTag = "v0.9.51", Mode = InstallMode.OptiScaler };
            m.Save(root);
        });
        Assert.Equal("OptiScaler-NR", OptiRowName(vm));

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
        Assert.StartsWith("OptiScaler-NR v0.9.51:", new CompatReport("v0.9.51", [], false, "v0.9.51").Summary);
    }
}
