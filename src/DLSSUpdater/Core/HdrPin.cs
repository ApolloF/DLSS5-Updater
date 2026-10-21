namespace DLSSUpdater.Core;

/// <summary>
/// A game's own pick of a RenoDX HDR build (design.md §2 "Per-game pick"): one of the library's builds of the mod the game is picked
/// for. The pick lives in the generic per-component settings; without one the game follows the catalog's current build.
/// </summary>
public static class HdrPin
{
    /// <summary>The library builds a pick may name for this choice: the same mod and architecture, newest first.</summary>
    public static IReadOnlyList<HdrBuild> Candidates(HdrChoice choice, IEnumerable<HdrBuild> library)
    {
        if (!choice.IsOffered) return [];
        var mod = choice.ModId ?? choice.Title;
        return library
            .Where(b => b.ModId.Equals(mod, StringComparison.OrdinalIgnoreCase) && b.Arch.Equals(choice.File!.Arch, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(b => b.Date)
            .ToList();
    }

    /// <summary>
    /// The choice with the pinned build in place of the catalog's. A pick that doesn't fit (another mod, or the file left the
    /// library) is ignored, so the game follows the catalog again instead of failing.
    /// </summary>
    public static HdrChoice Apply(HdrChoice choice, string? pin, IEnumerable<HdrBuild> library)
    {
        if (pin is null || Candidates(choice, library).FirstOrDefault(b => b.Id.Equals(pin, StringComparison.OrdinalIgnoreCase)) is not { } build
            || !File.Exists(ComponentStore.HdrPathOf(build)))
            return choice;
        var file = new CatalogBinary
        {
            Name = build.FileName, Arch = build.Arch, Url = build.Url ?? "", Sha256 = build.Id, Version = build.Version,
            FirstSeen = build.FirstSeen, License = build.License,
        };
        return choice with { File = file, Pinned = build, Reason = choice.Reason + $" You pinned {build.Version}." };
    }
}
