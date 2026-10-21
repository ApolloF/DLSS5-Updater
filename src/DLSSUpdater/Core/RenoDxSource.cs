using System.IO.Compression;
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
        var build = RetainAddon(AddonKind.RenoDx, source, RenoDxFile, version, null, version, downloaded);
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
        var zipSha = newest.File.Sha256!.ToLowerInvariant();
        if (AddonBuilds(AddonKind.RenoDx).Any(b => string.Equals(b.CatalogSha, zipSha, StringComparison.OrdinalIgnoreCase))) return null;

        var work = Path.Combine(AppPaths.Cache, "renodx-dlss-" + zipSha[..12]);
        try
        {
            Directory.CreateDirectory(work);
            var zip = Path.Combine(work, newest.File.FileName);
            var result = await downloader.DownloadAsync(newest.File, zip, progress, ct);
            if (!result.Ok)
            {
                Log.Info($"ShortFuse's add-on {newest.Version} not downloaded: {result.Refusal}");
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
            if (AdoptImportedAddon(settings, AddonKind.RenoDx, build)) Log.Info($"ShortFuse's add-on default is now {build.Label}");
            Log.Info($"ShortFuse's add-on {newest.Version}: {build.Label} downloaded from the catalog");
            return build;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"ShortFuse's add-on {newest.Version} from the catalog was not usable: {ex.Message}");
            return null;
        }
        finally
        {
            try { FileUtil.DeleteDirectory(work); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

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
