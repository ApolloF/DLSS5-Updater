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
                    var version = meta.Label ?? meta.Tag.TrimStart('v', 'V');
                    found.Add(new LibraryBuild(LibraryKind.Feeder, meta.Tag, version + (meta.Imported ? " (imported)" : PreTag().IsMatch(meta.Tag) ? " beta" : ""),
                        version, meta.Published ?? meta.Fetched, dir, Downloaded: !meta.Imported));
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

    /// <summary>"Auto (1.19.0)" for the pickers: what the default installs now. The newest release is fetched on the first install when the library is empty.</summary>
    public static string AutoLabel(string? defaultPick) => $"Auto ({Choose(defaultPick)?.Label ?? "the newest release, downloaded on install"})";

    // ---------- import ----------

    private const string ReadMe = "READ-ME-FIRST.txt";

    /// <summary>
    /// Adds a build the user got themselves: a release .zip, the extracted release folder (or a folder holding it), or
    /// <c>dlss5-feed.addon64</c> in it. The four package files are copied byte for byte; the build's id is the SHA-256 of the 64-bit
    /// add-on, so importing the same add-on again replaces that build's set. Imports are never removed by the automatic cleanup and
    /// are not checked against GitHub's digest: the user chose the files.
    /// </summary>
    public static LibraryBuild Import(string source)
    {
        var staging = Root + ".new";
        if (Directory.Exists(staging)) FileUtil.DeleteDirectory(staging);
        try
        {
            var pkg = Path.Combine(staging, PackageDir);
            var readme = source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ExtractZip(source, pkg) : CopyFolder(source, pkg);
            var id = FileUtil.Sha256(Path.Combine(pkg, Addon64));
            var version = VersionOf(readme) ?? "imported " + id[..8];
            var meta = new CachedComponent { Tag = id, Fetched = DateTime.UtcNow, Imported = true, Label = version };
            FileUtil.AtomicWriteText(Path.Combine(staging, CompleteMarker), JsonSerializer.Serialize(meta, JsonCtx.Default.CachedComponent));
            var dir = DirOf(id);
            if (Directory.Exists(dir)) FileUtil.DeleteDirectory(dir);
            Directory.CreateDirectory(Root);
            Directory.Move(staging, dir);
            Log.Info($"Imported DLSS5-Feeder {version}");
            return Builds().First(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(staging)) FileUtil.DeleteDirectory(staging);
        }
    }

    /// <summary>The package files out of a zip whose files are at its top or one folder down; returns line 1 of its READ-ME-FIRST.txt.</summary>
    private static string? ExtractZip(string zip, string pkg)
    {
        using var archive = ZipFile.OpenRead(zip);
        string Norm(ZipArchiveEntry e) => e.FullName.Replace('\\', '/');
        var prefix = archive.Entries.Select(Norm)
            .Where(n => n.EndsWith(Addon64, StringComparison.OrdinalIgnoreCase) && n.Count(c => c == '/') <= 1 && (n.Length == Addon64.Length || n[^(Addon64.Length + 1)] == '/'))
            .Select(n => n[..^Addon64.Length]).OrderBy(n => n.Length).FirstOrDefault()
            ?? throw new InvalidDataException($"{Addon64} isn't in {Path.GetFileName(zip)}. Choose the DLSS5-Feeder release zip.");
        foreach (var (inZip, rel) in PackageFiles)
        {
            var entry = archive.Entries.FirstOrDefault(e => Norm(e).Equals(prefix + inZip, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"DLSS5-Feeder: {inZip} is missing from {Path.GetFileName(zip)}.");
            var dest = Path.Combine(pkg, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);
        }
        var note = archive.Entries.FirstOrDefault(e => Norm(e).Equals(prefix + ReadMe, StringComparison.OrdinalIgnoreCase));
        if (note is null) return null;
        using var reader = new StreamReader(note.Open());
        return reader.ReadLine();
    }

    private static string? CopyFolder(string source, string pkg)
    {
        var folder = ReleaseFolder(source) ?? throw new InvalidDataException(
            $"{Addon64} not found in {source}. Choose the extracted DLSS5-Feeder release folder, {Addon64} or the release zip.");
        foreach (var (inZip, rel) in PackageFiles)
        {
            var from = Path.Combine(folder, inZip.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(from)) throw new InvalidDataException($"DLSS5-Feeder: {inZip} is missing from {folder}.");
            FileUtil.AtomicCopy(from, Path.Combine(pkg, rel));
        }
        var note = Path.Combine(folder, ReadMe);
        return File.Exists(note) ? File.ReadLines(note).FirstOrDefault() : null;
    }

    /// <summary>"DLSS5-Feeder 1.19.0" (line 1 of the release's READ-ME-FIRST.txt) -> "1.19.0".</summary>
    internal static string? VersionOf(string? readmeLine) =>
        readmeLine is not null && Regex.Match(readmeLine, @"^\s*DLSS5-Feeder\s+(\S+)", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>The folder holding <c>dlss5-feed.addon64</c>: the one chosen, the folder of the file chosen, or a single subfolder of it.</summary>
    private static string? ReleaseFolder(string source)
    {
        if (File.Exists(source)) source = Path.GetDirectoryName(Path.GetFullPath(source))!;
        if (!Directory.Exists(source)) return null;
        if (File.Exists(Path.Combine(source, Addon64))) return source;
        var inner = Directory.EnumerateDirectories(source).Where(d => File.Exists(Path.Combine(d, Addon64))).Take(2).ToList();
        return inner.Count == 1 ? inner[0] : null;
    }

    /// <summary>
    /// Takes a build out of the library. A default or game pick on it goes back to Auto, never to another build
    /// (docs/design.md section 4); the caller saves the settings.
    /// </summary>
    public static void Remove(LibraryBuild build, AppSettings settings)
    {
        if (FileUtil.IsUnder(Path.GetFullPath(build.Dir), Root)) FileUtil.DeleteDirectory(build.Dir);
        if (settings.DefaultBuild(ComponentRegistry.Feeder)?.Equals(build.Id, StringComparison.OrdinalIgnoreCase) == true)
            settings.SetDefaultBuild(ComponentRegistry.Feeder, null);
        foreach (var game in settings.Games.Values)
            if (game.BuildPick(ComponentRegistry.Feeder)?.Equals(build.Id, StringComparison.OrdinalIgnoreCase) == true)
                game.SetBuildPick(ComponentRegistry.Feeder, null);
    }

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
        var is32 = game.Effective.Is32Bit;
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
