using System.Runtime.InteropServices;
using System.Text;
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
    public string? DfcId { get; set; }
    public string? DfcTag { get; set; }
    public string? MfgTag { get; set; }
    public string? DlssTag { get; set; }
    public string? StreamlineTag { get; set; }
    /// <summary>Serial number of the volume the install folder was last read from; tells a deleted folder from another device on the same drive letter.</summary>
    public uint? VolumeSerial { get; set; }

    public static InstallRefs From(InstallManifest m) => new()
    {
        OptiTag = m.OptiTag, UpliftTag = m.UpliftTag, DlssNrId = m.DlssNrId, PreviousDlssNrId = m.PreviousDlssNrId,
        RenoDxId = m.RenoDxId, RenoDxTag = m.RenoDxTag, LecramId = m.LecramId, LecramTag = m.LecramTag, DfcId = m.DfcId, DfcTag = m.DfcTag,
        MfgTag = m.MfgTag, DlssTag = m.DlssTag, StreamlineTag = m.StreamlineTag,
    };

    /// <summary>A record holding only the references, which is all <see cref="ComponentStore.PlanCleanup"/> reads.</summary>
    public InstallManifest ToManifest(string dir) => new()
    {
        Dir = dir, OptiTag = OptiTag, UpliftTag = UpliftTag, DlssNrId = DlssNrId, PreviousDlssNrId = PreviousDlssNrId,
        RenoDxId = RenoDxId, RenoDxTag = RenoDxTag, LecramId = LecramId, LecramTag = LecramTag, DfcId = DfcId, DfcTag = DfcTag,
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
        if (DfcId is not null || DfcTag is not null) yield return LibraryKind.Dfc;
    }
}

/// <summary>
/// What the cleanup protects: the install records it read, plus the remembered references of installs it can't read now.
/// <see cref="Unsure"/> are the libraries such an install points into, which the automatic cleanup leaves alone;
/// <see cref="Unreachable"/> and <see cref="Unreadable"/> count the installs behind it.
/// <see cref="Unknown"/>: a record or install-refs.json can't be read and nothing is remembered for it, so no cleanup may run.
/// </summary>
public sealed record CleanupRecords(List<InstallManifest> Records, HashSet<LibraryKind> Unsure, bool Unknown, int Unreachable = 0, int Unreadable = 0)
{
    /// <summary>One short line for Settings: which libraries the automatic cleanup skips and why; null when it skips none.</summary>
    public string? AutoSkipNote()
    {
        if (Unsure.Count == 0) return null;
        var why = new List<string>();
        if (Unreachable > 0) why.Add($"{Unreachable} game folder{(Unreachable == 1 ? "" : "s")} with installs can't be reached (drive not connected?)");
        if (Unreadable > 0) why.Add($"{Unreadable} install record{(Unreadable == 1 ? "" : "s")} can't be read");
        var libraries = string.Join(", ", Enum.GetValues<LibraryKind>().Where(Unsure.Contains).Select(ComponentStore.LibraryTitle));
        return $"Automatic cleanup skips {libraries}: {string.Join(" and ", why)}.";
    }
}

/// <summary>Where an install folder stands: readable, on a drive that isn't there now, or deleted.</summary>
public enum FolderState { Here, Unplugged, Gone }

/// <summary>
/// Remembers, per install folder, the builds its record names (install-refs.json). Games on an unplugged drive or hidden
/// from the list still keep their builds: an entry stays until the folder is reachable with no record in it or deleted
/// while its drive is there (uninstalled), or the app uninstalls the game or the user removes it from the app.
/// </summary>
public static class KnownInstalls
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.Root, "install-refs.json");

    /// <summary>
    /// A missing folder counts as deleted only when it is reported as not found and the volume it sits on is the one it was last
    /// read from (same serial). A different or unknown volume (a reused drive letter, an offline folder mount), a failed lookup,
    /// no stored serial, or a folder that can't be accessed all count as unplugged, so the remembered builds stay.
    /// </summary>
    public static FolderState Probe(string dir, uint? storedSerial) => Probe(dir, storedSerial, VolumeSerial);

    public static FolderState Probe(string dir, uint? storedSerial, Func<string, uint?> volumeSerial)
    {
        if (Directory.Exists(dir)) return FolderState.Here;
        if (storedSerial is null) return FolderState.Unplugged;
        try { File.GetAttributes(dir); return FolderState.Unplugged; }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException) { }
        catch (Exception) { return FolderState.Unplugged; }
        uint? now;
        try { now = volumeSerial(dir); }
        catch (Exception) { return FolderState.Unplugged; }
        return now == storedSerial ? FolderState.Gone : FolderState.Unplugged;
    }

    /// <summary>The serial number of the volume mounted at the folder's real mount point (drive letter, folder mount or share); null when it can't be told.</summary>
    public static uint? VolumeSerial(string dir)
    {
        var mount = new StringBuilder(1024);
        if (!GetVolumePathName(Path.GetFullPath(dir), mount, (uint)mount.Capacity)) return null;
        return GetVolumeInformation(mount.ToString(), null, 0, out var serial, out _, out _, null, 0) ? serial : null;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetVolumePathName(string path, StringBuilder volumePath, uint length);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetVolumeInformation(string root, StringBuilder? name, uint nameSize, out uint serial,
        out uint maxComponent, out uint flags, StringBuilder? fileSystem, uint fileSystemSize);

    /// <summary>The remembered references; false when install-refs.json exists but can't be read (nothing may overwrite it then).</summary>
    private static bool TryLoad(out Dictionary<string, InstallRefs> known)
    {
        known = new Dictionary<string, InstallRefs>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(FilePath)) return true;
            var saved = JsonSerializer.Deserialize(File.ReadAllText(FilePath), JsonCtx.Default.DictionaryStringInstallRefs) ?? [];
            foreach (var (dir, refs) in saved)
                if (!string.IsNullOrWhiteSpace(dir) && refs is not null) known[FileUtil.Normalize(dir)] = refs;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Error($"Could not read {FilePath}; no cleanup runs until it can be read", ex);
            return false;
        }
    }

    private static void Save(Dictionary<string, InstallRefs> known)
    {
        try { FileUtil.AtomicWriteText(FilePath, JsonSerializer.Serialize(known, JsonCtx.Default.DictionaryStringInstallRefs)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log.Error($"Could not save {FilePath}", ex); }
    }

    /// <summary>The app uninstalled these folders, or the user removed their game from the app. Never throws; a file it can't read stays as it is.</summary>
    public static void Forget(IEnumerable<string> dirs)
    {
        lock (Gate)
        {
            if (!TryLoad(out var known)) return;
            var removed = dirs.Count(d => known.Remove(FileUtil.Normalize(d)));
            if (removed > 0) Save(known);
        }
    }

    /// <summary>
    /// Reads the records in <paramref name="dirs"/> (the scanned games' install folders) and in every remembered folder,
    /// remembers what each one names, and falls back to the remembered references for a folder that is
    /// <see cref="FolderState.Unplugged"/> or whose record can't be read. A <see cref="FolderState.Gone"/> folder is forgotten.
    /// Before install-refs.json exists, <paramref name="firstRun"/> adds the folders the scan doesn't list (hidden games).
    /// </summary>
    public static CleanupRecords Gather(IEnumerable<string> dirs, Func<string, InstallManifest?> read, Func<string, uint?, FolderState> probe,
        Func<IEnumerable<string>>? firstRun = null, Func<string, uint?>? volumeSerial = null)
    {
        lock (Gate)
        {
            if (!TryLoad(out var known)) return new CleanupRecords([], [], true);
            var records = new List<InstallManifest>();
            var unsure = new HashSet<LibraryKind>();
            var (unknown, unreachable, unreadable) = (false, 0, 0);
            var fresh = !File.Exists(FilePath);
            var changed = fresh;
            var all = dirs.Select(FileUtil.Normalize).Concat(known.Keys.ToList());
            if (fresh && firstRun is not null)
            {
                try { all = all.Concat(firstRun().Select(FileUtil.Normalize).ToList()); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Error("Could not look up the install folders of hidden games; no cleanup runs this time", ex);
                    return new CleanupRecords([], [], true);
                }
            }
            foreach (var dir in all.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            {
                known.TryGetValue(dir, out var refs);
                void Remembered()
                {
                    if (refs is null) return;
                    records.Add(refs.ToManifest(dir));
                    unsure.UnionWith(refs.Libraries());
                }
                switch (probe(dir, refs?.VolumeSerial))
                {
                    case FolderState.Gone:
                        changed |= known.Remove(dir);
                        continue;
                    case FolderState.Unplugged:
                        if (refs is not null) unreachable++;
                        Remembered();
                        continue;
                }
                InstallManifest? m;
                try { m = read(dir); }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    Log.Error($"The install record in {dir} can't be read{(refs is null ? "" : "; the builds it named last time stay")}", ex);
                    if (refs is null) unknown = true;
                    else unreadable++;
                    Remembered();
                    continue;
                }
                if (m is null)
                {
                    changed |= known.Remove(dir);
                    continue;
                }
                m.Dir = dir;
                records.Add(m);
                uint? serial = null;
                try { serial = (volumeSerial ?? VolumeSerial)(dir); }
                catch (Exception ex) { Log.Error($"Could not tell which volume {dir} is on", ex); }
                var now = InstallRefs.From(m) with { VolumeSerial = serial ?? refs?.VolumeSerial };
                if (now != refs)
                {
                    known[dir] = now;
                    changed = true;
                }
            }
            // A first run with an unknown record saves nothing, so the lookup of hidden games runs again next time.
            if (changed && !(fresh && unknown)) Save(known);
            return new CleanupRecords(records, unsure, unknown, unreachable, unreadable);
        }
    }
}
