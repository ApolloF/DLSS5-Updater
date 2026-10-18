using System.IO.Compression;

namespace DLSSUpdater.Core;

public sealed partial class ComponentStore
{
    internal static ReleaseInfo? UpliftRelease(IEnumerable<GhRelease> releases)
    {
        foreach (var r in releases.Where(r => !r.Draft).OrderByDescending(r => r.PublishedAt))
        {
            var name = $"GITC-Uplift-{r.TagName.TrimStart('v', 'V')}.zip";
            var zip = r.Assets.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (zip is null) continue;
            var files = new List<(string, string, string?)> { (zip.Name, zip.Url, zip.Sha256) };
            if (r.Assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) is { } sums)
                files.Add((sums.Name, sums.Url, null));
            return new ReleaseInfo { Tag = r.TagName, Published = r.PublishedAt, Prerelease = r.Prerelease, Notes = r.Body, Files = files };
        }
        return null;
    }

    private async Task<ReleaseInfo?> ResolveUpliftAsync(CancellationToken ct) =>
        UpliftRelease(await _gh.GetReleasesAsync(UpliftRepo, 10, ct));

    internal static void VerifyUplift(string zip, string assetName, string? sums)
    {
        if (sums is null) return;
        var line = sums.Split('\n').Select(l => l.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length == 2 && parts[1].Equals(assetName, StringComparison.Ordinal));
        if (line is null || line[0].Length != 64 || !line[0].All(Uri.IsHexDigit)
            || !FileUtil.Sha256(zip).Equals(line[0], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{assetName}: SHA256SUMS verification failed.");
    }

    internal static void ExtractUplift(string zip, string pkg)
    {
        using var archive = ZipFile.OpenRead(zip);
        foreach (var rel in UpliftFiles)
        {
            var source = rel.StartsWith(DfcShaderDir, StringComparison.OrdinalIgnoreCase)
                ? "Shaders/" + Path.GetFileName(rel) : "Addons/" + rel;
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(source, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"GITC Uplift: {source} missing from the package.");
            var dest = Path.Combine(pkg, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);
        }
    }

    public async Task<string> EnsureUpliftAsync(string? tag, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var r = CachedOrNewest(Component.Uplift, tag, Uplift, "GITC Uplift")
            ?? throw new InvalidOperationException("GITC Uplift release unknown (offline and nothing cached).");
        var dir = TagDir(Component.Uplift, r.Tag);
        var pkg = Path.Combine(dir, "pkg");
        await _locks[Component.Uplift].WaitAsync(ct);
        try
        {
            if (IsCached(Component.Uplift, r.Tag) && UpliftFiles.All(f => File.Exists(Path.Combine(pkg, f)))) return pkg;
            var marker = Path.Combine(dir, CompleteMarker);
            if (File.Exists(marker)) File.Delete(marker);
            if (r.FromCache) throw new InvalidOperationException("Cached GITC Uplift files are incomplete.");
            var (name, url, _) = r.Files[0];
            var zip = Path.Combine(dir, name);
            await _gh.DownloadAsync(url, zip, $"GITC Uplift {r.Tag}", progress, ct);
            try
            {
                var sums = r.Files.FirstOrDefault(f => f.Name == "SHA256SUMS.txt");
                var text = sums.Url is null ? null : await _gh.GetStringAsync(sums.Url, ct);
                progress?.Report(new TransferProgress("Verifying GITC Uplift", null));
                await Task.Run(() =>
                {
                    VerifyUplift(zip, name, text);
                    progress?.Report(new TransferProgress("Extracting GITC Uplift", null));
                    ExtractUplift(zip, pkg);
                }, ct);
                MarkComplete(Component.Uplift, r);
                Log.Info($"Cached GITC Uplift {r.Tag}");
                return pkg;
            }
            finally { File.Delete(zip); }
        }
        finally { _locks[Component.Uplift].Release(); }
    }
}
