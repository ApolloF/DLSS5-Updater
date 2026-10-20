using System.Text.Json;

namespace DLSSUpdater.Core;

public sealed record NrVersion(string Id, string Version, string Sha256)
{
    public string Label => $"{Version} · {(ComponentStore.KnownDlssNr.GetValueOrDefault(Sha256) ?? "Unverified build")} · {Sha256[..8]}";
    public string? Hint
    {
        get
        {
            var known = ComponentStore.KnownDlssNrHints.GetValueOrDefault(Sha256);
            if (!HasPresets) return known;
            return known is null ? PresetHint : $"{known} {PresetHint}";
        }
    }

    /// <summary>310.9 DLLs carry a main and a small model; Preset picks between them inside one file.</summary>
    public bool HasPresets => FileUtil.ParseTag(Version) >= new Version(310, 9);

    private const string PresetHint = "Holds Preset A (main) and B (small, about half the cost): choose with the NR Preset setting, no DLL switch needed.";
    public override string ToString() => Label;
}

public sealed partial class ComponentStore
{
    private static string NrRoot => Path.Combine(AppPaths.Components, "dlssnr");
    private static string LegacyNr => Path.Combine(AppPaths.Components, DlssNrFile);

    public IReadOnlyList<NrVersion> DlssNrVersions
    {
        get
        {
            if (!Directory.Exists(NrRoot)) return [];
            var versions = new List<NrVersion>();
            foreach (var file in Directory.EnumerateFiles(NrRoot, "version.json", SearchOption.AllDirectories))
            {
                try
                {
                    var v = JsonSerializer.Deserialize(File.ReadAllText(file), JsonCtx.Default.NrVersion);
                    if (v is not null && ValidId(v.Id) && v.Sha256 == v.Id && Path.GetFileName(Path.GetDirectoryName(file)) == v.Id
                        && File.Exists(Path.Combine(NrRoot, v.Id, DlssNrFile))) versions.Add(v);
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                { Log.Error($"Could not read NR library entry {file}", ex); }
            }
            return BuildOrder.NewestFirst(versions, v => v.Version, _ => null, v => v.Id);
        }
    }

    private static bool ValidId(string? id) => id is { Length: 64 } && id.All(char.IsAsciiHexDigit);

    public string DlssNrFor(string? id)
    {
        if (id is null)
        {
            if (File.Exists(LegacyNr)) return LegacyNr;
            id = DlssNrVersions.FirstOrDefault()?.Id;
            if (id is null) return LegacyNr;
        }
        if (!ValidId(id)) throw new InvalidDataException("Invalid NR version choice. Import the file again.");
        return Path.Combine(NrRoot, id, DlssNrFile);
    }

    /// <summary>New imports must identify themselves through their version resource, not their renamed file.</summary>
    public NrVersion ImportDlssNr(string source)
    {
        if (FileUtil.ReadVersion(source) is null || !IsDlssNr(source))
            throw new InvalidDataException("Choose nvngx_dlssnr.dll (NVIDIA DLSSNR) with a readable DLL version. Extract archives first.");
        return RetainDlssNr(source);
    }

    /// <summary>
    /// Builds name themselves either by original filename or by internal name: 310.9.16 sets OriginalFilename
    /// to its build changelist ("CL 39025366") and keeps InternalName "DLSSNR".
    /// </summary>
    internal static bool IsDlssNr(string path) =>
        string.Equals(FileUtil.OriginalFilename(path), DlssNrFile, StringComparison.OrdinalIgnoreCase)
        || string.Equals(FileUtil.InternalName(path), "DLSSNR", StringComparison.OrdinalIgnoreCase);

    /// <summary>Also retains older installs without a version resource so upgrading never destroys a way back.</summary>
    internal NrVersion RetainDlssNr(string source)
    {
        var sha = HashCache.Get(source);
        var v = new NrVersion(sha, FileUtil.Format(FileUtil.ReadVersion(source)), sha);
        var dest = DlssNrFor(sha);
        if (!FileUtil.SameFile(source, dest)) FileUtil.AtomicCopy(source, dest);
        FileUtil.AtomicWriteText(Path.Combine(NrRoot, sha, "version.json"), JsonSerializer.Serialize(v, JsonCtx.Default.NrVersion));
        return v;
    }

    /// <summary>
    /// A newer build that enters the library (imported, or kept from a game) becomes the default once. Switching back
    /// to an older one afterwards sticks: <see cref="AppSettings.NewestDlssNrSeen"/> remembers what was offered.
    /// Returns the version it selected, or null.
    /// </summary>
    public NrVersion? AdoptNewerDlssNr(AppSettings s)
    {
        var versions = DlssNrVersions;
        if (versions.Count == 0 || versions[0].Id == s.NewestDlssNrSeen) return null;
        var newest = versions[0];
        var firstRun = s.NewestDlssNrSeen is null;
        s.NewestDlssNrSeen = newest.Id;
        var selected = versions.FirstOrDefault(v => v.Id == s.DlssNrId);
        // Settings from before this feature: a choice already made is kept.
        if (selected is not null && (firstRun || FileUtil.ParseTag(newest.Version) <= FileUtil.ParseTag(selected.Version))) return null;
        s.SelectDlssNr(newest.Id);
        return newest;
    }

    public NrVersion? MigrateDlssNr() => File.Exists(LegacyNr) ? RetainDlssNr(LegacyNr) : null;

    public string VerifiedDlssNrPath(string? id)
    {
        var path = DlssNrFor(id);
        if (!File.Exists(path)) throw new FileNotFoundException("Import this NR version in Settings → Local components first.", path);
        if (id is not null && !FileUtil.Sha256(path).Equals(id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The stored NR file changed. Import it again before switching.");
        return path;
    }

    public ReleaseInfo ImportDlss(string source)
    {
        var original = FileUtil.OriginalFilename(source);
        var file = DlssFiles.FirstOrDefault(f => f.Equals(original, StringComparison.OrdinalIgnoreCase));
        var version = FileUtil.ReadVersion(source);
        if (file is null || version is null) throw new InvalidDataException("Choose a DLSS SR, RR or FG DLL with a matching original filename and version resource.");
        var sha = HashCache.Get(source);
        var release = new ReleaseInfo { Tag = $"v{version}-local-{sha[..12]}", FromCache = true };
        FileUtil.AtomicCopy(source, Path.Combine(TagDir(Component.Dlss, release.Tag), file));
        MarkComplete(Component.Dlss, release);
        return release;
    }

    public static IReadOnlyList<ReleaseInfo> CachedReleases(Component c)
    {
        var root = ComponentDir(c);
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, CompleteMarker, SearchOption.AllDirectories).Select(f =>
        {
            try
            {
                var m = JsonSerializer.Deserialize(File.ReadAllText(f), JsonCtx.Default.CachedComponent);
                return m is null ? null : new ReleaseInfo { Tag = m.Tag, FromCache = true, Published = m.Published, Prerelease = m.Prerelease };
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            { Log.Error($"Could not read cached component {f}", ex); return null; }
        }).OfType<ReleaseInfo>().OrderByDescending(r => r.Version).ToList();
    }
}
