using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Game to catalog entry matching: overrides, Steam AppID, then normalised names with a sequel guard.</summary>
public sealed class GameMatcherTests
{
    private static CatalogMod Mod(string id, string name, int[]? appIds = null, string listing = "main", bool files = true, string[]? aliases = null) => new()
    {
        Id = id,
        Name = name,
        SteamAppIds = [.. appIds ?? []],
        Listing = listing,
        Aliases = [.. aliases ?? []],
        Files = files ? [new CatalogBinary { Name = id + ".addon64", Url = "https://x.test/" + id, Sha256 = new string('a', 64) }] : [],
    };

    private static GameMatcher Matcher(CatalogOverrides? overrides = null, params CatalogMod[] mods) =>
        new(new CatalogRenodx { Mods = [.. mods], Overrides = overrides ?? new CatalogOverrides() });

    private static readonly CatalogMod[] Library =
    [
        Mod("hades", "Hades", [1145360]),
        Mod("hades-ii", "Hades II", [1145350]),
        Mod("code-vein", "Code Vein"),
        Mod("alien-isolation", "Alien: Isolation", [214490]),
        Mod("ac2", "Assassin’s Creed® II"),
        Mod("ac", "Assassin's Creed"),
        Mod("nioh", "Nioh"),
        Mod("nioh-3", "Nioh 3"),
        Mod("sleeping-dogs", "Sleeping Dogs"),
        Mod("sleeping-dogs-de", "Sleeping Dogs: Definitive Edition"),
        Mod("old", "Old Game", [555], listing: "deprecated"),
        Mod("luma", "Luma Framework", listing: "related"),
        Mod("portal", "Portal"),
        Mod("abz", "ABZÛ"),
    ];

    private static string? Id(CatalogMatch m) => m.Mod?.Id;

    [Fact]
    public void Steam_app_id_wins_over_a_conflicting_name()
    {
        var m = Matcher(null, Library).Match(214490, "Hades");

        Assert.Equal("alien-isolation", Id(m));
        Assert.Equal(MatchKind.AppId, m.Kind);
    }

    [Fact]
    public void Without_a_known_app_id_the_name_decides()
    {
        var m = Matcher(null, Library).Match(424242, "Alien Isolation");

        Assert.Equal(MatchKind.Exact, m.Kind);
        Assert.Equal("alien-isolation", Id(m));
    }

    [Theory]
    [InlineData("HADES", "hades")]
    [InlineData("Hades II", "hades-ii")]
    [InlineData("Assassin's Creed™ II", "ac2")]
    [InlineData("assassins creed ii", "ac2")]
    [InlineData("Abzu", "abz")]
    [InlineData("alien: isolation", "alien-isolation")]
    public void Exact_names_ignore_case_punctuation_trademark_signs_and_diacritics(string game, string expected)
    {
        var m = Matcher(null, Library).Match(null, game);

        Assert.Equal(MatchKind.Exact, m.Kind);
        Assert.Equal(expected, Id(m));
    }

    [Fact]
    public void A_game_name_that_contains_an_entry_name_matches_and_the_longest_entry_wins()
    {
        var matcher = Matcher(null, Library);

        var code = matcher.Match(null, "Code Vein GOTY");
        var dogs = matcher.Match(null, "Sleeping Dogs: Definitive Edition Remastered");

        Assert.Equal((MatchKind.Contains, "code-vein"), (code.Kind, Id(code)));
        Assert.Equal((MatchKind.Contains, "sleeping-dogs-de"), (dogs.Kind, Id(dogs)));
    }

    [Fact]
    public void A_short_entry_name_is_never_matched_by_containment()
    {
        var matcher = Matcher(null, Mod("ori", "Ori"), Mod("doom", "Doom"));

        Assert.Equal(MatchKind.None, matcher.Match(null, "Ori and the Blind Forest").Kind);
        Assert.Equal("doom", Id(matcher.Match(null, "Doom Eternal")));
    }

    [Fact]
    public void A_game_that_is_an_entry_plus_only_a_sequel_number_is_not_that_entry()
    {
        var matcher = Matcher(null, Library);

        Assert.Equal(MatchKind.None, matcher.Match(null, "Hades III").Kind);
        Assert.Equal(MatchKind.None, matcher.Match(null, "Nioh 2").Kind);
        Assert.Equal("nioh", Id(matcher.Match(null, "Nioh Complete Edition")));
    }

    [Fact]
    public void An_entry_that_starts_with_the_game_name_matches()
    {
        var m = Matcher(null, Library).Match(null, "Alien");

        Assert.Equal((MatchKind.StartsWith, "alien-isolation"), (m.Kind, Id(m)));
    }

    [Theory]
    [InlineData("Hades")]
    [InlineData("Sleeping")]
    public void Starts_with_picks_the_shortest_name_and_skips_sequels(string game)
    {
        var m = Matcher(null, Mod("a", "Hades II"), Mod("b", "Sleeping Dogs: Definitive Edition"), Mod("c", "Sleeping Dogs")).Match(null, game);

        Assert.Equal(game == "Hades" ? MatchKind.None : MatchKind.StartsWith, m.Kind);
        if (game != "Hades") Assert.Equal("c", Id(m));
    }

    [Theory]
    [InlineData("ii", true)]
    [InlineData("iv", true)]
    [InlineData("xxxix", true)]
    [InlineData("2", true)]
    [InlineData("2016", true)]
    [InlineData("civic", false)]
    [InlineData("xl", false)]
    [InlineData("isolation", false)]
    [InlineData("2remastered", false)]
    [InlineData("", false)]
    public void Sequel_suffixes_are_digits_or_small_roman_numerals(string suffix, bool sequel) =>
        Assert.Equal(sequel, GameMatcher.IsSequelSuffix(suffix));

    [Fact]
    public void A_game_name_too_short_for_a_prefix_match_gets_nothing()
    {
        Assert.Equal(MatchKind.None, Matcher(null, Library).Match(null, "Ali").Kind);
        Assert.Equal(MatchKind.None, Matcher(null, Library).Match(null, "").Kind);
        Assert.Equal(MatchKind.None, Matcher(null, Library).Match(null, "™").Kind);
    }

    [Fact]
    public void An_unknown_game_matches_nothing()
    {
        var m = Matcher(null, Library).Match(1, "Totally Unknown Game");

        Assert.Equal(CatalogMatch.None, m);
    }

    [Fact]
    public void Deprecated_and_related_entries_are_not_matched()
    {
        var matcher = Matcher(null, Library);

        Assert.Equal(MatchKind.None, matcher.Match(555, "Old Game").Kind);
        Assert.Equal(MatchKind.None, matcher.Match(null, "Luma Framework").Kind);
    }

    [Fact]
    public void An_alias_override_beats_the_app_id_and_every_fuzzy_rule()
    {
        var overrides = new CatalogOverrides { NameFixes = [new CatalogNameFix { Id = "nioh", Aliases = ["Nioh 3 Trial"] }] };
        var matcher = Matcher(overrides, Library);

        var m = matcher.Match(null, "Nioh 3 Trial");

        Assert.Equal((MatchKind.Override, "nioh"), (m.Kind, Id(m)));
        Assert.Equal("nioh-3", Id(matcher.Match(null, "Nioh 3")));
    }

    [Fact]
    public void An_app_id_override_beats_the_entrys_own_app_id()
    {
        var overrides = new CatalogOverrides { AppIdFixes = [new CatalogAppIdFix { Id = "hades-ii", SteamAppIds = [1145360] }] };

        var m = Matcher(overrides, Library).Match(1145360, "Hades");

        Assert.Equal((MatchKind.Override, "hades-ii"), (m.Kind, Id(m)));
    }

    [Fact]
    public void An_override_for_an_unknown_entry_is_ignored()
    {
        var overrides = new CatalogOverrides { NameFixes = [new CatalogNameFix { Id = "nope", Aliases = ["Hades"] }] };

        Assert.Equal("hades", Id(Matcher(overrides, Library).Match(null, "Hades")));
    }

    [Fact]
    public void A_blocked_entry_still_matches_so_the_ui_can_say_why()
    {
        var blocked = Mod("crashy", "Crashy", [777]);
        blocked.Blocked = true;
        blocked.BlockReason = "crashes";

        var m = Matcher(null, blocked).Match(777, "Crashy");

        Assert.True(m.Mod!.Blocked);
        Assert.Equal("crashes", m.Mod.BlockReason);
    }

    [Fact]
    public void Entries_sharing_an_app_id_prefer_one_that_is_not_blocked_and_has_files()
    {
        var blocked = Mod("a-blocked", "Same", [9]);
        blocked.Blocked = true;
        var empty = Mod("b-empty", "Same", [9], files: false);
        var good = Mod("c-good", "Same", [9]);

        Assert.Equal("c-good", Id(Matcher(null, blocked, empty, good).Match(9, "x")));
    }

    [Fact]
    public void A_game_that_uses_the_generic_engine_mod_counts_as_having_one()
    {
        var generic = Mod("deaths-door", "Death's Door", [894020], files: false);
        generic.Generic = "unity";

        var m = Matcher(null, generic, Mod("other", "Death's Door", [1], files: false)).Match(894020, "Death's Door");

        Assert.Equal("deaths-door", Id(m));
    }

    [Fact]
    public void The_fixture_catalog_matches_games_the_way_the_scanner_reports_them()
    {
        var matcher = new GameMatcher(CatalogFixtures.Renodx());

        Assert.Equal("hades", Id(matcher.Match(1145360, "Hades")));
        Assert.Equal("hades-ii", Id(matcher.Match(null, "Hades II")));
        Assert.Equal("alien-isolation", Id(matcher.Match(null, "Alien: Isolation GOTY")));
        Assert.Equal("deaths-door", Id(matcher.Match(894020, "Death’s Door")));
        Assert.Equal(MatchKind.None, matcher.Match(999, "Old Mod").Kind);
    }

    [Fact]
    public void Matching_a_scanned_game_uses_its_steam_app_id()
    {
        var game = new DLSSUpdater.Scan.GameInfo { Name = "Something Else Entirely", SteamAppId = 1145360 };

        Assert.Equal("hades", Id(Matcher(null, Library).Match(game)));
    }

    [Theory]
    [InlineData("Assassin’s Creed® II", "assassinscreedii")]
    [InlineData("Pokémon™ Legends", "pokemonlegends")]
    [InlineData("Half-Life 2", "halflife2")]
    [InlineData("™®©", "")]
    public void Normalize_drops_signs_diacritics_and_punctuation(string name, string expected) =>
        Assert.Equal(expected, GameMatcher.Normalize(name));
}
