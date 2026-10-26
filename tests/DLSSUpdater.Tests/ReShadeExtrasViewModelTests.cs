using DLSSUpdater.Core;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>The game's Add-ons &amp; shaders tab and the Settings lists: Auto and Reset to auto, own picks, blocked games, the update flag.</summary>
public sealed class ReShadeExtrasViewModelTests : ExtrasTestBase
{
    private readonly List<string> _jobs = [];

    private Services Services()
    {
        var store = new ComponentStore(new GitHubClient(), () => true);
        return new Services { Settings = Settings, Store = store, Installer = new Installer(store), Extras = Extras };
    }

    private GameViewModel Vm(int bits = 64, string? antiCheat = null)
    {
        var store = new ComponentStore(new GitHubClient(), () => true);
        var services = new Services { Settings = Settings, Store = store, Installer = new Installer(store), Extras = Extras };
        services.RunJob = async (label, work) =>
        {
            _jobs.Add(label);
            await work(default);
        };
        services.Rescan = _ => Task.CompletedTask;
        var vm = new GameViewModel(Game(bits, antiCheat), services);
        vm.DetailTab = "Extras";
        return vm;
    }

    [Fact]
    public void The_tab_lists_the_catalog_and_follows_Settings_until_the_game_has_its_own_pick()
    {
        Publish([SwapChain(), XinpZip()], [LiliumPack(), SweetPack()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);
        var vm = Vm();

        var tab = vm.Extras!;

        Assert.Equal(["swapchain", "xinp"], tab.Addons.Select(a => a.Id));
        Assert.Equal(["swapchain"], tab.Addons.Where(a => a.Selected).Select(a => a.Id));
        Assert.StartsWith("Auto: follows Settings", tab.AddonsMode);
        Assert.False(tab.AddonsOwn);
        Assert.StartsWith("Auto: the HDR essentials", tab.PacksMode);
        Assert.Contains("lilium-hdr", tab.Packs.Where(p => p.Selected).Select(p => p.Id));
    }

    [Fact]
    public void Ticking_gives_the_game_its_own_list_and_Reset_to_auto_goes_back()
    {
        Publish([SwapChain(), XinpZip()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);
        var vm = Vm();
        var tab = vm.Extras!;

        tab.Addons.First(a => a.Id == "xinp").Selected = true;

        Assert.True(tab.AddonsOwn);
        Assert.Equal(["swapchain", "xinp"], ExtrasPicks.AddonIds(Settings, Settings.Games[vm.Id]).Order());
        Assert.Equal(["swapchain"], ExtrasPicks.AddonIds(Settings, null));

        tab.ResetAddonsCommand.Execute(null);

        Assert.False(tab.AddonsOwn);
        Assert.False(ExtrasPicks.GameHasAddons(Settings.Games[vm.Id]));
        Assert.False(tab.Addons.First(a => a.Id == "xinp").Selected);
    }

    [Fact]
    public void A_pack_pick_of_the_game_wins_over_the_defaults_and_resets()
    {
        Publish(packs: [LiliumPack(), SweetPack()]);
        var vm = Vm();
        var tab = vm.Extras!;

        tab.Packs.First(p => p.Id == Sweet).Selected = true;

        Assert.True(tab.PacksOwn);
        Assert.Equal([Lilium, Sweet], ExtrasPicks.PackIds(Settings, Settings.Games[vm.Id], Extras.Catalog.PackList).Order());
        tab.ResetPacksCommand.Execute(null);
        Assert.False(tab.PacksOwn);
        Assert.Equal([Lilium], tab.Packs.Where(p => p.Selected).Select(p => p.Id));
    }

    [Fact]
    public void The_bitness_shows_the_detected_value_and_a_manual_choice_is_stored_per_game()
    {
        Publish([SwapChain()]);
        var vm = Vm(bits: 32);
        var tab = vm.Extras!;

        Assert.Contains("Auto: 32-bit", tab.BitsText);
        tab.BitsChoice = tab.BitsChoices[2];

        Assert.Equal(64, ExtrasPicks.BitsOf(Settings.Games[vm.Id]));
        Assert.Contains("64-bit chosen", tab.BitsText);
        tab.ResetBitsCommand.Execute(null);
        Assert.Null(ExtrasPicks.BitsOf(Settings.Games[vm.Id]));
    }

    [Fact]
    public void A_game_with_anti_cheat_shows_why_and_cannot_apply()
    {
        Publish([SwapChain()]);
        var vm = Vm(antiCheat: "EasyAntiCheat");
        var tab = vm.Extras!;

        Assert.True(tab.IsBlocked);
        Assert.Contains("EasyAntiCheat", tab.Blocked);
        Assert.False(tab.ApplyAddonsCommand.CanExecute(null));
        Assert.False(tab.ApplyPacksCommand.CanExecute(null));
    }

    [Fact]
    public void An_add_on_the_app_manages_elsewhere_cannot_be_ticked_and_says_why()
    {
        Publish([new CatalogAddon { Id = "renodx", Name = "RenoDX", ManagedBy = "renodx" }]);
        var vm = Vm();

        var row = Assert.Single(vm.Extras!.Addons);

        Assert.False(row.Available);
        Assert.Contains("RenoDX HDR mods", row.Note);
    }

    [Fact]
    public async Task Apply_runs_as_a_job_and_installs_what_the_game_has_ticked()
    {
        Publish([SwapChain()]);
        var vm = Vm();
        var tab = vm.Extras!;
        tab.Addons[0].Selected = true;

        await tab.ApplyAddonsCommand.ExecuteAsync(null);

        Assert.Equal(["Installing ReShade add-ons"], _jobs);
        Assert.True(File.Exists(Path.Combine(Target, "swapchain_override.addon64")));
        Assert.Contains("1 add-on installed", tab.Status);
    }

    [Fact]
    public async Task Settings_changes_flag_installed_games_for_an_update_but_not_games_without_the_component()
    {
        Publish([SwapChain("1")]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);
        var vm = Vm();
        await vm.Extras!.ApplyAddonsCommand.ExecuteAsync(null);
        vm.Manifest = InstallManifest.Read(Target);
        vm.RefreshExtrasUpdate();
        Assert.False(vm.NeedsUpdate);

        Publish([SwapChain("2")]);
        vm.RefreshExtrasUpdate();

        Assert.True(vm.NeedsUpdate);
        Assert.False(vm.CoreNeedsUpdate);
        Assert.Equal("Reinstall", vm.ActionText);
        Assert.Equal([ComponentRegistry.ReShadeAddons], vm.ExtrasToUpdate);
    }

    [Fact]
    public void The_settings_lists_filter_by_search_and_category()
    {
        Publish([SwapChain(), XinpZip()], [LiliumPack(), SweetPack()]);
        var settings = new ReShadeExtrasSettingsViewModel(Services());

        settings.Filter = "xin";
        Assert.Equal(["xinp"], settings.Addons.Select(a => a.Id));
        settings.Filter = "";
        settings.PackCategory = settings.PackCategories.First(c => c.Value == "general");
        Assert.Equal([Sweet], settings.Packs.Select(p => p.Id));
        settings.PackCategory = settings.PackCategories.First(c => c.Value == "hdr");
        Assert.Equal([Lilium], settings.Packs.Select(p => p.Id));
    }

    [Fact]
    public void A_tick_in_Settings_changes_the_global_selection_and_a_reset_goes_back_to_the_defaults()
    {
        Publish([SwapChain()], [LiliumPack(), SweetPack()]);
        var settings = new ReShadeExtrasSettingsViewModel(Services());
        Assert.StartsWith("Auto: the HDR essentials", settings.PacksMode);
        Assert.False(settings.PacksOwn);

        settings.Addons[0].Selected = true;
        settings.Packs.First(p => p.Id == Sweet).Selected = true;

        Assert.Equal(["swapchain"], ExtrasPicks.AddonIds(Settings, null));
        Assert.Equal([Lilium, Sweet], ExtrasPicks.PackIds(Settings, null, Extras.Catalog.PackList).Order());
        Assert.True(settings.PacksOwn);

        settings.ResetPacksCommand.Execute(null);
        Assert.Equal([Lilium], ExtrasPicks.PackIds(Settings, null, Extras.Catalog.PackList));
        Assert.False(settings.PacksOwn);
    }

    [Fact]
    public void The_settings_list_shows_imports_and_presets()
    {
        Publish([SwapChain()]);
        Write(Path.Combine(Tmp, "in", "t.addon64"), "x");
        ExtrasLibrary.ImportAddon(Path.Combine(Tmp, "in", "t.addon64"));
        Write(Path.Combine(Tmp, "in", "P.ini"), "Techniques=A@a.fx");
        ReShadePresets.Import(Path.Combine(Tmp, "in", "P.ini"));
        Extras.Reload();

        var settings = new ReShadeExtrasSettingsViewModel(Services());

        Assert.Equal(["imported:t"], settings.Imports.Select(i => i.Id));
        Assert.Equal(["swapchain", "imported:t"], settings.Addons.Select(a => a.Id));
        Assert.Equal(["P"], settings.Presets.Select(p => p.Name));
    }
}
