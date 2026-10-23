namespace DLSSUpdater.Core;

public sealed partial class ComponentStore
{
    /// <summary>
    /// The DLSS5-Feeder build to install, downloaded and verified when it isn't in the library yet. A pick (a library id) must be
    /// there already; with none, the newest release is fetched, and offline the newest build the library has is used.
    /// </summary>
    public async Task<LibraryBuild> EnsureFeederAsync(string? pick, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var library = new FeederLibrary(_gh);
        if (pick is not null)
            return FeederLibrary.Builds().FirstOrDefault(b => b.Id.Equals(pick, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"DLSS5-Feeder {pick} is no longer in the library; pick another build or follow the newest.");
        var latest = await library.LatestAsync(_includePrereleases(), ct);
        if (latest is not null) return await library.EnsureAsync(latest, progress, ct);
        return FeederLibrary.Choose(null)
            ?? throw new InvalidOperationException("DLSS5-Feeder isn't downloaded yet and GitHub can't be reached (or its release has no verifiable SHA-256).");
    }
}
