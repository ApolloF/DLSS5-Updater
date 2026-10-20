using System.IO.Compression;
using System.Text.Json;

namespace DLSSUpdater.Core;

/// <summary>
/// Deep Fried Chicken is closed source and its licence allows personal use only: no redistribution, rehosting or
/// modification. It is import-only: the user's own files are kept byte for byte in the components folder and copied
/// unchanged into games. Nothing is ever downloaded. Every import is a build of the add-on library
/// (<see cref="AddonKind.Dfc"/>): components\dfc\&lt;sha256 of the add-on&gt;\ holds its whole 64-bit set, the default cfg
/// and version.json.
/// </summary>
public sealed partial class ComponentStore
{
    public static string DfcDir => Path.Combine(AppPaths.Components, "dfc");
    public static string DfcBuildDir(AddonBuild build) => Path.Combine(DfcDir, build.Id);
    public static string DfcConfigOf(AddonBuild build) => Path.Combine(DfcBuildDir(build), DfcConfigFile);
    public bool DfcImported => DfcBuilds.Count > 0;

    /// <summary>
    /// From 4.x (INSTALL.txt 4.0.6 preview) DFC also runs in games without their own DLSS, on its own or an external motion
    /// provider's vectors. 2.x needed the game's DLSS.
    /// </summary>
    public const int DfcNonDlssMajor = 4;

    /// <summary>Whether <paramref name="build"/> runs in games without their own DLSS.</summary>
    public static bool DfcSupportsNonDlssGames(AddonBuild build) => build.ParsedVersion is { Major: >= DfcNonDlssMajor };

    /// <summary>
    /// Whether <paramref name="relative"/> (a path relative to the game folder) is one of the files the 64-bit set had
    /// before imports took the whole folder: the add-on, the bridge, the present-support dll, anything in the neural
    /// folder and DFC's own ReShade effects. Installs recorded without <see cref="InstallManifest.DfcFiles"/> rely on it.
    /// </summary>
    public static bool IsDfcPackagePath(string relative)
    {
        var rel = relative.Replace('/', '\\').TrimStart('\\');
        if (rel.StartsWith(DfcNeuralDir + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        var dir = Path.GetDirectoryName(rel) ?? "";
        var name = Path.GetFileName(rel);
        if (dir.Length == 0) return DfcPackageFiles.Contains(name, StringComparer.OrdinalIgnoreCase);
        return dir.Equals(DfcShaderDir, StringComparison.OrdinalIgnoreCase) && name.StartsWith(DfcShaderPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a file of the 64-bit folder (path relative to it) is copied into games. Not taken: the cfg (kept as the
    /// default and merged separately), logs, what the user adds per README.txt or the app places itself (NVIDIA's nvngx_*
    /// runtimes, ReShade and its proxy dlls, all beside the exe), an install record of ours and the library's version.json.
    /// </summary>
    public static bool IsDfcSetFile(string relative)
    {
        var rel = relative.Replace('/', '\\').TrimStart('\\');
        var name = Path.GetFileName(rel);
        if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return false;
        if (rel.Contains('\\')) return !rel.StartsWith(InstallManifest.DirName + "\\", StringComparison.OrdinalIgnoreCase);
        return !name.Equals(DfcConfigFile, StringComparison.OrdinalIgnoreCase)
            && !name.Equals(BuildRecordFile, StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("nvngx_", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase)
            && !AppSettings.IsKnownProxy(name);
    }

    /// <summary>Most files and bytes a whole-folder import takes; more means the folder is not DFC's 64-bit folder (v5: 11 files, 75 MB).</summary>
    public const int DfcMaxFiles = 64;
    public const long DfcMaxBytes = 1L << 30;

    /// <summary>The 64-bit set of <paramref name="build"/> as paths relative to its folder (the game folder layout), without the cfg.</summary>
    public static IReadOnlyList<string> DfcInstallFiles(AddonBuild build)
    {
        var dir = DfcBuildDir(build);
        return !Directory.Exists(dir) ? []
            : Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f))
                .Where(IsDfcSetFile)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// <paramref name="source"/> is deep-fried-chicken.addon64, the folder that holds it, the extracted package folder
    /// with its 64-bit subfolder, or a .zip of one of these. The NGX bridge must sit beside the add-on; the default cfg is
    /// taken when present. From 4.x the package says to copy the contents of the 64-bit folder next to the game exe, so
    /// the whole folder is taken (see <see cref="IsDfcSetFile"/>) and must hold the present-support dll and the neural
    /// folder. 2.x takes the add-on and the bridge only.
    /// Everything is staged first and becomes one library build; other builds are never touched. The same add-on again is
    /// the same build: its set is replaced as a whole by the new one (so no file of an older set stays behind), its name and
    /// import date stay.
    /// </summary>
    public AddonBuild ImportDfc(string source)
    {
        if (source.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The app can't open {Path.GetFileName(source)}. Extract it first (Deep Fried Chicken's .7z needs the password from its release post), then import the extracted folder.");
        if (!source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return ImportDfcFrom(source, source);
        var unpacked = DfcDir + ".zip";
        try
        {
            ExtractDfcZip(source, unpacked);
            return ImportDfcFrom(FindDfcAddon(unpacked) ?? unpacked, source);
        }
        finally
        {
            if (Directory.Exists(unpacked)) Directory.Delete(unpacked, true);
        }
    }

    private AddonBuild ImportDfcFrom(string source, string shownSource)
    {
        var addon = ResolveDfcAddon(source);
        if (!File.Exists(addon))
            throw new InvalidDataException($"{DfcFile} not found in {shownSource}. Choose the extracted Deep Fried Chicken release folder, its 64-bit folder, {DfcFile} or a .zip of the release.");
        if (FileUtil.ReadVersion(addon) is not { } version)
            throw new InvalidDataException($"Choose {DfcFile} with a readable version resource.");
        var original = FileUtil.OriginalFilename(addon);
        if (!string.Equals(original, DfcFile, StringComparison.OrdinalIgnoreCase))
        {
            var what = string.IsNullOrEmpty(original) ? "a file without an original filename" : original;
            throw new InvalidDataException($"This file is {what}, not {DfcFile}. Choose Deep Fried Chicken's {DfcFile}.");
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(addon))!;
        if (File.Exists(Path.Combine(dir, DfcHost64Exe)))
            throw new InvalidDataException($"This is the companion set of DFC's 32-bit route ({Path.GetFileName(dir)}). Import from the package's 64-bit folder; 32-bit games are not supported by this app.");
        var bridge = Path.Combine(dir, DfcBridgeFile);
        if (!File.Exists(bridge))
            throw new InvalidDataException($"{DfcBridgeFile} is missing next to {Path.GetFileName(addon)}. Import from the extracted release folder, which holds both files.");
        var wholeFolder = version.Major >= DfcNonDlssMajor;
        var files = wholeFolder ? WholeFolderIn(dir) : LegacyFilesIn(dir);
        if (wholeFolder)
        {
            if (!files.Contains(DfcPresentSupportFile, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"{DfcPresentSupportFile} is missing next to {DfcFile}. Deep Fried Chicken {version.Major}.x needs it; import the whole 64-bit folder.");
            if (!files.Any(f => f.StartsWith(DfcNeuralDir + "\\", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"The {DfcNeuralDir} folder is missing next to {DfcFile}. Deep Fried Chicken {version.Major}.x needs it whole; import the whole 64-bit folder.");
        }
        var config = Path.Combine(dir, DfcConfigFile);

        var staging = DfcDir + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        try
        {
            foreach (var rel in files) FileUtil.AtomicCopy(Path.Combine(dir, rel), Path.Combine(staging, rel));
            if (File.Exists(config)) FileUtil.AtomicCopy(config, Path.Combine(staging, DfcConfigFile));
            var build = StoreDfcBuild(staging, FileUtil.Format(version));
            Log.Info($"Imported {DfcFile} {build.FileVersion} from {shownSource} ({files.Count} files)");
            if (DfcCompat([], build.Id) is { HasIssues: true } report) Log.Info("Config check: " + report.Summary.ReplaceLineEndings(" "));
            return build;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    /// <summary>
    /// Moves a staged set into the library as the build its add-on names. A build that is already there is swapped out
    /// whole; when the swap fails it is put back, so a failed import leaves the library as it was.
    /// </summary>
    private AddonBuild StoreDfcBuild(string staging, string version)
    {
        var id = FileUtil.Sha256(Path.Combine(staging, DfcFile));
        var build = DfcBuilds.FirstOrDefault(b => b.Id == id) ?? new AddonBuild(id, DfcFile, version, null, version, null, DateTime.UtcNow);
        FileUtil.AtomicWriteText(Path.Combine(staging, BuildRecordFile), JsonSerializer.Serialize(build, JsonCtx.Default.AddonBuild));
        var dest = DfcBuildDir(build);
        var old = dest + DeletingSuffix;
        Directory.CreateDirectory(DfcDir);
        if (Directory.Exists(dest))
        {
            FileUtil.DeleteDirectory(old);
            Directory.Move(dest, old);
        }
        try { Directory.Move(staging, dest); }
        catch
        {
            if (Directory.Exists(old) && !Directory.Exists(dest)) Directory.Move(old, dest);
            throw;
        }
        finally { ForgetAddons(AddonKind.Dfc); }
        // A copy in use stays as a deleted build's rest, which the cleanup lists.
        try { FileUtil.DeleteDirectory(old); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Could not delete the replaced set {old}: {ex.Message}"); }
        return build;
    }

    /// <summary>
    /// First run with the library: an import of the single-slot layout (its set directly in components\dfc) becomes a
    /// build. Everything moves into the build folder and the add-on goes last, after version.json, so a run that stops
    /// part-way leaves the add-on where the next run finds it again. An unreadable import is logged and stays.
    /// </summary>
    public void MigrateDfc()
    {
        var legacy = Path.Combine(DfcDir, DfcFile);
        if (!File.Exists(legacy)) return;
        try
        {
            var version = FileUtil.Format(FileUtil.ReadVersion(legacy));
            var build = new AddonBuild(FileUtil.Sha256(legacy), DfcFile, version, null, version, null, File.GetCreationTimeUtc(legacy));
            var dest = DfcBuildDir(build);
            Directory.CreateDirectory(dest);
            foreach (var entry in Directory.EnumerateFileSystemEntries(DfcDir).ToList())
            {
                var target = Path.Combine(dest, Path.GetFileName(entry));
                if (entry.Equals(dest, StringComparison.OrdinalIgnoreCase) || entry.Equals(legacy, StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(entry)) Directory.Move(entry, target);
                else File.Move(entry, target, overwrite: true);
            }
            FileUtil.AtomicWriteText(Path.Combine(dest, BuildRecordFile), JsonSerializer.Serialize(build, JsonCtx.Default.AddonBuild));
            File.Move(legacy, Path.Combine(dest, DfcFile), overwrite: true);
            ForgetAddons(AddonKind.Dfc);
            Log.Info($"Moved the imported Deep Fried Chicken {version} into the add-on library");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log.Error("Could not move the imported Deep Fried Chicken into the add-on library", ex); }
    }

    /// <summary>The add-on path: the file itself, the folder holding it, or the package folder's 64-bit subfolder.</summary>
    private static string ResolveDfcAddon(string source)
    {
        if (!Directory.Exists(source)) return source;
        var direct = Path.Combine(source, DfcFile);
        if (File.Exists(direct)) return direct;
        var x64 = Path.Combine(source, DfcPackage64Dir, DfcFile);
        return File.Exists(x64) ? x64 : direct;
    }

    /// <summary>Every file of the 64-bit folder that goes into games, relative to it. A folder holding much more than DFC's set is refused.</summary>
    private static List<string> WholeFolderIn(string dir)
    {
        var files = new List<string>();
        long bytes = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(dir, f);
            if (!IsDfcSetFile(rel)) continue;
            files.Add(rel);
            bytes += new FileInfo(f).Length;
            if (files.Count > DfcMaxFiles || bytes > DfcMaxBytes)
                throw new InvalidDataException($"{dir} holds more than Deep Fried Chicken's files. Choose the package's 64-bit folder or the {DfcFile} in it.");
        }
        return files;
    }

    /// <summary>2.x: the add-on and the bridge beside it; nothing else in that folder (it may be Downloads) is taken.</summary>
    private static List<string> LegacyFilesIn(string dir) =>
        Directory.EnumerateFiles(dir).Select(f => Path.GetFileName(f)).Where(IsDfcPackagePath).ToList();

    /// <summary>Extracts a .zip after a size check; ZipFile refuses entries that would land outside <paramref name="dest"/>.</summary>
    private static void ExtractDfcZip(string zip, string dest)
    {
        if (Directory.Exists(dest)) Directory.Delete(dest, true);
        using (var archive = ZipFile.OpenRead(zip))
        {
            if (archive.Entries.Count > 4 * DfcMaxFiles || archive.Entries.Sum(e => e.Length) > 2 * DfcMaxBytes)
                throw new InvalidDataException($"{Path.GetFileName(zip)} holds more than a Deep Fried Chicken release. Extract it and import the 64-bit folder.");
        }
        ZipFile.ExtractToDirectory(zip, dest);
    }

    /// <summary>The one add-on in an unpacked archive that is not the 32-bit route's host64 copy; null when there is none or more than one.</summary>
    private static string? FindDfcAddon(string root)
    {
        if (!Directory.Exists(root)) return null;
        var addons = Directory.EnumerateFiles(root, DfcFile, SearchOption.AllDirectories)
            .Where(f => !File.Exists(Path.Combine(Path.GetDirectoryName(f)!, DfcHost64Exe))).ToList();
        return addons.Count == 1 ? addons[0] : null;
    }

    /// <summary>
    /// A build (<paramref name="id"/>; null is the default build) against the version and config schema this app was tested
    /// with, plus which of <paramref name="managedKeys"/> its default cfg lacks. Null when there is no such build.
    /// </summary>
    public DfcCompatReport? DfcCompat(IEnumerable<string> managedKeys, string? id = null)
    {
        if (DfcBuildFor(id) is not { } build) return null;
        var cfg = DfcConfigOf(build);
        return DfcConfig.Check(build.FileVersion, File.Exists(cfg) ? File.ReadAllText(cfg) : null, managedKeys);
    }
}
