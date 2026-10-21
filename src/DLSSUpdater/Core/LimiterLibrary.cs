using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>One add-on file the library knows: which limiter it belongs to and its bitness.</summary>
public sealed record LimiterFile(string LimiterId, string Name, string Arch)
{
    public int Bits => Arch == "x86" ? 32 : 64;
}

/// <summary>The files of the two limiters, by the names their authors publish them under.</summary>
public static class LimiterFiles
{
    public static readonly IReadOnlyList<LimiterFile> All =
    [
        new(ComponentRegistry.ReLimiter, "relimiter.addon64", "x64"),
        new(ComponentRegistry.ReLimiter, "relimiter.addon32", "x86"),
        new(ComponentRegistry.DisplayCommander, "zzz_display_commander.addon64", "x64"),
        new(ComponentRegistry.DisplayCommander, "zzz_display_commander.addon32", "x86"),
    ];

    public static LimiterFile? Find(string? name) => All.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static LimiterFile? Of(string limiterId, string arch) =>
        All.FirstOrDefault(f => f.LimiterId == limiterId && f.Arch.Equals(arch, StringComparison.OrdinalIgnoreCase));

    public static string Names(string limiterId) => string.Join(" / ", All.Where(f => f.LimiterId == limiterId).Select(f => f.Name));

    /// <summary>"x64" or "x86" for a game's bitness (a game whose exe couldn't be read counts as 64-bit).</summary>
    public static string ArchOf(bool is32Bit) => is32Bit ? "x86" : "x64";
}

/// <summary>
/// One limiter add-on file in the library, components\frame-limiters\&lt;limiter&gt;\&lt;sha256&gt;\&lt;FileName&gt; with this record next to it as
/// version.json. A build is one file: the 64-bit and the 32-bit file of a release are two builds.
/// </summary>
public sealed record LimiterBuild(
    string Id,
    string LimiterId,
    string FileName,
    string Arch,
    string Version,
    string? FirstSeen,
    string? Url,
    string? License,
    /// <summary>The user supplied the file; the automatic cleanup never deletes it.</summary>
    bool Imported,
    DateTime Added)
{
    [JsonIgnore]
    public string Label => $"{LimiterName} {Version}{(Arch == "x86" ? " (32-bit)" : "")}";

    [JsonIgnore]
    public string LimiterName => ComponentRegistry.Find(LimiterId)?.Name ?? LimiterId;

    [JsonIgnore]
    public DateTime Date => DateTime.TryParse(FirstSeen, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : Added;
}

public sealed partial class ComponentStore
{
    private const string LimiterDir = "frame-limiters";
    private static string LimiterRoot => Path.Combine(AppPaths.Components, LimiterDir);
    private (string Root, IReadOnlyList<LimiterBuild> Builds)? _limiterCache;

    /// <summary>Every readable build of both limiters, newest first (by version, then date).</summary>
    public IReadOnlyList<LimiterBuild> LimiterBuilds
    {
        get
        {
            var root = LimiterRoot;
            lock (_addonCache)
            {
                if (_limiterCache is { } c && c.Root == root) return c.Builds;
                var builds = ReadLimiterBuilds(root);
                _limiterCache = (root, builds);
                return builds;
            }
        }
    }

    private void ForgetLimiters()
    {
        lock (_addonCache) _limiterCache = null;
    }

    private static IReadOnlyList<LimiterBuild> ReadLimiterBuilds(string root)
    {
        if (!Directory.Exists(root)) return [];
        var builds = new List<LimiterBuild>();
        foreach (var file in Directory.EnumerateFiles(root, BuildRecordFile, SearchOption.AllDirectories))
        {
            try
            {
                var b = JsonSerializer.Deserialize(File.ReadAllText(file), JsonCtx.Default.LimiterBuild);
                var dir = Path.GetDirectoryName(file)!;
                if (b is not null && ValidId(b.Id) && Path.GetFileName(dir) == b.Id && Path.GetFileName(Path.GetDirectoryName(dir)) == b.LimiterId
                    && LimiterFiles.Find(b.FileName) is { } spec && spec.LimiterId == b.LimiterId && spec.Arch == b.Arch
                    && File.Exists(Path.Combine(dir, b.FileName))) builds.Add(b);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            { Log.Error($"Could not read frame limiter library entry {file}", ex); }
        }
        return BuildOrder.NewestFirst(builds, b => b.Version, b => b.Date, b => b.Id);
    }

    /// <summary>The builds of one limiter and bitness, newest first.</summary>
    public IReadOnlyList<LimiterBuild> LimiterBuildsOf(string limiterId, string arch) =>
        LimiterBuilds.Where(b => b.LimiterId == limiterId && b.Arch.Equals(arch, StringComparison.OrdinalIgnoreCase)).ToList();

    public LimiterBuild? LimiterBuildFor(string? id) => id is null ? null : LimiterBuilds.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private LimiterBuild? LimiterBuildFor(string limiterId, string id) =>
        LimiterBuilds.FirstOrDefault(b => b.LimiterId == limiterId && b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string LimiterPathOf(LimiterBuild build) => Path.Combine(LimiterRoot, build.LimiterId, build.Id, build.FileName);

    /// <summary>A build chosen by id: it is in the library, but the file is checked against its hash before it is placed.</summary>
    public LimiterBuild VerifiedLimiterBuild(LimiterBuild build)
    {
        var path = LimiterPathOf(build);
        if (!File.Exists(path)) throw new FileNotFoundException($"{build.Label} is no longer in the library. Pick another build or import it again.", path);
        if (!FileUtil.Sha256(path).Equals(build.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The stored file of {build.Label} changed. Remove the build in Settings and download or import it again.");
        return build;
    }

    private IReadOnlyList<LibraryBuild> LimiterLibraryBuilds() => LimiterBuilds
        .Select(b => new LibraryBuild(LibraryKind.FrameLimiter, b.Id, b.Label, b.Version, b.Date, Path.Combine(LimiterRoot, b.LimiterId, b.Id),
            Group: $"{b.LimiterId}/{b.Arch}", Downloaded: !b.Imported))
        .ToList();

    /// <summary>
    /// The library's copy of a catalog file, downloaded (mirror first, then the author) and checked against the catalog's SHA-256 and
    /// size when it isn't there yet. A refused download throws with the one-line reason.
    /// </summary>
    public async Task<LimiterBuild> EnsureLimiterBuildAsync(CatalogLimiter limiter, CatalogBinary file, CatalogDownloader downloader,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!file.IsInstallable) throw new InvalidOperationException($"{file.Name}: the catalog has no SHA-256 for it, so it isn't installed.");
        var sha = file.Sha256!.ToLowerInvariant();
        var spec = LimiterFiles.Find(file.Name);
        if (!ValidId(sha) || spec is null || spec.LimiterId != limiter.Id)
            throw new InvalidDataException($"The catalog entry {file.Name} isn't a {limiter.Name} add-on file.");

        var dir = Path.Combine(LimiterRoot, limiter.Id, sha);
        var dest = Path.Combine(dir, file.Name);
        if (LimiterBuildFor(limiter.Id, sha) is { } have && File.Exists(dest) && FileUtil.Sha256(dest).Equals(sha, StringComparison.OrdinalIgnoreCase)) return have;

        Directory.CreateDirectory(dir);
        var result = await downloader.DownloadAsync(file, dest, progress, ct);
        if (!result.Ok)
        {
            FileUtil.TryDeleteEmptyDir(dir);
            throw new InvalidOperationException(result.Refusal ?? $"{file.Name} could not be downloaded.");
        }

        var build = new LimiterBuild(sha, limiter.Id, file.Name, spec.Arch, limiter.VersionOf(file), file.FirstSeen, file.Url, file.License ?? limiter.License, Imported: false, DateTime.UtcNow);
        FileUtil.AtomicWriteText(Path.Combine(dir, BuildRecordFile), JsonSerializer.Serialize(build, JsonCtx.Default.LimiterBuild));
        ForgetLimiters();
        NoteAdded(sha);
        Log.Info($"{limiter.Name}: downloaded {build.Label}{(result.FromMirror ? " from the catalog mirror" : "")}");
        return build;
    }

    /// <summary>
    /// Adds files the user got from the author: one add-on file (relimiter.addon64, zzz_display_commander.addon32, ...), a folder with them,
    /// or a .zip of a release. Files are copied byte for byte; each must be a PE file of the bitness its name says. A build already there
    /// stays as it is, except that importing a downloaded one by hand makes it an import (the automatic cleanup then leaves it).
    /// </summary>
    public IReadOnlyList<LimiterBuild> ImportLimiter(string source)
    {
        var work = Path.Combine(AppPaths.Cache, "limiter-import-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var found = new List<(LimiterFile Spec, string Path)>();
            if (Directory.Exists(source))
                foreach (var f in EnumerateTree(source, 3))
                    if (LimiterFiles.Find(Path.GetFileName(f)) is { } spec) found.Add((spec, f));
            if (File.Exists(source))
            {
                if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) found.AddRange(ExtractLimiters(source, work));
                else if (LimiterFiles.Find(Path.GetFileName(source)) is { } spec) found.Add((spec, source));
                else throw new InvalidDataException($"{Path.GetFileName(source)} isn't a limiter add-on. Expected {string.Join(" or ", LimiterFiles.All.Select(f => f.Name))}.");
            }
            else if (!Directory.Exists(source)) throw new FileNotFoundException("That file or folder does not exist.", source);

            if (found.Count == 0)
                throw new InvalidDataException($"No limiter add-on found there. Expected {string.Join(" or ", LimiterFiles.All.Select(f => f.Name))}.");
            var builds = new List<LimiterBuild>();
            foreach (var (spec, path) in found.GroupBy(f => f.Spec.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                builds.Add(RetainLimiter(spec, path));
            ForgetLimiters();
            return builds;
        }
        finally { FileUtil.DeleteDirectory(work); }
    }

    private LimiterBuild RetainLimiter(LimiterFile spec, string path)
    {
        var pe = PeReader.Read(path) ?? throw new InvalidDataException($"{spec.Name} is not a readable Windows add-on (DLL).");
        if (pe.Bits != spec.Bits) throw new InvalidDataException($"{spec.Name} is a {pe.Bits}-bit file, but its name says {spec.Bits}-bit.");

        var sha = FileUtil.Sha256(path);
        var dir = Path.Combine(LimiterRoot, spec.LimiterId, sha);
        var meta = Path.Combine(dir, BuildRecordFile);
        var existing = LimiterBuildFor(spec.LimiterId, sha);
        if (existing is not null)
        {
            if (existing.Imported) return existing;
            existing = existing with { Imported = true };
            FileUtil.AtomicWriteText(meta, JsonSerializer.Serialize(existing, JsonCtx.Default.LimiterBuild));
            return existing;
        }
        var version = FileUtil.ReadVersion(path) is { } v ? FileUtil.Format(v) : "imported " + sha[..8];
        var build = new LimiterBuild(sha, spec.LimiterId, spec.Name, spec.Arch, version, null, null, null, Imported: true, DateTime.UtcNow);
        FileUtil.AtomicCopy(path, Path.Combine(dir, spec.Name));
        FileUtil.AtomicWriteText(meta, JsonSerializer.Serialize(build, JsonCtx.Default.LimiterBuild));
        Log.Info($"Imported {build.Label} ({sha[..12]})");
        return build;
    }

    private static IEnumerable<string> EnumerateTree(string dir, int depth)
    {
        IEnumerable<string> files;
        IEnumerable<string> subs;
        try
        {
            files = Directory.EnumerateFiles(dir).ToList();
            subs = depth > 0 ? Directory.EnumerateDirectories(dir).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var f in files) yield return f;
        foreach (var s in subs)
            foreach (var f in EnumerateTree(s, depth - 1)) yield return f;
    }

    /// <summary>Takes the known add-on files out of a zip by their file names; paths inside the zip are never used.</summary>
    private static List<(LimiterFile Spec, string Path)> ExtractLimiters(string zipPath, string work)
    {
        var found = new List<(LimiterFile, string)>();
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                if (entry.Name.Length == 0 || LimiterFiles.Find(entry.Name) is not { } spec) continue;
                var dir = Path.Combine(work, spec.Arch + "-" + spec.LimiterId);
                Directory.CreateDirectory(dir);
                var dest = Path.Combine(dir, spec.Name);
                entry.ExtractToFile(dest, true);
                found.Add((spec, dest));
            }
        }
        catch (InvalidDataException ex) { throw new InvalidDataException($"{Path.GetFileName(zipPath)} is not a readable zip ({ex.Message})."); }
        return found;
    }

    public void RemoveLimiter(string id)
    {
        if (!ValidId(id)) throw new InvalidDataException("Invalid frame limiter build id.");
        foreach (var dir in Directory.Exists(LimiterRoot) ? Directory.EnumerateDirectories(LimiterRoot).ToList() : [])
        {
            var build = Path.Combine(dir, id);
            if (Directory.Exists(build)) Directory.Delete(build, true);
        }
        ForgetLimiters();
    }
}
