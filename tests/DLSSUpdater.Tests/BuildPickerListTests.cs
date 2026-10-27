using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>The one list every build picker shows: library builds and catalog offers by version, one download per version.</summary>
public sealed class BuildPickerListTests
{
    private static PickerEntry Lib(string id, string version, string? label = null) => new(id, version, label ?? version, id.PadRight(8, '0'));
    private static PickerEntry Offer(string sha, string version) => new("catalog:" + sha, version, version, sha.PadRight(8, '0'));

    [Fact]
    public void Library_and_offers_mix_newest_version_first()
    {
        var list = BuildPickerList.Merge([Lib("a", "310.9.16"), Lib("b", "310.7.0")], [Offer("c", "310.8.2"), Offer("d", "310.10.0")]);

        Assert.Equal(["310.10.0 · download", "310.9.16", "310.8.2 · download", "310.7.0"], list.Select(c => c.Label));
        Assert.Equal(["catalog:d", "a", "catalog:c", "b"], list.Select(c => c.Tag));
    }

    [Fact]
    public void A_version_the_library_has_gets_no_download_and_other_versions_get_one_offer()
    {
        var list = BuildPickerList.Merge([Lib("a", "310.8.0")], [Offer("x", "310.8.0"), Offer("y", "310.8.1"), Offer("z", "310.8.1")]);

        Assert.Equal(["a", "catalog:y"], list.Select(c => c.Tag).Reverse());
        Assert.Equal(["310.8.1 · download", "310.8.0"], list.Select(c => c.Label));
    }

    [Fact]
    public void Several_library_builds_of_one_version_all_stay_and_are_told_apart_by_hash()
    {
        var list = BuildPickerList.Merge([Lib("aaaaaaaa1", "310.8.0"), Lib("bbbbbbbb2", "310.8.0", "310.8.0 · ShortFuse compat (RTX 20–50)")], []);

        Assert.Equal(["310.8.0", "310.8.0 · ShortFuse compat (RTX 20–50)"], list.Select(c => c.Label));

        var same = BuildPickerList.Merge([Lib("aaaaaaaa1", "310.8.0"), Lib("bbbbbbbb2", "310.8.0")], []);

        Assert.Equal(["310.8.0 · aaaaaaaa", "310.8.0 · bbbbbbbb"], same.Select(c => c.Label));
    }

    [Fact]
    public void A_pre_release_sorts_before_its_release_and_a_v_prefix_matches_the_same_version()
    {
        var list = BuildPickerList.Merge([Lib("a", "v11.0.0-beta.6")], [Offer("x", "11.0.0-beta.6"), Offer("y", "v11.0.0")]);

        Assert.Equal(["catalog:y", "a"], list.Select(c => c.Tag));
    }

    [Fact]
    public void The_nr_picker_label_is_short_and_drops_the_unverified_note()
    {
        var known = new NrVersion("cdfa6c1ec784867182164c7836f324f087b5c9f374d0c9fe46f54999feabf3ed", "310.9.16", "cdfa6c1ec784867182164c7836f324f087b5c9f374d0c9fe46f54999feabf3ed");
        var unknown = new NrVersion(new string('f', 64), "310.8.3", new string('f', 64));

        Assert.Equal("310.9.16 · ShortFuse compat (RTX 20–50, CUDA 13.4.3)", known.ToPickerEntry().Label);
        Assert.Equal("310.8.3", unknown.ToPickerEntry().Label);
    }

    [Fact]
    public void The_newest_choice_of_an_add_on_card_has_no_double_parentheses()
    {
        Assert.Equal("Newest · 5.0.0 (2026-10-20)", ViewModels.AddonLibraryViewModel.NewestLabel("5.0.0 (2026-10-20)"));
    }

    // ---------- the game page's labels (GamePickerLabels) ----------

    private static PickerChoice Row(string tag, string name, bool download = false) => new(tag, name, download);

    private static IEnumerable<string> Labels(IEnumerable<PickerChoice> list) => list.Select(c => c.Label);

    [Fact]
    public void A_default_that_follows_the_newest_build_is_one_newest_row_without_a_duplicate()
    {
        var list = GamePickerLabels.Compose(new PickerDefault("b3", "2026-10-16 13:04", FollowsNewest: true),
            [Row("b3", "2026-10-16 13:04"), Row("b2", "2026-10-03 23:50"), Row("b1", "2026-09-28 02:05"), Row("catalog:x", "2026-09-27 21:25", true)]);

        Assert.Equal(["Newest · 2026-10-16 13:04", "2026-10-03 23:50", "2026-09-28 02:05", "2026-09-27 21:25 · download"], Labels(list));
        Assert.Equal([null, "b2", "b1", "catalog:x"], list.Select(c => c.Tag));
    }

    [Fact]
    public void A_pinned_default_says_default_and_the_newest_build_is_marked()
    {
        var list = GamePickerLabels.Compose(new PickerDefault("b1", "v11.9.0", FollowsNewest: false), [Row("b2", "v12.5.0"), Row("b1", "v11.9.0")]);

        Assert.Equal(["Default · v11.9.0", "v12.5.0 · newest"], Labels(list));
    }

    [Fact]
    public void A_pinned_default_that_is_the_newest_build_says_so()
    {
        var list = GamePickerLabels.Compose(new PickerDefault("b2", "5.0.0", FollowsNewest: false), [Row("b2", "5.0.0"), Row("b1", "2.0.0")]);

        Assert.Equal(["Default · 5.0.0 · newest", "2.0.0"], Labels(list));
    }

    [Fact]
    public void A_newer_download_than_the_library_keeps_the_first_row_default_and_marks_the_download_newest()
    {
        var list = GamePickerLabels.Compose(new PickerDefault("own", "310.7.0", FollowsNewest: true),
            [Row("catalog:n", "310.8.3", true), Row("own", "310.7.0")]);

        Assert.Equal(["Default · 310.7.0", "310.8.3 · newest · download"], Labels(list));
    }

    [Fact]
    public void The_build_in_the_game_is_marked_installed()
    {
        var list = GamePickerLabels.Compose(new PickerDefault("b2", "v1.4.2", FollowsNewest: true), [Row("b2", "v1.4.2"), Row("b1", "v1.3.0")], installed: "b1");
        Assert.Equal(["Newest · v1.4.2", "v1.3.0 · installed"], Labels(list));

        var current = GamePickerLabels.Compose(new PickerDefault("b2", "v1.4.2", FollowsNewest: true), [Row("b2", "v1.4.2")], installed: "B2");
        Assert.Equal(["Newest · v1.4.2 · installed"], Labels(current));
    }

    [Fact]
    public void A_game_pick_of_the_default_build_keeps_its_row()
    {
        var list = GamePickerLabels.Compose(new PickerDefault("b2", "v1.4.2", FollowsNewest: true), [Row("b2", "v1.4.2"), Row("b1", "v1.3.0")], pin: "b2");

        Assert.Equal([null, "b2", "b1"], list.Select(c => c.Tag));
    }

    [Fact]
    public void A_default_release_not_in_the_library_yet_is_newest_and_downloads()
    {
        var list = GamePickerLabels.Compose(new PickerDefault("v0.9.51", "v0.9.51", FollowsNewest: true, Download: true),
            [Row("v0.9.47", "OptiScaler-NR v0.9.47")], newest: "v0.9.51");

        Assert.Equal(["Newest · v0.9.51 · download", "OptiScaler-NR v0.9.47"], Labels(list));
    }

    [Fact]
    public void An_empty_library_that_downloads_on_install_reads_newest_download_and_one_that_cannot_says_default()
    {
        Assert.Equal(["Newest · download"], Labels(GamePickerLabels.Compose(new PickerDefault(null, "", FollowsNewest: true, Download: true), [])));
        Assert.Equal(["Default · none imported", "26.1 · newest · download"],
            Labels(GamePickerLabels.Compose(new PickerDefault(null, "none imported", FollowsNewest: true), [Row("catalog:x", "26.1", true)])));
    }

    [Fact]
    public void An_explicit_empty_newest_marks_nothing()
    {
        var list = GamePickerLabels.Compose(new PickerDefault("id1", "6.8.0 · Stable (add-on)", FollowsNewest: false),
            [Row("channel:stable", "Newest stable (add-on)"), Row("id1", "6.8.0 · Stable (add-on)")], newest: string.Empty);

        Assert.Equal(["Default · 6.8.0 · Stable (add-on)", "Newest stable (add-on)"], Labels(list));
    }

    [Theory]
    [InlineData("26.1016.1304", "2026-10-16 13:04")]
    [InlineData("26.0928.0205", "2026-09-28 02:05")]
    [InlineData("26.1340.1304", "26.1340.1304")]
    [InlineData("0.55", "0.55")]
    [InlineData("v12.5.0", "v12.5.0")]
    public void A_shortfuse_build_stamp_reads_as_its_date(string label, string shown) => Assert.Equal(shown, ComponentStore.ReadableStamp(label));

    [Theory]
    [InlineData("v12.5.0", "12.5.0.3000", "v12.5.0")]
    [InlineData("5.0.0", "", "5.0.0")]
    [InlineData("v11.0.0-beta.6", "11.0.0.6", "v11.0.0-beta.6")]
    [InlineData("v10.7.0", "1.2.3.4", "v10.7.0 · 1.2.3.4")]
    public void An_add_on_picker_label_drops_the_import_date_and_a_file_version_that_says_nothing_new(string detected, string file, string shown)
    {
        var build = new AddonBuild(new string('a', 64), "dlss5.addon64", file, null, detected, null, new DateTime(2026, 10, 27));

        Assert.Equal(shown, build.PickerLabel);
    }
}
