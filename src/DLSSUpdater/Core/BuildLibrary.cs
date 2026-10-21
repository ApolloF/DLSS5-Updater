using System.Text.RegularExpressions;

namespace DLSSUpdater.Core;

/// <summary>
/// Every library of kept builds: the downloaded OptiScaler and GITC Uplift releases, the imported add-ons and the NR runtime.
/// Each keeps several builds side by side; settings choose a default and each game may choose its own.
/// </summary>
public enum LibraryKind { OptiScaler, Uplift, Lecram, RenoDx, Dfc, DlssNr, RenoDxHdr, ReShade, Feeder }

/// <summary>
/// One kept build as the lists and the cleanup see it. <see cref="Id"/> is what settings, game picks and install records name
/// it by: the SHA-256 for imports and the NR runtime, the app-internal tag for downloads. <see cref="Dir"/> holds the whole build.
/// Builds of one <see cref="Group"/> (an OptiScaler source) compete for "newest". <see cref="Downloaded"/>: the app downloaded
/// it itself (OptiScaler, GITC Uplift, Lecram's channel); only those may go in an automatic cleanup, imports never do.
/// </summary>
public sealed record LibraryBuild(LibraryKind Kind, string Id, string Label, string Version, DateTime? Date, string Dir, string Group = "", bool Downloaded = false)
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

/// <summary>A build the keep count would drop that stays anyway, with every reason (see <see cref="ComponentStore.ProtectionReasons"/>).</summary>
public sealed record KeptBuild(LibraryBuild Build, IReadOnlyList<string> Reasons);

/// <summary>
/// Why a library holds more builds than its keep count, for the cleanup area of Settings: the protected builds beyond
/// the newest ones, and for OptiScaler the count per source (each source keeps its own newest, so 3 builds can be all kept).
/// </summary>
public sealed record LibraryKeepNote(IReadOnlyList<KeptBuild> Kept, string? PerSource)
{
    private const int ShownReasons = 2;

    /// <summary>One short line, or null when nothing needs explaining.</summary>
    public string? Line
    {
        get
        {
            var parts = new List<string>();
            if (Kept.Count > 0)
            {
                var reasons = Kept.SelectMany(k => k.Reasons.Select(r => r == "default" ? $"default ({k.Build.Version})" : r)).Distinct().ToList();
                var more = reasons.Count > ShownReasons ? $" and {reasons.Count - ShownReasons} more" : "";
                parts.Add($"{Kept.Count} kept: {string.Join(", ", reasons.Take(ShownReasons))}{more}");
            }
            if (PerSource is not null) parts.Add($"per source: {PerSource}");
            return parts.Count == 0 ? null : string.Join("  ·  ", parts);
        }
    }

    /// <summary>Every kept build with all its reasons, one per line; null when none is kept.</summary>
    public string? Details => Kept.Count == 0 ? null
        : string.Join(Environment.NewLine, Kept.Select(k => $"{k.Build.Label}: {string.Join(", ", k.Reasons)}"));
}

/// <summary>Which builds the cleanup removes. Pure: what to protect is collected by <see cref="ComponentStore.ProtectedBuilds"/>.</summary>
public static class BuildCleanup
{
    public const int DefaultKeep = 2;
    public const int MaxKeep = 10;

    /// <summary>
    /// The builds of <paramref name="newestFirst"/> beyond the newest <paramref name="keep"/> of their group (at least one),
    /// minus every protected id.
    /// </summary>
    public static List<LibraryBuild> Select(IEnumerable<LibraryBuild> newestFirst, int keep, IReadOnlySet<string> protectedIds) =>
        BeyondKeep(newestFirst, keep).Where(b => !protectedIds.Contains(b.Id)).ToList();

    /// <summary>The builds of <paramref name="newestFirst"/> beyond the newest <paramref name="keep"/> of their group (at least one).</summary>
    public static List<LibraryBuild> BeyondKeep(IEnumerable<LibraryBuild> newestFirst, int keep)
    {
        keep = Math.Clamp(keep, 1, MaxKeep);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var beyond = new List<LibraryBuild>();
        foreach (var b in newestFirst)
        {
            var n = seen[b.Group] = seen.GetValueOrDefault(b.Group) + 1;
            if (n > keep) beyond.Add(b);
        }
        return beyond;
    }

    /// <summary>The status when a cleanup would delete nothing; says so when protected builds are why libraries hold more than the keep count.</summary>
    public static string NothingToCleanText(IEnumerable<LibraryKeepNote> notes) =>
        notes.Any(n => n.Kept.Count > 0) ? "Nothing to clean up: the rest are defaults or used by games." : "Nothing to clean up.";

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

    public static LibraryKind KindOf(AddonKind kind) => kind switch
    {
        AddonKind.RenoDx => LibraryKind.RenoDx,
        AddonKind.Lecram => LibraryKind.Lecram,
        _ => LibraryKind.Dfc,
    };

    /// <summary>The add-on library behind <paramref name="kind"/>; null for the download libraries and the NR runtime.</summary>
    private static AddonKind? AddonOf(LibraryKind kind) => kind switch
    {
        LibraryKind.RenoDx => AddonKind.RenoDx,
        LibraryKind.Lecram => AddonKind.Lecram,
        LibraryKind.Dfc => AddonKind.Dfc,
        _ => null,
    };

    /// <summary>The builds of one library, newest first (see <see cref="BuildOrder"/>). Only metadata is read.</summary>
    public IReadOnlyList<LibraryBuild> Library(LibraryKind kind) => kind switch
    {
        LibraryKind.OptiScaler => ReleaseBuilds(Component.OptiScaler),
        LibraryKind.Uplift => ReleaseBuilds(Component.Uplift),
        LibraryKind.RenoDxHdr => HdrLibraryBuilds(),
        LibraryKind.ReShade => ReShadeBuilds.Select(b => new LibraryBuild(kind, b.Id, b.Label, b.Version, b.Added, ReShadeBuildDir(b), Downloaded: b.Downloaded)).ToList(),
        LibraryKind.Feeder => FeederLibrary.Builds(),
        _ when AddonOf(kind) is { } addon => AddonBuilds(addon)
            .Select(b => new LibraryBuild(kind, b.Id, b.Label, b.DetectedLabel, b.Imported, Path.Combine(AddonRoot(addon), b.Id), Downloaded: b.Downloaded))
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
                found.Add((new LibraryBuild(kind, meta.Tag, $"{source.Name} {raw}{pre}", raw, meta.Published ?? meta.Fetched, dir, source.Repo, Downloaded: true), raw));
            }
            else found.Add((new LibraryBuild(kind, meta.Tag, meta.Tag + pre, meta.Tag, meta.Published ?? meta.Fetched, dir, Downloaded: true), meta.Tag));
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

    /// <summary>A build was just downloaded into a library: the next automatic cleanup keeps it.</summary>
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
    public HashSet<string> ProtectedBuilds(LibraryKind kind, AppSettings s, IEnumerable<InstallManifest> manifests) =>
        ProtectionReasons(kind, s, manifests, null).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The one source of what <see cref="ProtectedBuilds"/> keeps: each (build id, reason) pair, a build once per reason.
    /// <paramref name="gameNames"/> maps game ids and install folders to the names the game list shows; a game without an
    /// entry is named by its folder.
    /// </summary>
    public IEnumerable<(string Id, string Reason)> ProtectionReasons(LibraryKind kind, AppSettings s, IEnumerable<InstallManifest> manifests,
        IReadOnlyDictionary<string, string>? gameNames)
    {
        var found = new List<(string Id, string Reason)>();
        void Add(string? id, string reason)
        {
            if (!string.IsNullOrEmpty(id) && !found.Any(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && f.Reason == reason)) found.Add((id, reason));
        }
        const string Default = "default", SwitchBack = "NR runtime to switch back to";
        string PickedFor(string gameId) => "picked for " + GameName(gameNames, gameId, gameId);
        string InstalledIn(InstallManifest m) => "installed in " + (m.Dir is null ? "a game" : GameName(gameNames, m.Dir, GameFolder(m)));
        var installed = manifests.ToList();
        switch (kind)
        {
            case LibraryKind.OptiScaler:
                Add(s.OptiTag, Default);
                Add(Opti?.Tag, "latest release");
                foreach (var (id, g) in s.Games) Add(g.OptiTag, PickedFor(id));
                foreach (var m in installed) Add(m.OptiTag, InstalledIn(m));
                break;
            case LibraryKind.Uplift:
                Add(s.UpliftTag, Default);
                Add(Uplift?.Tag, "latest release");
                foreach (var (id, g) in s.Games) Add(g.UpliftTag, PickedFor(id));
                foreach (var m in installed) Add(m.UpliftTag, InstalledIn(m));
                break;
            case LibraryKind.RenoDx:
            case LibraryKind.Lecram:
            case LibraryKind.Dfc:
                var addon = AddonOf(kind)!.Value;
                Add(s.AddonId(addon), Default);
                // Without a chosen default the newest build is the default.
                Add(AddonBuildFor(addon, null)?.Id, Default);
                foreach (var (id, g) in s.Games)
                    Add(addon switch { AddonKind.RenoDx => g.RenoDxId, AddonKind.Lecram => g.LecramId, _ => g.DfcId }, PickedFor(id));
                foreach (var m in installed)
                {
                    var (id, tag) = addon switch
                    {
                        AddonKind.RenoDx => (m.RenoDxId, m.RenoDxTag),
                        AddonKind.Lecram => (m.LecramId, m.LecramTag),
                        _ => (m.DfcId, m.DfcTag),
                    };
                    // Records from before the library only carry the tag.
                    foreach (var b in AddonBuilds(addon).Where(b => b.Matches(id, tag))) Add(b.Id, InstalledIn(m));
                }
                if (addon == AddonKind.Lecram) Add(s.LecramLastBuildId, "last downloaded Lecram build");
                break;
            case LibraryKind.ReShade:
                if (!ReShadePick.IsChannel(s.ReShadeId)) Add(s.ReShadeId, Default);
                Add(ReShadeBuildFor(null)?.Id, Default);
                foreach (var c in new[] { ReShadeChannel.Stable, ReShadeChannel.StableNoAddons, ReShadeChannel.Nightly })
                    Add(ReShadeBuilds.FirstOrDefault(b => b.Channel == c)?.Id, "latest " + ReShadeBuild.ChannelLabel(c));
                foreach (var (id, g) in s.Games)
                    Add(ReShadePick.IsChannel(g.ReShadeId) ? ReShadeBuildFor(g.ReShadeId)?.Id : g.ReShadeId, PickedFor(id));
                foreach (var m in installed) Add(m.ReShadeId, InstalledIn(m));
                break;
            case LibraryKind.DlssNr:
                Add(s.DlssNrId, Default);
                Add(Path.GetFileName(Path.GetDirectoryName(DlssNrPath)), Default);
                Add(s.PreviousDlssNrId, SwitchBack);
                foreach (var (id, g) in s.Games) Add(g.DlssNrId, PickedFor(id));
                foreach (var m in installed)
                {
                    Add(m.DlssNrId, InstalledIn(m));
                    Add(m.PreviousDlssNrId, SwitchBack);
                }
                break;
        }
        // Components recorded the generic way: their default, every game's pick and what is installed, by component id.
        foreach (var def in ComponentRegistry.OfLibrary(kind).Where(d => !d.Legacy))
        {
            Add(s.DefaultBuild(def.Id), Default);
            foreach (var (id, g) in s.Games) Add(g.BuildPick(def.Id), PickedFor(id));
            foreach (var m in installed) Add(m.Component(def.Id)?.BuildId, InstalledIn(m));
        }
        return found;
    }

    private static string GameName(IReadOnlyDictionary<string, string>? names, string key, string path) =>
        names is not null && names.TryGetValue(key, out var name) ? name : FolderName(path);

    private static string FolderName(string path) => Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : path;

    /// <summary>The game folder a record belongs to, from its install folder and the root it names; the install folder when that can't be told.</summary>
    private static string GameFolder(InstallManifest m)
    {
        try { return Path.GetFullPath(Path.Combine(m.Dir!, m.RootRel)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return m.Dir!; }
    }

    /// <summary>
    /// Why <paramref name="kind"/> holds more builds than its keep count: the protected builds beyond the newest ones with
    /// their reasons and, for OptiScaler with several sources, the count per source.
    /// </summary>
    public LibraryKeepNote KeepNote(LibraryKind kind, AppSettings s, IEnumerable<InstallManifest> manifests, IReadOnlyDictionary<string, string>? gameNames)
    {
        var builds = Library(kind);
        var keep = s.KeepBuildsFor(kind);
        var reasons = ProtectionReasons(kind, s, manifests, gameNames).ToLookup(p => p.Id, p => p.Reason, StringComparer.OrdinalIgnoreCase);
        var kept = BuildCleanup.BeyondKeep(builds, keep).Where(b => reasons.Contains(b.Id))
            .Select(b => new KeptBuild(b, reasons[b.Id].ToList())).ToList();
        string? perSource = null;
        if (kind == LibraryKind.OptiScaler && builds.Count > keep && builds.Select(b => b.Group).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            perSource = string.Join(", ", builds.GroupBy(b => b.Group, StringComparer.OrdinalIgnoreCase).Select(g => $"{OptiSources.For(g.Key).Name} {g.Count()}"));
        return new LibraryKeepNote(kept, perSource);
    }

    public Dictionary<LibraryKind, LibraryKeepNote> KeepNotes(AppSettings s, IEnumerable<InstallManifest> manifests, IReadOnlyDictionary<string, string>? gameNames)
    {
        var records = manifests.ToList();
        return Enum.GetValues<LibraryKind>().ToDictionary(k => k, k => KeepNote(k, s, records, gameNames));
    }

    /// <summary>
    /// What the cleanup deletes in one library, under the keep count of the settings, keeping <paramref name="keep"/> too.
    /// Imports count toward the keep count either way; <paramref name="downloadsOnly"/> (the automatic cleanup) never deletes one.
    /// </summary>
    public List<LibraryBuild> CleanupCandidates(LibraryKind kind, AppSettings s, IEnumerable<InstallManifest> manifests,
        IEnumerable<string>? keep = null, bool downloadsOnly = false)
    {
        var ids = ProtectedBuilds(kind, s, manifests);
        if (keep is not null) ids.UnionWith(keep);
        var delete = BuildCleanup.Select(Library(kind), s.KeepBuildsFor(kind), ids);
        return downloadsOnly ? delete.Where(b => b.Downloaded).ToList() : delete;
    }

    /// <summary>
    /// Everything the cleanup would delete now: old builds of every library and stale download leftovers, with sizes.
    /// <paramref name="keep"/> are ids that stay as well (the builds just downloaded); libraries in <paramref name="skip"/> are left alone.
    /// <paramref name="downloadsOnly"/>: the automatic cleanup, which leaves every imported build alone.
    /// </summary>
    public List<CleanupItem> PlanCleanup(AppSettings s, IReadOnlyCollection<InstallManifest> manifests,
        IReadOnlySet<string>? keep = null, IReadOnlySet<LibraryKind>? skip = null, bool downloadsOnly = false)
    {
        var items = new List<CleanupItem>();
        foreach (var kind in Enum.GetValues<LibraryKind>().Where(k => skip?.Contains(k) != true))
            foreach (var b in CleanupCandidates(kind, s, manifests, keep, downloadsOnly))
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
        LibraryKind.Dfc => "Deep Fried Chicken",
        LibraryKind.DlssNr => "NR runtime",
        LibraryKind.ReShade => "ReShade",
        _ => ComponentRegistry.OfLibrary(kind).FirstOrDefault()?.Name ?? kind.ToString(),
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
        foreach (var kind in Enum.GetValues<AddonKind>()) ForgetAddons(kind);
        ForgetHdr();
        ForgetReShade();
        if (count > 0) Log.Info($"Cleanup freed {BuildCleanup.FormatBytes(freed)} ({count} item(s))");
        return freed;
    }
}
