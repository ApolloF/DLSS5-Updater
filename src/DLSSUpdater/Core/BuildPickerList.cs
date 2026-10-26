namespace DLSSUpdater.Core;

/// <summary>
/// One candidate for a build picker. <see cref="Version"/> is the version label the order and the one-per-version rule use
/// ("310.9.16", "v11.0.0-beta.6"); <see cref="Fallback"/> stands in when that label carries no version. <see cref="Hash"/> only
/// tells entries with the same label apart.
/// </summary>
public sealed record PickerEntry(string Tag, string Version, string Label, string Hash, System.Version? Fallback = null);

/// <summary>
/// The list a build picker shows after its "Default" entry, for every picker that mixes library builds with catalog downloads
/// (NR runtime, ShortFuse, Lecram): one list, newest version first. A version the library has shows its library builds (the
/// user's own files, all kept) and no download; any other version shows a single download offer.
/// </summary>
public static class BuildPickerList
{
    public const string DownloadSuffix = " · download";

    public static IReadOnlyList<PickerChoice> Merge(IEnumerable<PickerEntry> library, IEnumerable<PickerEntry> offers)
    {
        var owned = library.Select(e => (Entry: e, Key: BuildOrder.Key(e.Version, e.Fallback))).ToList();
        var have = owned.Select(o => o.Key).ToHashSet();
        var offered = new List<(PickerEntry Entry, (Version, bool, int, int) Key)>();
        foreach (var e in offers)
        {
            var key = BuildOrder.Key(e.Version, e.Fallback);
            if (have.Add(key)) offered.Add((e, key));
        }
        var shown = owned.Select(o => (o.Entry, o.Key, Offer: false)).Concat(offered.Select(o => (o.Entry, o.Key, Offer: true)))
            .OrderByDescending(s => s.Key).ToList();
        var taken = shown.GroupBy(s => s.Entry.Label, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return shown.Select(s =>
        {
            var label = taken.Contains(s.Entry.Label) && s.Entry.Hash.Length >= 8 ? $"{s.Entry.Label} · {s.Entry.Hash[..8]}" : s.Entry.Label;
            return new PickerChoice(s.Entry.Tag, s.Offer ? label + DownloadSuffix : label);
        }).ToList();
    }
}

/// <summary>A picker row: the tag the choice stores and the label it shows.</summary>
public sealed record PickerChoice(string Tag, string Label);
