using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

public enum NvGroup
{
    Dlss,
    FrameGeneration,
    SmoothMotion,
    RtxHdr,
    Sync,
    Other,
}

/// <summary>One value of a driver option: the settings it writes. No writes = "use global" (every id of the option is removed).</summary>
public sealed record NvChoice(string Label, string Description, IReadOnlyList<(uint Id, uint Value)> Writes)
{
    public bool IsInherit => Writes.Count == 0;
}

/// <summary>
/// One option of the per-game NVIDIA driver profile: the DRS setting ids it owns and the values it offers.
/// <see cref="Source"/> says where ids and values were checked.
/// </summary>
public sealed class NvOptionDef(string key, NvGroup group, string label, string hint, string topic, uint[] ids, IReadOnlyList<NvChoice> choices)
{
    public string Key { get; } = key;
    public NvGroup Group { get; } = group;
    public string Label { get; } = label;
    public string Hint { get; } = hint;
    /// <summary>HelpTopics id behind the info button.</summary>
    public string Topic { get; } = topic;
    public uint[] Ids { get; } = ids;
    /// <summary>The values to pick from, without the "use global" entry.</summary>
    public IReadOnlyList<NvChoice> Choices { get; } = choices;
    /// <summary>Game feature the setting acts on; None = applies to every game.</summary>
    public GameFeatures Requires { get; init; }
    /// <summary>Which ids/values were checked against what (file + id), for whoever updates the list next.</summary>
    public string Source { get; init; } = "";
    /// <summary>Writes to this setting have been seen to need administrator rights (research notes); the elevation prompt covers it.</summary>
    public bool MayNeedAdmin { get; init; }

    /// <summary>The choice whose writes all equal the value of each id, or null (custom).</summary>
    public NvChoice? Match(Func<uint, uint?> value) =>
        Choices.FirstOrDefault(c => !c.IsInherit && c.Writes.All(w => value(w.Id) == w.Value));
}

/// <summary>
/// The driver settings the per-game NVIDIA section offers. Every id and value was cross-checked against
/// nvidiaProfileInspector's <c>CustomSettingNames.xml</c> (Orbmu2k/nvidiaProfileInspector master, commit 337087c, 2026-10-13);
/// <c>Reference.xml</c> in the same repo is the source where noted. Ids the file lists without values, or at all unclear
/// ones, are not here (see <see cref="NotOffered"/>).
/// </summary>
public static class NvidiaProfileCatalog
{
    public const uint SrPreset = 0x10E41DF3, RrPreset = 0x10E41DF7, FgPreset = 0x10E41DF1;
    public const uint SrOverride = 0x10E41E01, RrOverride = 0x10E41E02, FgOverride = 0x10E41E03;
    public const uint SrMode = 0x10AFB768, SrRatio = 0x10E41DF5;
    public const uint MfgMode = 0x10308298, MfgFactor = 0x104D6667, MfgDynamicMax = 0x10562D0F, MfgTargetFps = 0x10CF4125;
    public const uint SmoothMotion = 0xB0D384C0, SmoothMotionApis = 0xB0CC0875;
    public const uint RtxHdrAllow = 0x1077A11A, RtxHdrEnable = 0x00DD48FB, RtxHdrDriverFlags = 0x00432F84;
    public const uint VsyncMode = 0x00A879CF, VsyncTear = 0x005A375C;
    public const uint LowLatencyState = 0x0005F543, LowLatencyEnabled = 0x10835000;
    public const uint FrameRateLimit = 0x10835002;
    public const uint Vibrance = 0x00980880, PowerMode = 0x1057EB71;

    public const uint TearStandard = 0x96861077, TearAdaptive = 0x99941284;
    public const uint RecommendedPreset = 0x00FFFFFF, RecommendedFgPreset = 0x00FFFFFE;
    public const uint MaxRefreshRate = 0x01000000;

    /// <summary>Frame Rate Limiter V3 accepts 0 (off) and 20..1000 (CustomSettingNames.xml, 0x10835002).</summary>
    public const int MinFrameRate = 20, MaxFrameRate = 1000;

    /// <summary>Things people ask for that this list leaves out, and why. Shown in the help topic.</summary>
    public static readonly IReadOnlyList<string> NotOffered =
    [
        "RTX HDR peak brightness (0x00DD48FC), middle grey (0x00DD48FD), contrast (0x00DD48FE) and saturation (0x00DD48FF): Profile Inspector names the ids but lists no values, " +
        "and its notes on the driver flags say HDR applied through the driver ignores brightness, contrast and saturation (the NVIDIA App applies those itself). Not offered until the value encoding is documented.",
        "Smooth Motion flip metering (0xB03A4546/7) and debug bars (0xB01B8B02): listed without a documented effect.",
        "Frame Rate Limiter background/NVCPL variants (0x10835005, 0x1083500A) and the legacy limiter (0x10834FEE): not needed for a per-game cap.",
    ];

    private static NvChoice C(string label, string description, params (uint Id, uint Value)[] writes) => new(label, description, writes);

    /// <summary>A preset choice also turns the override switch on, the way the NVIDIA App stores a preset (a preset without its switch may not take effect).</summary>
    private static NvChoice Preset(uint id, uint overrideId, uint value, string label, string description) => C(label, description, (id, value), (overrideId, 1));

    private static NvChoice Off(uint id, string description) => C("Off", description, (id, 0));

    /// <summary>"Off" for the DLSS presets: preset 0 together with the override switch off.</summary>
    private static NvChoice Off(uint id, uint overrideId, string description) => C("Off", description, (id, 0), (overrideId, 0));

    private static NvChoice[] OnOff(uint id, string offDescription, string onDescription) =>
        [C("Off", offDescription, (id, 0)), C("On", onDescription, (id, 1))];

    private static NvChoice Fps(uint id, int fps, string description) => C($"{fps} FPS", description, (id, (uint)fps));

    private static readonly int[] LimiterSteps = [30, 40, 60, 72, 90, 117, 120, 138, 141, 144, 157, 162, 165, 225, 237, 240];
    private static readonly int[] DynamicTargetSteps = [60, 72, 90, 100, 120, 144, 165, 240];
    private static readonly int[] ScaleSteps = [40, 45, 50, 55, 60, 65, 70, 77, 80, 85, 90, 95];

    public static IReadOnlyList<NvOptionDef> Create() =>
    [
        // ---- DLSS overrides. Source: CustomSettingNames.xml 0x10E41DF3 / DF7 / DF1 (preset letters), 0x10E41E01-03 (DLL override), 0x10AFB768 + 0x10E41DF5 (quality level, scaling ratio).
        new("sr-preset", NvGroup.Dlss, "DLSS Super Resolution preset", "Force a DLSS SR model preset for this game.", "nv-sr-preset", [SrPreset, SrOverride],
        [
            Off(SrPreset, SrOverride, "No SR preset forced for this game, even if the global profile has one (stored as 0, which Profile Inspector calls N/A)."),
            Preset(SrPreset, SrOverride, RecommendedPreset, "Recommended", "DLSS picks per quality mode: K for DLAA / Quality / Balanced, M for Performance, L for Ultra Performance."),
            Preset(SrPreset, SrOverride, 0x0B, "K", "Transformer generation 1. Default for DLAA, Quality and Balanced."),
            Preset(SrPreset, SrOverride, 0x0A, "J", "Transformer generation 1. Like K with slightly less ghosting but more flicker."),
            Preset(SrPreset, SrOverride, 0x0D, "M", "Transformer generation 2. Default for Performance mode."),
            Preset(SrPreset, SrOverride, 0x0C, "L", "Transformer generation 2. Default for Ultra Performance mode."),
        ]) { Requires = GameFeatures.SR, Source = "CustomSettingNames.xml 0x10E41DF3, 0x10E41E01" },
        new("sr-mode", NvGroup.Dlss, "DLSS render resolution", "Override the game's DLSS quality mode.", "nv-sr-mode", [SrMode, SrRatio],
        [
            C("Game setting", "Explicitly let the game choose (value 3, which Profile Inspector calls N/A).", (SrMode, 3)),
            C("DLAA", "Native resolution, DLSS only as anti-aliasing. Highest cost.", (SrMode, 4)),
            C("Quality (67%)", "Renders at 67% per axis.", (SrMode, 2)),
            C("Balanced (58%)", "Renders at 58% per axis.", (SrMode, 1)),
            C("Performance (50%)", "Renders at 50% per axis.", (SrMode, 0)),
            C("Ultra Performance (33%)", "Renders at 33% per axis.", (SrMode, 5)),
            .. ScaleSteps.Select(p => C($"Custom {p}%", p == 77 ? "Profile Inspector calls 77% 'Ultra Quality'." : "Exact render scale per axis (driver 572.83 or newer).", (SrMode, 6), (SrRatio, (uint)p))),
        ]) { Requires = GameFeatures.SR, Source = "CustomSettingNames.xml 0x10AFB768 (Custom = 6), 0x10E41DF5 (ratio = percent, 0x21..0x64)" },
        new("rr-preset", NvGroup.Dlss, "DLSS Ray Reconstruction preset", "Force a DLSS RR model preset for this game.", "nv-rr-preset", [RrPreset, RrOverride],
        [
            Off(RrPreset, RrOverride, "No RR preset forced for this game (stored as 0, N/A in Profile Inspector)."),
            Preset(RrPreset, RrOverride, RecommendedPreset, "Recommended", "The driver picks the preset."),
            Preset(RrPreset, RrOverride, 0x04, "D", "Transformer generation 1."),
            Preset(RrPreset, RrOverride, 0x06, "F", "Transformer generation 2."),
        ]) { Requires = GameFeatures.RR, Source = "CustomSettingNames.xml 0x10E41DF7, 0x10E41E02" },
        new("fg-preset", NvGroup.Dlss, "DLSS Frame Generation preset", "Force a DLSS FG model for this game.", "nv-fg-preset", [FgPreset, FgOverride],
        [
            Off(FgPreset, FgOverride, "No FG preset forced for this game (stored as 0, N/A in Profile Inspector)."),
            Preset(FgPreset, FgOverride, RecommendedFgPreset, "NVIDIA default", "The driver's recommended FG model."),
            Preset(FgPreset, FgOverride, 0x01, "A", "Profile Inspector: 'Default Preset'."),
            Preset(FgPreset, FgOverride, 0x02, "B", "Profile Inspector: 'UI Focus'."),
        ]) { Requires = GameFeatures.FG, Source = "CustomSettingNames.xml 0x10E41DF1, 0x10E41E03" },

        // ---- Multi frame generation. Source: CustomSettingNames.xml 0x10308298, 0x104D6667, 0x10562D0F (driver 595.97+), 0x10CF4125 (driver 595.97+).
        new("mfg-mode", NvGroup.FrameGeneration, "Multi frame generation mode", "Force fixed or dynamic multi frame generation.", "nv-mfg-mode", [MfgMode],
        [
            Off(MfgMode, "No mode forced (stored as 0, N/A in Profile Inspector)."),
            C("Fixed", "Uses the fixed generation factor below.", (MfgMode, 2)),
            C("Dynamic", "Uses the dynamic maximum and target frame rate below.", (MfgMode, 4)),
        ]) { Requires = GameFeatures.FG, Source = "CustomSettingNames.xml 0x10308298" },
        new("mfg-factor", NvGroup.FrameGeneration, "Fixed generation factor", "Frames shown per rendered frame in fixed mode.", "nv-mfg", [MfgFactor],
        [
            C("Game setting", "Explicitly use the game's own multiplier (stored as 0, N/A in Profile Inspector).", (MfgFactor, 0)),
            .. Enumerable.Range(2, 5).Select(n => C($"{n}x", $"{n - 1} generated frame{(n == 2 ? "" : "s")} per rendered frame. {n}x and up needs an RTX 50 card.", (MfgFactor, (uint)(n - 1)))),
        ]) { Requires = GameFeatures.FG, Source = "CustomSettingNames.xml 0x104D6667 (1 = 2x .. 5 = 6x)" },
        new("mfg-dynamic-max", NvGroup.FrameGeneration, "Dynamic maximum", "Highest multiplier dynamic mode may use.", "nv-mfg-dynamic", [MfgDynamicMax],
            [.. Enumerable.Range(2, 5).Select(n => C($"Up to {n}x", "Needs driver 595.97 or newer.", (MfgDynamicMax, (uint)(n - 1))))])
            { Requires = GameFeatures.FG, Source = "CustomSettingNames.xml 0x10562D0F" },
        new("mfg-dynamic-fps", NvGroup.FrameGeneration, "Dynamic target frame rate", "Output frame rate dynamic mode aims for.", "nv-mfg-dynamic-fps", [MfgTargetFps],
        [
            C("Max refresh rate", "Aim for the display's refresh rate.", (MfgTargetFps, MaxRefreshRate)),
            .. DynamicTargetSteps.Select(f => Fps(MfgTargetFps, f, "Needs driver 595.97 or newer.")),
        ]) { Requires = GameFeatures.FG, Source = "CustomSettingNames.xml 0x10CF4125 (0x01000000 = max refresh, otherwise fps from 60)" },

        // ---- Smooth Motion. Source: CustomSettingNames.xml 0xB0D384C0 (driver 571.86+), 0xB0CC0875 (bitfield 1 DX12, 2 DX11, 4 Vulkan).
        new("smooth-motion", NvGroup.SmoothMotion, "Smooth Motion", "Driver frame generation for games without DLSS FG.", "nv-smooth-motion", [SmoothMotion],
            OnOff(SmoothMotion, "Off for this game.", "The driver interpolates frames (RTX 40 / 50, driver 571.86+)."))
            { MayNeedAdmin = true, Source = "CustomSettingNames.xml 0xB0D384C0; admin note from docs/research/rhi-2026-10.md 9.2" },
        new("smooth-motion-apis", NvGroup.SmoothMotion, "Smooth Motion graphics APIs", "Which graphics APIs Smooth Motion may act on.", "nv-smooth-motion-apis", [SmoothMotionApis],
        [
            C("DX12 + DX11", "Not Vulkan. Use this for Vulkan games that run the neural-rendering add-ons.", (SmoothMotionApis, 3)),
            C("All (DX12 + DX11 + Vulkan)", "The driver default.", (SmoothMotionApis, 7)),
            C("DX12 only", "", (SmoothMotionApis, 1)),
            C("DX11 only", "", (SmoothMotionApis, 2)),
            C("Vulkan only", "", (SmoothMotionApis, 4)),
            C("None", "Smooth Motion acts on no API.", (SmoothMotionApis, 0)),
        ]) { MayNeedAdmin = true, Source = "CustomSettingNames.xml 0xB0CC0875" },

        // ---- RTX HDR. Source: Reference.xml 0x1077A11A (RTX HDR Allow); CustomSettingNames.xml 0x00DD48FB (enable), 0x00432F84 (driver flags).
        new("rtx-hdr-allow", NvGroup.RtxHdr, "RTX HDR allowed", "Whether the profile may use RTX HDR at all.", "nv-rtx-hdr-allow", [RtxHdrAllow],
        [
            C("Disallow", "RTX HDR can't be turned on for this game.", (RtxHdrAllow, 0)),
            C("Allow", "RTX HDR may be turned on for this game.", (RtxHdrAllow, 1)),
        ]) { Source = "Reference.xml 0x1077A11A" },
        new("rtx-hdr", NvGroup.RtxHdr, "RTX HDR", "AI SDR-to-HDR conversion by the driver.", "nv-rtx-hdr", [RtxHdrEnable],
            OnOff(RtxHdrEnable, "Off for this game.", "Converts SDR to HDR. Needs Windows HDR on; costs some FPS."))
            { Source = "CustomSettingNames.xml 0x00DD48FB" },
        new("rtx-hdr-debanding", NvGroup.RtxHdr, "RTX HDR debanding", "Driver flags: HDR applied by the driver, with a debanding strength.", "nv-rtx-hdr-debanding", [RtxHdrDriverFlags],
        [
            C("Off", "No driver flags (0x00): the NVIDIA App applies RTX HDR.", (RtxHdrDriverFlags, 0x00)),
            C("No debanding", "HDR applied via the driver (0x06).", (RtxHdrDriverFlags, 0x06)),
            C("Medium debanding", "HDR applied via the driver (0x0A).", (RtxHdrDriverFlags, 0x0A)),
            C("Very high debanding", "HDR applied via the driver (0x02).", (RtxHdrDriverFlags, 0x02)),
        ]) { Source = "CustomSettingNames.xml 0x00432F84 (the 0x03 indicator and 0x23 debug values are left out)" },

        // ---- Sync and latency. Source: CustomSettingNames.xml 0x00A879CF + 0x005A375C (VSync), 0x0005F543 + 0x10835000 (low latency), 0x10835002 (limiter V3, driver 441.87+).
        new("vsync", NvGroup.Sync, "Vertical sync", "Driver VSync override (same choices as the NVIDIA Control Panel).", "nv-vsync", [VsyncMode, VsyncTear],
        [
            C("Use the 3D application setting", "The game decides.", (VsyncMode, 0x60925292), (VsyncTear, TearStandard)),
            C("Off", "Force off. Lowest latency, tearing without VRR.", (VsyncMode, 0x08416747), (VsyncTear, TearStandard)),
            C("On", "Force on. The usual choice with G-Sync plus an FPS cap.", (VsyncMode, 0x47814940), (VsyncTear, TearStandard)),
            C("Fast", "Fast Sync: no tearing above refresh without VSync's latency.", (VsyncMode, 0x18888888), (VsyncTear, TearStandard)),
            C("Adaptive", "VSync on at refresh rate, off below it (tears instead of stuttering).", (VsyncMode, 0x47814940), (VsyncTear, TearAdaptive)),
            C("Adaptive (half refresh rate)", "Adaptive at half the refresh rate.", (VsyncMode, 0x32610244), (VsyncTear, TearAdaptive)),
            C("1/2 refresh rate", "One frame per two refreshes.", (VsyncMode, 0x32610244), (VsyncTear, TearStandard)),
            C("1/3 refresh rate", "One frame per three refreshes.", (VsyncMode, 0x71271021), (VsyncTear, TearStandard)),
            C("1/4 refresh rate", "One frame per four refreshes.", (VsyncMode, 0x13245256), (VsyncTear, TearStandard)),
        ]) { Source = "CustomSettingNames.xml 0x00A879CF, 0x005A375C" },
        new("low-latency", NvGroup.Sync, "Low latency mode", "Driver low-latency mode (Control Panel: Off / On / Ultra).", "nv-low-latency", [LowLatencyState, LowLatencyEnabled],
        [
            C("Off", "Both low-latency settings off.", (LowLatencyState, 0), (LowLatencyEnabled, 0)),
            C("On", "Control Panel state 'On'; the Ultra flag stays off.", (LowLatencyState, 1), (LowLatencyEnabled, 0)),
            C("Ultra", "Control Panel state 'Ultra'; the Ultra flag is on.", (LowLatencyState, 2), (LowLatencyEnabled, 1)),
        ]) { MayNeedAdmin = true, Source = "CustomSettingNames.xml 0x0005F543, 0x10835000; admin note from docs/research/rhi-2026-10.md 9.2" },
        new("frame-limit", NvGroup.Sync, "Max frame rate", "Driver FPS limiter (Frame Rate Limiter V3).", "nv-fps", [FrameRateLimit],
        [
            C("Off", "No limit for this game.", (FrameRateLimit, 0)),
            .. LimiterSteps.Select(f => Fps(FrameRateLimit, f, f is 117 or 138 or 141 or 157 or 162 or 225 or 237
                ? "A few FPS under a common refresh rate: keeps G-Sync / VRR active." : $"Cap at {f} FPS.")),
        ]) { Source = "CustomSettingNames.xml 0x10835002 (0 = off, 20..1000)" },

        // ---- Not in the lists above, kept from the earlier version of the tab. Source: CustomSettingNames.xml 0x00980880, 0x1057EB71.
        new("vibrance", NvGroup.Other, "RTX Dynamic Vibrance", "AI colour and saturation boost for SDR.", "nv-vibrance", [Vibrance],
            OnOff(Vibrance, "Off for this game.", "Adds vibrance / clarity to SDR output.")) { Source = "CustomSettingNames.xml 0x00980880" },
        new("power", NvGroup.Other, "Power management", "GPU clock behaviour.", "nv-power", [PowerMode],
        [
            C("Optimal power", "Clocks drop when the frame is unchanged.", (PowerMode, 5)),
            C("Adaptive", "Clocks follow load.", (PowerMode, 0)),
            C("Prefer max performance", "Holds high clocks; fewer stutters from clock changes, more power.", (PowerMode, 1)),
        ]) { Source = "CustomSettingNames.xml 0x1057EB71" },
    ];
}
