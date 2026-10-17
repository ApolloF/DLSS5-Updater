using System.Text.Json;

namespace DLSSUpdater.Core;

public sealed record NrVersion(string Id, string Version, string Sha256)
{
    public string Label => $"{Version} · {(ComponentStore.KnownDlssNr.GetValueOrDefault(Sha256) ?? "Unverified build")} · {Sha256[..8]}";
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
            return versions.OrderByDescending(v => FileUtil.ParseTag(v.Version)).ThenBy(v => v.Id).ToList();
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
        if (FileUtil.ReadVersion(source) is null || !string.Equals(FileUtil.OriginalFilename(source), DlssNrFile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose nvngx_dlssnr.dll with a readable DLL version and matching original filename. Extract archives first.");
        return RetainDlssNr(source);
    }

    /// <summary>Also retains older installs without a version resource so upgrading never destroys a way back.</summary>
    internal NrVersion RetainDlssNr(string source)
    {
        var sha = FileUtil.Sha256(source);
        var v = new NrVersion(sha, FileUtil.Format(FileUtil.ReadVersion(source)), sha);
        var dest = DlssNrFor(sha);
        if (!FileUtil.SameFile(source, dest)) FileUtil.AtomicCopy(source, dest);
        FileUtil.AtomicWriteText(Path.Combine(NrRoot, sha, "version.json"), JsonSerializer.Serialize(v, JsonCtx.Default.NrVersion));
        return v;
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
        var sha = FileUtil.Sha256(source);
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
