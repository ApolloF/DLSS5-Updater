using System.Text.Json.Serialization;
using Generation = DLSSUpdater.Core.LecramGeneration;

namespace DLSSUpdater.Core;

/// <summary>Lecram's add-on ships in two generations that can't run together in one game; each build in the library knows its own.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LecramGeneration>))]
public enum LecramGeneration { V10, V11 }

/// <summary>
/// Lecram's DLSS5 Neural Rendering add-on is imported and, in the private build only (BuildFlags.LecramChannel), also taken from the author's v11 update channel.
/// v10 and older (renodx-dlss5.addon64) name themselves in the version resource; v11+
/// (dlss5-nr.addon64) has no version resource and is recognised by strings in the binary. Every imported build stays in the
/// library; a game gets the one chosen for it, under its generation's file name.
/// </summary>
public sealed partial class ComponentStore
{
    private readonly SemaphoreSlim _lecramCheck = new(1, 1);

    /// <param name="force">Check even when automatic checks are off (the Check now button).</param>
    public Task CheckLecramAsync(AppSettings settings, CancellationToken ct, bool force = false, IProgress<TransferProgress>? progress = null) =>
        CheckLecramAsync(settings, new MediaFireClient(), ct, force, BuildFlags.LecramChannel, progress);

    /// <param name="channelEnabled">False in the public build: the paid channel is never contacted, not even when forced.</param>
    internal async Task CheckLecramAsync(AppSettings settings, MediaFireClient client, CancellationToken ct, bool force = false, bool channelEnabled = true,
        IProgress<TransferProgress>? progress = null)
    {
        if (!channelEnabled || !(settings.AutoDownloadLecram || force) || !await _lecramCheck.WaitAsync(0, ct)) return;
        var temp = Path.Combine(AppPaths.Cache, "lecram-download-" + Guid.NewGuid().ToString("N"));
        try
        {
            var channel = await client.CheckFoldersAsync(settings.LecramFolderKey, ct);
            if (!channel.Offline) settings.LecramFolderKey = channel.Key;
            if (channel.Offline) settings.LecramLastResult = "Offline; keeping the Lecram library (cached listing).";
            else if (channel.Latest is not { } latest) settings.LecramLastResult = "No v11 or newer builds found; keeping the Lecram library.";
            else if (latest.Hash.Equals(settings.LecramLastAssetHash, StringComparison.OrdinalIgnoreCase)
                     && settings.LecramLastBuildId is { } id && LecramBuilds.Any(b => b.Id == id))
                settings.LecramLastResult = $"Up to date: {settings.LecramLastVersion}.";
            else
            {
                Directory.CreateDirectory(temp);
                var package = Path.Combine(temp, "package");
                await client.DownloadAsync(latest, package, ct, progress);
                var set = Path.Combine(temp, "set");
                var addon = Path.Combine(set, Lecram11File);
                var host = false;
                if (latest.Rank == 2)
                {
                    Directory.CreateDirectory(set);
                    File.Move(package, addon);
                }
                else host = MediaFireClient.ExtractPackage(package, set);
                if (!LooksLikeLecramV11(addon)) throw new InvalidDataException("Downloaded add-on is not a recognised Lecram v11 build.");
                var before = LecramBuilds.Select(b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var build = ImportLecram(addon, downloaded: true);
                if (build.Generation != Generation.V11) throw new InvalidDataException("Downloaded add-on is not Lecram v11.");
                if (host) StoreLecramHost(build, set);
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
            if (!UserErrors.IsCurated(ex)) Log.Warn($"Lecram check failed ({ex.Message})");
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
    /// ShortFuse's add-on and Deep Fried Chicken are named in the error instead. The author's setup exe (regular or Early Access) and his
    /// add-on zip for a hand install are accepted too: their root dlss5-nr.addon64 is the build, and from v12.9 the 32-bit add-on and
    /// NR's host folder come with it (<see cref="StoreLecramHost"/>).
    /// </summary>
    public AddonBuild ImportLecram(string source) =>
        IsLecramPackage(source) ? ImportLecramPackage(source) : ImportLecram(source, downloaded: false);

    private static bool IsLecramPackage(string source) =>
        Path.GetExtension(source).Equals(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>The package is read in memory and only its known entries are written (<see cref="MediaFireClient.ExtractPackage"/>); the exe is never run.</summary>
    private AddonBuild ImportLecramPackage(string source)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("The file doesn't exist.", source);
        var temp = Path.Combine(AppPaths.Cache, "lecram-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            var host = MediaFireClient.ExtractPackage(source, temp);
            Log.Info($"Extracted {Lecram11File}{(host ? $", {Lecram32File} and {LecramHostDir}" : "")} from {source}");
            var build = ImportLecram(Path.Combine(temp, Lecram11File), downloaded: false);
            if (host && build.Generation == Generation.V11) StoreLecramHost(build, temp);
            return build;
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("Could not remove the Lecram import folder", ex); }
        }
    }

    /// <summary>
    /// Puts the host set extracted to <paramref name="set"/> (<see cref="LecramHostFiles"/>) into the build's folder beside its add-on. The
    /// same add-on imported again from a package replaces its set as a whole; a bare add-on import keeps it.
    /// A copy that fails part-way takes the set out again, so a build never holds half a host.
    /// </summary>
    private void StoreLecramHost(AddonBuild build, string set)
    {
        var dir = Path.Combine(AddonRoot(AddonKind.Lecram), build.Id);
        try
        {
            foreach (var rel in LecramHostFiles) FileUtil.AtomicCopy(Path.Combine(set, rel), Path.Combine(dir, rel));
        }
        catch
        {
            RemoveLecramHostFiles(dir);
            throw;
        }
        Log.Info($"Lecram {build.Label}: {Lecram32File} and NR's host ({LecramHostDir}) kept with the build");
    }

    private static void RemoveLecramHostFiles(string dir)
    {
        try
        {
            foreach (var rel in LecramHostFiles) FileUtil.DeleteFile(Path.Combine(dir, rel));
            FileUtil.DeleteDirectory(Path.Combine(dir, LecramHostDir));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error($"Could not remove the partial host set in {dir}", ex); }
    }

    /// <summary>
    /// The library folder of <paramref name="build"/> when it holds the whole host set (<see cref="LecramHostFiles"/>), null when it holds
    /// none (v10, a v11-generation build from before the host, or a bare dlss5-nr.addon64 import). Part of a set is an error: the library was
    /// changed by hand.
    /// </summary>
    public static string? LecramHostOf(AddonBuild build)
    {
        if (build.Generation != Generation.V11) return null;
        var dir = Path.Combine(AddonRoot(AddonKind.Lecram), build.Id);
        var present = LecramHostFiles.Count(rel => File.Exists(Path.Combine(dir, rel)));
        if (present == 0) return null;
        return present == LecramHostFiles.Length ? dir
            : throw new InvalidDataException($"The Lecram build {build.Name} has only part of NR's host in the library. Import its setup exe or add-on zip again.");
    }

    /// <summary>Whether <paramref name="build"/> carries the 32-bit add-on and NR's host; a partial set counts as none.</summary>
    public static bool LecramHasHost(AddonBuild build)
    {
        try { return LecramHostOf(build) is not null; }
        catch (InvalidDataException) { return false; }
    }

    /// <param name="downloaded">Fetched from Lecram's channel by the app, so the automatic cleanup may remove it later.</param>
    /// <param name="knownLabel">The version the catalog names the build by (its release tag); it wins over the binary's strings.</param>
    internal AddonBuild ImportLecram(string source, bool downloaded, string? knownLabel = null)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("The add-on file doesn't exist.", source);
        var original = FileUtil.OriginalFilename(source);
        Generation generation;
        if (string.IsNullOrEmpty(original))
        {
            if (!LooksLikeLecramV11(source))
                throw new InvalidDataException(
                    $"This file isn't Lecram's add-on. Choose {Lecram10File} (v10 and older), {Lecram11File} (v11 and newer), the author's DLSS5 NR setup exe or his add-on zip.");
            generation = Generation.V11;
        }
        else if (original.Equals(Lecram10File, StringComparison.OrdinalIgnoreCase)) generation = Generation.V10;
        else if (original.Equals(Lecram11File, StringComparison.OrdinalIgnoreCase)) generation = Generation.V11;
        else
        {
            var which = original.Equals(RenoDxFile, StringComparison.OrdinalIgnoreCase) || LegacyRenoDxFiles.Contains(original, StringComparer.OrdinalIgnoreCase)
                ? "ShortFuse's add-on; import it under the RenoDX DLSS add-on instead"
                : original.Equals(DfcFile, StringComparison.OrdinalIgnoreCase) ? "Deep Fried Chicken, which this app no longer installs"
                : null;
            throw new InvalidDataException(
                $"This file is {original}, {(which is null ? "not Lecram's add-on" : "which is " + which)}. Choose {Lecram10File} (v10 and older) or {Lecram11File} (v11 and newer).");
        }

        var file = generation == Generation.V10 ? Lecram10File : Lecram11File;
        var fileVersion = FileUtil.ReadVersion(source) is { } v ? FileUtil.Format(v) : "";
        // The catalog's version, else the string the build names itself by in its binary; without one the file version (v10) or the generation (v11) stands in.
        var known = IsLecramVersionLabel(knownLabel) ? knownLabel!.Trim() : null;
        var label = known ?? FindBuildLabel(File.ReadAllBytes(source)) ?? FallbackLecramLabel(fileVersion);
        var build = RetainAddon(AddonKind.Lecram, source, file, fileVersion, generation, label, downloaded);
        if (known is not null && build.DetectedLabel != known) build = RelabelAddon(AddonKind.Lecram, build, known);
        Log.Info($"{(downloaded ? "Downloaded" : "Imported")} {file} {build.Label} from {source}");
        return build;
    }
}
