using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>Which Luma mod a game gets: exact normalised name, the generic Unreal mod's eligibility, the manual pick, and the catalog's parsing.</summary>
public class LumaPickTests
{
    private static LumaChoice Pick(GameInfo g, string? manual = null) => LumaFixtures.Picker().Pick(g, manual);

    [Fact]
    public void The_luma_section_of_the_catalog_is_parsed()
    {
        var luma = LumaFixtures.Section();

        Assert.Equal("latest-755", luma.Release);
        Assert.Equal(3, luma.Packages.Count);
        Assert.Equal(LumaFixtures.Sha(LumaFixtures.PreyZip), luma.PackageOf("Prey")!.FileFor(64)!.Sha256);
        Assert.Null(luma.PackageOf("Borderlands_2")!.FileFor(64));
        Assert.NotNull(luma.PackageOf("Borderlands_2")!.FileFor(32));
        var bl2 = luma.Mods.Single(m => m.Id == "borderlands-2");
        Assert.True(bl2.RequiresDgVoodoo);
        Assert.Equal(["2.82.3"], bl2.DgVoodooVersions);
        Assert.Equal("-dx11", luma.UnrealGames[0].LaunchArgs.Single());
        Assert.Equal("r.LumaTest", luma.UnrealGames[0].EngineIni.Single().Key);
        Assert.True(luma.UnrealGames[1].Blocked);
    }

    [Theory]
    [InlineData("Prey")]
    [InlineData("PREY")]
    [InlineData("prey ")]
    public void A_game_is_matched_by_its_exact_normalised_name(string name)
    {
        var c = Pick(LumaFixtures.Plain(name));

        Assert.Equal(LumaKind.Named, c.Kind);
        Assert.Equal("prey", c.ModId);
        Assert.Equal("latest-755", c.Version);
        Assert.Equal("Prey", c.PackageId);
        Assert.Contains("Needs the 2017 build.", c.Notes);
    }

    [Theory]
    [InlineData("Prey 2")]
    [InlineData("Prey Digital Deluxe Edition")]
    [InlineData("Pre")]
    public void No_substring_matches(string name)
    {
        var c = Pick(LumaFixtures.Plain(name));

        Assert.False(c.IsOffered);
        Assert.Null(c.ModId);
    }

    [Fact]
    public void A_32_bit_mod_is_not_offered_to_a_64_bit_game_and_the_other_way_round()
    {
        Assert.False(Pick(LumaFixtures.Plain("Borderlands 2", bits: 64)).IsOffered);
        Assert.True(Pick(LumaFixtures.Plain("Borderlands 2", bits: 32)).IsOffered);
        Assert.False(Pick(LumaFixtures.Plain("Prey", bits: 32)).IsOffered);
    }

    [Fact]
    public void A_mod_with_dgVoodoo_says_so_and_never_names_a_download_the_app_makes()
    {
        var c = Pick(LumaFixtures.Plain("Borderlands 2", bits: 32));

        Assert.True(c.NeedsDgVoodoo);
        Assert.Contains("2.82.3", c.DgVoodooLines[0]);
        Assert.Contains("dgVoodoo2/releases", c.DgVoodooLines[0]);
        Assert.Contains("doesn't download", c.DgVoodooLines[0]);
        Assert.Contains("Set the output API to DirectX 11.", c.DgVoodooLines);
    }

    [Fact]
    public void A_mod_the_author_publishes_elsewhere_has_no_download_but_a_link()
    {
        var c = Pick(LumaFixtures.Plain("Nexus Mod"));

        Assert.False(c.IsOffered);
        Assert.Equal("https://nexusmods.com/x", c.Link);
    }

    [Fact]
    public void A_listed_Unreal_DX11_game_gets_the_generic_mod_with_its_ini_lines_and_launch_args()
    {
        var c = Pick(LumaFixtures.Unreal("Listed Game"));

        Assert.Equal(LumaKind.Unreal, c.Kind);
        Assert.Equal("Unreal_Engine", c.PackageId);
        Assert.Equal("r.LumaTest", c.EngineIni.Single().Key);
        Assert.Equal(["-dx11"], c.LaunchArgs);
        Assert.Contains("Disable motion blur.", c.Notes);
    }

    [Theory]
    [InlineData("Unlisted Game", "4.27.2", GraphicsApis.Dx11, 64)]
    [InlineData("Listed Game", "4.27.2", GraphicsApis.Dx12, 64)]
    [InlineData("Listed Game", "5.3.2", GraphicsApis.Dx11, 64)]
    [InlineData("Listed Game", "4.27.2", GraphicsApis.Dx11, 32)]
    [InlineData("Blocked Game", "4.26.0", GraphicsApis.Dx11, 64)]
    public void The_generic_mod_is_not_offered_automatically_when_a_rule_fails(string name, string ue, GraphicsApis apis, int bits)
    {
        Assert.False(Pick(LumaFixtures.Unreal(name, bits, ue, apis)).IsOffered);
    }

    [Fact]
    public void A_UE5_game_is_offered_when_the_user_set_DirectX_11_by_hand()
    {
        var g = LumaFixtures.Unreal("Listed Game", version: "5.3.2", apis: GraphicsApis.Dx12);
        g.Manual = new TechOverride { Apis = GraphicsApis.Dx11 };

        Assert.True(Pick(g).IsOffered);
    }

    [Fact]
    public void Anti_cheat_games_get_nothing_even_by_hand()
    {
        var g = LumaFixtures.Plain("Prey");
        g.AntiCheat = "EasyAntiCheat";

        Assert.False(Pick(g).IsOffered);
        Assert.False(Pick(g, "prey").IsOffered);
        Assert.Contains("EasyAntiCheat", Pick(g).Reason);
    }

    [Fact]
    public void A_manual_pick_wins_and_turns_soft_rules_into_notes()
    {
        var c = Pick(LumaFixtures.Unreal("Unlisted Game", version: "5.3.2", apis: GraphicsApis.Dx12), LumaPicker.UnrealId);

        Assert.True(c.IsOffered);
        Assert.Contains(c.Notes, n => n.Contains("DirectX 11"));
        Assert.Contains(c.Notes, n => n.Contains("Unreal Engine 5"));
        Assert.Contains(c.Notes, n => n.Contains("untested"));
        Assert.Equal(["-dx11"], c.LaunchArgs);

        var named = Pick(LumaFixtures.Plain("Some Other Game"), "prey");
        Assert.Equal("prey", named.ModId);
        Assert.Contains("your pick", named.Reason);
    }

    [Fact]
    public void A_manual_pick_still_refuses_what_is_a_fact()
    {
        Assert.False(Pick(LumaFixtures.Unreal("Listed Game", bits: 32), LumaPicker.UnrealId).IsOffered);
        Assert.False(Pick(LumaFixtures.Plain("X", bits: 64), "borderlands-2").IsOffered);
        Assert.False(Pick(LumaFixtures.Plain("X"), "nexus-mod").IsOffered);
        Assert.False(Pick(LumaFixtures.Plain("X"), "gone").IsOffered);
    }

    [Fact]
    public void The_manual_list_has_every_mod_with_a_download_and_the_generic_Unreal_mod_but_not_the_Nexus_one()
    {
        var ids = LumaFixtures.Picker().ManualChoices().Select(c => c.Id).ToList();

        Assert.Equal(["borderlands-2", "unreal", "prey"], ids);
    }

    [Fact]
    public void The_signature_changes_with_the_file_and_with_the_HDR_setting()
    {
        var a = Pick(LumaFixtures.Plain("Prey"));
        var b = LumaFixtures.Picker(LumaFixtures.PreyZipV2).Pick(LumaFixtures.Plain("Prey"));

        Assert.NotEqual(a.Signature(null), b.Signature(null));
        Assert.NotEqual(a.Signature(null), a.Signature(true));
        Assert.NotEqual(a.Signature(true), a.Signature(false));
    }
}
