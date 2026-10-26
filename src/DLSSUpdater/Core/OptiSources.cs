using System.Text.RegularExpressions;

namespace DLSSUpdater.Core;

/// <summary>
/// One OptiScaler build the app can install. <paramref name="TagSuffix"/> is appended to the GitHub tag inside this
/// app so cache folders, manifests and "installed vs latest" stay separate per build. <paramref name="Name"/> is the
/// product name every label in the app uses; <paramref name="Label"/> adds the maintainer for the build dropdown.
/// <paramref name="UnsupportedKeys"/> are keys no build of the source has; <paramref name="VersionedKeys"/> are keys only some
/// of its builds have, so whether a build has one depends on its tag.
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

    /// <summary>F5 0.1.34's built-in MFG unlock, one key per GPU family; 0.1.33 had none, older builds the [DLSSG] Ada/Ampere keys.</summary>
    internal static readonly string[] MfgUnlockKeys = ["XeFG", "RTX40", "RTX2030"];

    public static readonly OptiSource Default = new(
        ComponentStore.OptiRepo, "OptiScaler-NR", "OptiScaler-NR (ShyVortex)",
        new Regex(@"^OptiScaler-NR-v[\d.]+\.zip$", RegexOptions.IgnoreCase), "", "v0.9.51", [],
        [.. HdrOutputKeys.Select(k => ("HDR", k)), .. MfgUnlockKeys.Select(k => ("MFGUnlock", k))]);

    /// <summary>Asset names: "-0.1.32.zip" up to 0.1.32, "-0.1.33-with-dlss-fg.zip", then "-v0.1.34-with-dlss-fg.zip".</summary>
    public static readonly OptiSource F5 = new(
        "janblade/OptiScaler-F5-DLSSNR-Multipass", "OptiScaler F5", "OptiScaler F5 (janblade)",
        new Regex(@"^OptiScaler-DLSSNR-F5-v?[\d.]+(-with-dlss-fg)?\.zip$", RegexOptions.IgnoreCase), "-f5", "v0.1.34",
        ["nvngx.dll_dlssnr.dll"],
        [("DlssNr", "DenoiseFirst"), ("DlssNr", "PrivateUpscaler"), ("DlssNr", "ResidualAcrossRR"), ("DlssNr", "HdrTransfer"), ("DlssNr", "AutoExposureHighlightProtection")],
        [
            new("HDR", "ForceHDR", Until: "v0.1.33"), new("HDR", "SkipColorSpace", Until: "v0.1.33"),
            .. HdrOutputKeys.Select(k => new OptiKeyRange("HDR", k, Since: "v0.1.33")),
            new("DLSSG", "AdaMfgUnlock", Until: "v0.1.33"), new("DLSSG", "AmpereMfgUnlock", Until: "v0.1.33"),
            .. MfgUnlockKeys.Select(k => new OptiKeyRange("MFGUnlock", k, Since: "v0.1.34")),
        ]);

    public static readonly IReadOnlyList<OptiSource> All = [Default, F5];

    /// <summary>The source for a saved repo name; anything unknown falls back to the default.</summary>
    public static OptiSource For(string? repo) =>
        All.FirstOrDefault(s => s.Repo.Equals(repo, StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>The source an app-internal tag belongs to.</summary>
    public static OptiSource OfTag(string? tag) =>
        tag is null ? Default
            : All.FirstOrDefault(s => s.TagSuffix.Length > 0 && tag.EndsWith(s.TagSuffix, StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>The GitHub tag behind an app-internal tag.</summary>
    public static string RawTag(string tag)
    {
        var s = OfTag(tag);
        return s.TagSuffix.Length > 0 ? tag[..^s.TagSuffix.Length] : tag;
    }
}
