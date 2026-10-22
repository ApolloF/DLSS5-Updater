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
}
