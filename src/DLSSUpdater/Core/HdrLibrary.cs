using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

/// <summary>
/// One RenoDX HDR build in the library, components\renodx-hdr\&lt;sha256&gt;\&lt;FileName&gt; with this record next to it as
/// version.json. <see cref="Version"/> is the catalog's name for the build (first-seen date plus short hash).
/// </summary>
public sealed record HdrBuild(
    string Id,
    string FileName,
    string Arch,
    string ModId,
    string ModName,
    string Version,
    string? FirstSeen,
    string? Url,
    string? License,
    DateTime Added)
{
    [JsonIgnore]
    public string Label => $"{ModName} {Version}";

    /// <summary>The catalog's first-seen time, else when the app downloaded it.</summary>
    [JsonIgnore]
    public DateTime Date => DateTime.TryParse(FirstSeen, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : Added;
}

public sealed partial class ComponentStore
{
    private const string HdrDir = "renodx-hdr";
    private static string HdrRoot => Path.Combine(AppPaths.Components, HdrDir);
    private (string Root, IReadOnlyList<HdrBuild> Builds)? _hdrCache;

    /// <summary>Every readable build, newest first (the catalog's first-seen date, then the id).</summary>
    public IReadOnlyList<HdrBuild> HdrBuilds
    {
        get
        {
            var root = HdrRoot;
            lock (_addonCache)
            {
                if (_hdrCache is { } c && c.Root == root) return c.Builds;
                var builds = ReadHdrBuilds(root);
                _hdrCache = (root, builds);
                return builds;
            }
        }
    }

    private void ForgetHdr()
    {
        lock (_addonCache) _hdrCache = null;
    }

    private static IReadOnlyList<HdrBuild> ReadHdrBuilds(string root)
    {
        if (!Directory.Exists(root)) return [];
        var builds = new List<HdrBuild>();
        foreach (var file in Directory.EnumerateFiles(root, BuildRecordFile, SearchOption.AllDirectories))
        {
            try
            {
                var b = JsonSerializer.Deserialize(File.ReadAllText(file), JsonCtx.Default.HdrBuild);
                if (b is not null && ValidId(b.Id) && Path.GetFileName(Path.GetDirectoryName(file)) == b.Id
                    && ValidHdrFile(b.FileName) && File.Exists(Path.Combine(root, b.Id, b.FileName))) builds.Add(b);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            { Log.Error($"Could not read RenoDX HDR library entry {file}", ex); }
        }
        return builds.OrderByDescending(b => b.Date).ThenBy(b => b.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A plain file name with an add-on extension: the catalog is trusted for the bytes (it gives the hash), not for a path.</summary>
    internal static bool ValidHdrFile(string? name) =>
        !string.IsNullOrEmpty(name) && Path.GetFileName(name) == name && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && (name.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase));

    public HdrBuild? HdrBuildFor(string? id) => id is null ? null : HdrBuilds.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string HdrPathOf(HdrBuild build) => Path.Combine(HdrRoot, build.Id, build.FileName);

    /// <summary>A build the game is pinned to: it is already in the library, so nothing is downloaded, but the file is checked against its hash before it is placed.</summary>
    public HdrBuild VerifiedHdrBuild(HdrBuild build)
    {
        var path = HdrPathOf(build);
        if (!File.Exists(path)) throw new FileNotFoundException($"The pinned build {build.Label} is no longer in the library. Pick another build or the default.", path);
        if (!FileUtil.Sha256(path).Equals(build.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The stored file of {build.Label} changed. Remove the build in Settings and let the app download it again.");
        return build;
    }

    private IReadOnlyList<LibraryBuild> HdrLibraryBuilds() => HdrBuilds
        .Select(b => new LibraryBuild(LibraryKind.RenoDxHdr, b.Id, b.Label, b.Version, b.Date, Path.Combine(HdrRoot, b.Id), Group: b.ModId, Downloaded: true))
        .ToList();

    /// <summary>
    /// The library's copy of a catalog build, downloaded (mirror first, then the author) and checked against the catalog's SHA-256
    /// and size when it isn't there yet. A refused download throws with the one-line reason.
    /// </summary>
    public async Task<HdrBuild> EnsureHdrBuildAsync(CatalogBinary file, string modId, string modName, CatalogDownloader downloader,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!file.IsInstallable) throw new InvalidOperationException($"{file.Name}: the catalog has no SHA-256 for it, so it isn't installed.");
        var sha = file.Sha256!.ToLowerInvariant();
        if (!ValidId(sha) || !ValidHdrFile(file.Name)) throw new InvalidDataException($"The catalog entry {file.Name} isn't a RenoDX add-on file.");

        var dir = Path.Combine(HdrRoot, sha);
        var dest = Path.Combine(dir, file.Name);
        if (HdrBuildFor(sha) is { } have && File.Exists(dest) && FileUtil.Sha256(dest).Equals(sha, StringComparison.OrdinalIgnoreCase)) return have;

        Directory.CreateDirectory(dir);
        var result = await downloader.DownloadAsync(file, dest, progress, ct);
        if (!result.Ok)
        {
            FileUtil.TryDeleteEmptyDir(dir);
            throw new InvalidOperationException(result.Refusal ?? $"{file.Name} could not be downloaded.");
        }

        var build = new HdrBuild(sha, file.Name, file.Arch, modId, modName, file.Version ?? sha[..12], file.FirstSeen, file.Url, file.License, DateTime.UtcNow);
        FileUtil.AtomicWriteText(Path.Combine(dir, BuildRecordFile), JsonSerializer.Serialize(build, JsonCtx.Default.HdrBuild));
        ForgetHdr();
        NoteAdded(sha);
        Log.Info($"RenoDX HDR: downloaded {build.Label} ({file.Name}){(result.FromMirror ? " from the catalog mirror" : "")}");
        return build;
    }

    public void RemoveHdr(string id)
    {
        if (!ValidId(id)) throw new InvalidDataException("Invalid RenoDX HDR build id.");
        var dir = Path.Combine(HdrRoot, id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ForgetHdr();
    }
}
