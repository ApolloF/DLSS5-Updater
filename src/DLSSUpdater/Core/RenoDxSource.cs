using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DLSSUpdater.Core;

/// <summary>
/// ShortFuse's renodx-dlss add-on isn't published on any official GitHub. The app downloads it from the catalog (the mirror
/// first, the author's URL as the fallback, hash-checked either way) and still accepts an import. Either way the file is checked
/// by its version resource and kept in the add-on library (one folder per SHA-256, see AddonLibrary.cs).
/// </summary>
public sealed partial class ComponentStore
{
    /// <summary>Only a file that names itself renodx-dlss.addon64 is accepted; sibling add-ons are named in the error.</summary>
    public AddonBuild ImportRenoDx(string source) => ImportRenoDx(source, downloaded: false);

    private AddonBuild ImportRenoDx(string source, bool downloaded)
    {
        if (FileUtil.ReadVersion(source) is null)
        {
            if (File.Exists(source) && LooksLikeLecramV11(source))
                throw new InvalidDataException($"This is Lecram's {Lecram11File}, a different neural-rendering add-on. Import it under Lecram's DLSS5 add-on instead.");
            throw new InvalidDataException("Choose renodx-dlss.addon64 with a readable version resource. Extract archives first.");
        }
        var original = FileUtil.OriginalFilename(source);
        if (!string.Equals(original, RenoDxFile, StringComparison.OrdinalIgnoreCase))
        {
            var what = string.IsNullOrEmpty(original) ? "a file without an original filename" : original;
            throw new InvalidDataException(
                $"This file is {what}, not {RenoDxFile}. " +
                (LegacyRenoDxFiles.Contains(original ?? "", StringComparer.OrdinalIgnoreCase)
                    ? "That add-on has no neural rendering. "
                    : original is not null && original.StartsWith("renodx-dlss5", StringComparison.OrdinalIgnoreCase)
                        ? "That is Lecram's DLSS5 add-on: import it under Lecram's add-on instead. "
                        : "") +
                $"Choose ShortFuse's {RenoDxFile}.");
        }
        var version = FileUtil.Format(FileUtil.ReadVersion(source));
        var build = RetainAddon(AddonKind.RenoDx, source, RenoDxFile, version, null, RenoDxBuildLabel(File.ReadAllBytes(source), version), downloaded);
        Log.Info($"Imported {RenoDxFile} {build.Label} from {source}");
        return build;
    }

    /// <summary>
    /// Pulls the newest catalog build of the add-on into the library when it isn't there yet. The catalog gives a zip with a
    /// SHA-256 (the download is refused on a mismatch); the add-on inside is checked like an import. It becomes the default when
    /// it is newer than the current one, as an import does. Offline, refused or odd downloads are logged and change nothing.
    /// </summary>
    public async Task<AddonBuild?> CheckRenoDxCatalogAsync(AppSettings settings, CatalogRenodxDlss? section, ICatalogFetch downloader,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!settings.AutoDownloadRenoDx) return null;
        var newest = section?.Builds.FirstOrDefault(b => b.File.IsInstallable);
        if (newest is null) return null;
        if (HasRenoDxCatalogBuild(newest)) return null;

        var build = await FetchRenoDxAsync(newest, downloader, progress, ct);
        if (build is not null && AdoptImportedAddon(settings, AddonKind.RenoDx, build)) Log.Info($"ShortFuse's add-on default is now {build.Label}");
        return build;
    }

    private bool HasRenoDxCatalogBuild(string zipSha) =>
        AddonBuilds(AddonKind.RenoDx).Any(b => string.Equals(b.CatalogSha, zipSha, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when the library has the catalog build: downloaded from that zip, or a build with the same version (the mirror names a
    /// release after the build time the add-on carries, see <see cref="RenoDxBuildLabel"/>), which catches a copy imported by hand.
    /// </summary>
    private bool HasRenoDxCatalogBuild(CatalogDlssBuild entry) =>
        HasRenoDxCatalogBuild(entry.File.Sha256!) || AddonBuilds(AddonKind.RenoDx).Any(b => b.Order == BuildOrder.Key(RenoDxCatalogVersion(entry)));

    private static string RenoDxCatalogVersion(CatalogDlssBuild entry) => string.IsNullOrWhiteSpace(entry.Version) ? entry.Tag : entry.Version;

    /// <summary>One catalog build of the add-on into the library, stamped with the catalog hash it came from. Null (logged) when refused.</summary>
    private async Task<AddonBuild?> FetchRenoDxAsync(CatalogDlssBuild entry, ICatalogFetch downloader, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var zipSha = entry.File.Sha256!.ToLowerInvariant();
        var work = Path.Combine(AppPaths.Cache, "renodx-dlss-" + zipSha[..12]);
        try
        {
            Directory.CreateDirectory(work);
            var zip = Path.Combine(work, entry.File.FileName);
            var result = await downloader.DownloadAsync(entry.File, zip, progress, ct);
            if (!result.Ok)
            {
                Log.Info($"ShortFuse's add-on {entry.Version} not downloaded: {result.Refusal}");
                return null;
            }
            // Builds from the catalog's incoming folder are the bare add-on, not a zip.
            var addon = zip.EndsWith(RenoDxFile, StringComparison.OrdinalIgnoreCase) ? zip : await Task.Run(() => ExtractAddon(zip, work), ct);
            var build = ImportRenoDx(addon, downloaded: true);
            if (!string.Equals(build.CatalogSha, zipSha, StringComparison.OrdinalIgnoreCase))
            {
                build = build with { CatalogSha = zipSha };
                FileUtil.AtomicWriteText(Path.Combine(AddonRoot(AddonKind.RenoDx), build.Id, BuildRecordFile), JsonSerializer.Serialize(build, JsonCtx.Default.AddonBuild));
                ForgetAddons(AddonKind.RenoDx);
            }
            Log.Info($"ShortFuse's add-on {entry.Version}: {build.Label} downloaded from the catalog");
            return build;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"ShortFuse's add-on {entry.Version} from the catalog was not usable: {ex.Message}");
            return null;
        }
        finally
        {
            try { FileUtil.DeleteDirectory(work); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // ---------- build label ----------

    /// <summary>
    /// What names a build of the add-on: the build time it shows in its About section ("Build: 2026-09-28T02:05:38"), written the way
    /// the mirror names its releases after it ("26.0928.0205", from renodx-dlss-SF-26.0928.0205). The file version
    /// (0.2026.928.306) is stamped at another moment and doesn't match the release, so it is only the fallback, in the same form.
    /// </summary>
    internal static string RenoDxBuildLabel(ReadOnlySpan<byte> data, string fileVersion) =>
        FindRenoDxBuildTime(data) is { } time ? StampLabel(time) : RenoDxLabelOfFileVersion(fileVersion);

    private static readonly byte[] BuildCaption = "\0Build: \0"u8.ToArray();
    private const string BuildTimeFormat = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>The time string right before the About section's "Build: " caption, or null when the binary has none.</summary>
    internal static DateTime? FindRenoDxBuildTime(ReadOnlySpan<byte> data)
    {
        for (var offset = 0; offset < data.Length;)
        {
            var at = data[offset..].IndexOf(BuildCaption);
            if (at < 0) return null;
            var i = offset + at;
            offset = i + 1;
            if (i < BuildTimeFormat.Length) continue;
            var text = Encoding.ASCII.GetString(data[(i - BuildTimeFormat.Length)..i]);
            if (DateTime.TryParseExact(text, BuildTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return time;
        }
        return null;
    }

    /// <summary>A file version 0.YYYY.MDD.HMM in the label's form; any other file version as it is.</summary>
    internal static string RenoDxLabelOfFileVersion(string fileVersion)
    {
        if (!Version.TryParse(fileVersion, out var v) || v is not { Major: 0, Minor: >= 2000 and < 2100 }) return fileVersion;
        var (build, revision) = (Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        try { return StampLabel(new DateTime(v.Minor, build / 100, build % 100, revision / 100, revision % 100, 0)); }
        catch (ArgumentOutOfRangeException) { return fileVersion; }
    }

    private static string StampLabel(DateTime time) => time.ToString("yy.MMdd.HHmm", CultureInfo.InvariantCulture);

    /// <summary>A build stamp label ("26.1016.1304") as the date it is ("2026-10-16 13:04"); any other label as it is.</summary>
    public static string ReadableStamp(string label) =>
        DateTime.TryParseExact(label, "yy.MMdd.HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : label;

    private const long MaxAddonBytes = 256L << 20;

    /// <summary>The add-on out of the release zip, by file name only (no path from the archive is used).</summary>
    private static string ExtractAddon(string zip, string dir)
    {
        using var archive = ZipFile.OpenRead(zip);
        var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(RenoDxFile, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"The download has no {RenoDxFile}.");
        if (entry.Length is <= 0 or > MaxAddonBytes) throw new InvalidDataException($"{RenoDxFile} in the download has an unexpected size.");
        var dest = Path.Combine(dir, RenoDxFile);
        entry.ExtractToFile(dest, true);
        return dest;
    }

    /// <summary>The crashed-pair warning for the exact chosen pair, or null when it isn't marked crashed or both add-ons are not selected.</summary>
    public string? SfLecramMessage(bool renoDx, bool lecram, string? renoDxId, string? lecramId) =>
        LecramCompat.Message(renoDx, lecram, PairTestFor(renoDxId, lecramId));
}
