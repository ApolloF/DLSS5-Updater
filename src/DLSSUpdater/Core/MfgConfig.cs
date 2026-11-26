namespace DLSSUpdater.Core;

/// <summary>
/// MFG Unlock's config contract (mavismmg/MFGAdaUnlock-RenoDx, MIT); see docs/addons/mfg-unlock.md. The add-on reads two files:
/// the game's ReShade.ini, section <see cref="Section"/> (the keys the app offers, all present since 1.4.1), and
/// <see cref="AddonIni"/> beside the game (performance overlay, DLSS 5 neural rendering, diagnostics, written by the add-on's own panel).
/// The app never writes <see cref="AddonIni"/>: its DLSS 5 neural rendering (<c>[DLSS5] Enabled</c>, off by default) would be a second
/// neural route next to the one the app installs (docs/advisor.md, one neural consumer), and the add-on only stands aside for
/// another add-on whose file name has "dlss5" in it or that created NR through the driver (1.12.1 nr_runtime.hpp, Foreign()).
/// </summary>
public static class MfgConfig
{
    public const string TestedVersion = "1.12.1";
    public const string Section = "RenoDX.MFGUnlock";
    public const string AddonIni = "MFGAdaUnlock.ini";

    public static string? Warning(string? tag) => FileUtil.ParseTag(tag) > FileUtil.ParseTag(TestedVersion)
        ? $"MFG Unlock {tag} is newer than the tested {TestedVersion}; its settings may have changed." : null;
}
