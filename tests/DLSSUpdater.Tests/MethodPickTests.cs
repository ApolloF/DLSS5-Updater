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
        Component(vm, "dlssnr").Enabled = false;
        var opti = Row(Method.OptiScaler, InstallMode.OptiScaler, ComponentRegistry.OptiScaler);
        var nr = Row(Method.OptiScalerNr, InstallMode.OptiScaler, ComponentRegistry.OptiScaler, ComponentRegistry.DlssNr);
        vm.Methods.Add(opti);
        vm.Methods.Add(nr);

        Component(vm, "dlssnr").Enabled = true;

        Assert.True(nr.IsPicked);
        Assert.False(opti.IsPicked);

        Component(vm, "dlssnr").Enabled = false;

        Assert.True(opti.IsPicked);
        Assert.False(nr.IsPicked);
    }

    [Fact]
    public void PickingASmallerCard_TurnsOffThePreviousCardsExtras()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        var opti = Row(Method.OptiScaler, InstallMode.OptiScaler, ComponentRegistry.OptiScaler);
        var nr = Row(Method.OptiScalerNr, InstallMode.OptiScaler, ComponentRegistry.OptiScaler, ComponentRegistry.DlssNr);
        vm.Methods.Add(opti);
        vm.Methods.Add(nr);
        vm.PickMethodCommand.Execute(nr);
        Assert.True(nr.IsPicked);

        vm.PickMethodCommand.Execute(opti);

        Assert.False(Component(vm, "dlssnr").Enabled);
        Assert.True(Component(vm, "opti").Enabled);
        Assert.True(opti.IsPicked);
        Assert.False(nr.IsPicked);
    }

    private static MethodRow Mfg() => Row(Method.MfgUnlock, null, ComponentRegistry.MfgUnlock);

    [Fact]
    public void PickingAMethod_KeepsTheMfgAddOnOn()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        var addon = Row(Method.NeuralAddon, InstallMode.ReShadeOnly, ComponentRegistry.ReShade, ComponentRegistry.DlssNr, ComponentRegistry.Lecram);
        var opti = Row(Method.OptiScaler, InstallMode.OptiScaler, ComponentRegistry.OptiScaler);
        var mfg = Mfg();
        vm.Methods.Add(opti);
        vm.Methods.Add(addon);
        vm.AddOns.Add(mfg);
        Component(vm, "mfg").Enabled = false;
        vm.PickMethodCommand.Execute(mfg);
        Assert.True(Component(vm, "mfg").Enabled);

        vm.PickMethodCommand.Execute(addon);

        Assert.True(Component(vm, "mfg").Enabled);
        Assert.True(Component(vm, "lecram").Enabled);
        Assert.True(addon.IsPicked);
        Assert.True(mfg.IsPicked);

        vm.PickMethodCommand.Execute(opti);

        Assert.Equal(InstallMode.OptiScaler, vm.Mode);
        Assert.True(Component(vm, "mfg").Enabled);
        Assert.True(opti.IsPicked);
        Assert.True(mfg.IsPicked);
    }

    [Fact]
    public void TogglingTheMfgAddOn_LeavesThePickedMethodAndModeAlone()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        var addon = Row(Method.NeuralAddon, InstallMode.ReShadeOnly, ComponentRegistry.ReShade, ComponentRegistry.DlssNr, ComponentRegistry.Lecram);
        var mfg = Mfg();
        vm.Methods.Add(addon);
        vm.AddOns.Add(mfg);
        vm.PickMethodCommand.Execute(addon);
        Component(vm, "mfg").Enabled = true;
        Assert.True(mfg.IsPicked);

        vm.PickMethodCommand.Execute(mfg);

        Assert.False(Component(vm, "mfg").Enabled);
        Assert.False(mfg.IsPicked);
        Assert.True(addon.IsPicked);
        Assert.True(Component(vm, "lecram").Enabled);
        Assert.Equal(InstallMode.ReShadeOnly, vm.Mode);

        vm.PickMethodCommand.Execute(mfg);

        Assert.True(Component(vm, "mfg").Enabled);
        Assert.True(mfg.IsPicked);
        Assert.True(addon.IsPicked);
    }

    [Fact]
    public void MfgAddOn_WorksWithoutAnyMethodPicked()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        var mfg = Mfg();
        vm.AddOns.Add(mfg);
        Component(vm, "mfg").Enabled = false;
        var mode = vm.Mode;

        vm.PickMethodCommand.Execute(mfg);

        Assert.True(Component(vm, "mfg").Enabled);
        Assert.True(mfg.IsPicked);
        Assert.Equal(mode, vm.Mode);
    }

    [Fact]
    public void AnMfgAddOnThatIsNotPossible_CantBeSwitchedOn()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        var mfg = new MethodRow(new MethodOption(Method.MfgUnlock, "MFG Unlock", Availability.NotPossible, "reason", [], [], null, false), false);
        vm.AddOns.Add(mfg);
        Component(vm, "mfg").Enabled = false;

        vm.PickMethodCommand.Execute(mfg);

        Assert.False(Component(vm, "mfg").Enabled);
        Assert.False(mfg.IsPicked);
        Assert.False(mfg.IsClickable);
    }

    [Fact]
    public void AGameWithDlssFrameGeneration_ShowsMfgUnlockAsAnAddOn_NotAsAMethod()
    {
        var root = Path.Combine(_tmp, "fg-game");
        Directory.CreateDirectory(root);
        var info = new GameInfo { Id = root, Root = root, Name = "FG game", Installs = [root] };
        info.Tech = new GameTech { Apis = GraphicsApis.Dx12, Bits = 64 };
        info.Dlss.Add(new DlssDll { Name = "nvngx_dlss.dll", Path = Path.Combine(root, "nvngx_dlss.dll") });
        info.Dlss.Add(new DlssDll { Name = "nvngx_dlssg.dll", Path = Path.Combine(root, "nvngx_dlssg.dll") });
        var store = new ComponentStore(new GitHubClient(), () => true);
        var vm = new GameViewModel(info, new Services { Settings = new AppSettings(), Store = store, Installer = new Installer(store) });

        vm.RefreshAdvice();

        Assert.DoesNotContain(vm.Methods, r => r.Option.Method == Method.MfgUnlock);
        Assert.Equal(Method.MfgUnlock, Assert.Single(vm.AddOns).Option.Method);
        Assert.True(vm.HasAddOns);
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
