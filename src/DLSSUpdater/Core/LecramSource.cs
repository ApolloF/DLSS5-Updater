using System.Text.Json.Serialization;
using Generation = DLSSUpdater.Core.LecramGeneration;

namespace DLSSUpdater.Core;

/// <summary>Lecram's add-on ships in two generations that can't run together in one game; each build in the library knows its own.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LecramGeneration>))]
public enum LecramGeneration { V10, V11 }

/// <summary>
/// Lecram's DLSS5 Neural Rendering add-on supports local imports and the author's public v11 update channel.
/// v10 and older (renodx-dlss5.addon64) name themselves in the version resource; v11+
/// (dlss5-nr.addon64) has no version resource and is recognised by strings in the binary. Every imported build stays in the
/// library; a game gets the one chosen for it, under its generation's file name.
/// </summary>
public sealed partial class ComponentStore
{
    private readonly SemaphoreSlim _lecramCheck = new(1, 1);

    /// <param name="force">Check even when automatic checks are off (the Check now button).</param>
    public Task CheckLecramAsync(AppSettings settings, CancellationToken ct, bool force = false) =>
        CheckLecramAsync(settings, new MediaFireClient(), ct, force);

    internal async Task CheckLecramAsync(AppSettings settings, MediaFireClient client, CancellationToken ct, bool force = false)
    {
        if (!(settings.AutoDownloadLecram || force) || !await _lecramCheck.WaitAsync(0, ct)) return;
        var temp = Path.Combine(AppPaths.Cache, "lecram-download-" + Guid.NewGuid().ToString("N"));
        try
        {
            var channel = await client.CheckAsync(settings.LecramFolderKey, ct);
            if (!channel.Offline) settings.LecramFolderKey = channel.Key;
            if (channel.Offline) settings.LecramLastResult = "Offline; keeping the Lecram library (cached listing).";
            else if (channel.Latest is not { } latest) settings.LecramLastResult = "No v11 builds found; keeping the Lecram library.";
            else if (latest.Hash.Equals(settings.LecramLastAssetHash, StringComparison.OrdinalIgnoreCase)
                     && settings.LecramLastBuildId is { } id && LecramBuilds.Any(b => b.Id == id))
                settings.LecramLastResult = $"Up to date: {settings.LecramLastVersion}.";
            else
            {
                Directory.CreateDirectory(temp);
                var package = Path.Combine(temp, "package");
                await client.DownloadAsync(latest, package, ct);
                var addon = Path.Combine(temp, Lecram11File);
                if (latest.Rank == 2) File.Move(package, addon);
                else MediaFireClient.ExtractAddon(package, addon);
                if (!LooksLikeLecramV11(addon)) throw new InvalidDataException("Downloaded add-on is not a recognised Lecram v11 build.");
                var before = LecramBuilds.Select(b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var build = ImportLecram(addon, downloaded: true);
                if (build.Generation != Generation.V11) throw new InvalidDataException("Downloaded add-on is not Lecram v11.");
                if (!before.Contains(build.Id)) AdoptImportedAddon(settings, AddonKind.Lecram, build);
                settings.LecramLastAssetHash = latest.Hash;
                settings.LecramLastBuildId = build.Id;
                settings.LecramLastVersion = build.DetectedLabel;
                settings.LecramLastResult = $"Available in library: {build.DetectedLabel}.";
            }
            if (!string.IsNullOrWhiteSpace(channel.Notice)) settings.LecramLastResult += " " + channel.Notice;
            Log.Info("Lecram: " + settings.LecramLastResult);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested)
                                   && ex is HttpRequestException or OperationCanceledException or IOException or InvalidDataException
                                      or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            settings.LecramLastResult = "Check failed; keeping the Lecram library. " + (UserErrors.IsCurated(ex) ? ex.Message : "The details are in the log.");
            Log.Info("Lecram: " + settings.LecramLastResult);
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("Could not remove Lecram temporary download", ex); }
            _lecramCheck.Release();
        }
    }

    /// <summary>
    /// True for a PE file with the strings only Lecram's v11 binary has: the ini section it reads, and its NR-SETTINGS and
    /// NR-COMPAT log lines. v10 builds have the section but neither log line.
    /// </summary>
    internal static bool LooksLikeLecramV11(string path)
    {
        var data = File.ReadAllBytes(path);
        return data.Length > 0x40 && data[0] == 'M' && data[1] == 'Z'
               && data.AsSpan().IndexOf("NR-SETTINGS"u8) >= 0
               && data.AsSpan().IndexOf("NR-COMPAT"u8) >= 0
               && data.AsSpan().IndexOf("RenoDX.DLSS5"u8) >= 0;
    }

    /// <summary>
    /// Imports renodx-dlss5.addon64 (v10 and older) or dlss5-nr.addon64 (v11+) into the library; the file itself decides the generation.
    /// ShortFuse's add-on and Deep Fried Chicken are named in the error instead.
    /// </summary>
    public AddonBuild ImportLecram(string source) => ImportLecram(source, downloaded: false);

    /// <param name="downloaded">Fetched from Lecram's channel by the app, so the automatic cleanup may remove it later.</param>
    internal AddonBuild ImportLecram(string source, bool downloaded)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("The add-on file doesn't exist.", source);
        var original = FileUtil.OriginalFilename(source);
        Generation generation;
        if (string.IsNullOrEmpty(original))
        {
            if (!LooksLikeLecramV11(source))
                throw new InvalidDataException(
                    $"This file isn't Lecram's add-on. Choose {Lecram10File} (v10 and older) or {Lecram11File} (v11 and newer). Extract archives first.");
            generation = Generation.V11;
        }
        else if (original.Equals(Lecram10File, StringComparison.OrdinalIgnoreCase)) generation = Generation.V10;
        else if (original.Equals(Lecram11File, StringComparison.OrdinalIgnoreCase)) generation = Generation.V11;
        else
        {
            var which = original.Equals(RenoDxFile, StringComparison.OrdinalIgnoreCase) || LegacyRenoDxFiles.Contains(original, StringComparer.OrdinalIgnoreCase)
                ? "ShortFuse's add-on; import it under the RenoDX DLSS add-on instead"
                : original.Equals(DfcFile, StringComparison.OrdinalIgnoreCase) ? "Deep Fried Chicken; import it from its release folder instead"
                : null;
            throw new InvalidDataException(
                $"This file is {original}, {(which is null ? "not Lecram's add-on" : "which is " + which)}. Choose {Lecram10File} (v10 and older) or {Lecram11File} (v11 and newer).");
        }

        var file = generation == Generation.V10 ? Lecram10File : Lecram11File;
        var fileVersion = FileUtil.ReadVersion(source) is { } v ? FileUtil.Format(v) : "";
        // The build names itself in a string of its binary; without one the file version (v10) or the generation (v11) stands in.
        var label = FindBuildLabel(File.ReadAllBytes(source)) ?? (fileVersion.Length > 0 ? fileVersion : "v11");
        var build = RetainAddon(AddonKind.Lecram, source, file, fileVersion, generation, label, downloaded);
        Log.Info($"{(downloaded ? "Downloaded" : "Imported")} {file} {build.Label} from {source}");
        return build;
    }
}
