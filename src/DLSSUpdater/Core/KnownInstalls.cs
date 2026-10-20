using System.Text.Json;

namespace DLSSUpdater.Core;

/// <summary>The builds one install record names: all the cleanup needs to keep them while the game can't be read.</summary>
public sealed record InstallRefs
{
    public string? OptiTag { get; set; }
    public string? UpliftTag { get; set; }
    public string? DlssNrId { get; set; }
    public string? PreviousDlssNrId { get; set; }
    public string? RenoDxId { get; set; }
    public string? RenoDxTag { get; set; }
    public string? LecramId { get; set; }
    public string? LecramTag { get; set; }
    public string? MfgTag { get; set; }
    public string? DlssTag { get; set; }
    public string? StreamlineTag { get; set; }

    public static InstallRefs From(InstallManifest m) => new()
    {
        OptiTag = m.OptiTag, UpliftTag = m.UpliftTag, DlssNrId = m.DlssNrId, PreviousDlssNrId = m.PreviousDlssNrId,
        RenoDxId = m.RenoDxId, RenoDxTag = m.RenoDxTag, LecramId = m.LecramId, LecramTag = m.LecramTag,
        MfgTag = m.MfgTag, DlssTag = m.DlssTag, StreamlineTag = m.StreamlineTag,
    };

    /// <summary>A record holding only the references, which is all <see cref="ComponentStore.PlanCleanup"/> reads.</summary>
    public InstallManifest ToManifest() => new()
    {
        OptiTag = OptiTag, UpliftTag = UpliftTag, DlssNrId = DlssNrId, PreviousDlssNrId = PreviousDlssNrId,
        RenoDxId = RenoDxId, RenoDxTag = RenoDxTag, LecramId = LecramId, LecramTag = LecramTag,
        MfgTag = MfgTag, DlssTag = DlssTag, StreamlineTag = StreamlineTag,
    };

    /// <summary>The libraries these references point into.</summary>
    public IEnumerable<LibraryKind> Libraries()
    {
        if (OptiTag is not null) yield return LibraryKind.OptiScaler;
        if (UpliftTag is not null) yield return LibraryKind.Uplift;
        if (DlssNrId is not null || PreviousDlssNrId is not null) yield return LibraryKind.DlssNr;
        if (RenoDxId is not null || RenoDxTag is not null) yield return LibraryKind.RenoDx;
        if (LecramId is not null || LecramTag is not null) yield return LibraryKind.Lecram;
    }
}

/// <summary>
/// What the cleanup protects: the install records it read, plus the remembered references of installs it can't read now.
/// <see cref="Unsure"/> are the libraries such an install points into, which the automatic cleanup leaves alone.
/// <see cref="Unknown"/>: a record can't be read and nothing is remembered for it, so no cleanup may run.
/// </summary>
public sealed record CleanupRecords(List<InstallManifest> Records, HashSet<LibraryKind> Unsure, bool Unknown);

/// <summary>
/// Remembers, per install folder, the builds its record names (install-refs.json). Games on an unplugged drive or hidden
/// from the list still keep their builds: an entry stays until the folder is reachable with no record in it (uninstalled),
/// or the app uninstalls the game or the user removes it from the app.
/// </summary>
public static class KnownInstalls
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.Root, "install-refs.json");

    public static Dictionary<string, InstallRefs> Load()
    {
        var known = new Dictionary<string, InstallRefs>(StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return known;
                var saved = JsonSerializer.Deserialize(File.ReadAllText(FilePath), JsonCtx.Default.DictionaryStringInstallRefs) ?? [];
                foreach (var (dir, refs) in saved)
                    if (!string.IsNullOrWhiteSpace(dir) && refs is not null) known[FileUtil.Normalize(dir)] = refs;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
            { Log.Error($"Could not read {FilePath}", ex); }
        }
        return known;
    }

    private static void Save(Dictionary<string, InstallRefs> known) =>
        FileUtil.AtomicWriteText(FilePath, JsonSerializer.Serialize(known, JsonCtx.Default.DictionaryStringInstallRefs));

    /// <summary>The app uninstalled these folders, or the user removed their game from the app.</summary>
    public static void Forget(IEnumerable<string> dirs)
    {
        lock (Gate)
        {
            var known = Load();
            var removed = dirs.Count(d => known.Remove(FileUtil.Normalize(d)));
            if (removed > 0) Save(known);
        }
    }

    /// <summary>
    /// Reads the records in <paramref name="dirs"/> (the scanned games' install folders) and in every remembered folder,
    /// remembers what each one names, and falls back to the remembered references for a folder that is not
    /// <paramref name="reachable"/> or whose record can't be read.
    /// </summary>
    public static CleanupRecords Gather(IEnumerable<string> dirs, Func<string, InstallManifest?> read, Func<string, bool> reachable)
    {
        lock (Gate)
        {
            var known = Load();
            var records = new List<InstallManifest>();
            var unsure = new HashSet<LibraryKind>();
            var unknown = false;
            var changed = false;
            foreach (var dir in dirs.Select(FileUtil.Normalize).Concat(known.Keys.ToList()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                known.TryGetValue(dir, out var refs);
                void Remembered()
                {
                    if (refs is null) return;
                    records.Add(refs.ToManifest());
                    unsure.UnionWith(refs.Libraries());
                }
                if (!reachable(dir))
                {
                    Remembered();
                    continue;
                }
                InstallManifest? m;
                try { m = read(dir); }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    Log.Error($"The install record in {dir} can't be read{(refs is null ? "" : "; the builds it named last time stay")}", ex);
                    if (refs is null) unknown = true;
                    Remembered();
                    continue;
                }
                if (m is null)
                {
                    changed |= known.Remove(dir);
                    continue;
                }
                records.Add(m);
                var now = InstallRefs.From(m);
                if (now != refs)
                {
                    known[dir] = now;
                    changed = true;
                }
            }
            if (changed)
            {
                try { Save(known); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Log.Error($"Could not save {FilePath}", ex); }
            }
            return new CleanupRecords(records, unsure, unknown);
        }
    }
}
