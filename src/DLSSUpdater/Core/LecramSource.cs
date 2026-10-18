using System.Text.Json.Serialization;
using Generation = DLSSUpdater.Core.LecramGeneration;

namespace DLSSUpdater.Core;

/// <summary>Lecram's add-on ships in two generations that can't run together in one game; each build in the library knows its own.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LecramGeneration>))]
public enum LecramGeneration { V10, V11 }

/// <summary>
/// Lecram's DLSS5 Neural Rendering add-on is distributed by its author to supporters, so it is import-only like
/// ShortFuse's add-on. v10 and older (renodx-dlss5.addon64) name themselves in the version resource; v11+
/// (dlss5-nr.addon64) has no version resource and is recognised by strings in the binary. Every imported build stays in the
/// library; a game gets the one chosen for it, under its generation's file name.
/// </summary>
public sealed partial class ComponentStore
{
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
    public AddonBuild ImportLecram(string source)
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
        var build = RetainAddon(AddonKind.Lecram, source, file, fileVersion, generation, label);
        Log.Info($"Imported {file} {build.Label} from {source}");
        return build;
    }
}
