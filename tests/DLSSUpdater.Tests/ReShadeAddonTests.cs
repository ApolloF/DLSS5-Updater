using DLSSUpdater.Scan;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>ReShade add-ons from the catalog: chosen globally or per game, placed by bitness and [ADDON] AddonPath, recorded, updated and removed.</summary>
public sealed class ReShadeAddonTests : ExtrasTestBase
{
    private async Task<ExtrasResult> Apply(GameInfo? game = null) => await Extras.ApplyAddonsAsync(game ?? Game(), Target, null, default);

    [Fact]
    public async Task A_64_bit_game_gets_the_64_bit_add_on_only_and_the_record_lists_it()
    {
        Publish([SwapChain()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);

        var result = await Apply();

        Assert.True(result.Ok, result.Message);
        Assert.Equal("SWAP64-1", File.ReadAllText(Path.Combine(Target, "swapchain_override.addon64")));
        Assert.False(File.Exists(Path.Combine(Target, "swapchain_override.addon32")));
        var c = Assert.Single(Manifest()!.Components);
        Assert.Equal(ComponentRegistry.ReShadeAddons, c.Id);
        Assert.Equal("swapchain_override.addon64", Path.GetFileName(Assert.Single(c.Files).Path));
        Assert.Equal(FileUtil.Sha256(Path.Combine(Target, "swapchain_override.addon64")), c.Files[0].Sha256);
    }

    [Fact]
    public async Task A_32_bit_game_gets_the_32_bit_add_on()
    {
        Publish([SwapChain()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);

        var result = await Apply(Game(bits: 32));

        Assert.True(result.Ok, result.Message);
        Assert.Equal("SWAP32-1", File.ReadAllText(Path.Combine(Target, "swapchain_override.addon32")));
        Assert.False(File.Exists(Path.Combine(Target, "swapchain_override.addon64")));
    }

    [Fact]
    public async Task A_manual_bitness_wins_over_the_detected_one()
    {
        Publish([SwapChain()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);
        var game = Game(bits: 64);
        ExtrasPicks.SetGameBits(Settings.For(game.Id), 32);

        await Apply(game);

        Assert.True(File.Exists(Path.Combine(Target, "swapchain_override.addon32")));
        Assert.False(File.Exists(Path.Combine(Target, "swapchain_override.addon64")));
    }

    [Fact]
    public async Task AddonPath_in_ReShade_ini_decides_the_folder()
    {
        Write(Path.Combine(Target, "ReShade.ini"), "[ADDON]\r\nAddonPath=.\\addons\r\n[GENERAL]\r\nEffectSearchPaths=.\\reshade-shaders\\Shaders\\**\r\n");
        Publish([SwapChain()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);

        var result = await Apply();

        Assert.True(result.Ok, result.Message);
        Assert.True(File.Exists(Path.Combine(Target, "addons", "swapchain_override.addon64")));
        Assert.False(File.Exists(Path.Combine(Target, "swapchain_override.addon64")));
    }

    [Fact]
    public async Task An_AddonPath_outside_the_game_is_refused_and_nothing_is_written()
    {
        var outside = Path.Combine(Tmp, "elsewhere");
        Write(Path.Combine(Target, "ReShade.ini"), $"[ADDON]\r\nAddonPath={outside}\r\n");
        Publish([SwapChain()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);

        var result = await Apply();

        Assert.False(result.Ok);
        Assert.Contains("outside the game", result.Message);
        Assert.False(Directory.Exists(outside));
        Assert.Null(Manifest());
    }

    [Fact]
    public async Task A_zip_add_on_places_its_binary_and_its_effects_in_the_shader_folder()
    {
        Publish([XinpZip()]);
        ExtrasPicks.SetAddons(Settings, ["xinp"]);

        var result = await Apply();

        Assert.True(result.Ok, result.Message);
        Assert.Equal("X64-1", File.ReadAllText(Path.Combine(Target, "Xinp.addon64")));
        Assert.False(File.Exists(Path.Combine(Target, "Xinp.addon32")));
        Assert.Equal("XFX-1", File.ReadAllText(Path.Combine(Target, @"reshade-shaders\Shaders\Xinp\Xinp.fx")));
    }

    [Fact]
    public async Task Effects_of_an_add_on_are_put_on_a_search_path_that_lacks_them()
    {
        Write(Path.Combine(Target, "ReShade.ini"), "[GENERAL]\r\nEffectSearchPaths=.\\Other\\**\r\n");
        Publish([XinpZip()]);
        ExtrasPicks.SetAddons(Settings, ["xinp"]);

        await Apply();

        Assert.Equal(@".\Other\**,.\reshade-shaders\Shaders\**", Ini("GENERAL", "EffectSearchPaths"));
        await Extras.RemoveAsync(Game(), Target, ComponentRegistry.ReShadeAddons, default);
        Assert.Equal(@".\Other\**", Ini("GENERAL", "EffectSearchPaths"));
    }

    [Fact]
    public async Task Removing_deletes_what_was_placed_and_puts_back_the_original()
    {
        Write(Path.Combine(Target, "swapchain_override.addon64"), "USERS-OWN");
        Publish([SwapChain()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);
        var before = Snapshot(Target);
        await Apply();
        Assert.Equal("SWAP64-1", File.ReadAllText(Path.Combine(Target, "swapchain_override.addon64")));

        var result = await Extras.RemoveAsync(Game(), Target, ComponentRegistry.ReShadeAddons, default);

        Assert.True(result.Ok);
        Assert.Equal(before, Snapshot(Target));
        Assert.Null(Manifest());
    }

    [Fact]
    public async Task Clearing_the_selection_and_applying_removes_the_component()
    {
        Publish([SwapChain()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);
        await Apply();

        ExtrasPicks.SetAddons(Settings, []);
        await Apply();

        Assert.False(File.Exists(Path.Combine(Target, "swapchain_override.addon64")));
        Assert.Null(Manifest()?.Component(ComponentRegistry.ReShadeAddons));
    }

    [Fact]
    public async Task A_game_can_have_its_own_list_and_reset_to_follow_the_global_one()
    {
        Publish([SwapChain(), XinpZip()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);
        var game = Game();
        ExtrasPicks.SetGameAddons(Settings.For(game.Id), ["xinp"]);

        Assert.Equal(["xinp"], ExtrasPicks.AddonIds(Settings, Settings.For(game.Id)));
        await Apply(game);
        Assert.True(File.Exists(Path.Combine(Target, "Xinp.addon64")));
        Assert.False(File.Exists(Path.Combine(Target, "swapchain_override.addon64")));

        ExtrasPicks.SetGameAddons(Settings.For(game.Id), null);
        Assert.Equal(["swapchain"], ExtrasPicks.AddonIds(Settings, Settings.For(game.Id)));
        Assert.False(ExtrasPicks.GameHasAddons(Settings.For(game.Id)));
    }

    [Fact]
    public async Task Add_ons_the_app_manages_elsewhere_or_that_have_no_download_are_left_out_with_a_reason()
    {
        var renodx = new CatalogAddon { Id = "renodx", Name = "RenoDX", ManagedBy = "renodx" };
        var manual = new CatalogAddon { Id = "pyhook", Name = "PyHook", Manual = "No download is published for this package; get it from the repository." };
        Publish([renodx, manual, SwapChain()]);
        ExtrasPicks.SetAddons(Settings, ["renodx", "pyhook", "swapchain"]);

        var result = await Apply();

        Assert.True(result.Ok);
        Assert.Contains(result.Notes, n => n.StartsWith("RenoDX") && n.Contains("RenoDX HDR mods"));
        Assert.Contains(result.Notes, n => n.StartsWith("PyHook") && n.Contains("No download"));
        Assert.True(File.Exists(Path.Combine(Target, "swapchain_override.addon64")));
    }

    [Fact]
    public async Task A_download_that_does_not_match_the_catalog_hash_changes_nothing()
    {
        var addon = SwapChain();
        Publish([addon]);
        Web[addon.Files[1].Url] = Bytes("TAMPERED");
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);

        var result = await Apply();

        Assert.False(result.Ok);
        Assert.Contains("refused", result.Message);
        Assert.False(File.Exists(Path.Combine(Target, "swapchain_override.addon64")));
        Assert.Null(Manifest());
    }

    [Fact]
    public async Task A_file_that_belongs_to_another_add_on_leaves_the_second_one_out_whole()
    {
        var a = AddonOf("a", Serve("shared.addon64", Bytes("A"), "x64"));
        var b = AddonOf("b", Serve("shared.addon64", Bytes("B-different"), "x64"), Serve("b_extra.addon64", Bytes("B2"), "x64"));
        Publish([a, b]);
        ExtrasPicks.SetAddons(Settings, ["a", "b"]);

        var result = await Apply();

        Assert.True(result.Ok);
        Assert.Equal("A", File.ReadAllText(Path.Combine(Target, "shared.addon64")));
        Assert.False(File.Exists(Path.Combine(Target, "b_extra.addon64")));
        Assert.Contains(result.Notes, n => n.StartsWith("b:") && n.Contains("shared.addon64"));
    }

    [Fact]
    public async Task Nothing_is_installed_in_a_game_with_anti_cheat_or_without_ReShade()
    {
        Publish([SwapChain()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);

        var blocked = await Apply(Game(antiCheat: "EasyAntiCheat"));
        Assert.False(blocked.Ok);
        Assert.Contains("EasyAntiCheat", blocked.Message);

        File.Delete(Path.Combine(Target, "ReShade.ini"));
        var noReShade = await Apply();
        Assert.False(noReShade.Ok);
        Assert.Contains("ReShade isn't installed", noReShade.Message);
        Assert.False(File.Exists(Path.Combine(Target, "swapchain_override.addon64")));
    }

    [Fact]
    public async Task Without_a_catalog_the_installed_add_ons_are_not_touched()
    {
        Publish([SwapChain()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);
        await Apply();

        Extras.Reload(new CatalogDocument { Schema = 1 });
        var result = await Apply();

        Assert.False(result.Ok);
        Assert.True(File.Exists(Path.Combine(Target, "swapchain_override.addon64")));
    }

    [Fact]
    public async Task A_new_catalog_hash_or_a_changed_selection_shows_as_an_update_and_applies()
    {
        Publish([SwapChain("1"), XinpZip()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);
        var game = Game();
        await Apply(game);
        Assert.Empty(Extras.ComponentsToUpdate(game, Target, Manifest()));

        Publish([SwapChain("2"), XinpZip()]);
        Assert.Equal([ComponentRegistry.ReShadeAddons], Extras.ComponentsToUpdate(game, Target, Manifest()));
        var results = await Extras.UpdateAsync(game, Target, null, default);
        Assert.All(results, r => Assert.True(r.Ok, r.Message));
        Assert.Equal("SWAP64-2", File.ReadAllText(Path.Combine(Target, "swapchain_override.addon64")));
        Assert.Empty(Extras.ComponentsToUpdate(game, Target, Manifest()));

        ExtrasPicks.SetAddons(Settings, ["swapchain", "xinp"]);
        Assert.Equal([ComponentRegistry.ReShadeAddons], Extras.ComponentsToUpdate(game, Target, Manifest()));
        await Extras.UpdateAsync(game, Target, null, default);
        Assert.True(File.Exists(Path.Combine(Target, "Xinp.addon64")));
        Assert.Empty(Extras.ComponentsToUpdate(game, Target, Manifest()));
    }

    [Fact]
    public async Task A_game_without_the_component_is_never_listed_for_an_update()
    {
        Publish([SwapChain()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);

        Assert.Empty(Extras.ComponentsToUpdate(Game(), Target, Manifest()));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_file_the_user_changed_since_is_kept_when_the_add_on_is_removed()
    {
        Publish([SwapChain()]);
        ExtrasPicks.SetAddons(Settings, ["swapchain"]);
        await Apply();
        File.WriteAllText(Path.Combine(Target, "swapchain_override.addon64"), "EDITED");

        var result = await Extras.RemoveAsync(Game(), Target, ComponentRegistry.ReShadeAddons, default);

        Assert.Equal("EDITED", File.ReadAllText(Path.Combine(Target, "swapchain_override.addon64")));
        Assert.Contains(result.Notes, n => n.Contains("changed since"));
    }
}
