namespace DLSSUpdater.Core;

/// <summary>
/// Where Lecram's DLSS5 add-on keeps its settings in ReShade.ini (docs/addons/lecram-dlss5.md). Both generations use
/// the one section; a few keys exist in one generation only.
/// </summary>
public static class LecramIni
{
    public const string Section = "RenoDX.DLSS5";

    /// <summary>Set to "observe" so Lecram keeps running next to ShortFuse's add-on; its default "yield" stands down.</summary>
    public const string ForeignNrKey = "ForeignNr";

    /// <summary>v10 and older only: the character mask is always on in v11.</summary>
    private static readonly HashSet<string> V10Only = new(["NRAutoMask"], StringComparer.OrdinalIgnoreCase);

    /// <summary>v11 only: v10.7 does not read these.</summary>
    private static readonly HashSet<string> V11Only = new(["NeuralUplift", "NRPreset", "NRVramBuffering"], StringComparer.OrdinalIgnoreCase);

    public static bool IsLecram(IniOverride o) => o.Section.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase);

    public static bool IsForeignNr(IniOverride o) => IsLecram(o) && o.Key.Trim().Equals(ForeignNrKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>False for a key the imported generation doesn't read; everything is available while nothing is imported.</summary>
    public static bool IsAvailable(string key, LecramGeneration? generation) => generation switch
    {
        LecramGeneration.V10 => !V11Only.Contains(key),
        LecramGeneration.V11 => !V10Only.Contains(key),
        _ => true,
    };

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

/// <summary>
/// ShortFuse's renodx-dlss and Lecram's add-on were tested together on the 0928 build of ShortFuse's; newer builds
/// crashed alongside it.
/// </summary>
public static class LecramCompat
{
    /// <summary>Compared on major.minor.build: file version 0.2026.0928.x.</summary>
    public static readonly Version TestedSfVersion = new(0, 2026, 928);

    /// <summary>The warning for installing both add-ons with a ShortFuse build newer than the tested one; null otherwise.</summary>
    public static string? Warning(bool renoDx, bool lecram, Version? shortFuse)
    {
        if (!renoDx || !lecram || shortFuse is null) return null;
        if (new Version(shortFuse.Major, shortFuse.Minor, Math.Max(shortFuse.Build, 0)) <= TestedSfVersion) return null;
        return $"ShortFuse renodx-dlss {FileUtil.Format(shortFuse)} is newer than the build tested with Lecram's add-on (0.2026.0928); " +
               "newer builds crashed together with it. Import the 0928 build or install only one of them.";
    }
}
