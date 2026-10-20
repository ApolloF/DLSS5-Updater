using System.Text.RegularExpressions;

namespace DLSSUpdater.Core;

/// <summary>
/// Every library of kept builds: the downloaded OptiScaler and GITC Uplift releases, the imported add-ons and the NR runtime.
/// Each keeps several builds side by side; settings choose a default and each game may choose its own.
/// </summary>
public enum LibraryKind { OptiScaler, Uplift, Lecram, RenoDx, DlssNr }

/// <summary>
/// One kept build as the lists and the cleanup see it. <see cref="Id"/> is what settings, game picks and install records name
/// it by: the SHA-256 for imports and the NR runtime, the app-internal tag for downloads. <see cref="Dir"/> holds the whole build.
/// Builds of one <see cref="Group"/> (an OptiScaler source) compete for "newest".
/// </summary>
public sealed record LibraryBuild(LibraryKind Kind, string Id, string Label, string Version, DateTime? Date, string Dir, string Group = "")
{
    public override string ToString() => Label;
}

/// <summary>The one place that decides which build is newer, for every list and the cleanup.</summary>
public static partial class BuildOrder
{
    /// <summary>
    /// Sort key of a version label ("v11.0.0-beta.48", "v8.5.0-rc10-model-sdr1", "v0.7.0-pre10", "310.9.16"): the version,
    /// then a release after its pre-releases ("v11.0.0-beta.6" before "v11.0.0"), alpha before beta before pre before rc,
    /// then the pre-release number. The stage is the first dash- or dot-separated part that starts with one; its number
    /// follows it directly ("rc10") or as the next part ("beta.48"). A label without a version uses <paramref name="fallback"/>.
    /// </summary>
    public static (Version Version, bool Release, int Stage, int Number) Key(string? label, Version? fallback = null)
    {
        var text = (label ?? "").Trim().TrimStart('v', 'V');
        var dash = text.IndexOf('-');
        var core = dash < 0 ? text : text[..dash];
        var pre = dash < 0 ? [] : text[(dash + 1)..].Split('-', '.');
        var (stage, number) = (0, 0);
        for (var i = 0; i < pre.Length; i++)
        {
            if (PreRelease().Match(pre[i]) is not { Success: true } m) continue;
            stage = m.Groups[1].Value.ToLowerInvariant() switch { "alpha" => 1, "beta" => 2, "pre" => 3, _ => 4 };
            var digits = m.Groups[2].Value.Length > 0 ? m.Groups[2].Value : i + 1 < pre.Length ? pre[i + 1] : "";
            number = int.TryParse(digits, out var n) ? n : 0;
            break;
        }
        var version = (core.Length > 0 && char.IsAsciiDigit(core[0]) ? FileUtil.ParseTag(core) : null) ?? fallback ?? new Version(0, 0);
        return (Normalize(version), dash < 0, stage, number);
    }

    [GeneratedRegex(@"^(alpha|beta|pre|rc)(\d*)$", RegexOptions.IgnoreCase)]
    private static partial Regex PreRelease();

    /// <summary>1.2 and 1.2.0.0 are the same version here.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>Newest first by <see cref="Key"/>, then the later date, then the id for a stable order.</summary>
    public static List<T> NewestFirst<T>(IEnumerable<T> items, Func<T, string?> label, Func<T, DateTime?> date, Func<T, string> id, Func<T, Version?>? fallback = null) =>
        items.OrderByDescending(i => Key(label(i), fallback?.Invoke(i)))
            .ThenByDescending(i => date(i) ?? DateTime.MinValue)
            .ThenBy(id, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>Something the cleanup would delete, with its size on disk.</summary>
public sealed record CleanupItem(string What, string Path, long Bytes);

/// <summary>Which builds the cleanup removes. Pure: what to protect is collected by <see cref="ComponentStore.ProtectedBuilds"/>.</summary>
public static class BuildCleanup
{
    public const int DefaultKeep = 2;
    public const int MaxKeep = 10;

    /// <summary>
    /// The builds of <paramref name="newestFirst"/> beyond the newest <paramref name="keep"/> of their group (at least one),
    /// minus every protected id.
    /// </summary>
    public static List<LibraryBuild> Select(IEnumerable<LibraryBuild> newestFirst, int keep, IReadOnlySet<string> protectedIds)
    {
        keep = Math.Clamp(keep, 1, MaxKeep);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var delete = new List<LibraryBuild>();
        foreach (var b in newestFirst)
        {
            var n = seen[b.Group] = seen.GetValueOrDefault(b.Group) + 1;
            if (n > keep && !protectedIds.Contains(b.Id)) delete.Add(b);
        }
        return delete;
    }

    /// <summary>Bytes of every file under <paramref name="path"/>; links are not followed, unreadable parts count as zero.</summary>
    public static long SizeOf(string path)
    {
        if (File.Exists(path)) return new FileInfo(path).Length;
        if (!Directory.Exists(path)) return 0;
        var walk = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        return new DirectoryInfo(path).EnumerateFiles("*", walk).Sum(f => f.Length);
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} bytes",
    };
}

public sealed partial class ComponentStore
{
    private const string DeletingSuffix = ".dlssu-delete";
    private static int _releaseGeneration;
    private readonly Dictionary<Component, (string Root, int Generation, IReadOnlyList<LibraryBuild> Builds)> _releaseCache = [];

    public static LibraryKind KindOf(AddonKind kind) => kind == AddonKind.RenoDx ? LibraryKind.RenoDx : LibraryKind.Lecram;

    /// <summary>The builds of one library, newest first (see <see cref="BuildOrder"/>). Only metadata is read.</summary>
    public IReadOnlyList<LibraryBuild> Library(LibraryKind kind) => kind switch
    {
        LibraryKind.OptiScaler => ReleaseBuilds(Component.OptiScaler),
        LibraryKind.Uplift => ReleaseBuilds(Component.Uplift),
        LibraryKind.RenoDx or LibraryKind.Lecram => AddonBuilds(kind == LibraryKind.RenoDx ? AddonKind.RenoDx : AddonKind.Lecram)
            .Select(b => new LibraryBuild(kind, b.Id, b.Label, b.DetectedLabel, b.Imported, Path.Combine(AddonRoot(kind == LibraryKind.RenoDx ? AddonKind.RenoDx : AddonKind.Lecram), b.Id)))
            .ToList(),
        _ => DlssNrVersions.Select(v => new LibraryBuild(kind, v.Id, v.Label, v.Version, null, Path.Combine(NrRoot, v.Id))).ToList(),
    };

    /// <summary>
    /// The complete downloads of OptiScaler (both sources, the selected list grouped by source) or GITC Uplift, newest first.
    /// Each folder's .complete record is the build's metadata; the list is read once per change.
    /// </summary>
    public IReadOnlyList<LibraryBuild> ReleaseBuilds(Component c)
    {
        var root = ComponentDir(c);
        var generation = Volatile.Read(ref _releaseGeneration);
        lock (_releaseCache)
        {
            if (_releaseCache.TryGetValue(c, out var cached) && cached.Root == root && cached.Generation == generation) return cached.Builds;
            var builds = ReadReleaseBuilds(c, root);
            _releaseCache[c] = (root, generation, builds);
            return builds;
        }
    }

    private static IReadOnlyList<LibraryBuild> ReadReleaseBuilds(Component c, string root)
    {
        var kind = c == Component.OptiScaler ? LibraryKind.OptiScaler : LibraryKind.Uplift;
        var found = new List<(LibraryBuild Build, string Raw)>();
        foreach (var (dir, meta) in CompleteBuilds(c, root))
        {
            if (!Path.GetFileName(dir).Equals(SafeName(meta.Tag), StringComparison.OrdinalIgnoreCase)) continue;
            var pre = meta.Prerelease ? " pre" : "";
            if (c == Component.OptiScaler)
            {
                var source = OptiSources.OfTag(meta.Tag);
                var raw = OptiSources.RawTag(meta.Tag);
                found.Add((new LibraryBuild(kind, meta.Tag, $"{source.Name} {raw}{pre}", raw, meta.Published ?? meta.Fetched, dir, source.Repo), raw));
            }
            else found.Add((new LibraryBuild(kind, meta.Tag, meta.Tag + pre, meta.Tag, meta.Published ?? meta.Fetched, dir), meta.Tag));
        }
        // OptiScaler's sources number their releases differently, so each source is its own newest-first run.
        return found.GroupBy(f => f.Build.Group)
            .OrderBy(g => OptiSourceIndex(g.Key))
            .SelectMany(g => BuildOrder.NewestFirst(g, f => f.Raw, f => f.Build.Date, f => f.Build.Id))
            .Select(f => f.Build)
            .ToList();
    }

    private static int OptiSourceIndex(string repo)
    {
        for (var i = 0; i < OptiSources.All.Count; i++)
            if (OptiSources.All[i].Repo.Equals(repo, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>A download completed or a build was deleted: the next list read sees it.</summary>
    private static void ReleasesChanged() => Interlocked.Increment(ref _releaseGeneration);

    /// <summary>
    /// First run with the build library: complete OptiScaler and GITC Uplift downloads move from the cache into
    /// components (a rename on the same drive, nothing is downloaded again). One that can't move stays and is logged;
    /// once the library has that build, the cleanup lists the copy left in the cache (see <see cref="StaleDownloads"/>).
    /// </summary>
    public void MigrateReleaseLibraries()
    {
        foreach (var c in new[] { Component.OptiScaler, Component.Uplift })
        {
            var legacy = LegacyDir(c);
            var moved = 0;
            try
            {
                if (!Directory.Exists(legacy)) continue;
                foreach (var dir in Directory.EnumerateDirectories(legacy).ToList())
                {
                    if (!File.Exists(Path.Combine(dir, CompleteMarker))) continue;
                    var dest = Path.Combine(ComponentDir(c), Path.GetFileName(dir));
                    if (Directory.Exists(dest)) continue;
                    try
                    {
                        Directory.CreateDirectory(ComponentDir(c));
                        Directory.Move(dir, dest);
                        moved++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { Log.Error($"Could not move {dir} into the build library", ex); }
                }
                FileUtil.TryDeleteEmptyDir(legacy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Log.Error($"Could not read {legacy} to move its builds into the build library", ex); }
            if (moved > 0)
            {
                ReleasesChanged();
                Log.Info($"Moved {moved} downloaded {(c == Component.OptiScaler ? "OptiScaler" : "GITC Uplift")} build(s) into the build library");
            }
        }
    }

    /// <summary>Where 2.0 and older kept OptiScaler and GITC Uplift downloads.</summary>
    private static string LegacyDir(Component c) => Path.Combine(AppPaths.Cache, c.ToString().ToLowerInvariant());

    // ---------- cleanup ----------

    private readonly HashSet<string> _added = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A build just entered a library (downloaded or imported): the next automatic cleanup keeps it.</summary>
    private void NoteAdded(string id)
    {
        lock (_added) _added.Add(id);
    }

    /// <summary>The builds added since the last call, which the automatic cleanup that follows must not delete.</summary>
    public HashSet<string> TakeAdded()
    {
        lock (_added)
        {
            var ids = new HashSet<string>(_added, StringComparer.OrdinalIgnoreCase);
            _added.Clear();
            return ids;
        }
    }

    /// <summary>
    /// Builds of <paramref name="kind"/> the cleanup must keep: the default and every game's own pick, what any game has
    /// installed, the NR runtime a game or the default can switch back to, and Lecram's last downloaded build.
    /// </summary>
    public HashSet<string> ProtectedBuilds(LibraryKind kind, AppSettings s, IEnumerable<InstallManifest> manifests)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? id)
        {
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        }
        var games = s.Games.Values;
        var installed = manifests.ToList();
        switch (kind)
        {
            case LibraryKind.OptiScaler:
                Add(s.OptiTag);
                Add(Opti?.Tag);
                foreach (var g in games) Add(g.OptiTag);
                foreach (var m in installed) Add(m.OptiTag);
                break;
            case LibraryKind.Uplift:
                Add(s.UpliftTag);
                Add(Uplift?.Tag);
                foreach (var g in games) Add(g.UpliftTag);
                foreach (var m in installed) Add(m.UpliftTag);
                break;
            case LibraryKind.RenoDx:
            case LibraryKind.Lecram:
                var addon = kind == LibraryKind.RenoDx ? AddonKind.RenoDx : AddonKind.Lecram;
                Add(s.AddonId(addon));
                Add(AddonBuildFor(addon, null)?.Id);
                foreach (var g in games) Add(addon == AddonKind.RenoDx ? g.RenoDxId : g.LecramId);
                foreach (var m in installed)
                {
                    var (id, tag) = addon == AddonKind.RenoDx ? (m.RenoDxId, m.RenoDxTag) : (m.LecramId, m.LecramTag);
                    // Records from before the library only carry the tag.
                    foreach (var b in AddonBuilds(addon).Where(b => b.Matches(id, tag))) Add(b.Id);
                }
                if (addon == AddonKind.Lecram) Add(s.LecramLastBuildId);
                break;
            default:
                Add(s.DlssNrId);
                Add(s.PreviousDlssNrId);
                Add(Path.GetFileName(Path.GetDirectoryName(DlssNrPath)));
                foreach (var g in games) Add(g.DlssNrId);
                foreach (var m in installed)
                {
                    Add(m.DlssNrId);
                    Add(m.PreviousDlssNrId);
                }
                break;
        }
        return ids;
    }

    /// <summary>What the cleanup deletes in one library, under the keep count of the settings, keeping <paramref name="keep"/> too.</summary>
    public List<LibraryBuild> CleanupCandidates(LibraryKind kind, AppSettings s, IEnumerable<InstallManifest> manifests, IEnumerable<string>? keep = null)
    {
        var ids = ProtectedBuilds(kind, s, manifests);
        if (keep is not null) ids.UnionWith(keep);
        return BuildCleanup.Select(Library(kind), s.KeepBuildsFor(kind), ids);
    }

    /// <summary>
    /// Everything the cleanup would delete now: old builds of every library and stale download leftovers, with sizes.
    /// <paramref name="keep"/> are ids that stay as well (the builds just added); libraries in <paramref name="skip"/> are left alone.
    /// </summary>
    public List<CleanupItem> PlanCleanup(AppSettings s, IReadOnlyCollection<InstallManifest> manifests,
        IReadOnlySet<string>? keep = null, IReadOnlySet<LibraryKind>? skip = null)
    {
        var items = new List<CleanupItem>();
        foreach (var kind in Enum.GetValues<LibraryKind>().Where(k => skip?.Contains(k) != true))
            foreach (var b in CleanupCandidates(kind, s, manifests, keep))
                items.Add(new CleanupItem($"{LibraryTitle(kind)} {b.Label}", b.Dir, BuildCleanup.SizeOf(b.Dir)));
        items.AddRange(StaleDownloads(s, manifests));
        return items;
    }

    public static string LibraryTitle(LibraryKind kind) => kind switch
    {
        LibraryKind.OptiScaler => "OptiScaler",
        LibraryKind.Uplift => "GITC Uplift",
        LibraryKind.Lecram => "Lecram DLSS5",
        LibraryKind.RenoDx => "ShortFuse RenoDX DLSS",
        _ => "NR runtime",
    };

    /// <summary>
    /// Leftovers under the cache: unfinished downloads, temporary Lecram downloads, and downloaded MFG Unlock, DLSS,
    /// Streamline and ReShade releases that are neither the newest, the current one, chosen in the settings or a game,
    /// nor installed anywhere. DLSS files the user imported are never stale.
    /// </summary>
    public List<CleanupItem> StaleDownloads(AppSettings s, IEnumerable<InstallManifest> manifests)
    {
        var items = new List<CleanupItem>();
        var installed = manifests.ToList();
        if (Directory.Exists(AppPaths.Cache))
            foreach (var dir in Directory.EnumerateDirectories(AppPaths.Cache, "lecram-download-*"))
                items.Add(new CleanupItem("Unfinished Lecram download", dir, BuildCleanup.SizeOf(dir)));
        var walk = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        foreach (var root in new[] { AppPaths.Components, AppPaths.Cache }.Where(Directory.Exists))
            foreach (var dir in Directory.EnumerateDirectories(root, "*" + DeletingSuffix, walk))
                items.Add(new CleanupItem($"Rest of a deleted build {Path.GetFileName(dir)}", dir, BuildCleanup.SizeOf(dir)));

        foreach (var c in Enum.GetValues<Component>())
        {
            var library = c is Component.OptiScaler or Component.Uplift;
            // The library's own unfinished downloads, and whatever the move into the library left in the cache.
            var roots = library ? new[] { ComponentDir(c), LegacyDir(c) } : [ComponentDir(c)];
            foreach (var root in roots.Where(Directory.Exists))
                foreach (var dir in Directory.EnumerateDirectories(root))
                    if (!File.Exists(Path.Combine(dir, CompleteMarker)) && !dir.EndsWith(DeletingSuffix, StringComparison.OrdinalIgnoreCase))
                        items.Add(new CleanupItem($"Unfinished {c} download {Path.GetFileName(dir)}", dir, BuildCleanup.SizeOf(dir)));
            if (library)
            {
                // A complete build the migration couldn't move: once the library holds the same build, the copy is spare.
                if (Directory.Exists(LegacyDir(c)))
                    foreach (var dir in Directory.EnumerateDirectories(LegacyDir(c)))
                        if (File.Exists(Path.Combine(dir, CompleteMarker)) && File.Exists(Path.Combine(ComponentDir(c), Path.GetFileName(dir), CompleteMarker)))
                            items.Add(new CleanupItem($"Leftover {c} download {Path.GetFileName(dir)}", dir, BuildCleanup.SizeOf(dir)));
                continue;
            }

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? tag)
            {
                if (!string.IsNullOrEmpty(tag)) keep.Add(SafeName(tag));
            }
            Add(Get(c)?.Tag);
            Add(CachedReleases(c).FirstOrDefault()?.Tag);
            switch (c)
            {
                case Component.MfgUnlock:
                    foreach (var m in installed) Add(m.MfgTag);
                    break;
                case Component.Dlss:
                    Add(s.DlssTag);
                    foreach (var g in s.Games.Values) Add(g.DlssTag);
                    foreach (var m in installed) Add(m.DlssTag);
                    break;
                case Component.Streamline:
                    foreach (var m in installed) Add(m.StreamlineTag);
                    break;
            }
            var cacheRoot = ComponentDir(c);
            if (!Directory.Exists(cacheRoot)) continue;
            foreach (var dir in Directory.EnumerateDirectories(cacheRoot))
            {
                var name = Path.GetFileName(dir);
                if (!File.Exists(Path.Combine(dir, CompleteMarker)) || keep.Contains(name) || name.EndsWith(DeletingSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                if (c == Component.Dlss && name.Contains("-local-", StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new CleanupItem($"Old {c} download {name}", dir, BuildCleanup.SizeOf(dir)));
            }
        }
        return items;
    }

    /// <summary>
    /// Deletes the planned items for good (not to the recycle bin), each logged with its size. One that is in use or
    /// locked is logged and skipped. Only folders inside the app's components and cache folders are ever touched.
    /// Returns the bytes freed.
    /// </summary>
    public long RunCleanup(IEnumerable<CleanupItem> items)
    {
        long freed = 0;
        var count = 0;
        foreach (var item in items)
        {
            if (!FileUtil.IsUnder(item.Path, AppPaths.Components) && !FileUtil.IsUnder(item.Path, AppPaths.Cache))
            {
                Log.Warn($"Cleanup skipped {item.Path}: outside the app's data folder");
                continue;
            }
            try
            {
                // Renamed first: a file in use fails the rename and leaves the build whole. A delete that fails halfway
                // leaves a folder no library reads, which the next cleanup removes.
                var doomed = item.Path.EndsWith(DeletingSuffix, StringComparison.OrdinalIgnoreCase) ? item.Path : item.Path + DeletingSuffix;
                if (doomed != item.Path)
                {
                    if (Directory.Exists(doomed)) FileUtil.DeleteDirectory(doomed);
                    Directory.Move(item.Path, doomed);
                }
                FileUtil.DeleteDirectory(doomed);
                freed += item.Bytes;
                count++;
                Log.Info($"Deleted {item.What} ({BuildCleanup.FormatBytes(item.Bytes)})");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Log.Warn($"Could not delete {item.What}, skipped: {ex.Message}"); }
        }
        ReleasesChanged();
        ForgetAddons(AddonKind.RenoDx);
        ForgetAddons(AddonKind.Lecram);
        if (count > 0) Log.Info($"Cleanup freed {BuildCleanup.FormatBytes(freed)} ({count} item(s))");
        return freed;
    }
}
