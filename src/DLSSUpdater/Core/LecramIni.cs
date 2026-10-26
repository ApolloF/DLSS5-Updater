namespace DLSSUpdater.Core;

/// <summary>
/// Where Lecram's DLSS5 add-on keeps its settings in ReShade.ini (docs/addons/lecram-dlss5.md). Both generations use
/// the one section; a few keys exist in one generation only.
/// </summary>
public static class LecramIni
{
    public const string Section = "RenoDX.DLSS5";

    /// <summary>
    /// Set to "observe" so a v11 build keeps running next to ShortFuse's add-on; its default "yield" stands down. v12's default
    /// "auto" already runs beside it (<see cref="AutoRunsBesideForeignNr"/>).
    /// </summary>
    public const string ForeignNrKey = "ForeignNr";

    /// <summary>v10 and older only: the character mask is always on in v11.</summary>
    private static readonly HashSet<string> V10Only = new(["NRAutoMask"], StringComparer.OrdinalIgnoreCase);

    /// <summary>v11 only: v10.7 does not read these.</summary>
    private static readonly HashSet<string> V11Only = new(
        ["NeuralUplift", "NRPreset", "NRPass2Preset", "NRPass3Preset", "NRPass4Preset", "NRVramBuffering", "NRGpuCrashWitness", "NRPresentFgPath"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>v11 keys that only later builds read, by the first checked build that has them (docs/addons/lecram-dlss5.md).</summary>
    private static readonly Dictionary<string, string> FirstV11Build = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NRGpuCrashWitness"] = "v11.0.0-beta.22",
        ["NRPass2Preset"] = "v11.0.0-beta.29",
        ["NRPass3Preset"] = "v11.0.0-beta.29",
        ["NRPass4Preset"] = "v11.0.0-beta.29",
        ["NRPresentFgPath"] = "v11.0.0-beta.48",
    };

    /// <summary>v11-generation keys a later build no longer acts on, by the first checked build that retired them (it still reads them and logs <c>retired=1</c>).</summary>
    private static readonly Dictionary<string, string> RetiredIn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NRPresentFgPath"] = "v12.0.0",
    };

    /// <summary>The first build whose default ForeignNr=auto keeps running next to another NR producer such as ShortFuse's add-on.</summary>
    private const string FirstAutoForeignNrBuild = "v12.0.0";

    public static bool IsLecram(IniOverride o) => o.Section.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase);

    public static bool IsForeignNr(IniOverride o) => IsLecram(o) && o.Key.Trim().Equals(ForeignNrKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// False for a key the build doesn't read: the other generation's keys, v11 keys newer than <paramref name="build"/>
    /// (its detected label) and keys it retired. Everything is available while nothing is imported; a build without a known label
    /// gets every v11 key.
    /// </summary>
    public static bool IsAvailable(string key, LecramGeneration? generation, string? build = null) => generation switch
    {
        LecramGeneration.V10 => !V11Only.Contains(key),
        LecramGeneration.V11 => !V10Only.Contains(key) && !OlderThanFirstBuild(key.Trim(), build) && !Retired(key.Trim(), build),
        _ => true,
    };

    /// <summary>True when <paramref name="build"/>'s default ForeignNr already runs beside ShortFuse's add-on, so the app needn't write observe.</summary>
    public static bool AutoRunsBesideForeignNr(LecramGeneration? generation, string? build) =>
        generation == LecramGeneration.V11 && AtLeast(build, FirstAutoForeignNrBuild);

    private static bool OlderThanFirstBuild(string key, string? build) =>
        !string.IsNullOrWhiteSpace(build) && FirstV11Build.TryGetValue(key, out var first) && !AtLeast(build, first);

    private static bool Retired(string key, string? build) => RetiredIn.TryGetValue(key, out var retired) && AtLeast(build, retired);

    private static bool AtLeast(string? build, string first) =>
        !string.IsNullOrWhiteSpace(build) && BuildOrder.Key(build).CompareTo(BuildOrder.Key(first)) >= 0;

    /// <summary>The 31 model-tuning axes that v11's one-time migration resets to default.</summary>
    public static readonly string[] ModelAxisKeys =
    [
        "NRModelStyleOffset",
        "NRModelToneOffset",
        "NRModelStructureOffset",
        "NRModelChromaDirection",
        "NRModelLumaDirection",
        "NRModelNoiseDirection",
        "NRModelMidAttentionGain",
        "NRModelLateAttentionGain",
        "NRModelDecoderSkipGain",
        "NRModelRgbHeadGain",
        "NRModelStyleSensitivity",
        "NRModelToneSensitivity",
        "NRModelStructureSensitivity",
        "NRModelSkinSensitivity",
        "NRModelMaskSensitivity",
        "NRModelEncoderShallowQGain",
        "NRModelEncoderDeepQGain",
        "NRModelGlobalAttentionQGain",
        "NRModelDecoderBroadQGain",
        "NRModelDecoderMediumQGain",
        "NRModelDecoderEntryQGain",
        "NRModelDecoderFineQGain",
        "NRModelSkip512Gain",
        "NRModelSkip256Gain",
        "NRModelSkip128Gain",
        "NRModelSkip64Gain",
        "NRModelFullLowGain",
        "NRModelFullSourceGain",
        "NRModelHistoryHeadGain",
        "NRModelVitFfnResidualGain",
        "NRModelVitAttentionResidualGain",
    ];

    /// <summary>True when a v11 build would treat this section as v10 and run its one-time migration on first load.</summary>
    public static bool NeedsV11Migration(IniFile ini)
    {
        var cvStr = ini.Get(Section, "ConfigVersion");
        if (cvStr is not null && int.TryParse(cvStr, System.Globalization.CultureInfo.InvariantCulture, out var cv))
            return cv <= 10;
        return ini.Get(Section, "NeuralUplift") is not null;
    }

    /// <summary>Applies v11's one-time config migration so the add-on sees an already-migrated section and keeps our chosen NRHookPoint.</summary>
    public static bool MigrateToV11(IniFile ini)
    {
        if (!NeedsV11Migration(ini)) return false;

        ini.Set(Section, "ConfigVersion", "11");
        ini.Set(Section, "NRModelCarryoverReset", "1");
        foreach (var key in ModelAxisKeys)
        {
            if (ini.Get(Section, key) is not null)
            {
                var def = key.EndsWith("Offset", StringComparison.OrdinalIgnoreCase) ||
                          key.EndsWith("Direction", StringComparison.OrdinalIgnoreCase)
                          ? "0" : "1";
                ini.Set(Section, key, def);
            }
        }
        return true;
    }
}

/// <summary>The warning for a pair the user marked as crashed; build versions do not imply compatibility.</summary>
public static class LecramCompat
{
    public static string? Message(bool renoDx, bool lecram, AddonPairTest? pair)
    {
        if (!renoDx || !lecram || pair?.Status != AddonPairStatus.Crashed) return null;
        var marked = pair.Marked is { } date ? date.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "date unknown";
        return $"This ShortFuse + Lecram pair crashed before (marked {marked}). Pick another pair or install only one of them.";
    }
}
