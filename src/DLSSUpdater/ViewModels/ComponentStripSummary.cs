namespace DLSSUpdater.ViewModels;

/// <summary>The one line the collapsed component strip shows instead of the release chips.</summary>
public static class ComponentStripSummary
{
    /// <param name="newReleases">Chips with a release the user hasn't opened yet.</param>
    /// <param name="gamesWithUpdates">Games that Update all would update.</param>
    /// <param name="checkedOnce">The first release check has finished; before that nothing is known to be current.</param>
    /// <param name="online">The last check reached GitHub; offline, the releases come from the cache.</param>
    public static string Text(int newReleases, int gamesWithUpdates, bool checkedOnce, bool online)
    {
        if (!checkedOnce) return "Components: checking…";
        var parts = new List<string>(3);
        if (newReleases > 0) parts.Add(newReleases == 1 ? "1 new release" : $"{newReleases} new releases");
        if (gamesWithUpdates > 0) parts.Add(gamesWithUpdates == 1 ? "1 game needs updates" : $"{gamesWithUpdates} games need updates");
        if (!online) parts.Add("offline");
        return "Components: " + (parts.Count == 0 ? "all current" : string.Join(" · ", parts));
    }
}
