using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

public sealed class UnsafeManifestException(string targetDir, string detail)
    : Exception($"DLSS5 Updater's install record in {targetDir} points outside the game and install folders ({detail}). Nothing was changed.");

public sealed class BackupEntry
{
    /// <summary>Original location, an entry as in <see cref="InstallManifest.Files"/>.</summary>
    public string Original { get; set; } = "";
    /// <summary>
    /// Backup location, relative to the backup folder. Records from 0ed78f7 may hold "&lt;original&gt;.N" as a full path
    /// instead (an install folder on another drive than the game).
    /// </summary>
    public string Backup { get; set; } = "";
    /// <summary>"dlss" for nvngx swaps, "file" for anything else we displaced.</summary>
    public string Kind { get; set; } = "file";
    /// <summary>Version we put in place, used to detect a game patch replacing our file with a new original.</summary>
    public string? InstalledSha { get; set; }
}

/// <summary>Lives in &lt;target&gt;\.dlssupdater\manifest.json and travels with the game folder.</summary>
public sealed class InstallManifest
{
    // Keeps the pre-rename name on purpose: every existing game install keeps its backups under this folder.
    public const string DirName = ".dlssupdater";

    /// <summary>
    /// 1: per-component fields only (up to 2.2.x). 2: adds <see cref="Components"/>; the per-component fields are still
    /// written for the components that use them, so 2.2.x can read, update and uninstall such a record (see docs/design.md).
    /// </summary>
    public const int CurrentSchema = 2;

    public int Schema { get; set; } = CurrentSchema;
    /// <summary>The install folder this record was read from; set by <see cref="KnownInstalls"/> for the cleanup's messages, never saved.</summary>
    [JsonIgnore]
    public string? Dir { get; set; }
    /// <summary>The install folder <see cref="Read"/> loaded this record from; never saved.</summary>
    [JsonIgnore]
    internal string? ReadFrom { get; set; }
    /// <summary>Game root relative to the target dir (e.g. "..\..\.."), or a full path when the two are on different drives.</summary>
    public string RootRel { get; set; } = ".";
    /// <summary>Proxy the game loads: OptiScaler's, or ReShade's in ReShade-only mode.</summary>
    public string? Proxy { get; set; }
    public InstallMode Mode { get; set; }

    public bool Opti { get; set; }
    public bool ReShade { get; set; }
    public bool Mfg { get; set; }
    public bool DlssNr { get; set; }
    public bool Dlss { get; set; }

    public string? OptiTag { get; set; }
    public string? MfgTag { get; set; }
    public string? DlssTag { get; set; }
    public bool DlssPinned { get; set; }
    public string? DlssNrSha { get; set; }
    public string? DlssNrId { get; set; }
    public string? PreviousDlssNrId { get; set; }
    public bool RenoDx { get; set; }
    public string? RenoDxTag { get; set; }
    /// <summary>Library id of the ShortFuse build placed; null in records from before the library.</summary>
    public string? RenoDxId { get; set; }
    public bool Lecram { get; set; }
    public string? LecramTag { get; set; }
    /// <summary>Library id of the Lecram build placed; null in records from before the library.</summary>
    public string? LecramId { get; set; }
    /// <summary>The generation's file we placed: renodx-dlss5.addon64 (v10) or dlss5-nr.addon64 (v11).</summary>
    public string? LecramFile { get; set; }
    public bool Uplift { get; set; }
    public string? UpliftTag { get; set; }
    /// <summary>NR placement relative to the target; null in older records means beside the executable.</summary>
    public string? DlssNrFile { get; set; }
    public bool Dfc { get; set; }
    public string? DfcTag { get; set; }
    /// <summary>Library id of the Deep Fried Chicken build placed; null in records from before the library.</summary>
    public string? DfcId { get; set; }
    /// <summary>
    /// Files of DFC's 64-bit set we placed (not the cfg), entries as in <see cref="Files"/>. Older records lack it; their
    /// files are found by <see cref="ComponentStore.IsDfcPackagePath"/>.
    /// </summary>
    public List<string> DfcFiles { get; set; } = [];
    /// <summary>Values we wrote into deep-fried-chicken.cfg last time, keyed "DFC/key".</summary>
    public Dictionary<string, string> DfcCfg { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// NVIDIA runtimes placed for DFC in a game without its own DLSS (nvngx_dlss.dll, and nvngx_dlssg.dll for its frame
    /// generation), entries as in <see cref="Files"/>. They leave with DFC.
    /// </summary>
    public List<string> DfcRuntimes { get; set; } = [];
    public string? ReShadeSha { get; set; }
    /// <summary>Library id of the ReShade build placed (its ReShade64.dll hash); null in records from before the library.</summary>
    public string? ReShadeId { get; set; }
    /// <summary>ReShade loads through the Vulkan implicit layer: no dll was placed in the game, only its ReShade.ini.</summary>
    public bool ReShadeVulkan { get; set; }
    public string? StreamlineTag { get; set; }
    public bool Streamline { get; set; }
    /// <summary>Values we wrote into OptiScaler.ini / ReShade.ini last time, keyed "Section/Key".</summary>
    public Dictionary<string, string> OptiIni { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ReShadeIni { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AntiCheatConfirmed { get; set; }
    /// <summary>The clashes (<see cref="Installer.ConflictKey"/>) the user accepted for what is installed; kept until the selection changes them.</summary>
    public string? ConfirmedConflict { get; set; }

    /// <summary>Files we placed: relative to the game root, or <see cref="InstallTag"/>\… relative to the install folder (see <see cref="Entry"/>).</summary>
    public List<string> Files { get; set; } = [];
    /// <summary>Folders we own, entries as in <see cref="Files"/>.</summary>
    public List<string> Dirs { get; set; } = [];
    public List<BackupEntry> Backups { get; set; } = [];
    /// <summary>
    /// Components recorded the generic way (<see cref="InstalledComponent"/>). Their files and backups are also in
    /// <see cref="Files"/> and <see cref="Backups"/>, so an older version that doesn't know this list still restores the originals.
    /// </summary>
    public List<InstalledComponent> Components { get; set; } = [];
    public DateTime Updated { get; set; }
    /// <summary>Fields this version doesn't know (written by a newer one); kept so saving doesn't drop them.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    public static string DirFor(string targetDir) => Path.Combine(targetDir, DirName);
    public static string PathFor(string targetDir) => Path.Combine(DirFor(targetDir), "manifest.json");
    public static string BackupDirFor(string targetDir) => Path.Combine(DirFor(targetDir), "backup");

    /// <summary>
    /// Game list label of what runs in the game: the loader (OptiScaler, or ReShade in ReShade-only mode) followed by the
    /// add-ons, e.g. "OptiScaler + MFG + Lecram"; null when no mod is installed (a DLSS swap only).
    /// </summary>
    public string? ModLabel()
    {
        var parts = new[]
        {
            Opti ? "OptiScaler" : Mode == InstallMode.ReShadeOnly && ReShade ? "ReShade" : null,
            RenoDx ? "ShortFuse" : null,
            Lecram ? "Lecram" : null,
            Dfc ? "DFC" : null,
            Uplift ? "Uplift" : null,
            Mfg ? "MFG" : null,
            Streamline ? "Streamline" : null,
        }.Concat(Components.Select(c => ComponentRegistry.Find(c.Id)?.Label)).OfType<string>().ToList();
        return parts.Count == 0 ? null : string.Join(" + ", parts);
    }

    /// <summary>For display: the manifest, or null when there is none or it can't be read.</summary>
    public static InstallManifest? Load(string targetDir)
    {
        try { return Read(targetDir); }
        catch (InvalidDataException ex)
        {
            Log.Error(ex.Message, ex.InnerException);
            return null;
        }
    }

    /// <summary>
    /// For changes: the manifest, or null when there is none. One that exists but can't be read throws, so an
    /// install never starts a fresh record over the original-file backups.
    /// </summary>
    public static InstallManifest? Read(string targetDir)
    {
        var path = PathFor(targetDir);
        if (!File.Exists(path)) return null;
        try
        {
            var m = JsonSerializer.Deserialize(File.ReadAllText(path), JsonCtx.Default.InstallManifest)
                    ?? throw new JsonException("empty manifest");
            m.Repair();
            m.ReadFrom = FileUtil.Normalize(targetDir);
            return m;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new InvalidDataException(
                $"DLSS5 Updater's install record {path} can't be read ({ex.Message}). Nothing was changed; retry, or restore that file.", ex);
        }
    }

    /// <summary>
    /// A hand-edited or damaged record may hold null for any list of <see cref="Components"/>; dictionaries come back
    /// case-sensitive from JSON. Both are put right so the rest of the code can rely on them.
    /// </summary>
    internal void Repair()
    {
        Components = (Components ?? []).Where(c => c is not null).ToList();
        foreach (var c in Components)
        {
            c.Id ??= "";
            c.Files = (c.Files ?? []).Where(f => f is not null).ToList();
            c.ConfigEdits = (c.ConfigEdits ?? []).Where(e => e is not null).ToList();
            c.Attributes = (c.Attributes ?? []).Where(a => a is not null).ToList();
            c.Backups = new Dictionary<string, string>(c.Backups ?? [], StringComparer.OrdinalIgnoreCase);
            c.Extras = new Dictionary<string, string>(c.Extras ?? [], StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>A component recorded the generic way, or null.</summary>
    public InstalledComponent? Component(string id) => Components.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every component installed here by id: the ones in <see cref="Components"/> and, for components still kept in the
    /// per-component fields, the view <see cref="LegacyComponents"/> builds of them.
    /// </summary>
    public IReadOnlyList<InstalledComponent> InstalledComponents() =>
        [.. Components, .. LegacyComponents.Project(this).Where(l => Component(l.Id) is null)];

    /// <summary>Starts an entry that is relative to the install folder. '|' can't occur in a Windows path, so no game file collides.</summary>
    public const string InstallTag = "|install|";

    /// <summary>
    /// Tags for config files outside the game that a component may edit (UE's Engine.ini lives in %LocalAppData%, many
    /// games keep their settings in Documents\My Games). Only config edits and attribute changes may use them.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownFolderTags = ["|localappdata|", "|documents|"];

    /// <summary>The folder behind a known-folder tag; tests point it at a temp folder.</summary>
    internal static Func<string, string?> KnownFolder { get; set; } = DefaultKnownFolder;

    internal static string? DefaultKnownFolder(string tag) => tag switch
    {
        "|localappdata|" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "|documents|" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        _ => null,
    };

    /// <summary>Config files a component may edit outside the game and install folders.</summary>
    private static readonly string[] ConfigExtensions = [".ini", ".cfg"];

    /// <summary>
    /// How a config file <paramref name="full"/> is recorded: as <see cref="Entry"/> does inside the game or install
    /// folder, otherwise behind the tag of the known folder it lies in (an .ini or .cfg only).
    /// </summary>
    public static string ConfigEntry(string full, string targetDir, string gameRoot)
    {
        if (FileUtil.IsUnder(full, gameRoot) || FileUtil.IsUnder(full, targetDir)) return Entry(full, targetDir, gameRoot);
        if (ConfigExtensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
            foreach (var tag in KnownFolderTags)
                if (KnownFolder(tag) is { Length: > 0 } dir && FileUtil.IsUnder(full, dir)
                    && !FileUtil.Normalize(full).Equals(FileUtil.Normalize(dir), StringComparison.OrdinalIgnoreCase))
                    return tag + Path.DirectorySeparatorChar + Path.GetRelativePath(dir, full);
        throw new UnsafeManifestException(targetDir, $"{full} is not a config file DLSS5 Updater may change");
    }

    /// <summary>
    /// How <paramref name="full"/> is recorded: relative to the game root when it lies in the game, otherwise relative to
    /// the install folder behind <see cref="InstallTag"/>. Unlike a path relative to the game root, that also works for an
    /// install folder on another drive or network share.
    /// </summary>
    public static string Entry(string full, string targetDir, string gameRoot)
    {
        if (FileUtil.IsUnder(full, gameRoot)) return Path.GetRelativePath(gameRoot, full);
        if (FileUtil.IsUnder(full, targetDir)) return InstallTag + Path.DirectorySeparatorChar + Path.GetRelativePath(targetDir, full);
        throw new UnsafeManifestException(targetDir, $"{full} is outside {gameRoot} and {targetDir}");
    }

    /// <summary>
    /// Where an entry points; <paramref name="gameRoot"/> is the root it was recorded against. Says nothing about whether
    /// that is a safe place: see <see cref="Contain"/>.
    /// </summary>
    /// <exception cref="UnsafeManifestException">The entry is not a path DLSS5 Updater writes.</exception>
    public static string Resolve(string entry, string targetDir, string gameRoot)
    {
        if (entry.StartsWith(InstallTag, StringComparison.Ordinal))
        {
            var rel = entry[InstallTag.Length..];
            if (rel.Length > 1 && rel[0] is '\\' or '/' && IsRelative(rel[1..])) return Path.GetFullPath(Path.Combine(targetDir, rel[1..]));
        }
        else if (KnownFolderTags.FirstOrDefault(t => entry.StartsWith(t, StringComparison.Ordinal)) is { } tag)
        {
            var rel = entry[tag.Length..];
            if (rel.Length > 1 && rel[0] is '\\' or '/' && IsRelative(rel[1..]) && KnownFolder(tag) is { Length: > 0 } dir)
                return Path.GetFullPath(Path.Combine(dir, rel[1..]));
        }
        // Full paths are what 0ed78f7 recorded for an install folder on another drive or share.
        else if (IsFullPath(entry)) return Path.GetFullPath(entry);
        else if (IsRelative(entry)) return Path.GetFullPath(Path.Combine(gameRoot, entry));
        throw new UnsafeManifestException(targetDir, $"path \"{entry}\"");
    }

    /// <summary>
    /// The manifest travels with the game folder, so every path in it is untrusted. Each one must stay inside
    /// <paramref name="gameRoot"/> or the install folder (which may be a folder the user picked outside the game, on
    /// any drive or share) and not lead through a junction or symbolic link; owned folders must lie inside the install
    /// folder and backups inside its backup folder. Entries are rewritten the way <see cref="Entry"/> records them for
    /// <paramref name="gameRoot"/>, so the same install folder reached from another game root still finds its
    /// originals, and records from earlier versions are brought up to date.
    /// </summary>
    /// <exception cref="UnsafeManifestException">Any entry breaks these rules; nothing is changed.</exception>
    public void Contain(string targetDir, string gameRoot)
    {
        var target = FileUtil.Normalize(targetDir);
        var root = FileUtil.Normalize(gameRoot);
        var backupDir = BackupDirFor(target);
        // Links are looked for below whichever of the two trusted folders holds the path.
        string Base(string full) => FileUtil.IsUnder(full, root) ? root : target;
        if (FileUtil.LinkBelow(Base(backupDir), backupDir) is { } link) throw new UnsafeManifestException(target, $"{link} is a link");
        if (!IsRelative(RootRel) && !IsFullPath(RootRel)) throw new UnsafeManifestException(target, $"game root \"{RootRel}\"");
        if (Proxy is not null && !AppSettings.IsKnownProxy(Proxy)) throw new UnsafeManifestException(target, $"proxy \"{Proxy}\"");
        var oldRoot = FileUtil.Normalize(Path.Combine(target, RootRel));

        static bool StrictlyUnder(string full, string dir) =>
            FileUtil.IsUnder(full, dir) && !FileUtil.Normalize(full).Equals(dir, StringComparison.OrdinalIgnoreCase);

        string Check(string entry, bool inTargetOnly)
        {
            var full = Resolve(entry, target, oldRoot);
            var inside = StrictlyUnder(full, target) || (!inTargetOnly && StrictlyUnder(full, root));
            if (!inside) throw new UnsafeManifestException(target, $"\"{entry}\" is outside {(inTargetOnly ? target : $"{root} and {target}")}");
            if (FileUtil.LinkBelow(Base(full), full) is { } l) throw new UnsafeManifestException(target, $"\"{entry}\" leads through the link {l}");
            return full;
        }

        if (DlssNrFile is not null && DlssNrFile != ComponentStore.DlssNrFile
            && DlssNrFile != Path.Combine(ComponentStore.UpliftNrDir, ComponentStore.DlssNrFile))
            throw new UnsafeManifestException(target, "unknown NR runtime placement");
        if (DlssNr || Uplift)
            foreach (var runtime in new[] { Path.Combine(target, DlssNrFile ?? ComponentStore.DlssNrFile), Uplift ? UpliftConfig.NrPath(target) : Path.Combine(target, ComponentStore.DlssNrFile) })
                if (FileUtil.LinkBelow(Base(runtime), runtime) is { } runtimeLink)
                    throw new UnsafeManifestException(target, $"{runtimeLink} is a link");
        var files = Files.Select(f => Check(f, inTargetOnly: false)).ToList();
        var dirs = Dirs.Select(d => Check(d, inTargetOnly: true)).ToList();
        if (dirs.Any(d => FileUtil.IsUnder(d, DirFor(target))))
            throw new UnsafeManifestException(target, "an owned folder is DLSS5 Updater's own folder");
        var originals = Backups.Select(b => Check(b.Original, inTargetOnly: false)).ToList();
        for (var i = 0; i < Backups.Count; i++)
        {
            var b = Backups[i];
            string full;
            if (IsFullPath(b.Backup))
            {
                // 0ed78f7 joined a full original path onto the backup folder, which left the original beside itself as "<name>.N".
                full = Path.GetFullPath(b.Backup);
                if (!IsSideBackup(full, originals[i])) throw new UnsafeManifestException(target, $"backup \"{b.Backup}\"");
            }
            else
            {
                if (!IsRelative(b.Backup) || b.Backup.Split('\\', '/').Contains("..")) throw new UnsafeManifestException(target, $"backup \"{b.Backup}\"");
                full = Path.GetFullPath(Path.Combine(backupDir, b.Backup));
                if (!StrictlyUnder(full, backupDir)) throw new UnsafeManifestException(target, $"backup \"{b.Backup}\" is outside {backupDir}");
            }
            if (FileUtil.LinkBelow(Base(full), full) is { } l) throw new UnsafeManifestException(target, $"backup \"{b.Backup}\" leads through the link {l}");
        }

        // A config file may also lie in a known folder: an .ini or .cfg there, reached without a link.
        string CheckConfig(string entry)
        {
            var full = Resolve(entry, target, oldRoot);
            if (StrictlyUnder(full, target) || StrictlyUnder(full, root))
            {
                if (FileUtil.LinkBelow(Base(full), full) is { } l) throw new UnsafeManifestException(target, $"\"{entry}\" leads through the link {l}");
                return ConfigEntry(full, target, root);
            }
            var rewritten = ConfigEntry(full, target, root);
            var tag = KnownFolderTags.First(t => rewritten.StartsWith(t, StringComparison.Ordinal));
            if (FileUtil.LinkBelow(KnownFolder(tag)!, full) is { } link) throw new UnsafeManifestException(target, $"\"{entry}\" leads through the link {link}");
            return rewritten;
        }

        var components = new List<(List<string> Files, List<(string Original, string Backup)> Backups, List<string> Configs, List<string> Attributes)>();
        foreach (var c in Components)
        {
            if (string.IsNullOrWhiteSpace(c.Id)) throw new UnsafeManifestException(target, "a component without an id");
            foreach (var b in c.Backups.Values)
                if (!IsRelative(b) || b.Split('\\', '/').Contains("..")) throw new UnsafeManifestException(target, $"backup \"{b}\"");
            if (c.Attributes.Any(a => ((a.Before | a.After) & ~AttributeChange.Managed) != 0))
                throw new UnsafeManifestException(target, $"attributes other than read-only and hidden in {c.Id}");
            components.Add((
                c.Files.Select(f => Check(f.Path, inTargetOnly: false)).ToList(),
                c.Backups.Select(b => (Check(b.Key, inTargetOnly: false), b.Value)).ToList(),
                c.ConfigEdits.Select(e => CheckConfig(e.File)).ToList(),
                c.Attributes.Select(a => CheckConfig(a.Path)).ToList()));
        }

        Files = files.Select(f => Entry(f, target, root)).ToList();
        Dirs = dirs.Select(d => Entry(d, target, root)).ToList();
        for (var i = 0; i < Backups.Count; i++) Backups[i].Original = Entry(originals[i], target, root);
        for (var i = 0; i < Components.Count; i++)
        {
            var (cFiles, cBackups, cConfigs, cAttributes) = components[i];
            var c = Components[i];
            for (var j = 0; j < c.Files.Count; j++) c.Files[j].Path = Entry(cFiles[j], target, root);
            c.Backups = cBackups.ToDictionary(b => Entry(b.Original, target, root), b => b.Backup, StringComparer.OrdinalIgnoreCase);
            for (var j = 0; j < c.ConfigEdits.Count; j++) c.ConfigEdits[j].File = cConfigs[j];
            for (var j = 0; j < c.Attributes.Count; j++) c.Attributes[j].Path = cAttributes[j];
        }
        RootRel = Path.GetRelativePath(target, root);
    }

    /// <summary>"&lt;original&gt;.N" in the original's own folder.</summary>
    private static bool IsSideBackup(string full, string original) =>
        full.Length > original.Length + 1
        && full.StartsWith(original + ".", StringComparison.OrdinalIgnoreCase)
        && full[(original.Length + 1)..].All(char.IsAsciiDigit);

    /// <summary>Relative, with no drive or stream (':'). ".." parts are fine: where the path ends up is checked separately.</summary>
    private static bool IsRelative(string? rel) =>
        !string.IsNullOrWhiteSpace(rel) && !Path.IsPathRooted(rel) && !rel.Contains(':');

    /// <summary>A drive or UNC path with no stream (':') after its root. Device paths (\\?\, \\.\) are not accepted.</summary>
    private static bool IsFullPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
        && !(path.Length > 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/' && path[2] is '?' or '.')
        && !path[Path.GetPathRoot(path)!.Length..].Contains(':');

    public void Save(string targetDir)
    {
        Updated = DateTime.UtcNow;
        // A record from a newer version keeps its number: what this version doesn't know stays in Unknown.
        Schema = Math.Max(Schema, CurrentSchema);
        FileUtil.AtomicWriteText(PathFor(targetDir), JsonSerializer.Serialize(this, JsonCtx.Default.InstallManifest));
    }

    [JsonIgnore]
    public bool IsEmpty => Files.Count == 0 && Dirs.Count == 0 && Backups.Count == 0 && Components.Count == 0;

    public bool Owns(string rel) => Files.Contains(rel, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Full paths of the files and folders DLSS5 Updater added, for telling the game's own files from ours. A file that
    /// replaced an original (a DLSS update) is left out: the backed-up original was the game's. An entry that doesn't
    /// resolve is left out too; never use this for changes, see <see cref="Contain"/>.
    /// </summary>
    public List<string> AddedPaths(string targetDir)
    {
        var target = FileUtil.Normalize(targetDir);
        var list = new List<string>();
        try
        {
            var root = FileUtil.Normalize(Path.Combine(target, RootRel));
            foreach (var entry in Files.Concat(Dirs).Concat(Components.SelectMany(c => c.Files.Select(f => f.Path))))
            {
                if (BackupOf(entry) is not null) continue;
                try { list.Add(FileUtil.Normalize(Resolve(entry, target, root))); }
                catch (Exception ex) when (ex is UnsafeManifestException or ArgumentException or NotSupportedException) { }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
        return list;
    }

    public void AddFile(string rel)
    {
        if (!Owns(rel)) Files.Add(rel);
    }

    public void AddDir(string rel)
    {
        if (!Dirs.Contains(rel, StringComparer.OrdinalIgnoreCase)) Dirs.Add(rel);
    }

    public BackupEntry? BackupOf(string rel) =>
        Backups.FirstOrDefault(b => b.Original.Equals(rel, StringComparison.OrdinalIgnoreCase));
}
