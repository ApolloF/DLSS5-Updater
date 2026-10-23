using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

/// <summary>
/// One Luma zip in the library, components\luma\&lt;sha256&gt;\&lt;FileName&gt; with this record next to it as version.json.
/// <see cref="Version"/> is the upstream release tag the catalog pins ("latest-755"); an imported zip has "imported &lt;hash&gt;".
/// </summary>
public sealed record LumaBuild(
    string Id,
    string PackageId,
    string FileName,
    string Arch,
    string Version,
    string? FirstSeen,
    string? Url,
    bool Imported,
    DateTime Added)
{
    [JsonIgnore]
    public string Label => Imported ? $"{FileName} ({Version})" : $"{PackageId.Replace('_', ' ')} {Version}";

    [JsonIgnore]
    public DateTime Date => DateTime.TryParse(FirstSeen, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : Added;
}

/// <summary>
/// Which files of a Luma zip go where. The zips are laid out like the game's exe folder: a ReShade add-on, the Luma\ shader folder and
/// ReShade's own proxy dll, ini and DLSS dll, which the app manages itself and so never copies.
/// </summary>
public static class LumaFiles
{
    public const string TreeDir = "Luma";

    private static readonly string[] ProxyDlls =
        ["dxgi.dll", "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll", "opengl32.dll", "ddraw.dll"];

    public static bool IsAddon(string name) =>
        name.EndsWith(".addon", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase);

    /// <summary>False for what the app doesn't take from the zip: ReShade's proxy dlls, reshade.ini and logs next to the exe, and nvngx_dlss.dll anywhere.</summary>
    public static bool Include(string zipName)
    {
        var name = zipName.Replace('\\', '/');
        var file = name[(name.LastIndexOf('/') + 1)..];
        if (file.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Contains('/')) return true;
        return !ProxyDlls.Contains(file, StringComparer.OrdinalIgnoreCase)
               && !file.Equals("reshade.ini", StringComparison.OrdinalIgnoreCase)
               && !(file.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase) && file.EndsWith(".log", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>An add-on goes to ReShade's add-on folder, everything else beside the exe keeping its relative path.</summary>
    public static string Destination(string zipName, string targetDir, string addonDir)
    {
        var rel = zipName.Replace('/', Path.DirectorySeparatorChar);
        return IsAddon(zipName) ? Path.Combine(addonDir, Path.GetFileName(rel)) : Path.Combine(targetDir, rel);
    }
}

public sealed partial class ComponentStore
{
    private const string LumaDir = "luma";
    private const string LumaPackageFolder = "pkg";
    private static string LumaRoot => Path.Combine(AppPaths.Components, LumaDir);
    private (string Root, IReadOnlyList<LumaBuild> Builds)? _lumaCache;

    /// <summary>Every readable build, newest first.</summary>
    public IReadOnlyList<LumaBuild> LumaBuilds
    {
        get
        {
            var root = LumaRoot;
            lock (_addonCache)
            {
                if (_lumaCache is { } c && c.Root == root) return c.Builds;
                var builds = ReadLumaBuilds(root);
                _lumaCache = (root, builds);
                return builds;
            }
        }
    }

    private void ForgetLuma()
    {
        lock (_addonCache) _lumaCache = null;
    }

    private static IReadOnlyList<LumaBuild> ReadLumaBuilds(string root)
    {
        if (!Directory.Exists(root)) return [];
        var builds = new List<LumaBuild>();
        // One level only: the unpacked zip next to a build's record is not a library entry.
        foreach (var file in Directory.EnumerateDirectories(root).Select(d => Path.Combine(d, BuildRecordFile)).Where(File.Exists))
        {
            try
            {
                var b = JsonSerializer.Deserialize(File.ReadAllText(file), JsonCtx.Default.LumaBuild);
                if (b is not null && ValidId(b.Id) && Path.GetFileName(Path.GetDirectoryName(file)) == b.Id
                    && ValidLumaFile(b.FileName) && File.Exists(Path.Combine(root, b.Id, b.FileName))) builds.Add(b);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            { Log.Error($"Could not read Luma library entry {file}", ex); }
        }
        return builds.OrderByDescending(b => b.Date).ThenBy(b => b.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A plain file name ending in .zip: the catalog is trusted for the bytes (it gives the hash), not for a path.</summary>
    internal static bool ValidLumaFile(string? name) =>
        !string.IsNullOrEmpty(name) && Path.GetFileName(name) == name && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public LumaBuild? LumaBuildFor(string? id) => id is null ? null : LumaBuilds.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string LumaPathOf(LumaBuild build) => Path.Combine(LumaRoot, build.Id, build.FileName);

    /// <summary>Where a build's files are unpacked at install time.</summary>
    public static string LumaPackageDirOf(LumaBuild build) => Path.Combine(LumaRoot, build.Id, LumaPackageFolder);

    /// <summary>A build the game is pinned to: already in the library, so nothing is downloaded, but the file is checked against its hash before it is used.</summary>
    public LumaBuild VerifiedLumaBuild(LumaBuild build)
    {
        var path = LumaPathOf(build);
        if (!File.Exists(path)) throw new FileNotFoundException($"The pinned Luma build {build.Label} is no longer in the library. Pick another build or the default.", path);
        if (!FileUtil.Sha256(path).Equals(build.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The stored file of {build.Label} changed. Remove the build in Settings and let the app download it again.");
        return build;
    }

    private IReadOnlyList<LibraryBuild> LumaLibraryBuilds() => LumaBuilds
        .Select(b => new LibraryBuild(LibraryKind.Luma, b.Id, b.Label, b.Version, b.Date, Path.Combine(LumaRoot, b.Id), Group: b.PackageId, Downloaded: !b.Imported))
        .ToList();

    /// <summary>
    /// The library's copy of a catalog zip, downloaded (mirror first, then the author) and checked against the catalog's SHA-256 and size
    /// when it isn't there yet. A refused download throws with the one-line reason.
    /// </summary>
    public async Task<LumaBuild> EnsureLumaBuildAsync(CatalogBinary file, string packageId, string release, CatalogDownloader downloader,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!file.IsInstallable) throw new InvalidOperationException($"{file.Name}: the catalog has no SHA-256 for it, so it isn't installed.");
        var sha = file.Sha256!.ToLowerInvariant();
        if (!ValidId(sha) || !ValidLumaFile(file.Name)) throw new InvalidDataException($"The catalog entry {file.Name} isn't a Luma zip.");

        var dir = Path.Combine(LumaRoot, sha);
        var dest = Path.Combine(dir, file.Name);
        if (LumaBuildFor(sha) is { } have && File.Exists(dest) && FileUtil.Sha256(dest).Equals(sha, StringComparison.OrdinalIgnoreCase)) return have;

        Directory.CreateDirectory(dir);
        var result = await downloader.DownloadAsync(file, dest, progress, ct);
        if (!result.Ok)
        {
            FileUtil.TryDeleteEmptyDir(dir);
            throw new InvalidOperationException(result.Refusal ?? $"{file.Name} could not be downloaded.");
        }

        var build = new LumaBuild(sha, packageId, file.Name, file.Arch, release, file.FirstSeen, file.Url, false, DateTime.UtcNow);
        FileUtil.AtomicWriteText(Path.Combine(dir, BuildRecordFile), JsonSerializer.Serialize(build, JsonCtx.Default.LumaBuild));
        ForgetLuma();
        NoteAdded(sha);
        Log.Info($"Luma: downloaded {build.Label} ({file.Name}){(result.FromMirror ? " from the catalog mirror" : "")}");
        return build;
    }

    /// <summary>
    /// Adds a Luma zip the user has to the library under "custom". It must be a zip that holds a ReShade add-on. There is no catalog hash
    /// to check it against, so the copy's own SHA-256 becomes its id and every install checks the stored file against it.
    /// </summary>
    public LumaBuild ImportLumaBuild(string path)
    {
        var name = Path.GetFileName(path);
        if (!ValidLumaFile(name)) throw new InvalidDataException($"{name} isn't a zip file.");
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The zip wasn't found.", path);
        if (info.Length is 0 or > 1L << 30) throw new InvalidDataException($"{name} has an unexpected size.");
        string? addon;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            addon = zip.Entries.Select(e => e.FullName).FirstOrDefault(LumaFiles.IsAddon);
        }
        catch (InvalidDataException) { throw new InvalidDataException($"{name} isn't a readable zip file."); }
        if (addon is null) throw new InvalidDataException($"{name} has no ReShade add-on (.addon64 or .addon32) in it, so it isn't a Luma zip.");

        var sha = FileUtil.Sha256(path).ToLowerInvariant();
        if (LumaBuildFor(sha) is { } have && File.Exists(LumaPathOf(have))) return have;

        var dir = Path.Combine(LumaRoot, sha);
        Directory.CreateDirectory(dir);
        File.Copy(path, Path.Combine(dir, name), overwrite: true);
        var arch = addon.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase) ? "x86" : "x64";
        var build = new LumaBuild(sha, LumaManual.ImportedPackageId, name, arch, "imported " + sha[..8], null, null, true, DateTime.UtcNow);
        FileUtil.AtomicWriteText(Path.Combine(dir, BuildRecordFile), JsonSerializer.Serialize(build, JsonCtx.Default.LumaBuild));
        ForgetLuma();
        NoteAdded(sha);
        Log.Info($"Luma: imported {name}");
        return build;
    }

    public void RemoveLuma(string id)
    {
        if (!ValidId(id)) throw new InvalidDataException("Invalid Luma build id.");
        var dir = Path.Combine(LumaRoot, id);
        if (Directory.Exists(dir)) FileUtil.DeleteDirectory(dir);
        ForgetLuma();
    }
}
