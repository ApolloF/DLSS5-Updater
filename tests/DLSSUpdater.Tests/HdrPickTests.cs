using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>Which RenoDX HDR mod a game gets: named, Unreal Extended, plain generic Unreal, generic Unity in 32 or 64 bit, or nothing and why.</summary>
public class HdrPickTests
{
    private readonly HdrPicker _picker = new(HdrFixtures.Renodx());

    [Fact]
    public void A_named_mod_is_found_by_steam_appid_first()
    {
        // The store title differs from the catalog name; the AppID decides.
        var choice = _picker.Pick(HdrFixtures.Unreal("Supergiant Hades Store Edition", appId: 1145360), null);

        Assert.Equal(HdrKind.Named, choice.Kind);
        Assert.Equal("hades", choice.ModId);
        Assert.Equal("renodx-hades.addon64", choice.File!.Name);
        Assert.Contains("Steam AppID 1145360", choice.Reason);
        Assert.Equal("https://github.com/clshortfuse/renodx/discussions/1", choice.Link);
        Assert.False(choice.CanToggleUeExtended);
    }

    [Fact]
    public void A_named_mod_wins_over_the_engine_default_even_for_an_unreal_game()
    {
        var choice = _picker.Pick(HdrFixtures.Unreal("Hades"), null);

        Assert.Equal(HdrKind.Named, choice.Kind);
        Assert.Contains("matched by its name", choice.Reason);
    }

    [Fact]
    public void An_unreal_game_without_a_named_mod_gets_unreal_extended_by_default()
    {
        var choice = _picker.Pick(HdrFixtures.Unreal("Some UE Game"), null);

        Assert.Equal(HdrKind.UeExtended, choice.Kind);
        Assert.Equal("renodx-ue-extended.addon64", choice.File!.Name);
        Assert.True(choice.CanToggleUeExtended);
        Assert.NotNull(choice.Ue);
    }

    [Fact]
    public void The_users_switch_picks_the_plain_generic_unreal_add_on_and_back()
    {
        var game = HdrFixtures.Unreal("Some UE Game");

        var plain = _picker.Pick(game, false);
        var ext = _picker.Pick(game, true);

        Assert.Equal(HdrKind.UnrealGeneric, plain.Kind);
        Assert.Equal("renodx-unrealengine.addon64", plain.File!.Name);
        Assert.True(plain.CanToggleUeExtended);
        Assert.Equal(HdrKind.UeExtended, ext.Kind);
    }

    [Fact]
    public void A_catalog_opt_out_makes_the_plain_add_on_the_default_and_says_so()
    {
        var game = HdrFixtures.Unreal("Opted Out Game");

        var byDefault = _picker.Pick(game, null);
        var chosen = _picker.Pick(game, true);

        Assert.Equal(HdrKind.UnrealGeneric, byDefault.Kind);
        Assert.Contains(byDefault.Notes, n => n.Contains("opts this game out"));
        // The user can still ask for it knowingly.
        Assert.Equal(HdrKind.UeExtended, chosen.Kind);
        Assert.Contains(chosen.Notes, n => n.Contains("shouldn't get Unreal Extended"));
        Assert.Equal(HdrKind.UnrealGeneric, _picker.Pick(HdrFixtures.Unreal("Whatever", appId: 4242), null).Kind);
    }

    [Fact]
    public void Unity_gets_the_generic_add_on_in_the_games_bitness()
    {
        var sixtyFour = _picker.Pick(HdrFixtures.Unity("Some Unity Game", 64), null);
        var thirtyTwo = _picker.Pick(HdrFixtures.Unity("Some Unity Game", 32), null);

        Assert.Equal(HdrKind.UnityGeneric, sixtyFour.Kind);
        Assert.Equal("renodx-unityengine.addon64", sixtyFour.File!.Name);
        Assert.Equal("renodx-unityengine.addon32", thirtyTwo.File!.Name);
        Assert.Equal("x86", thirtyTwo.File.Arch);
        Assert.Contains("32-bit", thirtyTwo.Title);
    }

    [Fact]
    public void A_32_bit_game_gets_an_addon32_only_when_the_catalog_has_one()
    {
        // Without a hash a build can't be installed, so there is no 32-bit file to offer.
        var json = HdrFixtures.Json().Replace($"\"sha256\": \"{CatalogFixtures.Sha(HdrFixtures.Unity32Content)}\", ", "");
        var picker = new HdrPicker(HdrFixtures.Renodx(json));

        var choice = picker.Pick(HdrFixtures.Unity("Some Unity Game", 32), null);

        Assert.Equal(HdrKind.None, choice.Kind);
        Assert.Contains("32-bit", choice.Reason);
    }

    [Fact]
    public void A_32_bit_unreal_game_gets_nothing_because_the_unreal_add_ons_are_64_bit_only()
    {
        var choice = _picker.Pick(HdrFixtures.Unreal("Old UE Game", bits: 32), null);

        Assert.Equal(HdrKind.None, choice.Kind);
        Assert.Contains("32-bit", choice.Reason);
    }

    [Fact]
    public void A_named_mod_for_a_32_bit_game_uses_its_addon32()
    {
        var choice = _picker.Pick(HdrFixtures.Other("Alien Isolation", GameEngine.Unknown, bits: 32), null);

        Assert.Equal(HdrKind.Named, choice.Kind);
        Assert.Equal("renodx-alienisolation.addon32", choice.File!.Name);
        Assert.Contains(choice.Notes, n => n.Contains("in progress"));
    }

    [Fact]
    public void A_named_mod_with_only_a_32_bit_build_is_refused_for_a_64_bit_game()
    {
        var choice = _picker.Pick(HdrFixtures.Other("Alien Isolation", GameEngine.Unknown, bits: 64), null);

        Assert.Equal(HdrKind.None, choice.Kind);
        Assert.Contains("64-bit", choice.Reason);
    }

    [Fact]
    public void Unreal_Engine_3_gets_nothing_with_the_reason()
    {
        var choice = _picker.Pick(HdrFixtures.Other("Old Game", GameEngine.UnrealLegacy), null);

        Assert.Equal(HdrKind.None, choice.Kind);
        Assert.Contains("Unreal Engine 3", choice.Reason);
    }

    [Theory]
    [InlineData(GameEngine.Unknown)]
    [InlineData(GameEngine.REEngine)]
    public void An_unknown_or_other_engine_gets_nothing_with_a_reason(GameEngine engine)
    {
        var choice = _picker.Pick(HdrFixtures.Other("Mystery Game", engine), null);

        Assert.Equal(HdrKind.None, choice.Kind);
        Assert.False(string.IsNullOrWhiteSpace(choice.Reason));
        Assert.Null(choice.File);
    }

    [Fact]
    public void A_blocked_catalog_entry_gets_nothing_and_the_catalogs_reason()
    {
        var choice = _picker.Pick(HdrFixtures.Unreal("Crashy", appId: 777), null);

        Assert.Equal(HdrKind.None, choice.Kind);
        Assert.Contains("crashes on start", choice.Reason);
    }

    [Fact]
    public void A_blocklist_item_by_game_name_blocks_a_game_with_no_entry()
    {
        var choice = _picker.Pick(HdrFixtures.Unreal("Blocked By Name"), null);

        Assert.Equal(HdrKind.None, choice.Kind);
        Assert.Contains("anti-cheat driver", choice.Reason);
    }

    [Fact]
    public void A_named_entry_that_uses_the_generic_add_on_follows_the_engine_path()
    {
        var unity = _picker.Pick(HdrFixtures.Unity("Death's Door", appId: 894020), null);
        var unreal = _picker.Pick(HdrFixtures.Unreal("Named Unreal Game", appId: 555), null);

        Assert.Equal(HdrKind.UnityGeneric, unity.Kind);
        Assert.Contains("generic Unity add-on", unity.Reason);
        Assert.Equal(HdrKind.UeExtended, unreal.Kind);
        Assert.True(unreal.CanToggleUeExtended);
    }

    [Fact]
    public void A_named_entry_without_a_download_says_to_use_its_page_and_does_not_fall_back_to_the_engine()
    {
        var choice = _picker.Pick(HdrFixtures.Unreal("Nexus Only", appId: 556), null);

        Assert.Equal(HdrKind.None, choice.Kind);
        Assert.Contains("no download", choice.Reason);
        Assert.Equal("https://nexusmods.com/x", choice.Link);
    }

    [Fact]
    public void Anti_cheat_games_get_nothing_with_the_reason()
    {
        var game = HdrFixtures.Unreal("Hades", appId: 1145360);
        game.AntiCheat = "EasyAntiCheat";

        var choice = _picker.Pick(game, null);

        Assert.Equal(HdrKind.None, choice.Kind);
        Assert.Contains("EasyAntiCheat", choice.Reason);
    }

    [Fact]
    public void A_build_without_a_hash_is_never_offered()
    {
        var json = HdrFixtures.Json().Replace($"\"sha256\": \"{CatalogFixtures.Sha(HdrFixtures.HadesContent)}\", ", "");
        var choice = new HdrPicker(HdrFixtures.Renodx(json)).Pick(HdrFixtures.Unreal("Hades", appId: 1145360), null);

        Assert.Equal(HdrKind.None, choice.Kind);
    }

    // ---------- Unreal Extended method ----------

    [Theory]
    [InlineData("Native Game", UeMethod.Native, 0, false)]
    [InlineData("Ini Game", UeMethod.Ini, 0, true)]
    [InlineData("Upgrade Game", UeMethod.Upgrade, 1, false)]
    public void The_wiki_entry_decides_set_path_and_whether_engine_ini_is_touched(string name, UeMethod method, int setPath, bool engineIni)
    {
        var ue = _picker.Pick(HdrFixtures.Unreal(name), null).Ue!;

        Assert.True(ue.Known);
        Assert.Equal(method, ue.Method);
        Assert.Equal(setPath, ue.SetPath);
        Assert.Equal(engineIni, ue.WritesEngineIni);
    }

    [Fact]
    public void The_upgrade_entry_carries_its_format_mode_and_copy_destinations()
    {
        var choice = _picker.Pick(HdrFixtures.Unreal("Upgrade Game"), null);

        Assert.Equal([("B8G8R8A8_TYPELESS", 3)], choice.Ue!.Upgrades);
        Assert.False(choice.Ue.CopyDestinations);
        Assert.Contains(choice.Notes, n => n.Contains("in progress"));
    }

    [Theory]
    [InlineData("4.27.2", UeMethod.Upgrade, 1)]
    [InlineData("5.3.2", UeMethod.Ini, 0)]
    [InlineData("", UeMethod.Upgrade, 1)]
    public void A_game_the_wiki_does_not_list_takes_the_upgrade_path_on_UE4_or_unknown_and_native_with_engine_ini_on_UE5(string version, UeMethod method, int setPath)
    {
        var game = HdrFixtures.Unreal("Unlisted Game", version: version);
        game.Tech.EngineVersion = version.Length == 0 ? null : version;

        var ue = _picker.Pick(game, null).Ue!;

        Assert.False(ue.Known);
        Assert.Equal(method, ue.Method);
        Assert.Equal(setPath, ue.SetPath);
    }

    [Fact]
    public void The_signature_changes_with_the_build_the_method_and_the_peak_brightness()
    {
        var game = HdrFixtures.Unreal("Ini Game");
        var a = _picker.Pick(game, null).Signature(null);
        var newer = new HdrPicker(HdrFixtures.Renodx(HdrFixtures.Json("MZ ue-extended build 2", "2026-10-22-bbbbbbb"))).Pick(game, null).Signature(null);

        Assert.NotEqual(a, newer);
        Assert.NotEqual(a, _picker.Pick(game, null).Signature(1000));
        Assert.Equal(a, _picker.Pick(game, null).Signature(null));
    }
}
