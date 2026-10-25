namespace DLSSUpdater.ViewModels;

/// <summary>The one line the collapsed component strip shows instead of the release chips.</summary>
public static class ComponentStripSummary
{
    /// <param name="newReleases">Chips with a release the user hasn't opened yet.</param>
    /// <param name="gamesWithUpdates">Games that Update all would update.</param>
    public static string Text(int newReleases, int gamesWithUpdates)
    {
        var parts = new List<string>(2);
        if (newReleases > 0) parts.Add(newReleases == 1 ? "1 new release" : $"{newReleases} new releases");
        if (gamesWithUpdates > 0) parts.Add(gamesWithUpdates == 1 ? "1 game needs updates" : $"{gamesWithUpdates} games need updates");
        return "Components: " + (parts.Count == 0 ? "all current" : string.Join(" · ", parts));
    }
}
