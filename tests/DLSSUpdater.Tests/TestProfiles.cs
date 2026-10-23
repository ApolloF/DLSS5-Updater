using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>A non-empty OptiScaler.ini / ReShade.ini profile for tests (the values of the retired Recommended preset).</summary>
internal static class TestProfiles
{
    public static List<IniOverride> Recommended() =>
    [
        new("Upscalers", "Dx12Upscaler", "dlss"),
        new("DLSS", "RenderPresetOverride", "true"),
        new("DLSS", "RenderPresetForAll", "12"),
        new("Menu", "ShortcutKey", "0x2e"),
        new("Plugins", "LoadReshade", "true"),
        new("Log", "LogToFile", "true"),
        new("Log", "LogLevel", "2"),
        new("DlssNr", "Enabled", "true"),
        new("DlssNr", "RunBeforeSR", "true"),
        new("DlssNr", "ColourStrength", "0.200000"),
        new("DlssNr", "LocalStructure", "0.700000"),
        new("DlssNr", "LocalTone", "0.250000"),
        new("DlssNr", "SkinStructure", "0.500000"),
        new("DlssNr", "ToggleKey", "0xdc"),
        new("DlssNr", "WhitePointSource", "3"),
        new("DlssNr", "ReversibleMode", "3"),
    ];

    public static List<IniOverride> RecommendedReShade() =>
    [
        new("OVERLAY", "TutorialProgress", "4"),
        new(RenoDxIni.Section, "DirectNeuralRenderingHookStage", "1"),
        new(RenoDxIni.PresetSection, "DirectNeuralRenderingPassCount", "1"),
    ];
}
