using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

/// <summary>Where a ReShade build comes from. Stored in version.json, so keep the names.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReShadeChannel>))]
public enum ReShadeChannel
{
    /// <summary>The newest add-on build from reshade.me.</summary>
    Stable,
    /// <summary>The newest build without add-on support from reshade.me.</summary>
    StableNoAddons,
    /// <summary>crosire/reshade's CI build of the main branch (artifacts, through nightly.link).</summary>
    Nightly,
    /// <summary>A chosen older add-on build from reshade.me.</summary>
    Legacy,
    /// <summary>Imported by the user.</summary>
    Custom,
}

/// <summary>What the app checked a build against. Stored in version.json, so keep the names.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReShadeVerification>))]
public enum ReShadeVerification
{
    /// <summary>The SHA-256 of the download matched the digest GitHub publishes for the artifact.</summary>
    PublishedDigest,
    /// <summary>reshade.me publishes no digest and its add-on DLLs are not signed: the SHA-256 was recorded when it was downloaded over HTTPS from the official site.</summary>
    RecordedAtDownload,
    /// <summary>The user supplied the files.</summary>
    Imported,
}

/// <summary>
/// One ReShade build in the library, stored under components\reshade\&lt;Id&gt;\ with ReShade64.dll and/or ReShade32.dll and this
/// record next to them as version.json. <see cref="Id"/> is the SHA-256 of ReShade64.dll (of ReShade32.dll when the build has no
/// 64-bit file); <see cref="Sha64"/> and <see cref="Sha32"/> are what each file must still hash to.
/// </summary>
public sealed record ReShadeBuild(
    string Id,
    string Version,
    ReShadeChannel Channel,
    string? Sha64,
    string? Sha32,
    ReShadeVerification Verification,
    string? Origin,
    DateTime Added,
    string? UserLabel = null,
    string? Commit = null)
{
    public const string File64 = "ReShade64.dll";
    public const string File32 = "ReShade32.dll";

    /// <summary>Add-on support; false only for the "no add-ons" channel.</summary>
    [JsonIgnore]
    public bool Addon => Channel != ReShadeChannel.StableNoAddons;

    [JsonIgnore]
    public bool Downloaded => Channel != ReShadeChannel.Custom;

    [JsonIgnore]
    public bool Has64 => Sha64 is not null;

    [JsonIgnore]
    public bool Has32 => Sha32 is not null;

    public bool HasBits(int bits) => bits == 32 ? Has32 : Has64;

    [JsonIgnore]
    public Version? ParsedVersion => System.Version.TryParse(Version, out var v) ? v : null;

    /// <summary>Sort key (<see cref="BuildOrder.Key"/>): the version, then the date.</summary>
    [JsonIgnore]
    public (Version Version, bool Release, int Stage, int Number) Order => BuildOrder.Key(Version, ParsedVersion);

    [JsonIgnore]
    public string ChannelName => ChannelLabel(Channel);

    public static string ChannelLabel(ReShadeChannel c) => c switch
    {
        ReShadeChannel.Stable => "Stable (add-on)",
        ReShadeChannel.StableNoAddons => "Stable, no add-ons",
        ReShadeChannel.Nightly => "Nightly",
        ReShadeChannel.Legacy => "Legacy",
        _ => "Custom",
    };

    /// <summary>The user's own name, else "6.8.0 · Stable (add-on)" (Nightly: "6.8.1.2200 · Nightly 7bf9de8"; Custom: with the import date).</summary>
    [JsonIgnore]
    public string Label
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(UserLabel)) return UserLabel.Trim();
            return Channel switch
            {
                ReShadeChannel.Nightly => $"{Version} · Nightly" + (Commit is { Length: >= 7 } c ? " " + c[..7] : ""),
                ReShadeChannel.Custom => $"{Version} · Custom ({Added.ToLocalTime():yyyy-MM-dd})",
                _ => $"{Version} · {ChannelName}",
            };
        }
    }

    /// <summary>One line on what the build was checked against, for the library list.</summary>
    [JsonIgnore]
    public string VerificationText => Verification switch
    {
        ReShadeVerification.PublishedDigest => "SHA-256 matches the digest GitHub publishes for this CI build.",
        ReShadeVerification.RecordedAtDownload => Channel == ReShadeChannel.Nightly
            ? "Unverified source: GitHub published no digest for this CI build, so its SHA-256 was only recorded at download."
            : "reshade.me publishes no digest and signs no add-on build: SHA-256 recorded at download over HTTPS from the official site.",
        _ => "Imported by you; not checked against a source.",
    };

    public override string ToString() => Label;

    /// <summary>True when an install recorded under <paramref name="id"/> is this build.</summary>
    public bool Matches(string? id) => id is not null && id.Equals(Id, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What a build choice (app default or per game) means. <c>null</c> follows the default (newest Stable add-on build);
/// <c>"channel:nightly"</c> and friends follow the newest build of that channel; anything else is the id of one build.
/// </summary>
public static class ReShadePick
{
    private const string Prefix = "channel:";

    public static string ForChannel(ReShadeChannel c) => Prefix + c.ToString().ToLowerInvariant();

    /// <summary>The channel a pick follows, or null when it names one build (or follows the default).</summary>
    public static ReShadeChannel? ChannelOf(string? pick)
    {
        if (pick is null || !pick.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return Enum.TryParse<ReShadeChannel>(pick[Prefix.Length..], ignoreCase: true, out var c) && c != ReShadeChannel.Custom ? c : null;
    }

    public static bool IsChannel(string? pick) => ChannelOf(pick) is not null;

    /// <summary>The channel a "follow the newest" choice means; Legacy and Custom always name one build.</summary>
    public static bool Moves(ReShadeChannel c) => c is ReShadeChannel.Stable or ReShadeChannel.StableNoAddons or ReShadeChannel.Nightly;
}
