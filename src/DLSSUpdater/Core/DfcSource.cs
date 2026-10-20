using System.IO.Compression;

namespace DLSSUpdater.Core;

/// <summary>
/// Deep Fried Chicken is closed source and its licence allows personal use only: no redistribution, rehosting or
/// modification. It is import-only: the user's own files are kept byte for byte in the components folder and copied
/// unchanged into games. Nothing is ever downloaded.
/// </summary>
public sealed partial class ComponentStore
{
    public static string DfcDir => Path.Combine(AppPaths.Components, "dfc");
    public string DfcPath => Path.Combine(DfcDir, DfcFile);
    public string DfcBridgePath => Path.Combine(DfcDir, DfcBridgeFile);
    public string DfcConfigPath => Path.Combine(DfcDir, DfcConfigFile);
    public bool DfcImported => File.Exists(DfcPath) && File.Exists(DfcBridgePath);

    /// <summary>Version shown for the imported add-on, or null when none is imported.</summary>
    public string? DfcVersion => DfcImported ? FileUtil.Format(FileUtil.ReadVersion(DfcPath)) : null;

    /// <summary>
    /// From 4.x (INSTALL.txt 4.0.6 preview) DFC also runs in games without their own DLSS, on its own or an external motion
    /// provider's vectors. 2.x needed the game's DLSS.
    /// </summary>
    public const int DfcNonDlssMajor = 4;

    /// <summary>Whether the imported add-on runs in games without their own DLSS; false when none is imported.</summary>
    public bool DfcSupportsNonDlssGames => DfcImported && FileUtil.ReadVersion(DfcPath) is { Major: >= DfcNonDlssMajor };

    /// <summary>"{version}-{sha[..12]}" of the imported add-on; this is what an install records.</summary>
    public string? DfcTag => DfcImported ? DfcTagOf(DfcPath) : null;

    internal static string DfcTagOf(string path) =>
        $"{FileUtil.Format(FileUtil.ReadVersion(path))}-{HashCache.Get(path)[..12]}";

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
    /// runtimes, ReShade and its proxy dlls, all beside the exe) and an install record of ours.
    /// </summary>
    public static bool IsDfcSetFile(string relative)
    {
        var rel = relative.Replace('/', '\\').TrimStart('\\');
        var name = Path.GetFileName(rel);
        if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return false;
        if (rel.Contains('\\')) return !rel.StartsWith(InstallManifest.DirName + "\\", StringComparison.OrdinalIgnoreCase);
        return !name.Equals(DfcConfigFile, StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("nvngx_", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase)
            && !AppSettings.IsKnownProxy(name);
    }

    /// <summary>Most files and bytes a whole-folder import takes; more means the folder is not DFC's 64-bit folder (v5: 11 files, 75 MB).</summary>
    public const int DfcMaxFiles = 64;
    public const long DfcMaxBytes = 1L << 30;

    /// <summary>The imported 64-bit set as paths relative to <see cref="DfcDir"/> (the game folder layout), without the cfg.</summary>
    public IReadOnlyList<string> DfcInstallFiles() => !Directory.Exists(DfcDir) ? []
        : Directory.EnumerateFiles(DfcDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(DfcDir, f))
            .Where(IsDfcSetFile)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// <paramref name="source"/> is deep-fried-chicken.addon64, the folder that holds it, the extracted package folder
    /// with its 64-bit subfolder, or a .zip of one of these. The NGX bridge must sit beside the add-on; the default cfg is
    /// taken when present. From 4.x the package says to copy the contents of the 64-bit folder next to the game exe, so
    /// the whole folder is taken (see <see cref="IsDfcSetFile"/>) and must hold the present-support dll and the neural
    /// folder. 2.x takes the add-on and the bridge only.
    /// Everything is staged first and replaces the previous import as a whole, so no file of an older set stays behind.
    /// </summary>
    public string ImportDfc(string source)
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

    private string ImportDfcFrom(string source, string shownSource)
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
        foreach (var rel in files) FileUtil.AtomicCopy(Path.Combine(dir, rel), Path.Combine(staging, rel));
        if (File.Exists(config)) FileUtil.AtomicCopy(config, Path.Combine(staging, DfcConfigFile));
        if (Directory.Exists(DfcDir)) Directory.Delete(DfcDir, true);
        Directory.Move(staging, DfcDir);
        Log.Info($"Imported {DfcFile} {DfcVersion} from {shownSource} ({files.Count} files)");
        if (DfcCompat([]) is { HasIssues: true } report) Log.Info("Config check: " + report.Summary.ReplaceLineEndings(" "));
        return DfcTagOf(DfcPath);
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
    /// The imported add-on against the version and config schema this app was tested with, plus which of
    /// <paramref name="managedKeys"/> its default cfg lacks. Null when nothing is imported.
    /// </summary>
    public DfcCompatReport? DfcCompat(IEnumerable<string> managedKeys) => !DfcImported ? null
        : DfcConfig.Check(DfcVersion, File.Exists(DfcConfigPath) ? File.ReadAllText(DfcConfigPath) : null, managedKeys);

    public void RemoveDfcImport()
    {
        if (Directory.Exists(DfcDir)) Directory.Delete(DfcDir, true);
    }

    /// <summary>The imported add-on, or a clear error when none was imported.</summary>
    internal string RequireDfc() => DfcImported
        ? DfcPath
        : throw new FileNotFoundException($"Import {DfcFile} in Settings → Local components first.", DfcPath);
}
