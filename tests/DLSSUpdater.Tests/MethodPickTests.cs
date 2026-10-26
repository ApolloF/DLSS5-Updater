using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>Clicking a method card on the game page applies it; the picked card follows the page when the user changes it by hand.</summary>
public sealed class MethodPickTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-methodpick-" + Guid.NewGuid().ToString("N"));

    public MethodPickTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
    }

    public void Dispose() => Directory.Delete(_tmp, true);

    private GameViewModel NewGame(string? antiCheat = null)
    {
        var root = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(root);
        var store = new ComponentStore(new GitHubClient(), () => true);
        return new GameViewModel(new GameInfo { Id = root, Root = root, Name = "Pick game", Installs = [root], AntiCheat = antiCheat },
            new Services { Settings = new AppSettings(), Store = store, Installer = new Installer(store) });
    }

    private static MethodRow Row(Method method, InstallMode? mode, params string[] components) =>
        new(new MethodOption(method, method.ToString(), Availability.Available, "reason", components, [], mode, false), false);

    private static ComponentRow Component(GameViewModel vm, string key) => vm.Components.Single(c => c.Key == key);

    [Fact]
    public void PickingASelectableCard_SetsItsModeAndTurnsItsComponentsOn()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        Assert.Equal(InstallMode.OptiScaler, vm.Mode);
        var addon = Row(Method.NeuralAddon, InstallMode.ReShadeOnly, ComponentRegistry.ReShade, ComponentRegistry.RenoDx);
        var opti = Row(Method.OptiScaler, InstallMode.OptiScaler, ComponentRegistry.OptiScaler);
        vm.Methods.Add(opti);
        vm.Methods.Add(addon);

        vm.PickMethodCommand.Execute(addon);

        Assert.Equal(InstallMode.ReShadeOnly, vm.Mode);
        Assert.True(Component(vm, "renodx").Enabled);
        Assert.True(addon.IsPicked);
        Assert.False(opti.IsPicked);
    }

    [Fact]
    public void PickingACardWithoutComponents_OnlyOpensItsDetails()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        var hdr = Row(Method.RenoDxHdr, null);
        vm.Methods.Add(hdr);

        vm.PickMethodCommand.Execute(hdr);

        Assert.True(hdr.IsExpanded);
        Assert.False(hdr.IsPicked);
        Assert.Equal(InstallMode.OptiScaler, vm.Mode);
        vm.PickMethodCommand.Execute(hdr);
        Assert.False(hdr.IsExpanded);
    }

    [Fact]
    public void ChangingTheModeByHand_UnpicksACardThatNeedsTheOtherMode()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        var addon = Row(Method.NeuralAddon, InstallMode.ReShadeOnly, ComponentRegistry.ReShade, ComponentRegistry.RenoDx);
        vm.Methods.Add(addon);
        vm.PickMethodCommand.Execute(addon);
        Assert.True(addon.IsPicked);

        vm.Mode = InstallMode.OptiScaler;

        Assert.False(addon.IsPicked);
    }

    [Fact]
    public void TurningComponentsOnByHand_PicksTheMostSpecificMatchingCard()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        Component(vm, "mfg").Enabled = false;
        var opti = Row(Method.OptiScaler, InstallMode.OptiScaler, ComponentRegistry.OptiScaler);
        var mfg = Row(Method.MfgUnlock, InstallMode.OptiScaler, ComponentRegistry.OptiScaler, ComponentRegistry.MfgUnlock);
        vm.Methods.Add(opti);
        vm.Methods.Add(mfg);

        Component(vm, "mfg").Enabled = true;

        Assert.True(mfg.IsPicked);
        Assert.False(opti.IsPicked);

        Component(vm, "mfg").Enabled = false;

        Assert.True(opti.IsPicked);
        Assert.False(mfg.IsPicked);
    }

    [Fact]
    public void PickingASmallerCard_TurnsOffThePreviousCardsExtras()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        var opti = Row(Method.OptiScaler, InstallMode.OptiScaler, ComponentRegistry.OptiScaler);
        var mfg = Row(Method.MfgUnlock, InstallMode.OptiScaler, ComponentRegistry.OptiScaler, ComponentRegistry.MfgUnlock);
        vm.Methods.Add(opti);
        vm.Methods.Add(mfg);
        vm.PickMethodCommand.Execute(mfg);
        Assert.True(mfg.IsPicked);

        vm.PickMethodCommand.Execute(opti);

        Assert.False(Component(vm, "mfg").Enabled);
        Assert.True(Component(vm, "opti").Enabled);
        Assert.True(opti.IsPicked);
        Assert.False(mfg.IsPicked);
    }

    [Fact]
    public void PickingACardWhoseComponentsCantAllGoOn_MarksTheCardThePageMatches()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        Component(vm, "mfg").Enabled = false;
        var opti = Row(Method.OptiScaler, InstallMode.OptiScaler, ComponentRegistry.OptiScaler);
        var dlssNr = Row(Method.OptiScalerNr, InstallMode.OptiScaler, ComponentRegistry.OptiScaler, ComponentRegistry.DlssNr);
        vm.Methods.Add(opti);
        vm.Methods.Add(dlssNr);
        Component(vm, "dlssnr").Enabled = false;
        Component(vm, "dlssnr").Editable = false;

        vm.PickMethodCommand.Execute(dlssNr);

        Assert.False(dlssNr.IsPicked);
        Assert.True(opti.IsPicked);
    }

    [Fact]
    public void AntiCheatCaution_IsNotRepeatedUnderTheAntiCheatBanner()
    {
        var vm = NewGame("EasyAntiCheat");
        Assert.True(vm.HasAntiCheat);
        Assert.Equal(MethodAdvisor.AntiCheatCaution("EasyAntiCheat"), vm.AdviceCaution);
        Assert.False(vm.HasAdviceCaution);
    }
}
