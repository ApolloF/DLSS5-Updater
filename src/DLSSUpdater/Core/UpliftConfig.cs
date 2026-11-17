namespace DLSSUpdater.Core;

/// <summary>GITC Uplift v2.0.0's ReShade.ini contract; see docs/addons/gitc-uplift.md.</summary>
public static class UpliftConfig
{
    public const string TestedVersion = "2.0.0";
    public const string Section = "Uplift";
    public static bool IsUplift(IniOverride o) => o.Section.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase);
    public static string NrPath(string target) => Path.Combine(target, ComponentStore.UpliftNrDir, ComponentStore.DlssNrFile);
    public static string? Warning(string? tag) => FileUtil.ParseTag(tag) > FileUtil.ParseTag(TestedVersion)
        ? $"GITC Uplift {tag} is newer than the tested {TestedVersion}; its settings or package may have changed." : null;

    /// <summary>
    /// ReShade finds effects only in [GENERAL] EffectSearchPaths, resolved against the folder of its dll; without the key it searches
    /// ".\" alone, not recursively (crosire/reshade runtime.cpp). Uplift ships its effects in reshade-shaders\Shaders. Returns the
    /// list with that folder appended, or null when an entry already covers it.
    /// </summary>
    public static string? EffectSearchPathsFor(string? current, string baseDir)
    {
        var shaders = Path.GetFullPath(Path.Combine(baseDir, ComponentStore.ShaderDir)).TrimEnd('\\');
        var entries = (current ?? @".\").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (var entry in entries)
        {
            var p = entry.Trim('"').Replace('/', '\\');
            var recursive = p.EndsWith("**", StringComparison.Ordinal);
            if (recursive) p = p[..^2];
            if (p.Length == 0) p = @".\";
            string full;
            try { full = Path.GetFullPath(p, baseDir).TrimEnd('\\'); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { continue; }
            if (full.Equals(shaders, StringComparison.OrdinalIgnoreCase) || (recursive && FileUtil.IsUnder(shaders, full))) return null;
        }
        entries.Add(@".\" + ComponentStore.ShaderDir + @"\**");
        return string.Join(',', entries);
    }

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["StartEnabled"] = "0", ["EnableKey"] = "0", ["Intensity"] = "1.0", ["Style"] = "0", ["Preset"] = "1", ["PassCount"] = "1",
        ["ResolutionMode"] = "0", ["ResolutionScale"] = "100", ["Source"] = "0", ["PreUpscale"] = "0",
        ["KeepFaces"] = "0", ["FaceProtection"] = "0", ["LightingScale"] = "2.5", ["AutoMask"] = "1",
        ["LocalStructure"] = "1.0", ["LocalTone"] = "1.0", ["SkinStructure"] = "1.0", ["UICorrection"] = "0",
        ["MotionVectors"] = "0", ["Upsampling"] = "1", ["PresentWithFrameGen"] = "0", ["AllowCrossQueuePasses"] = "0", ["Encoding"] = "0",
        ["DiffuseWhiteNits"] = "0", ["InputExposure"] = "0", ["AutoExposureMode"] = "0", ["AutoExposureBlend"] = "0.8",
        ["ExposureAdapt"] = "1", ["VramCheck"] = "0", ["GraceSeconds"] = "5", ["NgxHooks"] = "0",
        ["ForeignNr"] = "0", ["VulkanNr"] = "0", ["UseD3D9Ex"] = "0", ["ShowNvIndicator"] = "0",
        ["LogLevel"] = "2", ["MaskBlendMode"] = "1",
    };
}
