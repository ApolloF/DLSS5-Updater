namespace DLSSUpdater.Core;

/// <summary>
/// OptiScaler's compatibility list (the catalog's <c>optiscalerCompat</c> section) looked up by game title. The list has no Steam
/// AppIDs, so entries are found by the normalised title through the same name rules as the RenoDX list (<see cref="GameMatcher.MatchName{T}"/>).
/// </summary>
public sealed class OptiCompatIndex
{
    private readonly List<(string Key, CatalogOptiGame Game)> _games = [];
    private readonly List<(string Key, string Name)> _hudfix = [];

    public OptiCompatIndex(CatalogOptiCompat? catalog)
    {
        foreach (var g in catalog?.Games ?? [])
            if (GameMatcher.Normalize(g.Name) is { Length: > 0 } key) _games.Add((key, g));
        foreach (var n in catalog?.HudfixIncompatible ?? [])
            if (GameMatcher.Normalize(n) is { Length: > 0 } key) _hudfix.Add((key, n));
    }

    public bool IsEmpty => _games.Count == 0;

    /// <summary>The list's entry for the game; the main list wins over the mod tables when a title is in both.</summary>
    public CatalogOptiGame? Find(string gameName) =>
        GameMatcher.MatchName(_games, GameMatcher.Normalize(gameName),
            list => list.OrderBy(g => g.List == "main" ? 0 : g.List == "upscalerMods" ? 1 : 2).ThenBy(g => g.Name, StringComparer.Ordinal).First()).Item;

    /// <summary>True when OptiScaler's wiki says OptiFG's HUDfix cannot work in this game.</summary>
    public bool IsHudfixIncompatible(string gameName) =>
        GameMatcher.MatchName(_hudfix, GameMatcher.Normalize(gameName), list => list[0]).Item is not null;
}
