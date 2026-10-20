using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

/// <summary>The import-only add-ons that keep a library of builds, like the NR library does for nvngx_dlssnr.dll.</summary>
public enum AddonKind { RenoDx, Lecram, Dfc }

/// <summary>
/// One imported build of ShortFuse's or Lecram's add-on or of Deep Fried Chicken, stored under
/// components\&lt;add-on dir&gt;\&lt;sha256&gt;\&lt;FileName&gt; with this record next to it as version.json. <see cref="FileName"/> is the
/// name the add-on is installed under. A Deep Fried Chicken build's folder holds its whole 64-bit set beside the add-on.
/// <see cref="Downloaded"/>: the app fetched it from Lecram's channel; false for every import and for builds from before the flag.
/// </summary>
public sealed record AddonBuild(
    string Id,
    string FileName,
    string FileVersion,
    LecramGeneration? Generation,
    string DetectedLabel,
    string? UserLabel,
    DateTime Imported,
    bool Downloaded = false)
{
    /// <summary>The name this build goes by: the user's own label, else what was detected in the file.</summary>
    [JsonIgnore]
    public string Name => string.IsNullOrWhiteSpace(UserLabel) ? DetectedLabel : UserLabel.Trim();

    /// <summary>What lists show: the user's label, else "&lt;detected label&gt; · &lt;file version&gt; (&lt;import date&gt;)".</summary>
    [JsonIgnore]
    public string Label
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(UserLabel)) return UserLabel.Trim();
            var version = FileVersion.Length > 0 && FileVersion != DetectedLabel ? $" · {FileVersion}" : "";
            return $"{DetectedLabel}{version} ({Imported.ToLocalTime():yyyy-MM-dd})";
        }
    }

    /// <summary>"{detected label}-{sha[..12]}", what an install records and the Installed column shows.</summary>
    [JsonIgnore]
    public string Tag => $"{DetectedLabel}-{Id[..12]}";

    /// <summary>Sort key (<see cref="BuildOrder.Key"/>): the detected label's version ("v10.7.0", or ShortFuse's file version), a pre-release before its release.</summary>
    [JsonIgnore]
    public (Version Version, bool Release, int Stage, int Number) Order => BuildOrder.Key(DetectedLabel, ParsedVersion);

    /// <summary>The file version as a <see cref="Version"/>, or null for a build without a version resource.</summary>
    [JsonIgnore]
    public Version? ParsedVersion => Version.TryParse(FileVersion, out var v) ? v : null;

    /// <summary>True when an install recorded under <paramref name="id"/> / <paramref name="tag"/> is this build; manifests from before the library only have the tag.</summary>
    public bool Matches(string? id, string? tag) =>
        id is not null ? id.Equals(Id, StringComparison.OrdinalIgnoreCase)
        : tag is not null && tag.EndsWith("-" + Id[..12], StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Label;
}

public enum AddonPairStatus { Untested, Works, Crashed }

/// <summary>A test result for two exact builds.</summary>
public sealed record AddonPairTest(string RenoDxId, string LecramId, AddonPairStatus Status = AddonPairStatus.Untested, DateTime? Marked = null)
{
    public bool Matches(string sf, string lecram) =>
        string.Equals(RenoDxId, sf, StringComparison.OrdinalIgnoreCase) && string.Equals(LecramId, lecram, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A library build together with the file an install copies.</summary>
public sealed record AddonFile(AddonBuild Build, string Path);

public sealed partial class ComponentStore
{
    private const string RenoDxDir = "renodx-dlss";
    private const string LecramDir = "lecram-dlss5";
    /// <summary>The <see cref="AddonBuild"/> record in each build folder.</summary>
    private const string BuildRecordFile = "version.json";

    /// <summary>Build id the settings choose as the default; null follows the newest import.</summary>
    public Func<string?> SelectedRenoDx { get; set; } = () => null;
    public Func<string?> SelectedLecram { get; set; } = () => null;
    public Func<string?> SelectedDfc { get; set; } = () => null;

    public Func<IReadOnlyList<AddonPairTest>> PairTests { get; set; } = () => [];
    public event Action? PairStatusChanged;

    public AddonPairTest? PairTestFor(string? renoDxId, string? lecramId)
    {
        var sf = RenoDxBuildFor(renoDxId);
        var lecram = LecramBuildFor(lecramId);
        return sf is null || lecram is null ? null : FindPairTest(PairTests(), sf.Id, lecram.Id);
    }

    private static AddonPairTest? FindPairTest(IEnumerable<AddonPairTest> tests, string sf, string lecram) =>
        tests.FirstOrDefault(t => t.Matches(sf, lecram));

    /// <summary>Records the result against the resolved build ids, so default selections never become moving targets.</summary>
    public void MarkAddonPair(AppSettings settings, string? renoDxId, string? lecramId, AddonPairStatus status)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        var sf = RenoDxBuildFor(renoDxId) ?? throw new FileNotFoundException("The ShortFuse build is no longer in the library.");
        var lecram = LecramBuildFor(lecramId) ?? throw new FileNotFoundException("The Lecram build is no longer in the library.");
        var result = new AddonPairTest(sf.Id, lecram.Id, status, DateTime.UtcNow);
        var tests = settings.AddonPairTests.Where(t => !t.Matches(sf.Id, lecram.Id)).ToList();
        tests.Add(result);
        // Persist before publishing the new result, so a failed write leaves the displayed status unchanged.
        var previous = settings.AddonPairTests;
        settings.AddonPairTests = tests;
        try { FileUtil.AtomicWriteText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, JsonCtx.Default.AppSettings)); }
        catch
        {
            settings.AddonPairTests = previous;
            throw;
        }
        PairStatusChanged?.Invoke();
    }

    public string KnownWorkingPairs => string.Join("; ", PairTests().Where(t => t.Status == AddonPairStatus.Works)
        .Select(t => (Sf: RenoDxBuildFor(t.RenoDxId), Lecram: LecramBuildFor(t.LecramId)))
        .Where(p => p.Sf is not null && p.Lecram is not null)
        .Select(p => $"ShortFuse {p.Sf!.Label} + Lecram {p.Lecram!.Label}"));

    private static string AddonRoot(AddonKind kind) => kind switch
    {
        AddonKind.RenoDx => Path.Combine(AppPaths.Components, RenoDxDir),
        AddonKind.Lecram => Path.Combine(AppPaths.Components, LecramDir),
        _ => DfcDir,
    };

    public IReadOnlyList<AddonBuild> RenoDxBuilds => AddonBuilds(AddonKind.RenoDx);
    public IReadOnlyList<AddonBuild> LecramBuilds => AddonBuilds(AddonKind.Lecram);
    public IReadOnlyList<AddonBuild> DfcBuilds => AddonBuilds(AddonKind.Dfc);
    public bool RenoDxImported => RenoDxBuilds.Count > 0;
    public bool LecramImported => LecramBuilds.Count > 0;

    private readonly Dictionary<AddonKind, (string Root, IReadOnlyList<AddonBuild> Builds)> _addonCache = [];

    /// <summary>Every readable build of one add-on, newest build first (<see cref="BuildOrder"/>). Read once; imports, renames and removals through this store refresh it.</summary>
    public IReadOnlyList<AddonBuild> AddonBuilds(AddonKind kind)
    {
        var root = AddonRoot(kind);
        lock (_addonCache)
        {
            if (_addonCache.TryGetValue(kind, out var cached) && cached.Root == root) return cached.Builds;
            var builds = ReadAddonBuilds(kind, root);
            _addonCache[kind] = (root, builds);
            return builds;
        }
    }

    private void ForgetAddons(AddonKind kind)
    {
        lock (_addonCache) _addonCache.Remove(kind);
    }

    private static IReadOnlyList<AddonBuild> ReadAddonBuilds(AddonKind kind, string root)
    {
        if (!Directory.Exists(root)) return [];
        var builds = new List<AddonBuild>();
        foreach (var file in Directory.EnumerateFiles(root, BuildRecordFile, SearchOption.AllDirectories))
        {
            try
            {
                var b = JsonSerializer.Deserialize(File.ReadAllText(file), JsonCtx.Default.AddonBuild);
                if (b is not null && ValidId(b.Id) && Path.GetFileName(Path.GetDirectoryName(file)) == b.Id
                    && ValidAddonFile(kind, b.FileName) && File.Exists(Path.Combine(root, b.Id, b.FileName))
                    && (kind != AddonKind.Dfc || File.Exists(Path.Combine(root, b.Id, DfcBridgeFile)))) builds.Add(b);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            { Log.Error($"Could not read add-on library entry {file}", ex); }
        }
        return BuildOrder.NewestFirst(builds, b => b.DetectedLabel, b => b.Imported, b => b.Id, b => b.ParsedVersion);
    }

    private static bool ValidAddonFile(AddonKind kind, string name) => kind switch
    {
        AddonKind.RenoDx => name == RenoDxFile,
        AddonKind.Lecram => LecramFiles.Contains(name),
        _ => name == DfcFile,
    };

    public string? DefaultAddonId(AddonKind kind) => kind switch
    {
        AddonKind.RenoDx => SelectedRenoDx(),
        AddonKind.Lecram => SelectedLecram(),
        _ => SelectedDfc(),
    };

    /// <summary>
    /// The build an id names. A given id must exist; no id follows the default of the settings, and falls back to the newest
    /// build when that default is unset or gone. Null when nothing matches.
    /// </summary>
    public AddonBuild? AddonBuildFor(AddonKind kind, string? id)
    {
        var builds = AddonBuilds(kind);
        if (id is not null) return builds.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var chosen = DefaultAddonId(kind);
        return builds.FirstOrDefault(b => chosen is not null && b.Id.Equals(chosen, StringComparison.OrdinalIgnoreCase)) ?? builds.FirstOrDefault();
    }

    public AddonBuild? RenoDxBuildFor(string? id) => AddonBuildFor(AddonKind.RenoDx, id);
    public AddonBuild? LecramBuildFor(string? id) => AddonBuildFor(AddonKind.Lecram, id);
    public AddonBuild? DfcBuildFor(string? id) => AddonBuildFor(AddonKind.Dfc, id);

    public static string AddonPathOf(AddonKind kind, AddonBuild build) => Path.Combine(AddonRoot(kind), build.Id, build.FileName);

    /// <summary>The chosen build and its file for an install; a missing or changed file is a clear error.</summary>
    internal AddonFile RequireAddon(AddonKind kind, string? id)
    {
        var what = kind switch
        {
            AddonKind.RenoDx => RenoDxFile,
            AddonKind.Lecram => $"Lecram's {Lecram10File} / {Lecram11File}",
            _ => DfcFile,
        };
        var build = AddonBuildFor(kind, id)
                    ?? throw new FileNotFoundException(id is null
                        ? $"Import {what} in Settings → Local components first."
                        : $"The chosen {what} build is no longer in the library. Import it again or pick another version.");
        var path = AddonPathOf(kind, build);
        if (!File.Exists(path)) throw new FileNotFoundException($"Import {what} in Settings → Local components first.", path);
        if (!HashCache.Get(path).Equals(build.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The stored add-on file changed. Import it again.");
        return new AddonFile(build, path);
    }

    /// <summary>
    /// Stores <paramref name="source"/> as a library build. A file that is already there changes nothing (the user's label and
    /// import date stay), except that importing a downloaded build by hand makes it an import; other builds are never touched.
    /// </summary>
    private AddonBuild RetainAddon(AddonKind kind, string source, string fileName, string fileVersion, LecramGeneration? generation, string detectedLabel,
        bool downloaded = false)
    {
        var sha = FileUtil.Sha256(source);
        var dest = Path.Combine(AddonRoot(kind), sha, fileName);
        var meta = Path.Combine(AddonRoot(kind), sha, BuildRecordFile);
        if (downloaded) NoteAdded(sha);
        var existing = AddonBuilds(kind).FirstOrDefault(b => b.Id == sha && b.FileName == fileName);
        if (existing is not null)
        {
            if (downloaded || !existing.Downloaded) return existing;
            existing = existing with { Downloaded = false };
            FileUtil.AtomicWriteText(meta, JsonSerializer.Serialize(existing, JsonCtx.Default.AddonBuild));
            ForgetAddons(kind);
            return existing;
        }
        var build = new AddonBuild(sha, fileName, fileVersion, generation, detectedLabel, null, DateTime.UtcNow, downloaded);
        if (!File.Exists(dest) || !FileUtil.SameFile(source, dest)) FileUtil.AtomicCopy(source, dest);
        FileUtil.AtomicWriteText(meta, JsonSerializer.Serialize(build, JsonCtx.Default.AddonBuild));
        ForgetAddons(kind);
        return build;
    }

    /// <summary>Sets the user's own name for a build; empty text goes back to the detected label.</summary>
    public AddonBuild RenameAddon(AddonKind kind, string id, string? label)
    {
        var build = AddonBuildFor(kind, id) ?? throw new FileNotFoundException("That add-on build is no longer in the library.");
        var renamed = build with { UserLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim() };
        FileUtil.AtomicWriteText(Path.Combine(AddonRoot(kind), id, BuildRecordFile), JsonSerializer.Serialize(renamed, JsonCtx.Default.AddonBuild));
        ForgetAddons(kind);
        return renamed;
    }

    /// <summary>Deletes one build from the library. Games keep the copy they already have.</summary>
    public void RemoveAddon(AddonKind kind, string id)
    {
        if (!ValidId(id)) throw new InvalidDataException("Invalid add-on build id.");
        var dir = Path.Combine(AddonRoot(kind), id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ForgetAddons(kind);
    }

    /// <summary>
    /// First run after the library arrived: the single files of the old layout move into it, an unreadable one stays where it is.
    /// v10 goes in before v11, so v11 is the newer import when both were there.
    /// </summary>
    public void MigrateAddons()
    {
        MigrateLegacy(Path.Combine(AppPaths.Components, RenoDxFile), ImportRenoDx);
        MigrateLegacy(Path.Combine(AppPaths.Components, Lecram10File), ImportLecram);
        MigrateLegacy(Path.Combine(AppPaths.Components, Lecram11File), ImportLecram);
    }

    private static void MigrateLegacy(string path, Func<string, AddonBuild> import)
    {
        if (!File.Exists(path)) return;
        try
        {
            var build = import(path);
            File.Delete(path);
            Log.Info($"Moved {Path.GetFileName(path)} into the add-on library as {build.Label}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { Log.Error($"Could not move {path} into the add-on library", ex); }
    }

    /// <summary>
    /// A default that is unset or points at a removed build follows the newest build (after a migration that is the migrated file).
    /// ShortFuse's default is written down as an id; Lecram's and Deep Fried Chicken's stay unset ("Newest"), a removed pin
    /// goes back to unset. Returns true when the settings changed.
    /// </summary>
    public bool EnsureAddonDefaults(AppSettings s)
    {
        var changed = false;
        foreach (var kind in Enum.GetValues<AddonKind>())
        {
            var builds = AddonBuilds(kind);
            var chosen = s.AddonId(kind);
            if (FollowsNewest(kind))
            {
                if (chosen is null || builds.Any(b => b.Id == chosen)) continue;
                s.SetAddonId(kind, null);
                changed = true;
                continue;
            }
            if (builds.Count == 0 || (chosen is not null && builds.Any(b => b.Id == chosen))) continue;
            s.SetAddonId(kind, builds[0].Id);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// An imported build becomes the default only when it is newer than the current default, so importing an older build
    /// to keep it around doesn't silently downgrade every game. Lecram and Deep Fried Chicken on "Newest" (no pinned id) follow
    /// the library by themselves. Returns true when the default changed.
    /// </summary>
    public bool AdoptImportedAddon(AppSettings s, AddonKind kind, AddonBuild imported)
    {
        if (FollowsNewest(kind) && s.AddonId(kind) is null) return false;
        var current = AddonBuilds(kind).FirstOrDefault(b => b.Id == s.AddonId(kind));
        if (current is not null && imported.Order.CompareTo(current.Order) <= 0) return false;
        s.SetAddonId(kind, imported.Id);
        return true;
    }

    /// <summary>Add-ons whose default may be "Newest" (no pinned id); ShortFuse's default is always a pinned build.</summary>
    public static bool FollowsNewest(AddonKind kind) => kind != AddonKind.RenoDx;

    /// <summary>Records the tested 0928 build pair as working when both builds are imported.</summary>
    public bool SeedTestedAddonPair(AppSettings s)
    {
        var sf = RenoDxBuilds.FirstOrDefault(b => b.ParsedVersion is { Major: 0, Minor: 2026, Build: 928 });
        var lecram = LecramBuilds.FirstOrDefault(b => b.DetectedLabel.StartsWith("v8.5.0-rc10", StringComparison.OrdinalIgnoreCase));
        if (sf is null || lecram is null || FindPairTest(s.AddonPairTests, sf.Id, lecram.Id) is not null) return false;
        s.AddonPairTests.Add(new AddonPairTest(sf.Id, lecram.Id, AddonPairStatus.Works));
        return true;
    }

    // ---------- build label ----------

    /// <summary>
    /// The build label of a Lecram add-on: the highest <c>v1.2.3</c> / <c>v1.2.3-pre</c> string in the binary. The RenoDX
    /// framework in it carries v6.1.0 / v6.1.2 strings, which lose by being lower; on a tie the one with a pre-release suffix wins,
    /// then the most frequent, then the shorter. Null when the binary has none.
    /// </summary>
    internal static string? FindBuildLabel(ReadOnlySpan<byte> data)
    {
        var found = new Dictionary<string, (long Rank, bool Pre, int Count)>();
        for (var offset = 0; offset < data.Length;)
        {
            var at = data[offset..].IndexOf((byte)'v');
            if (at < 0) break;
            var i = offset + at;
            offset = i + 1;
            if (SemverAt(data, i) is not { } s) continue;
            var rank = s.Major * 1_000_000_000_000L + s.Minor * 1_000_000L + s.Patch;
            var pre = s.Text.Contains('-');
            found[s.Text] = (rank, pre, found.TryGetValue(s.Text, out var old) ? old.Count + 1 : 1);
        }
        return found.Count == 0 ? null
            : found.OrderByDescending(f => f.Value.Rank).ThenByDescending(f => f.Value.Pre)
                .ThenByDescending(f => f.Value.Count).ThenBy(f => f.Key.Length).First().Key;
    }

    private static (string Text, long Major, long Minor, long Patch)? SemverAt(ReadOnlySpan<byte> data, int i)
    {
        var end = i + 1;
        Span<long> numbers = stackalloc long[3];
        for (var part = 0; part < 3; part++)
        {
            if (part > 0)
            {
                if (end >= data.Length || data[end] != (byte)'.') return null;
                end++;
            }
            var from = end;
            long n = 0;
            while (end < data.Length && data[end] is >= (byte)'0' and <= (byte)'9' && end - from < 7) n = n * 10 + (data[end++] - (byte)'0');
            if (end == from || (end < data.Length && data[end] is >= (byte)'0' and <= (byte)'9')) return null;
            numbers[part] = n;
        }
        var core = end;
        if (end < data.Length && data[end] == (byte)'-')
        {
            var from = ++end;
            while (end < data.Length && IsSuffixChar(data[end])) end++;
            var suffix = end;
            while (suffix > from && data[suffix - 1] is (byte)'.' or (byte)'-') suffix--;
            if (suffix > from) return (Encoding.ASCII.GetString(data[i..suffix]), numbers[0], numbers[1], numbers[2]);
        }
        return (Encoding.ASCII.GetString(data[i..core]), numbers[0], numbers[1], numbers[2]);
    }

    private static bool IsSuffixChar(byte b) =>
        b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9') or (byte)'.' or (byte)'-';
}
