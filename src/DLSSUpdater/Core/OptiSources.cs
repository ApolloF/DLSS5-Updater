using System.Text.RegularExpressions;

namespace DLSSUpdater.Core;

/// <summary>
/// One OptiScaler build the app can install. <paramref name="TagSuffix"/> is appended to the GitHub tag inside this
/// app so cache folders, manifests and "installed vs latest" stay separate per build.
/// </summary>
public sealed record OptiSource(
    string Repo, string Label, Regex Asset, string TagSuffix, string TestedTag,
    string[] ExtraFiles, (string Section, string Key)[] UnsupportedKeys)
{
    public bool IsUnsupported(string section, string key) =>
        UnsupportedKeys.Any(u => u.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && u.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>What a ComboBox shows; records otherwise print every member.</summary>
    public override string ToString() => Label;
}

public static class OptiSources
{
    public static readonly OptiSource Default = new(
        ComponentStore.OptiRepo, "OptiScaler-NR (ShyVortex)",
        new Regex(@"^OptiScaler-NR-v[\d.]+\.zip$", RegexOptions.IgnoreCase), "", "v0.9.51", [], []);

    public static readonly OptiSource F5 = new(
        "janblade/OptiScaler-F5-DLSSNR-Multipass", "OptiScaler F5 (janblade)",
        new Regex(@"^OptiScaler-DLSSNR-F5-[\d.]+\.zip$", RegexOptions.IgnoreCase), "-f5", "v0.1.32",
        ["nvngx.dll_dlssnr.dll"],
        [("DlssNr", "DenoiseFirst"), ("DlssNr", "PrivateUpscaler"), ("DlssNr", "ResidualAcrossRR"), ("DlssNr", "HdrTransfer"), ("DlssNr", "AutoExposureHighlightProtection")]);

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
