namespace DLSSUpdater.Core;

/// <summary>
/// ShortFuse's renodx-dlss add-on isn't published on any official GitHub, so it is import-only like a user-supplied
/// ReShade64.dll: the file is checked by its version resource and kept in the add-on library
/// (one folder per SHA-256, see AddonLibrary.cs).
/// </summary>
public sealed partial class ComponentStore
{
    /// <summary>Only a file that names itself renodx-dlss.addon64 is accepted; sibling add-ons are named in the error.</summary>
    public AddonBuild ImportRenoDx(string source)
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
        var build = RetainAddon(AddonKind.RenoDx, source, RenoDxFile, version, null, version);
        Log.Info($"Imported {RenoDxFile} {build.Label} from {source}");
        return build;
    }

    /// <summary>Warning for installing the chosen ShortFuse build next to Lecram's add-on, or null when there is none.</summary>
    public string? SfLecramWarning(bool renoDx, bool lecram, string? renoDxId) =>
        renoDx && lecram && RenoDxBuildFor(renoDxId) is { } sf ? LecramCompat.Warning(renoDx, lecram, sf.ParsedVersion) : null;
}
