using System.Text.RegularExpressions;

namespace DLSSUpdater.Core;

/// <summary>
/// The OptiScaler build the app installs (<see cref="OptiSources.F5"/>). <paramref name="TagSuffix"/> is appended to the GitHub tag
/// inside this app, so its cache folders and install records stay apart from the OptiScaler-NR tags older versions recorded.
/// <paramref name="Name"/> is the product name every label in the app uses; <paramref name="Label"/> adds the maintainer.
/// <paramref name="UnsupportedKeys"/> are keys no build of the source has (OptiScaler-NR's own, so an update from an NR install
/// drops what the app wrote for them); <paramref name="VersionedKeys"/> are keys only some of its builds have, so whether a build
/// has one depends on its tag.
/// </summary>
public sealed record OptiSource(
    string Repo, string Name, string Label, Regex Asset, string TagSuffix, string TestedTag,
    string[] ExtraFiles, (string Section, string Key)[] UnsupportedKeys, OptiKeyRange[]? VersionedKeys = null)
{
    public bool IsUnsupported(string section, string key) =>
        UnsupportedKeys.Any(u => u.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && u.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when the build behind <paramref name="tag"/> (app-internal or raw) has no such key. A null or unreadable tag means
    /// the tested build.
    /// </summary>
    public bool Lacks(string section, string key, string? tag)
    {
        if (IsUnsupported(section, key)) return true;
        var range = VersionedKeys?.FirstOrDefault(r => r.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && r.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (range is null) return false;
        var v = FileUtil.ParseTag(tag is null ? null : OptiSources.RawTag(tag)) ?? FileUtil.ParseTag(TestedTag)!;
        return (range.Since is { } since && v < FileUtil.ParseTag(since)) || (range.Until is { } until && v >= FileUtil.ParseTag(until));
    }

    /// <summary>What a ComboBox shows; records otherwise print every member.</summary>
    public override string ToString() => Label;
}

/// <summary>A key that exists from <paramref name="Since"/> (inclusive) up to <paramref name="Until"/> (exclusive); null is open-ended.</summary>
public sealed record OptiKeyRange(string Section, string Key, string? Since = null, string? Until = null);

public static class OptiSources
{
    /// <summary>F5 0.1.33 replaced [HDR] ForceHDR / SkipColorSpace with its HDR output for SDR games.</summary>
    private static readonly string[] HdrOutputKeys = ["Enabled", "Convert", "PeakNits", "GameNits", "Decode", "CompareKey"];

    /// <summary>F5 0.1.37's depth fade, all in [DlssNr].</summary>
    private static readonly string[] DepthFadeKeys =
    [
        "DepthFade", "DepthFadeFarStart", "DepthFadeFarEnd", "DepthFadeFarStrength", "DepthFadeNearStart", "DepthFadeNearEnd",
        "DepthFadeNearStrength", "DepthFadeSkyStrength",
    ];

    /// <summary>F5 0.1.39's F5Low settings for a native Vulkan swapchain.</summary>
    private static readonly string[] VulkanF5LowKeys = ["VulkanLatencyGate", "VulkanTearing", "VulkanFlipMetering", "VulkanPacer"];

    /// <summary>F5 0.1.34's built-in MFG unlock, one key per GPU family; 0.1.33 had none, older builds the [DLSSG] Ada/Ampere keys.</summary>
    internal static readonly string[] MfgUnlockKeys = ["XeFG", "RTX40", "RTX2030"];

    /// <summary>
    /// Asset names: "-0.1.32.zip" up to 0.1.32, "-0.1.33-with-dlss-fg.zip", "-v0.1.34-with-dlss-fg.zip" (up to 0.1.36), then
    /// "v0.1.37-Optiscaler-F5.zip" (the tag can have four parts, v0.1.37.2). The forwarder, the F5Low update and the guide
    /// beside the zip don't match.
    /// </summary>
    public static readonly OptiSource F5 = new(
        "janblade/OptiScaler-F5-DLSSNR-Multipass", "OptiScaler F5", "OptiScaler F5 (janblade)",
        new Regex(@"^(OptiScaler-DLSSNR-F5-v?[\d.]+(-with-dlss-fg)?|v[\d.]+-Optiscaler-F5)\.zip$", RegexOptions.IgnoreCase), "-f5", "v0.1.39",
        ["nvngx.dll_dlssnr.dll"],
        [("DlssNr", "DenoiseFirst"), ("DlssNr", "PrivateUpscaler"), ("DlssNr", "ResidualAcrossRR"), ("DlssNr", "HdrTransfer"), ("DlssNr", "AutoExposureHighlightProtection")],
        [
            new("HDR", "ForceHDR", Until: "v0.1.33"), new("HDR", "SkipColorSpace", Until: "v0.1.33"),
            .. HdrOutputKeys.Select(k => new OptiKeyRange("HDR", k, Since: "v0.1.33")),
            new("DLSSG", "AdaMfgUnlock", Until: "v0.1.33"), new("DLSSG", "AmpereMfgUnlock", Until: "v0.1.33"),
            .. MfgUnlockKeys.Select(k => new OptiKeyRange("MFGUnlock", k, Since: "v0.1.34")),
            new("DlssNr", "MultiViewScreens", Since: "v0.1.35.1"),
            new("F5Low", "FrameGenerator", Since: "v0.1.36"), new("F5Low", "KeepGameDlssg", Since: "v0.1.36"), new("DlssNr", "SecondaryFeatures", Since: "v0.1.36"),
            .. DepthFadeKeys.Select(k => new OptiKeyRange("DlssNr", k, Since: "v0.1.37")),
            .. VulkanF5LowKeys.Select(k => new OptiKeyRange("F5Low", k, Since: "v0.1.39")),
            new("DLSSG", "Preset", Since: "v0.1.39"), new("Memory", "ProtectMemory", Since: "v0.1.39"), new("XeSS", "UnrealDepth", Since: "v0.1.39"),
        ]);

    /// <summary>
    /// What the app called OptiScaler-NR (ShyVortex's fork, earlier wilsjo2's), the second source until F5 became the only one. Its
    /// app tags have no suffix, so a game whose record holds one still shows what it has until its next update installs F5.
    /// </summary>
    public const string LegacyNrName = "OptiScaler-NR";

    /// <summary>True for an app-internal tag of an F5 build; false for an OptiScaler-NR tag an older version recorded.</summary>
    public static bool IsF5Tag(string? tag) => tag is not null && tag.EndsWith(F5.TagSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The product name for an app-internal tag; null means the build the app installs (F5).</summary>
    public static string NameOf(string? tag) => tag is null || IsF5Tag(tag) ? F5.Name : LegacyNrName;

    /// <summary>The GitHub tag behind an app-internal tag.</summary>
    public static string RawTag(string tag) => IsF5Tag(tag) ? tag[..^F5.TagSuffix.Length] : tag;
}
