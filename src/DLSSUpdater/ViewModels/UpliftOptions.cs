using System.Collections.ObjectModel;
using System.Globalization;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

public static partial class SettingsOptions
{
    public static IniOptionViewModel[] Uplift(ObservableCollection<IniOverride> rs)
    {
        IniOptionViewModel Choices(string label, string hint, string key, params OptionChoice[] choices)
        {
            var value = UpliftConfig.Defaults[key];
            var def = choices.First(c => IniOptionViewModel.Same(c.Value, value));
            return new IniOptionViewModel(label, hint, UpliftConfig.Section, key, rs,
                [new($"Default · {def.Label}", value, def.Description), .. choices, new("Leave game setting", null)]).About("uplift");
        }
        IniOptionViewModel Enum(string label, string hint, string key, params string[] labels) =>
            Choices(label, hint, key, labels.Select((name, i) => new OptionChoice(name, i.ToString(CultureInfo.InvariantCulture))).ToArray());
        IniOptionViewModel Bool(string label, string hint, string key) => Enum(label, hint, key, "Off", "On");
        IniOptionViewModel Number(string label, string hint, string key, params double[] values) =>
            Choices(label, hint, key, values.Select(v => new OptionChoice(v.ToString(CultureInfo.InvariantCulture), v.ToString(CultureInfo.InvariantCulture))).ToArray());
        double[] Unit() => Enumerable.Range(0, 11).Select(i => i / 10d).ToArray();
        return
        [
            Number("Enable hotkey", "Virtual-key code 0-255; 0 means no hotkey.", "EnableKey", Enumerable.Range(0, 256).Select(i => (double)i).ToArray()),
            Number("Intensity", "NR strength, 0-10; the overlay slider covers 0-1.", "Intensity", [.. Unit(), 2, 3, 4, 5, 6, 7, 8, 9, 10]),
            Enum("Style", "NR style A, B or C.", "Style", "Style A", "Style B", "Style C"),
            Enum("Network preset", "Preset B (Small FP8) needs NR 310.9.16; its kernels are experimental and RTX 50 only.", "Preset", "Runtime default", "A: Legacy", "B: Small FP8"),
            Number("Pass count", "Number of NR passes, 1-10.", "PassCount", Enumerable.Range(1, 10).Select(i => (double)i).ToArray()),
            Enum("Model resolution", "Resolution at which NR runs. Custom uses Resolution scale.", "ResolutionMode", "Full", "Quality 67%", "Balanced 58%", "Performance 50%", "Match game", "Custom"),
            Number("Resolution scale", "Custom model resolution, 25-100%.", "ResolutionScale", Enumerable.Range(25, 76).Select(i => (double)i).ToArray()),
            Enum("NR stage", "Auto chooses the NR stage; After DLSS and Present select it explicitly.", "Source", "Auto", "After DLSS", "Present"),
            Bool("Before upscaling", "Run NR before upscaling (experimental).", "PreUpscale"),
            Bool("Keep faces", "Preserve faces; costs one extra NR run per frame.", "KeepFaces"),
            Number("Face protection", "Face protection strength, 0-1.", "FaceProtection", Unit()),
            Number("Lighting scale", "Lighting scale as a percentage of image height, 0.5-10%.", "LightingScale", 0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 7.5, 10),
            Bool("Character mask", "Enable the automatic character mask.", "AutoMask"),
            Number("Structure", "Local structure strength, 0-1.", "LocalStructure", Unit()),
            Number("Tone", "Local tone strength, 0-1.", "LocalTone", Unit()),
            Number("Skin structure", "Skin structure strength, 0-1; -1 follows Structure.", "SkinStructure", [-1, .. Unit()]),
            Enum("UI correction", "Auto, disable or enable UI correction.", "UICorrection", "Auto", "Off", "On"),
            Enum("Motion vectors", "Motion-vector source for NR.", "MotionVectors", "Auto", "DLSS", "Launchpad", "None", "Lumenite"),
            Enum("Upsampling", "Classic or edge-aware upsampling.", "Upsampling", "Classic", "Edge-aware"),
            Bool("Present with frame generation", "Runs NR on generated frames too: 3-4x the Present cost at 3x/4x frame generation, can wipe out its gain. After DLSS avoids this.", "PresentWithFrameGen"),
            Bool("Cross-queue passes", "Experimental, 64-bit DX12 only: a DLSS-stage pass and a later Present pass may run on different GPU queues. Can cost FPS and latency; with frame generation it also needs Present with frame generation.", "AllowCrossQueuePasses"),
            Enum("Input encoding", "Auto detects the input encoding; other choices select it explicitly.", "Encoding", "Auto", "Linear BT.709", "sRGB", "BT.2100 PQ", "scRGB", "scRGB-nl"),
            Number("Diffuse white", "0 = automatic; otherwise 48-500 nits.", "DiffuseWhiteNits", 0, 48, 80, 100, 150, 200, 250, 300, 400, 500),
            Enum("Input exposure", "Exposure source; Auto combines the game and Uplift meter.", "InputExposure", "Auto", "Game", "Metered", "Manual"),
            Enum("Auto exposure mode", "Blend or switch exposure sources; used only with Input exposure Auto.", "AutoExposureMode", "Blend", "Switch"),
            Number("Auto exposure blend", "0 = game exposure; 1 = Uplift meter. Used with automatic exposure.", "AutoExposureBlend", Unit()),
            Enum("Exposure adaptation", "Off or smooth exposure adaptation.", "ExposureAdapt", "Off", "Smooth"),
            Enum("VRAM check", "VRAM budget check policy.", "VramCheck", "Careful", "Relaxed", "Off"),
            Number("Grace period", "Grace period in seconds, 0-60.", "GraceSeconds", Enumerable.Range(0, 61).Select(i => (double)i).ToArray()),
            Enum("NGX hooks", "Off is safe mode: Present only. Takes effect from the next game start.", "NgxHooks", "Auto", "Off"),
            Enum("Foreign NR", "Yield stands down for a foreign NR producer; Observe watches it. Keep other NR routes uninstalled.", "ForeignNr", "Yield", "Observe"),
            Enum("Vulkan NR", "Native, Direct3D 12 or a separate helper process. Vulkan needs ReShade 6.8+.", "VulkanNr", "Native", "Direct3D 12", "Helper"),
            Bool("Use D3D9Ex", "From next start; exclusive fullscreen becomes borderless.", "UseD3D9Ex"),
            Bool("NVIDIA indicator", "Show NVIDIA's indicator from the next load.", "ShowNvIndicator"),
            Enum("Log level", "Amount of information written to the log.", "LogLevel", "Error", "Warning", "Info", "Debug"),
            Enum("Mask blend mode", "NR 310.9.16 only. The meanings of values 0-3 are undocumented.", "MaskBlendMode", "0 (undocumented)", "1 (undocumented)", "2 (undocumented)", "3 (undocumented)"),
        ];
    }
}
