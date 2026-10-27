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
            return new PickerChoice(s.Entry.Tag, label, s.Offer);
        }).ToList();
    }
}

/// <summary>A picker row: the tag the choice stores, the build's name and whether picking it downloads the build first.</summary>
public sealed record PickerChoice(string? Tag, string Name, bool Download = false)
{
    /// <summary>What the row shows: the name, with " · download" for a build the library lacks.</summary>
    public string Label => Download ? Name + BuildPickerList.DownloadSuffix : Name;
}

/// <summary>
/// The build a game's picker follows when the game has no pick of its own: what the default installs now. <see cref="Tag"/> is
/// that build's library id or release tag (null when there is none), <see cref="Name"/> what the row calls it,
/// <see cref="FollowsNewest"/> true when the default isn't pinned ("Newest", design.md section 2), <see cref="Download"/> true when
/// the build isn't in the library yet and comes with the install.
/// </summary>
public sealed record PickerDefault(string? Tag, string Name, bool FollowsNewest, bool Download = false);

/// <summary>
/// The labels of the build pickers in a game's component table, one scheme for every component. The first row follows the default:
/// "Newest · &lt;build&gt;" when the default follows the newest build and that is the newest one listed, else "Default · &lt;build&gt;".
/// Then the other builds newest first, without the one the first row already names (unless the game picked exactly that build).
/// Markers come in one order after the name: "newest" (the newest build listed, when the first row doesn't say so already),
/// "installed" (the build in this game) and "download" (not in the library; picking or installing fetches it).
/// </summary>
public static class GamePickerLabels
{
    public const string Newest = "Newest";
    public const string Default = "Default";

    /// <param name="builds">The builds newest first (library builds and catalog offers, see <see cref="BuildPickerList.Merge"/>).</param>
    /// <param name="installed">The tag of the build installed in this game, if any.</param>
    /// <param name="pin">The game's own pick, if any; its row always stays.</param>
    /// <param name="newest">The newest build's tag when it isn't the first of <paramref name="builds"/> (OptiScaler: the newest release of the chosen source).</param>
    public static IReadOnlyList<PickerChoice> Compose(PickerDefault def, IReadOnlyList<PickerChoice> builds, string? installed = null, string? pin = null, string? newest = null)
    {
        var newestTag = newest ?? builds.FirstOrDefault()?.Tag;
        var defInList = def.Tag is not null && builds.Any(b => Same(b.Tag, def.Tag));
        // A default that follows the newest release and isn't downloaded yet is newer than anything in the library.
        var defIsNewest = def.Tag is not null && (Same(def.Tag, newestTag) || def.FollowsNewest && !defInList && newest is null);
        var head = def.FollowsNewest && (defIsNewest || def.Tag is null && def.Download) ? Newest : Default;
        var list = new List<PickerChoice>
        {
            new(null, Join(head, def.Name, head == Default && defIsNewest ? "newest" : null, def.Tag is not null && Same(def.Tag, installed) ? "installed" : null,
                def.Download ? "download" : null)),
        };
        foreach (var b in builds)
        {
            if (Same(b.Tag, def.Tag) && !Same(b.Tag, pin)) continue;
            var name = Join(b.Name, Same(b.Tag, newestTag) && !(defIsNewest && Same(b.Tag, def.Tag)) ? "newest" : null, Same(b.Tag, installed) ? "installed" : null);
            list.Add(b with { Name = name });
        }
        return list;
    }

    private static bool Same(string? a, string? b) => a is not null && b is not null && a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
}
