using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

public sealed class ModPickerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-modpicker-" + Guid.NewGuid().ToString("N"));

    public ModPickerTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
    }

    public void Dispose() => Directory.Delete(_tmp, true);

    [Theory]
    [InlineData(true, true, true, true, true, Mod.Uplift)]
    [InlineData(true, false, true, true, true, Mod.Dfc)]
    [InlineData(true, false, false, true, true, Mod.Lecram)] // ShortFuse + Lecram run as a pair; Lecram wins
    [InlineData(true, false, false, false, true, Mod.RenoDx)]
    [InlineData(true, false, false, true, false, Mod.Lecram)]
    [InlineData(true, false, false, false, false, Mod.ReShade)]
    [InlineData(false, false, false, false, false, Mod.OptiScalerNr)]
    [InlineData(false, true, true, true, true, Mod.OptiScalerNr)] // add-ons don't exist outside ReShade-only mode
    public void DefaultFor_FollowsTheNrRoute(bool reShadeOnly, bool uplift, bool dfc, bool lecram, bool renoDx, Mod expected) =>
        Assert.Equal(expected, ModPicker.DefaultFor(reShadeOnly, uplift, dfc, lecram, renoDx));

    [Fact]
    public void Available_ListsAddOnsInReShadeOnlyModeOnly_AndEndsWithAllMods()
    {
        var opti = ModPicker.Available(false).Select(c => c.Mod).ToArray();
        Assert.Equal([Mod.OptiScalerNr, Mod.ReShade, Mod.Mfg, Mod.All], opti);
        var reShade = ModPicker.Available(true).Select(c => c.Mod).ToArray();
        Assert.Equal([Mod.OptiScalerNr, Mod.ReShade, Mod.Mfg, Mod.RenoDx, Mod.Lecram, Mod.Dfc, Mod.Uplift, Mod.All], reShade);
        Assert.DoesNotContain(Mod.OptiScalerNr, ModPicker.AvailableInSettings.Select(c => c.Mod));
        Assert.Equal(Mod.All, ModPicker.AvailableInSettings[^1].Mod);
    }

    [Fact]
    public void Shows_OnlyTheSelectedMod_OrEverythingForAllMods()
    {
        Assert.True(ModPicker.Shows(Mod.Mfg, Mod.Mfg));
        Assert.False(ModPicker.Shows(Mod.Mfg, Mod.ReShade));
        foreach (var mod in Enum.GetValues<Mod>()) Assert.True(ModPicker.Shows(Mod.All, mod));
    }

    private GameViewModel NewGame(AppSettings settings, Action<string>? seedGameDir = null)
    {
        var root = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(root);
        seedGameDir?.Invoke(root);
        var store = new ComponentStore(new GitHubClient(), () => true);
        return new GameViewModel(new GameInfo { Id = root, Root = root, Name = "Picker game", Installs = [root] },
            new Services { Settings = settings, Store = store, Installer = new Installer(store) });
    }

    [Fact]
    public void GameViewModel_PreselectsFromTicks_FollowsThem_AndKeepsAManualPick()
    {
        var vm = NewGame(new AppSettings());
        Assert.Equal(Mod.OptiScalerNr, vm.ModSelection.Mod);
        Assert.False(vm.ShowUplift);
        Assert.True(vm.ShowOptiNr);

        vm.Mode = InstallMode.ReShadeOnly;
        Assert.Equal(Mod.ReShade, vm.ModSelection.Mod);
        Assert.False(vm.ShowOptiNr);
        Assert.True(vm.ShowReShade);

        vm.Components.Single(c => c.Key == "renodx").Enabled = true;
        Assert.Equal(Mod.RenoDx, vm.ModSelection.Mod);
        vm.Components.Single(c => c.Key == "lecram").Enabled = true;
        Assert.Equal(Mod.Lecram, vm.ModSelection.Mod);
        vm.Components.Single(c => c.Key == "uplift").Enabled = true;
        Assert.Equal(Mod.Uplift, vm.ModSelection.Mod);
        Assert.True(vm.ShowUplift);
        Assert.False(vm.ShowLecram);

        vm.ModSelection = vm.ModChoices.Single(c => c.Mod == Mod.Mfg);
        vm.Components.Single(c => c.Key == "dfc").Enabled = true;
        Assert.Equal(Mod.Mfg, vm.ModSelection.Mod);
        Assert.True(vm.ShowMfg);
        Assert.False(vm.ShowDfc);

        vm.ModSelection = vm.ModChoices.Single(c => c.Mod == Mod.All);
        Assert.True(vm.ShowDfc && vm.ShowReShade && vm.ShowOptiNr);
    }

    [Fact]
    public void GameViewModel_ADroppedAddOnPick_FallsBackToTheRouteWhenTheModeLeavesReShadeOnly()
    {
        var vm = NewGame(new AppSettings { DefaultMode = InstallMode.ReShadeOnly, InstallUplift = true });
        Assert.Equal(Mod.Uplift, vm.ModSelection.Mod);
        vm.ModSelection = vm.ModChoices.Single(c => c.Mod == Mod.Dfc);
        vm.Mode = InstallMode.OptiScaler;
        Assert.DoesNotContain(vm.ModChoices, c => c.Mod == Mod.Dfc);
        Assert.Equal(Mod.OptiScalerNr, vm.ModSelection.Mod);
        Assert.False(vm.ShowDfc);
    }

    [Fact]
    public void GameViewModel_PreselectsFromTheInstallManifest()
    {
        var vm = NewGame(new AppSettings(), root => new InstallManifest { Mode = InstallMode.ReShadeOnly, ReShade = true, Lecram = true, DlssNr = true }.Save(root));
        Assert.Equal(Mod.Lecram, vm.ModSelection.Mod);
        Assert.True(vm.ShowLecram);
        Assert.False(vm.ShowRenoDx);
    }

    [Fact]
    public void GameViewModel_ReloadingTheGameForgetsTheManualPick()
    {
        var vm = NewGame(new AppSettings());
        vm.ModSelection = vm.ModChoices.Single(c => c.Mod == Mod.Mfg);
        vm.Load(vm.Info);
        Assert.Equal(Mod.OptiScalerNr, vm.ModSelection.Mod);
    }

    [Theory]
    [InlineData(false, false, false, false, Mod.ReShade)]
    [InlineData(false, true, true, true, Mod.ReShade)] // add-ons only count in ReShade-only mode
    [InlineData(true, true, false, false, Mod.Uplift)]
    [InlineData(true, false, true, true, Mod.Lecram)]
    [InlineData(true, false, false, true, Mod.RenoDx)]
    [InlineData(true, false, false, false, Mod.ReShade)]
    public void SettingsDefaults_PickTheRouteOfTheDefaultInstall_AndFallBackToReShade(bool reShadeOnly, bool uplift, bool lecram, bool renoDx, Mod expected)
    {
        Assert.Equal(expected, ModPicker.DefaultInSettings(reShadeOnly, uplift, false, lecram, renoDx));
        Assert.Contains(ModPicker.AvailableInSettings, c => c.Mod == expected);
    }
}
