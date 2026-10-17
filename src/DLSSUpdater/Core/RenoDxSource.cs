namespace DLSSUpdater.Core;

public sealed partial class ComponentStore
{
    internal static ReleaseInfo? ParseRenoDxRelease(IEnumerable<GhRelease> releases) => releases
        .Where(r => !r.Draft && (r.TagName == "snapshot" || r.TagName.StartsWith("nightly-", StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(r => r.PublishedAt)
        .Select(r =>
        {
            var asset = r.Assets.FirstOrDefault(a => a.Name.Equals(RenoDxFile, StringComparison.OrdinalIgnoreCase)
                || a.Name.Equals("renodx-dlss.addon64", StringComparison.OrdinalIgnoreCase));
            // Snapshot tags are mutable: include the asset digest in the cache key.
            return asset?.Sha256 is not { Length: 64 } sha ? null : new ReleaseInfo
            {
                Tag = $"{r.TagName}-{sha}", Published = r.PublishedAt, Prerelease = r.Prerelease, Notes = r.Body,
                Files = [(RenoDxFile, asset.Url, sha)],
            };
        }).FirstOrDefault(r => r is not null);

    private async Task<ReleaseInfo?> ResolveRenoDxAsync(CancellationToken ct) =>
        ParseRenoDxRelease(await _gh.GetReleasesAsync(RenoDxRepo, 40, ct));

    public async Task<string> EnsureRenoDxAsync(IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var r = RenoDx ?? throw new InvalidOperationException("RenoDX release unknown (offline and nothing cached).");
        var path = Path.Combine(TagDir(Component.RenoDx, r.Tag), RenoDxFile);
        await _locks[Component.RenoDx].WaitAsync(ct);
        try
        {
            if (IsCached(Component.RenoDx, r.Tag) && File.Exists(path)) return path;
            if (r.FromCache) throw new InvalidDataException("Cached RenoDX is incomplete.");
            var (_, url, sha) = r.Files[0];
            await _gh.DownloadAsync(url, path, "RenoDX DLSS add-on", progress, ct);
            await VerifyAsync(path, sha, ct);
            MarkComplete(Component.RenoDx, r);
            return path;
        }
        finally { _locks[Component.RenoDx].Release(); }
    }
}
