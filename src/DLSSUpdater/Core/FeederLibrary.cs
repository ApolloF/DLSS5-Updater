using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>A DLSS5-Feeder release the app can download: the zip and the SHA-256 GitHub published for it.</summary>
public sealed record FeederRelease(string Tag, DateTime? Published, bool Beta, string AssetName, string Url, string Sha256, string? Notes, bool Stale);

/// <summary>
/// DLSS5-Feeder (MIT, Jean-Laurent Rouzies, <see cref="Repo"/>): a ReShade add-on that makes the DLSS calls a game without DLSS never
/// makes, so a neural consumer (Deep Fried Chicken here) has something to act on. Downloaded from that one repository's releases and
/// verified against the digest GitHub publishes for the asset (no digest = no install); copycat repositories exist, so the owner and
/// name are pinned exactly. Every release is a build of <see cref="LibraryKind.Feeder"/>: components\dlss5-feeder\&lt;tag&gt;\pkg\
/// holds the files the app places, and the folder's .complete record is the build's metadata. docs/addons/dlss5-feeder.md has the layout.
/// </summary>
public sealed partial class FeederLibrary(GitHubClient github)
{
    public const string Repo = "jlrouzies-fr/DLSS5-Feeder";

    public const string Addon64 = "dlss5-feed.addon64";
    public const string Addon32 = "dlss5-feed.addon32";
    public const string Host64Dir = "host64";
    public const string Host64Exe = "dlss5-feed-host64.exe";
    public const string ShaderDir = @"reshade-shaders\Shaders";
    public const string Shader = "DLSS5_Feed.fx";
    private const string CompleteMarker = ".complete";
    private const string PackageDir = "pkg";

    /// <summary>The files of the zip the app keeps and places (zip path -> path in the build's pkg folder). The rest of the zip (Vulkan layers, helper mode, the PowerShell verifier) is not used.</summary>
    internal static readonly (string Zip, string Pkg)[] PackageFiles =
    [
        (Addon64, Addon64),
        (Addon32, Addon32),
        ($"{Host64Dir}/{Host64Exe}", Path.Combine(Host64Dir, Host64Exe)),
        ($"reshade-shaders/Shaders/{Shader}", Path.Combine(ShaderDir, Shader)),
    ];

    /// <summary>What the registry says the component owns, relative to the install folder.</summary>
    public static readonly string[] OwnedFiles = [Addon64, Addon32, Path.Combine(Host64Dir, Host64Exe), Path.Combine(ShaderDir, Shader)];

    public static string Root => Path.Combine(AppPaths.Components, ComponentRegistry.Feeder);
    private static string DirOf(string tag) => Path.Combine(Root, SafeName(tag));
    private static string SafeName(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    [GeneratedRegex(@"-(alpha|beta|rc|pre)", RegexOptions.IgnoreCase)]
    private static partial Regex PreTag();

    /// <summary>
    /// The newest release with the zip <c>DLSS5-Feeder-&lt;version&gt;.zip</c> and a published SHA-256. The author tags betas
    /// (<c>v1.18.0-beta.2</c>) without GitHub's prerelease flag, so a beta is told by its tag; stable ones win unless
    /// <paramref name="includeBetas"/>. A release without a digest is skipped: it could not be verified.
    /// </summary>
    public static FeederRelease? Pick(IEnumerable<GhRelease> releases, bool includeBetas)
    {
        var found = new List<FeederRelease>();
        foreach (var r in releases.Where(r => !r.Draft && !r.Prerelease))
        {
            var version = r.TagName.TrimStart('v', 'V');
            var asset = r.Assets.FirstOrDefault(a => a.Name.Equals($"DLSS5-Feeder-{version}.zip", StringComparison.OrdinalIgnoreCase));
            if (asset?.Sha256 is not { Length: 64 } sha) continue;
            var beta = PreTag().IsMatch(r.TagName);
            if (beta && !includeBetas) continue;
            found.Add(new FeederRelease(r.TagName, r.PublishedAt, beta, asset.Name, asset.Url, sha, r.Body, r.Stale));
        }
        return BuildOrder.NewestFirst(found, f => f.Tag, f => f.Published, f => f.Tag).FirstOrDefault();
    }

    /// <summary>The newest downloadable release; null when GitHub has none usable or can't be reached (the library keeps what it has).</summary>
    public async Task<FeederRelease?> LatestAsync(bool includeBetas, CancellationToken ct)
    {
        try { return Pick(await github.GetReleasesAsync(Repo, 15, ct), includeBetas); }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            Log.Info($"DLSS5-Feeder: GitHub unreachable ({ex.Message})");
            return Pick(github.CachedReleases(Repo), includeBetas);
        }
    }

    /// <summary>The builds in the library, newest first (see <see cref="BuildOrder"/>). Only metadata is read.</summary>
    public static IReadOnlyList<LibraryBuild> Builds()
    {
        var found = new List<LibraryBuild>();
        if (Directory.Exists(Root))
            foreach (var dir in Directory.EnumerateDirectories(Root))
            {
                var marker = Path.Combine(dir, CompleteMarker);
                if (dir.EndsWith(".dlssu-delete", StringComparison.OrdinalIgnoreCase) || !File.Exists(marker)) continue;
                try
                {
                    if (JsonSerializer.Deserialize(File.ReadAllText(marker), JsonCtx.Default.CachedComponent) is not { } meta || string.IsNullOrWhiteSpace(meta.Tag)
                        || !Path.GetFileName(dir).Equals(SafeName(meta.Tag), StringComparison.OrdinalIgnoreCase)) continue;
                    found.Add(new LibraryBuild(LibraryKind.Feeder, meta.Tag, meta.Tag.TrimStart('v', 'V') + (PreTag().IsMatch(meta.Tag) ? " beta" : ""),
                        meta.Tag, meta.Published ?? meta.Fetched, dir, Downloaded: true));
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                { Log.Error($"Could not read DLSS5-Feeder record {marker}", ex); }
            }
        return BuildOrder.NewestFirst(found, b => b.Version, b => b.Date, b => b.Id);
    }

    /// <summary>The build to install: the game's pick, else the default pick, else the newest in the library.</summary>
    public static LibraryBuild? Choose(string? pick)
    {
        var builds = Builds();
        return (pick is null ? null : builds.FirstOrDefault(b => b.Id.Equals(pick, StringComparison.OrdinalIgnoreCase))) ?? builds.FirstOrDefault();
    }

    public static string PackageOf(LibraryBuild build) => Path.Combine(build.Dir, PackageDir);

    /// <summary>
    /// Downloads <paramref name="release"/> into the library unless it is there already: the zip is hashed against GitHub's digest
    /// before anything is read from it, and only the files in <see cref="PackageFiles"/> are taken, each by its exact name.
    /// </summary>
    public async Task<LibraryBuild> EnsureAsync(FeederRelease release, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (Builds().FirstOrDefault(b => b.Id.Equals(release.Tag, StringComparison.OrdinalIgnoreCase)) is { } have) return have;
        var dir = DirOf(release.Tag);
        if (Directory.Exists(dir)) FileUtil.DeleteDirectory(dir);
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, release.AssetName);
        try
        {
            await github.DownloadAsync(release.Url, zip, $"DLSS5-Feeder {release.Tag}", progress, ct);
            progress?.Report(new TransferProgress("Verifying DLSS5-Feeder", null));
            await Task.Run(() =>
            {
                if (!FileUtil.Sha256(zip).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{release.AssetName}: refused, its SHA-256 doesn't match the digest GitHub published for it.");
                Extract(zip, Path.Combine(dir, PackageDir));
            }, ct);
            var meta = new CachedComponent { Tag = release.Tag, Prerelease = release.Beta, Published = release.Published, Fetched = DateTime.UtcNow };
            FileUtil.AtomicWriteText(Path.Combine(dir, CompleteMarker), JsonSerializer.Serialize(meta, JsonCtx.Default.CachedComponent));
            Log.Info($"Cached DLSS5-Feeder {release.Tag}");
        }
        catch
        {
            FileUtil.DeleteDirectory(dir);
            throw;
        }
        finally { FileUtil.TryDeleteFile(zip); }
        return Builds().First(b => b.Id.Equals(release.Tag, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Takes the package files out of the zip by exact name; a zip missing one is refused whole, so a build is never half there.</summary>
    internal static void Extract(string zip, string pkg)
    {
        using var archive = ZipFile.OpenRead(zip);
        foreach (var (inZip, rel) in PackageFiles)
        {
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(inZip, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"DLSS5-Feeder: {inZip} is missing from the release, so it isn't what this app expects. Update DLSS5 Updater.");
            var dest = Path.Combine(pkg, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);
        }
    }

    /// <summary>
    /// What Feeder puts in a game, following its README: a 64-bit game gets <c>dlss5-feed.addon64</c> beside the exe and the shader in
    /// <c>reshade-shaders\Shaders</c>; a 32-bit game gets <c>dlss5-feed.addon32</c>, the shader, and the 64-bit helper in <c>host64\</c>
    /// (NGX is 64-bit only). The helper's own ReShade and consumer files are not placed by the app (Extras says so).
    /// </summary>
    public static ComponentPlan Plan(LibraryBuild build, GameInfo game, string targetDir)
    {
        var pkg = PackageOf(build);
        var is32 = game.Tech.Is32Bit;
        var files = new List<(string Source, string Dest)>
        {
            (Path.Combine(pkg, is32 ? Addon32 : Addon64), Path.Combine(targetDir, is32 ? Addon32 : Addon64)),
            (Path.Combine(pkg, ShaderDir, Shader), Path.Combine(targetDir, ShaderDir, Shader)),
        };
        if (is32) files.Add((Path.Combine(pkg, Host64Dir, Host64Exe), Path.Combine(targetDir, Host64Dir, Host64Exe)));
        var extras = new Dictionary<string, string> { ["bits"] = is32 ? "32" : "64" };
        if (is32) extras["manual"] = "host64\\ needs a 64-bit ReShade and the consumer's files (Deep Fried Chicken's 64-bit set) placed by hand";
        return new ComponentPlan(ComponentRegistry.Feeder) { BuildId = build.Id, Version = build.Label, Files = files, Extras = extras };
    }
}
