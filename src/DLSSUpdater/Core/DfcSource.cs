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

    /// <summary>"{version}-{sha[..12]}" of the imported add-on; this is what an install records.</summary>
    public string? DfcTag => DfcImported ? DfcTagOf(DfcPath) : null;

    internal static string DfcTagOf(string path) =>
        $"{FileUtil.Format(FileUtil.ReadVersion(path))}-{HashCache.Get(path)[..12]}";

    /// <summary>
    /// Whether <paramref name="relative"/> (a path relative to the game folder) is one of the files the 64-bit set puts
    /// beside the game exe: the add-on, the bridge, the present-support dll, anything in the neural folder and DFC's
    /// own ReShade effects. The cfg is not included.
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

    /// <summary>The imported 64-bit set as paths relative to <see cref="DfcDir"/> (the game folder layout), without the cfg.</summary>
    public IReadOnlyList<string> DfcInstallFiles() => !Directory.Exists(DfcDir) ? []
        : Directory.EnumerateFiles(DfcDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(DfcDir, f))
            .Where(IsDfcPackagePath)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// <paramref name="source"/> is deep-fried-chicken.addon64, the folder that holds it, or the extracted package folder
    /// with its 64-bit subfolder. The NGX bridge must sit beside the add-on; the default cfg is taken when present. From
    /// 4.x INSTALL.txt also lists the present-support dll and the neural folder, which must then be there too.
    /// Everything is staged first and replaces the previous import as a whole, so no file of an older set stays behind.
    /// </summary>
    public string ImportDfc(string source)
    {
        var addon = ResolveDfcAddon(source);
        if (!File.Exists(addon))
            throw new InvalidDataException($"{DfcFile} not found in {source}. Extract the Deep Fried Chicken download first and choose the release folder or {DfcFile}.");
        if (FileUtil.ReadVersion(addon) is not { } version)
            throw new InvalidDataException($"Choose {DfcFile} with a readable version resource. Extract archives first.");
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
        var files = PackageFilesIn(dir);
        if (version.Major >= 4)
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
        Log.Info($"Imported {DfcFile} {DfcVersion} from {source} ({files.Count} files)");
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

    /// <summary>The files of the 64-bit set in <paramref name="dir"/>, relative to it; nothing else in that folder is taken.</summary>
    private static List<string> PackageFilesIn(string dir)
    {
        // Only the set's own places are searched: the add-on may lie in a big folder such as Downloads.
        var neural = Path.Combine(dir, DfcNeuralDir);
        var shaders = Path.Combine(dir, DfcShaderDir);
        return Directory.EnumerateFiles(dir)
            .Concat(Directory.Exists(neural) ? Directory.EnumerateFiles(neural, "*", SearchOption.AllDirectories) : [])
            .Concat(Directory.Exists(shaders) ? Directory.EnumerateFiles(shaders) : [])
            .Select(f => Path.GetRelativePath(dir, f))
            .Where(IsDfcPackagePath)
            .ToList();
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
