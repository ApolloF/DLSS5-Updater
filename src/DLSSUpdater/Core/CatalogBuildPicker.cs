namespace DLSSUpdater.Core;

/// <summary>The components whose older builds the catalog mirror holds and the build pickers can fetch on demand.</summary>
public enum CatalogBuildKind { DlssNr, Lecram, RenoDx }

/// <summary>
/// A catalog build the library doesn't have, offered in a build picker as "&lt;version&gt; · download". Its choice tag is
/// <see cref="Tag"/>: the catalog's SHA-256 behind a prefix no library id or release tag can carry, so a picker tells it from a library build.
/// For ShortFuse's add-on that hash is the zip's (the library id is the hash of the add-on inside).
/// </summary>
public sealed record CatalogOffer(CatalogBuildKind Kind, string Sha256, string Label, string Version = "")
{
    public const string TagPrefix = "catalog:";

    public string Tag => TagPrefix + Sha256;

    /// <summary>The picker entry: the plain version, since the picker offers one build per version.</summary>
    public PickerEntry ToEntry()
    {
        var version = Version.Length > 0 ? Version : Label;
        return new PickerEntry(Tag, version, version, Sha256);
    }

    public static bool IsOffer(string? tag) => tag is not null && tag.StartsWith(TagPrefix, StringComparison.Ordinal);

    /// <summary>The catalog hash an offer tag carries, or null for any other tag.</summary>
    public static string? ShaOf(string? tag) => IsOffer(tag) ? tag![TagPrefix.Length..] : null;
}

/// <summary>
/// Older builds from the catalog mirror, on demand. The auto-check only fetches the newest build; here the user picks any other
/// one in the build picker. It goes through the same checks (mirror, size, SHA-256, PE) and into the same library as the
/// auto-check, but it leaves the "newest fetched" markers and the default alone: choosing the build is the caller's business.
/// </summary>
public sealed partial class ComponentStore
{
    /// <summary>
    /// The catalog builds of <paramref name="kind"/> the library doesn't have, newest first, with the label the picker shows.
    /// Entries without a hash, a plain file name or a download URL are skipped. Lecram has none while <paramref name="lecramOff"/> is set
    /// (<see cref="BuildFlags.LecramCatalogOff"/>: the private channel, or public builds that import Lecram); the auto-check skips the mirror then too.
    /// </summary>
    public IReadOnlyList<CatalogOffer> OfferedBuilds(CatalogBuildKind kind, CatalogSections? sections, bool lecramOff)
    {
        switch (kind)
        {
            case CatalogBuildKind.DlssNr:
                return NeuralOffers(kind, sections?.Neural?.Dlssnr, DlssNrVersions.Select(v => v.Id));
            case CatalogBuildKind.Lecram:
                return lecramOff ? [] : NeuralOffers(kind, sections?.Neural?.Lecram, AddonBuilds(AddonKind.Lecram).Select(b => b.Id));
            default:
                var offers = new List<CatalogOffer>();
                if (sections?.RenodxDlss is not { } section) return offers;
                foreach (var b in section.Builds)
                {
                    if (!b.File.IsInstallable) continue;
                    var sha = b.File.Sha256!.ToLowerInvariant();
                    if (HasRenoDxCatalogBuild(sha) || offers.Any(o => o.Sha256 == sha)) continue;
                    offers.Add(new CatalogOffer(kind, sha, RenoDxLabel(section, b), string.IsNullOrWhiteSpace(b.Version) ? b.Tag : b.Version));
                }
                return offers;
        }
    }

    private static List<CatalogOffer> NeuralOffers(CatalogBuildKind kind, CatalogNeuralLine? line, IEnumerable<string> owned)
    {
        var offers = new List<CatalogOffer>();
        if (line is null) return offers;
        var have = new HashSet<string>(owned, StringComparer.OrdinalIgnoreCase);
        foreach (var b in line.All)
        {
            if (!b.ToBinary().IsInstallable) continue;
            var sha = b.Sha256.ToLowerInvariant();
            if (!have.Add(sha)) continue;
            offers.Add(new CatalogOffer(kind, sha, line.LabelOf(b), b.Version));
        }
        return offers;
    }

    /// <summary>The release's version, plus the short hash when another release of the list carries the same version.</summary>
    private static string RenoDxLabel(CatalogRenodxDlss section, CatalogDlssBuild build)
    {
        var label = string.IsNullOrWhiteSpace(build.Version) ? build.Tag : build.Version;
        var shared = section.Builds.Count(b => string.Equals(string.IsNullOrWhiteSpace(b.Version) ? b.Tag : b.Version, label, StringComparison.OrdinalIgnoreCase)) > 1;
        return shared && build.File.Sha256 is { Length: >= 8 } sha ? $"{label} · {sha[..8]}" : label;
    }

    /// <summary>
    /// Downloads the offered build <paramref name="sha256"/> into its library and returns the library id to pick (not the catalog
    /// hash: ShortFuse's add-on is a zip). Null, with a log line, when the catalog doesn't offer it any more, the download is
    /// refused (size, hash, not a Windows binary or not the right add-on) or the network is gone. Never throws for those; only a cancel does.
    /// </summary>
    public async Task<string?> DownloadCatalogBuildAsync(CatalogBuildKind kind, CatalogSections? sections, string sha256, ICatalogFetch fetch,
        IProgress<TransferProgress>? progress, bool lecramOff, CancellationToken ct)
    {
        if (!OfferedBuilds(kind, sections, lecramOff).Any(o => o.Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase)))
        {
            Log.Info("That build is no longer on offer from the catalog.");
            return null;
        }
        switch (kind)
        {
            case CatalogBuildKind.DlssNr:
                var nrLine = sections!.Neural!.Dlssnr!;
                return (await FetchDlssNrAsync(nrLine, Find(nrLine, sha256), fetch, progress, ct))?.Id;
            case CatalogBuildKind.Lecram:
                var lecramLine = sections!.Neural!.Lecram!;
                return (await FetchLecramAsync(lecramLine, Find(lecramLine, sha256), fetch, progress, ct))?.Id;
            default:
                var entry = sections!.RenodxDlss!.Builds.First(b => b.File.IsInstallable && b.File.Sha256!.Equals(sha256, StringComparison.OrdinalIgnoreCase));
                return (await FetchRenoDxAsync(entry, fetch, progress, ct))?.Id;
        }
    }

    private static CatalogNeuralBuild Find(CatalogNeuralLine line, string sha256) =>
        line.All.First(b => b.Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase) && b.ToBinary().IsInstallable);
}
