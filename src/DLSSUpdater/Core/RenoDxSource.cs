namespace DLSSUpdater.Core;

/// <summary>
/// ShortFuse's renodx-dlss add-on isn't published on any official GitHub, so it is import-only like a user-supplied
/// ReShade64.dll: the file is checked by its version resource and kept in the components folder.
/// </summary>
public sealed partial class ComponentStore
{
    public string RenoDxPath => Path.Combine(AppPaths.Components, RenoDxFile);
    public bool RenoDxImported => File.Exists(RenoDxPath);

    /// <summary>Version shown for the imported add-on, or null when none is imported.</summary>
    public string? RenoDxVersion => RenoDxImported ? FileUtil.Format(FileUtil.ReadVersion(RenoDxPath)) : null;

    /// <summary>"{version}-{sha[..12]}" of the imported add-on; this is what an install records.</summary>
    public string? RenoDxTag => RenoDxImported ? RenoDxTagOf(RenoDxPath) : null;

    internal static string RenoDxTagOf(string path) =>
        $"{FileUtil.Format(FileUtil.ReadVersion(path))}-{FileUtil.Sha256(path)[..12]}";

    /// <summary>Only a file that names itself renodx-dlss.addon64 is accepted; sibling add-ons are named in the error.</summary>
    public string ImportRenoDx(string source)
    {
        if (FileUtil.ReadVersion(source) is null)
            throw new InvalidDataException("Choose renodx-dlss.addon64 with a readable version resource. Extract archives first.");
        var original = FileUtil.OriginalFilename(source);
        if (!string.Equals(original, RenoDxFile, StringComparison.OrdinalIgnoreCase))
        {
            var what = string.IsNullOrEmpty(original) ? "a file without an original filename" : original;
            throw new InvalidDataException(
                $"This file is {what}, not {RenoDxFile}. " +
                (LegacyRenoDxFiles.Contains(original ?? "", StringComparer.OrdinalIgnoreCase)
                    ? "That add-on has no neural rendering. "
                    : original is not null && original.StartsWith("renodx-dlss5", StringComparison.OrdinalIgnoreCase)
                        ? "That is a different neural-rendering add-on. "
                        : "") +
                $"Choose ShortFuse's {RenoDxFile}.");
        }
        Directory.CreateDirectory(AppPaths.Components);
        FileUtil.AtomicCopy(source, RenoDxPath);
        Log.Info($"Imported {RenoDxFile} {RenoDxVersion} from {source}");
        return RenoDxTagOf(RenoDxPath);
    }

    public void RemoveRenoDxImport()
    {
        if (File.Exists(RenoDxPath)) File.Delete(RenoDxPath);
    }

    /// <summary>The imported add-on, or a clear error when none was imported.</summary>
    internal string RequireRenoDx() => RenoDxImported
        ? RenoDxPath
        : throw new FileNotFoundException($"Import {RenoDxFile} in Settings → Local components first.", RenoDxPath);
}
