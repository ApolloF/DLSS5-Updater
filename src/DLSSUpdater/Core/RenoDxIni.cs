namespace DLSSUpdater.Core;

/// <summary>
/// Where ShortFuse's renodx-dlss add-on keeps its settings in ReShade.ini. Like every RenoDX mod (renodx
/// utils/settings.hpp) it reads global settings from <see cref="Section"/> and all others from
/// <see cref="PresetSection"/>: at load it always uses preset slot 1, the overlay's slot choice is not saved.
/// The split below is taken from a ReShade.ini the add-on wrote itself.
/// </summary>
public static class RenoDxIni
{
    public const string Section = "RENODX-DLSS";
    public const string PresetSection = Section + "-preset1";

    private static readonly HashSet<string> PresetKeys = new(
        new[]
        {
            "AutoMask", "BlockHudless", "DownsampleFilter", "Exposure", "ExposureMode", "FilterDomain", "Intensity",
            "LocalStructureStrength", "LocalToneStrength", "PassCount", "Preset", "ProcessingScale",
            "ScaleReconstruction", "SignalEncoding", "SkinStructureStrength", "Style", "UpsampleFilter",
        }.Select(k => "DirectNeuralRendering" + k),
        StringComparer.OrdinalIgnoreCase);

    public static bool IsPresetKey(string key) => PresetKeys.Contains(key.Trim());

    /// <summary>The section the add-on reads <paramref name="key"/> from.</summary>
    public static string SectionOf(string key) => IsPresetKey(key) ? PresetSection : Section;

    /// <summary>True for either of the add-on's sections.</summary>
    public static bool IsRenoDx(IniOverride o) =>
        o.Section.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase)
        || o.Section.Trim().Equals(PresetSection, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Moves per-preset keys that older versions of this app stored under the global section, where the add-on
    /// never reads them. Returns true when something moved.
    /// </summary>
    public static bool Migrate(List<IniOverride>? list)
    {
        if (list is null) return false;
        var moved = false;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var o = list[i];
            if (!o.Section.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase) || !IsPresetKey(o.Key)) continue;
            moved = true;
            if (list.Any(x => x.Section.Trim().Equals(PresetSection, StringComparison.OrdinalIgnoreCase)
                              && x.Key.Trim().Equals(o.Key.Trim(), StringComparison.OrdinalIgnoreCase)))
                list.RemoveAt(i);
            else
                o.Section = PresetSection;
        }
        return moved;
    }

    /// <summary>Migrates every override list in the settings (global, presets, per-game layers and presets).</summary>
    public static bool Migrate(AppSettings s)
    {
        var moved = Migrate(s.ReShadeOverrides);
        foreach (var p in s.Presets) moved |= Migrate(p.ReShade);
        foreach (var g in s.Games.Values)
        {
            moved |= Migrate(g.ReShade);
            foreach (var p in g.Presets ?? []) moved |= Migrate(p.ReShade);
        }
        return moved;
    }
}
