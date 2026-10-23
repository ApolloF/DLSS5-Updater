using System.Text.Json;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>The add-on, shader pack and preset choices kept in the generic component picks: global, per game, auto and reset.</summary>
public sealed class ExtrasPicksTests
{
    private static CatalogShaderPacks Packs() => new()
    {
        Packs = [new CatalogShaderPack { Id = "lilium", Default = true }, new CatalogShaderPack { Id = "pumbo", Default = true }, new CatalogShaderPack { Id = "sweetfx" }],
    };

    [Fact]
    public void Nothing_picked_means_no_add_ons_and_the_default_packs()
    {
        var s = new AppSettings();

        Assert.Empty(ExtrasPicks.AddonIds(s, null));
        Assert.Equal(["lilium", "pumbo"], ExtrasPicks.PackIds(s, null, Packs()).Order());
        Assert.Null(ExtrasPicks.PresetOf(null));
        Assert.Null(ExtrasPicks.BitsOf(null));
    }

    [Fact]
    public void A_global_choice_replaces_the_defaults_and_an_empty_one_means_none()
    {
        var s = new AppSettings();

        ExtrasPicks.SetPacks(s, ["sweetfx"]);
        Assert.Equal(["sweetfx"], ExtrasPicks.PackIds(s, null, Packs()));

        ExtrasPicks.SetPacks(s, []);
        Assert.Empty(ExtrasPicks.PackIds(s, null, Packs()));
    }

    [Fact]
    public void A_game_follows_the_global_choice_until_it_has_its_own_and_goes_back_with_null()
    {
        var s = new AppSettings();
        var game = s.For(@"c:\games\a");
        ExtrasPicks.SetAddons(s, ["a1", "a2"]);

        Assert.Equal(["a1", "a2"], ExtrasPicks.AddonIds(s, game).Order());
        ExtrasPicks.SetGameAddons(game, ["a3"]);
        Assert.Equal(["a3"], ExtrasPicks.AddonIds(s, game));
        ExtrasPicks.SetGameAddons(game, []);
        Assert.Empty(ExtrasPicks.AddonIds(s, game));
        Assert.True(ExtrasPicks.GameHasAddons(game));
        ExtrasPicks.SetGameAddons(game, null);
        Assert.Equal(["a1", "a2"], ExtrasPicks.AddonIds(s, game).Order());
        Assert.False(ExtrasPicks.GameHasAddons(game));
        Assert.Null(game.PickOf(ComponentRegistry.ReShadeAddons));
    }

    [Fact]
    public void Bitness_and_the_add_on_list_are_kept_side_by_side()
    {
        var s = new AppSettings();
        var game = s.For("g");

        ExtrasPicks.SetGameBits(game, 32);
        ExtrasPicks.SetGameAddons(game, ["a"]);
        Assert.Equal(32, ExtrasPicks.BitsOf(game));
        Assert.Equal(["a"], ExtrasPicks.AddonIds(s, game));

        ExtrasPicks.SetGameBits(game, null);
        Assert.Null(ExtrasPicks.BitsOf(game));
        Assert.Equal(["a"], ExtrasPicks.AddonIds(s, game));
    }

    [Fact]
    public void The_preset_pick_is_per_game()
    {
        var game = new AppSettings().For("g");

        ExtrasPicks.SetGamePreset(game, "My Look");
        Assert.Equal("My Look", ExtrasPicks.PresetOf(game));
        ExtrasPicks.SetGamePreset(game, null);
        Assert.Null(ExtrasPicks.PresetOf(game));
    }

    [Fact]
    public void The_choices_survive_a_round_trip_through_settings_json()
    {
        var s = new AppSettings();
        ExtrasPicks.SetAddons(s, ["a1"]);
        ExtrasPicks.SetPacks(s, ["sweetfx"]);
        ExtrasPicks.SetGamePreset(s.For("g"), "My Look");
        ExtrasPicks.SetGameBits(s.For("g"), 32);

        var back = JsonSerializer.Deserialize(JsonSerializer.Serialize(s, JsonCtx.Default.AppSettings), JsonCtx.Default.AppSettings)!;

        Assert.Equal(["a1"], ExtrasPicks.AddonIds(back, null));
        Assert.Equal(["sweetfx"], ExtrasPicks.PackIds(back, null, Packs()));
        Assert.Equal("My Look", ExtrasPicks.PresetOf(back.Games["g"]));
        Assert.Equal(32, ExtrasPicks.BitsOf(back.Games["g"]));
    }
}
