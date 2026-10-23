using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>One CI run of crosire/reshade's build workflow on main that succeeded.</summary>
public sealed record NightlyRun(long Id, string HeadSha, DateTime Created, string ArtifactsUrl);

/// <summary>An artifact of a CI run; <see cref="Sha256"/> is the digest GitHub publishes for the zip.</summary>
public sealed record NightlyArtifact(string Name, string? Sha256, bool Expired, long Size);

/// <summary>What <see cref="ReShadeSource.Check"/> found out about a ReShade DLL.</summary>
public sealed record ReShadeFileCheck(bool Ok, string? Error, Version? Version);

/// <summary>
/// Where ReShade builds come from and how a download is turned into DLLs: the reshade.me pages and setups, the CI artifacts
/// of crosire/reshade (through nightly.link) and the checks every file passes before it enters the library. Pure parsing
/// and file handling; the downloads themselves are in <see cref="ComponentStore"/>.
/// </summary>
public static partial class ReShadeSource
{
    public const string Site = "https://reshade.me";

    /// <summary>The workflow file of crosire/reshade that builds every push (the API takes the file name as its id).</summary>
    public const string NightlyWorkflow = "build.yml";
    public const string NightlyBranch = "main";
    public const string NightlyArtifact64 = "ReShade (64-bit)";
    public const string NightlyArtifact32 = "ReShade (32-bit)";

    /// <summary>The newest successful push run on main.</summary>
    public static readonly string NightlyRunsUrl =
        $"https://api.github.com/repos/{ComponentStore.ReShadeRepo}/actions/workflows/{NightlyWorkflow}/runs?branch={NightlyBranch}&event=push&status=success&per_page=1";

    /// <summary>Legacy builds start here: older add-on builds are not offered (their add-on API differs).</summary>
    public static readonly Version OldestLegacy = new(6, 0, 0);

    public static string SetupUrl(string version, bool addon) =>
        $"{Site}/downloads/ReShade_Setup_{version}{(addon ? "_Addon" : "")}.exe";

    [GeneratedRegex(@"downloads/ReShade_Setup_(\d+(?:\.\d+)+)(_Addon)?\.exe", RegexOptions.IgnoreCase)]
    private static partial Regex SetupLink();

    /// <summary>The newest add-on and the newest plain setup linked on a reshade.me page; null for one the page doesn't link.</summary>
    public static (string? Addon, string? NoAddons) ParseSitePage(string html)
    {
        var links = SetupLink().Matches(html).Select(m => (Version: m.Groups[1].Value, Addon: m.Groups[2].Success)).ToList();
        string? Newest(bool addon) => links.Where(l => l.Addon == addon).Select(l => l.Version)
            .OrderByDescending(FileUtil.ParseTag).FirstOrDefault();
        return (Newest(true), Newest(false));
    }

    /// <summary>Releases (tags like "v6.7.3") from <see cref="OldestLegacy"/> on, newest first, as plain versions.</summary>
    public static IReadOnlyList<string> LegacyVersions(IEnumerable<string> tags) =>
        tags.Select(t => t.TrimStart('v', 'V'))
            .Where(t => FileUtil.ParseTag(t) is { } v && v >= OldestLegacy && Regex.IsMatch(t, @"^\d+(\.\d+)+$"))
            .Distinct()
            .OrderByDescending(FileUtil.ParseTag)
            .ToList();

    // ---------- nightly ----------

    /// <summary>The newest run in a workflow-runs answer, or null when there is none.</summary>
    public static NightlyRun? ParseNightlyRun(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array) return null;
            foreach (var r in runs.EnumerateArray())
            {
                if (!r.TryGetProperty("id", out var id) || !r.TryGetProperty("head_sha", out var sha) || !r.TryGetProperty("artifacts_url", out var url)) continue;
                var created = r.TryGetProperty("created_at", out var c) && c.TryGetDateTime(out var d) ? d : DateTime.UtcNow;
                if (sha.GetString() is { Length: > 0 } s && url.GetString() is { Length: > 0 } u)
                    return new NightlyRun(id.GetInt64(), s, created, u);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        return null;
    }

    public static IReadOnlyList<NightlyArtifact> ParseNightlyArtifacts(string json)
    {
        var found = new List<NightlyArtifact>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("artifacts", out var list) || list.ValueKind != JsonValueKind.Array) return found;
            foreach (var a in list.EnumerateArray())
            {
                if (!a.TryGetProperty("name", out var name) || name.GetString() is not { } n) continue;
                var digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null;
                found.Add(new NightlyArtifact(n, sha,
                    a.TryGetProperty("expired", out var e) && e.ValueKind == JsonValueKind.True,
                    a.TryGetProperty("size_in_bytes", out var sz) && sz.TryGetInt64(out var size) ? size : 0));
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return found;
    }

    /// <summary>
    /// nightly.link's download of the newest run's artifact. The name must be percent-encoded including the brackets:
    /// ".../ReShade (64-bit).zip" is a 404, ".../ReShade%20%2864-bit%29.zip" is the file.
    /// </summary>
    public static string NightlyZipUrl(string artifactName) =>
        $"https://nightly.link/{ComponentStore.ReShadeRepo}/workflows/{Path.GetFileNameWithoutExtension(NightlyWorkflow)}/{NightlyBranch}/{Uri.EscapeDataString(artifactName)}.zip";

    // ---------- files ----------

    /// <summary>ReShade's setup is an exe with a zip appended; the DLLs it holds are extracted into <paramref name="destDir"/>.</summary>
    /// <returns>The file names extracted.</returns>
    public static IReadOnlyList<string> ExtractSetup(string setupExe, string destDir)
    {
        var bytes = File.ReadAllBytes(setupExe);
        if (bytes.Length < 500 * 1024 || bytes[0] != 'M' || bytes[1] != 'Z')
            throw new InvalidDataException("That is not a ReShade setup (reshade.me may have answered with an error page).");
        var start = ComponentStore.AppendedZipStart(bytes);
        using var archive = new ZipArchive(new MemoryStream(bytes, start, bytes.Length - start, writable: false), ZipArchiveMode.Read);
        return ExtractDlls(archive, destDir);
    }

    /// <summary>The ReShade DLLs of a plain zip (a nightly artifact, or a zip the user imports).</summary>
    public static IReadOnlyList<string> ExtractZip(string zip, string destDir)
    {
        using var archive = ZipFile.OpenRead(zip);
        return ExtractDlls(archive, destDir);
    }

    private static List<string> ExtractDlls(ZipArchive archive, string destDir)
    {
        Directory.CreateDirectory(destDir);
        var extracted = new List<string>();
        foreach (var name in new[] { ReShadeBuild.File64, ReShadeBuild.File32 })
        {
            // Only these two names, only from the archive's top level: nothing in the archive picks where a file goes.
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry is null) continue;
            entry.ExtractToFile(Path.Combine(destDir, name), true);
            extracted.Add(name);
        }
        if (extracted.Count == 0) throw new InvalidDataException("No ReShade64.dll or ReShade32.dll in that archive.");
        return extracted;
    }

    /// <summary>
    /// A ReShade DLL passes when it is a PE file of the right bitness whose version resource says ReShade. A page saved by
    /// mistake, a renamed other mod or a 32-bit file called ReShade64.dll all fail here.
    /// </summary>
    public static ReShadeFileCheck Check(string path, int bits)
    {
        try
        {
            using (var fs = File.OpenRead(path))
            {
                var mz = new byte[2];
                if (fs.Read(mz, 0, 2) != 2 || mz[0] != 'M' || mz[1] != 'Z') return new(false, "not a Windows DLL", null);
            }
            if (PeReader.Read(path) is not { } pe) return new(false, "not a readable Windows DLL", null);
            if (pe.Bits != bits) return new(false, $"it is a {pe.Bits}-bit file, expected {bits}-bit", null);
            var info = FileVersionInfo.GetVersionInfo(path);
            var isReShade = string.Equals(info.ProductName?.Trim(), "ReShade", StringComparison.OrdinalIgnoreCase)
                            || (info.OriginalFilename?.Trim().StartsWith("ReShade", StringComparison.OrdinalIgnoreCase) ?? false);
            if (!isReShade) return new(false, "its version information does not say ReShade", null);
            var v = info.FileMajorPart == 0 && info.FileMinorPart == 0 ? null
                : new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
            return new(true, null, v);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(false, ex.Message, null);
        }
    }

    /// <summary>"6.8.0.2155" -> "6.8.0" for releases; a nightly keeps its four parts.</summary>
    public static string VersionText(Version? v, bool full) =>
        v is null ? "?" : full ? v.ToString(4) : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
}
