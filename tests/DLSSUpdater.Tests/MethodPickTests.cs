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
    public void UntickingTheOptiScalerRow_InstallsReShadeInstead_AndTickingItGoesBack()
    {
        var vm = NewGame();
        var opti = Component(vm, "opti");
        var reShade = Component(vm, "reshade");
        Assert.True(opti is { Visible: true, Editable: true, Enabled: true });
        Assert.True(vm.ShowOptiBuild);

        opti.Enabled = false;

        Assert.Equal(InstallMode.ReShadeOnly, vm.Mode);
        Assert.True(opti.Visible);
        Assert.True(reShade is { Enabled: true, Editable: false });
        Assert.False(vm.ShowOptiBuild);

        opti.Enabled = true;

        Assert.Equal(InstallMode.OptiScaler, vm.Mode);
        Assert.True(reShade.Editable);
        Assert.False(Component(vm, "renodx").Visible);
    }

    [Fact]
    public void PickingACardThatNeedsReShadeOnly_UnticksTheOptiScalerRow_AndTheOptiScalerCardTicksItAgain()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        var addon = Row(Method.NeuralAddon, InstallMode.ReShadeOnly, ComponentRegistry.ReShade, ComponentRegistry.RenoDx);
        var optiCard = Row(Method.OptiScaler, InstallMode.OptiScaler, ComponentRegistry.OptiScaler);
        vm.Methods.Add(optiCard);
        vm.Methods.Add(addon);

        vm.PickMethodCommand.Execute(addon);
        Assert.False(Component(vm, "opti").Enabled);

        vm.PickMethodCommand.Execute(optiCard);
        Assert.True(Component(vm, "opti").Enabled);
        Assert.Equal(InstallMode.OptiScaler, vm.Mode);
        Assert.True(optiCard.IsPicked);
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
    public void ADlssCardPickedEarlier_GivesWayWhenTheNeuralRowsGoOnByHand()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        vm.AddOns.Clear();
        var dlss = Row(Method.DlssUpdate, null, ComponentRegistry.Dlss);
        var nr = Row(Method.NeuralAddon, InstallMode.ReShadeOnly, ComponentRegistry.ReShade, ComponentRegistry.DlssNr, ComponentRegistry.Lecram);
        vm.Methods.Add(dlss);
        vm.Methods.Add(nr);
        vm.Mode = InstallMode.ReShadeOnly;
        Component(vm, "mfg").Enabled = false;
        Component(vm, "streamline").Enabled = false;
        vm.PickMethodCommand.Execute(dlss);
        Assert.True(dlss.IsPicked);

        Component(vm, "dlssnr").Enabled = true;
        Component(vm, "lecram").Enabled = true;

        Assert.True(nr.IsPicked);
        Assert.False(dlss.IsPicked);
        Assert.Equal("DLSS (SR · RR · FG)", nr.AlsoOn);
        Assert.Null(dlss.AlsoOn);
        Assert.Null(vm.CustomSelection);
    }

    [Fact]
    public void CardsListTheTableRowsTheyTurnOn_WithTheTablesNames()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        var nr = Row(Method.NeuralAddon, InstallMode.ReShadeOnly, ComponentRegistry.ReShade, ComponentRegistry.DlssNr, ComponentRegistry.Lecram);
        var dlss = Row(Method.DlssUpdate, null, ComponentRegistry.Dlss);
        vm.Methods.Add(nr);
        vm.Methods.Add(dlss);

        vm.PickMethodCommand.Execute(dlss);

        Assert.Equal("Turns on: ReShade · NR runtime · Lecram DLSS5 add-on. Turns off: " + Component(vm, "opti").Name, nr.TurnsOn);
        Assert.Equal("Turns on: " + Component(vm, "dlss").Name, dlss.TurnsOn);
    }

    [Fact]
    public void ATableNoCardMatches_ShowsTheCustomSelection_AndNoPickedCard()
    {
        var vm = NewGame();
        vm.Methods.Clear();
        vm.AddOns.Clear();
        var nr = Row(Method.NeuralAddon, InstallMode.ReShadeOnly, ComponentRegistry.ReShade, ComponentRegistry.DlssNr, ComponentRegistry.Lecram);
        vm.Methods.Add(nr);
        vm.Mode = InstallMode.ReShadeOnly;
        foreach (var key in new[] { "mfg", "dlssnr", "dlss", "streamline" }) Component(vm, key).Enabled = false;

        Component(vm, "streamline").Enabled = true;

        Assert.False(nr.IsPicked);
        Assert.Equal("ReShade · Streamline (for Dynamic MFG)", vm.CustomSelection);
        Assert.True(vm.HasCustomSelection);
    }

    [Fact]
    public void SwitchingTheNrAddOnInTheTable_RenamesTheNeuralCard()
    {
        var root = Path.Combine(_tmp, "nr-game");
        Directory.CreateDirectory(root);
        var info = new GameInfo { Id = root, Root = root, Name = "NR game", Installs = [root] };
        info.Tech = new GameTech { Apis = GraphicsApis.Dx12, Bits = 64 };
        info.Dlss.Add(new DlssDll { Name = "nvngx_dlss.dll", Path = Path.Combine(root, "nvngx_dlss.dll") });
        var store = new ComponentStore(new GitHubClient(), () => true);
        var vm = new GameViewModel(info, new Services { Settings = new AppSettings(), Store = store, Installer = new Installer(store) });
        vm.RefreshAdvice();
        var neural = vm.Methods.Single(r => r.Option.Method == Method.NeuralAddon);
        Assert.Equal("Neural rendering with GITC Uplift", neural.Title);
        vm.PickMethodCommand.Execute(neural);

        Component(vm, "uplift").Enabled = false;
        Component(vm, "lecram").Enabled = true;

        neural = vm.Methods.Single(r => r.Option.Method == Method.NeuralAddon);
        Assert.Equal("Neural rendering with Lecram DLSS5 add-on", neural.Title);
        Assert.Contains(ComponentRegistry.Lecram, neural.Option.Components);
        Assert.True(neural.IsPicked);
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
