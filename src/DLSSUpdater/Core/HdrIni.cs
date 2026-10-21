namespace DLSSUpdater.Core;

/// <summary>
/// The config keys the RenoDX HDR component writes: the reshade.ini [renodx] section for the generic add-ons, Unreal Extended's
/// Engine.ini HDR keys and the global peak brightness. Facts: docs/addons/renodx-hdr.md. Only keys that are missing are written
/// (<see cref="MissingOnly"/>), apart from the peak brightness, which the user set in this app.
/// </summary>
public static class HdrIni
{
    public const string Section = "renodx";

    /// <summary>The render-target formats the add-ons upgrade, as in their Upgrade_&lt;FORMAT&gt; keys.</summary>
    public static readonly IReadOnlyList<string> UpgradeFormats =
    [
        "B10G10R10A2_UNORM", "B8G8R8A8_TYPELESS", "B8G8R8A8_UNORM", "B8G8R8A8_UNORM_SRGB", "R10G10B10A2_TYPELESS", "R10G10B10A2_UNORM",
        "R11G11B10_FLOAT", "R16G16B16A16_TYPELESS", "R8G8B8A8_SNORM", "R8G8B8A8_TYPELESS", "R8G8B8A8_UNORM", "R8G8B8A8_UNORM_SRGB",
    ];

    private static readonly string[] UpgradeOptions = ["Upgrade_CopyDestinations", "Upgrade_SwapChainCompatibility", "Upgrade_UseSCRGB"];

    public static IEnumerable<string> UpgradeKeys => UpgradeFormats.Select(f => "Upgrade_" + f).Concat(UpgradeOptions);

    public const string EngineSystem = "SystemSettings", EngineRenderer = "/Script/Engine.RendererSettings";

    /// <summary>What Unreal Extended's HDR path needs in Engine.ini (the ini method).</summary>
    public static readonly IReadOnlyList<(string Section, string Key, string Value)> EngineHdrKeys =
    [
        (EngineSystem, "r.AllowHDR", "1"),
        (EngineSystem, "r.HDR.EnableHDROutput", "1"),
        (EngineSystem, "r.HDR.Display.OutputDevice", "3"),
        (EngineSystem, "r.HDR.Display.ColorGamut", "2"),
        (EngineSystem, "r.HDR.UI.CompositeMode", "1"),
        (EngineRenderer, "r.LUT.UpdateEveryFrame", "1"),
    ];

    public const int MinPeakNits = 80, MaxPeakNits = 10000;
    public const string PeakKey = "ToneMapPeakNits";
    public static readonly IReadOnlyList<string> PeakSections = ["renodx-preset1", "renodx-preset2", "renodx-preset3"];

    /// <summary>The [renodx] keys wanted for a pick; empty for a named mod, which has its own config.</summary>
    public static IReadOnlyList<(string Section, string Key, string Value)> ReShadeKeys(HdrChoice choice)
    {
        var keys = new List<(string Key, string Value)>();
        switch (choice.Kind)
        {
            case HdrKind.UeExtended when choice.Ue is { } ue:
                keys.AddRange([("DumpLUTShaders", "0"), ("ForceBorderless", "1"), ("FxUpgradeRender", "1"), ("PreventFullscreen", "1"), ("SettingsMode", "2"),
                    ("Set_Path", ue.SetPath.ToString())]);
                keys.AddRange(UpgradeKeys.Select(k => (k, "")));
                // The upgrade path names the formats to upgrade; the one key per format is the only value forced.
                foreach (var (format, mode) in ue.Upgrades) Force(keys, "Upgrade_" + format, mode.ToString());
                if (ue.CopyDestinations is { } copy) Force(keys, "Upgrade_CopyDestinations", copy ? "1" : "0");
                break;
            case HdrKind.UnrealGeneric:
                keys.AddRange(new[] { "DumpLUTShaders", "ForceBorderless", "PreventFullscreen", "SettingsMode" }.Select(k => (k, "")));
                keys.AddRange(UpgradeKeys.Select(k => (k, "")));
                break;
            case HdrKind.UnityGeneric:
                keys.AddRange(new[]
                {
                    "Blit_Copy_Hack", "DumpUberShaders", "ForceBorderless", "Force_Pipeline_Cloning", "PreventFullscreen", "Scaling_Offset", "SettingsMode",
                    "Swapchain_Encoding", "Tonemap_Offset", "Use_Resource_Cloning", "Use_Swapchain_Proxy",
                }.Select(k => (k, "")));
                keys.AddRange(UpgradeKeys.Select(k => (k, "")));
                break;
        }
        return keys.Select(k => (Section, k.Key, k.Value)).ToList();
    }

    private static void Force(List<(string Key, string Value)> keys, string key, string value)
    {
        var i = keys.FindIndex(k => k.Key == key);
        if (i >= 0) keys[i] = (key, value);
        else keys.Add((key, value));
    }

    /// <summary>The Engine.ini keys of an Unreal Extended pick, or none when its method doesn't need them.</summary>
    public static IReadOnlyList<(string Section, string Key, string Value)> EngineKeys(HdrChoice choice) =>
        choice is { Kind: HdrKind.UeExtended, Ue: { WritesEngineIni: true } ue }
            ? ue.EngineIni.Count > 0 ? ue.EngineIni : EngineHdrKeys
            : [];

    public static IReadOnlyList<(string Section, string Key, string Value)> PeakKeys(int? nits) =>
        nits is { } n ? PeakSections.Select(s => (s, PeakKey, n.ToString())).ToList() : [];

    public static int? ClampPeak(int? nits) => nits is { } n ? Math.Clamp(n, MinPeakNits, MaxPeakNits) : null;

    /// <summary>
    /// The keys of <paramref name="wanted"/> to write into one file, given the file as it is now and what an earlier run of this
    /// component recorded for it. A key is written when the file doesn't have it (or only an empty value), or still holds exactly
    /// what we wrote. A key the user has since set or changed is left out, and the installer then lets go of it.
    /// </summary>
    public static IReadOnlyList<(string Section, string Key, string Value)> MissingOnly(IniFile? current, InstalledComponent? record, string entry,
        IEnumerable<(string Section, string Key, string Value)> wanted)
    {
        var keys = new List<(string Section, string Key, string Value)>();
        foreach (var (section, key, value) in wanted)
        {
            var now = current?.Get(section, key);
            var ours = record?.EditOf(entry, section, key);
            var untouched = ours is not null && now is not null && string.Equals(now, ours.Value.Trim(), StringComparison.Ordinal);
            if (now is null || now.Length == 0 || untouched) keys.Add((section, key, value));
        }
        return keys;
    }
}
