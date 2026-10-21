namespace DLSSUpdater.Core;

// catalog.json of ApolloF/DLSS5-Updater-catalog (schema 1, schema/catalog.schema.json in that repo). Only the parts the
// app reads are modelled; unknown properties and sections are ignored, so the catalog can grow without breaking old apps.

public sealed class CatalogDocument
{
    public int Schema { get; set; }
    public string? Generated { get; set; }
    public CatalogSections Sections { get; set; } = new();
}

public sealed class CatalogSections
{
    public CatalogRenodx? Renodx { get; set; }
    /// <summary>ReShade add-on packages from crosire's list (see <see cref="CatalogAddon"/>).</summary>
    public CatalogAddons? Addons { get; set; }
    /// <summary>Shader repositories pinned to a release or commit (see <see cref="CatalogShaderPack"/>).</summary>
    public CatalogShaderPacks? ShaderPacks { get; set; }
    public CatalogOptiCompat? OptiscalerCompat { get; set; }
    /// <summary>ShortFuse's renodx-dlss add-on: hash-pinned builds, never mirrored (licence unknown).</summary>
    public CatalogRenodxDlss? RenodxDlss { get; set; }
    /// <summary>ReLimiter and Display Commander add-on files (ReLimiter mirrored, Display Commander pinned to the author's URL).</summary>
    public CatalogFrameLimiters? FrameLimiters { get; set; }
}

public sealed class CatalogFrameLimiters
{
    public string? Updated { get; set; }
    public List<CatalogLimiter> Limiters { get; set; } = [];

    public CatalogLimiter? For(string id) => Limiters.FirstOrDefault(l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One limiter's add-on files of one release; a 32-bit file is absent when the release has none.</summary>
public sealed class CatalogLimiter
{
    /// <summary>"relimiter" or "display-commander".</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Repository { get; set; }
    /// <summary>The release tag the files came from; Display Commander republishes "latest", so its version is the file's first-seen version.</summary>
    public string Release { get; set; } = "";
    public string? License { get; set; }
    public List<CatalogBinary> Files { get; set; } = [];

    public CatalogBinary? File(string arch) => Files.FirstOrDefault(f => f.Arch.Equals(arch, StringComparison.OrdinalIgnoreCase));

    /// <summary>The label shown for a file of this release: the tag without its "v", else the file's own first-seen version.</summary>
    public string VersionOf(CatalogBinary file) =>
        Release.Equals("latest", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(Release) ? file.Version ?? file.Sha256?[..12] ?? "?" : Release.TrimStart('v', 'V');
}

public sealed class CatalogRenodxDlss
{
    public string? Updated { get; set; }
    /// <summary>Newest first.</summary>
    public List<CatalogDlssBuild> Builds { get; set; } = [];
}

/// <summary>One release of the add-on. <see cref="File"/> is a zip that holds <c>renodx-dlss.addon64</c>.</summary>
public sealed class CatalogDlssBuild
{
    public string Tag { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Published { get; set; }
    public CatalogBinary File { get; set; } = new();
}

/// <summary>OptiScaler's own compatibility list (its wiki), as the catalog copied it: facts and a pointer back to the wiki page.</summary>
public sealed class CatalogOptiCompat
{
    public string? Updated { get; set; }
    public List<CatalogOptiGame> Games { get; set; } = [];
    /// <summary>Games OptiScaler's wiki lists as unable to use OptiFG's HUDfix.</summary>
    public List<string> HudfixIncompatible { get; set; } = [];
}

public sealed class CatalogOptiGame
{
    public string Name { get; set; } = "";
    /// <summary>The wiki page of the game: https://github.com/optiscaler/OptiScaler/wiki/&lt;Page&gt;; null when the list has none.</summary>
    public string? Page { get; set; }
    /// <summary>"main", "upscalerMods" (games that got an upscaler through a third-party mod) or "lumaUe".</summary>
    public string List { get; set; } = "main";
    /// <summary>"working", "single-os" or "not-working".</summary>
    public string Compat { get; set; } = "working";
    /// <summary>Upscaler inputs the game offers OptiScaler, as the wiki writes them: DLSS, FSR2, FSR3, FSR3.1/4, XeSS.</summary>
    public List<string> Inputs { get; set; } = [];
    public bool OptiPatcher { get; set; }
    public string? Notes { get; set; }
}

public sealed class CatalogRenodx
{
    public string? Updated { get; set; }
    /// <summary>The generic Unreal, Unity and Unreal Extended add-ons.</summary>
    public List<CatalogGeneric> Generic { get; set; } = [];
    public List<CatalogMod> Mods { get; set; } = [];
    public List<CatalogEngineGame> EngineGames { get; set; } = [];
    public CatalogOverrides Overrides { get; set; } = new();

    public CatalogGeneric? GenericFor(string id) => Generic.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

public sealed class CatalogGeneric
{
    /// <summary>"unreal", "unity" or "ue-extended".</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<CatalogBinary> Files { get; set; } = [];
}

public sealed class CatalogMod
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public List<int> SteamAppIds { get; set; } = [];
    public List<string> Authors { get; set; } = [];
    /// <summary>"main", "engine", "related" or "deprecated"; only main and engine entries are offered.</summary>
    public string Listing { get; set; } = "main";
    public string? Status { get; set; }
    /// <summary>"working", "in-progress" or null.</summary>
    public string? Compat { get; set; }
    public string? Notes { get; set; }
    /// <summary>"unreal" or "unity": the game has no file of its own and uses the generic add-on.</summary>
    public string? Generic { get; set; }
    public CatalogLinks? Links { get; set; }
    public bool Blocked { get; set; }
    public string? BlockReason { get; set; }
    public List<CatalogBinary> Files { get; set; } = [];
}

public sealed class CatalogLinks
{
    public string? Nexus { get; set; }
    public string? Discord { get; set; }
    public string? Discussion { get; set; }
    public string? GameBanana { get; set; }
}

/// <summary>One downloadable build with the hash the app must verify before it lands in a library.</summary>
public sealed class CatalogBinary
{
    public string Name { get; set; } = "";
    /// <summary>"x64" or "x86"; "both" for an add-on or shader package that serves 32-bit and 64-bit games.</summary>
    public string Arch { get; set; } = "x64";
    public string Url { get; set; } = "";
    public string? Sha256 { get; set; }
    public long Size { get; set; }
    /// <summary>First-seen UTC date plus short hash, e.g. 2026-10-21-ab12cd3: upstream snapshots have no version of their own.</summary>
    public string? Version { get; set; }
    public string? FirstSeen { get; set; }
    public string? License { get; set; }
    /// <summary>Immutable copy in the catalog repository; null = pinned (upstream URL plus hash only).</summary>
    public string? MirrorUrl { get; set; }
    public string? Mod { get; set; }
    /// <summary>Add-on packages: what to install from the download (an add-on binary or zip).</summary>
    public List<CatalogPayloadItem>? Payload { get; set; }
    /// <summary>Shader packs: where the Shaders and Textures folders are inside the zip.</summary>
    public CatalogPackLayout? Layout { get; set; }
    public List<CatalogHistory> History { get; set; } = [];

    /// <summary>Where to download from: the mirror when there is one, else the author's own URL.</summary>
    public string DownloadUrl => string.IsNullOrWhiteSpace(MirrorUrl) ? Url : MirrorUrl;

    /// <summary>Only entries with a hash can be installed ("no digest = no install").</summary>
    public bool IsInstallable => Sha256 is { Length: 64 } && !string.IsNullOrWhiteSpace(DownloadUrl);
}

public sealed class CatalogHistory
{
    public string Version { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string? FirstSeen { get; set; }
    public string? MirrorUrl { get; set; }
}

/// <summary>A game the wiki lists as working with a generic engine add-on.</summary>
public sealed class CatalogEngineGame
{
    /// <summary>"ue-extended", "unreal", "unity" or "source".</summary>
    public string Engine { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Compat { get; set; }
    public string? Notes { get; set; }
    /// <summary>Unreal Extended only: "native", "ini" or "upgrade", read from the wiki notes; null when they don't say.</summary>
    public string? Method { get; set; }
    /// <summary>Unreal Extended, upgrade method: the render-target formats to upgrade and how.</summary>
    public List<CatalogUpgrade> Upgrades { get; set; } = [];
    /// <summary>Unreal Extended: false when the notes say to switch "Upgrade Copy Destinations" off.</summary>
    public bool? CopyDestinations { get; set; }
    /// <summary>Unreal Extended: Engine.ini keys the notes name for this game; they replace the standard HDR keys.</summary>
    public List<CatalogEngineIniKey> EngineIni { get; set; } = [];
}

public sealed class CatalogUpgrade
{
    /// <summary>A DXGI format as in the add-on's Upgrade_ keys, e.g. B8G8R8A8_TYPELESS.</summary>
    public string Format { get; set; } = "";
    /// <summary>"output-size", "output-ratio" or "any-size".</summary>
    public string Mode { get; set; } = "";
}

public sealed class CatalogEngineIniKey
{
    public string Section { get; set; } = "SystemSettings";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>The hand-maintained overrides of the catalog repository, as published in the catalog.</summary>
public sealed class CatalogOverrides
{
    public List<CatalogNameFix> NameFixes { get; set; } = [];
    public List<CatalogAppIdFix> AppIdFixes { get; set; } = [];
    public List<CatalogBlockItem> Blocklist { get; set; } = [];
    public List<CatalogUeExtendedOptOut> UeExtendedOptOut { get; set; } = [];
    public List<CatalogEngineIniHint> EngineIniPaths { get; set; } = [];

    /// <summary>True when the game must not get Unreal Extended by default.</summary>
    public bool OptsOutOfUeExtended(int? steamAppId, string gameName) =>
        UeExtendedOptOut.Any(o => Same(o.SteamAppId, o.Name, steamAppId, gameName));

    /// <summary>The overridden Engine.ini location for the game (relative to %LOCALAPPDATA% unless rooted), or null.</summary>
    public string? EngineIniPath(int? steamAppId, string gameName) =>
        EngineIniPaths.FirstOrDefault(h => Same(h.SteamAppId, h.Name, steamAppId, gameName))?.Path;

    private static bool Same(int? entryAppId, string? entryName, int? appId, string gameName) =>
        (entryAppId is { } a && a == appId) ||
        (!string.IsNullOrWhiteSpace(entryName) && GameMatcher.Normalize(entryName) == GameMatcher.Normalize(gameName));
}

public sealed class CatalogNameFix
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public List<string> Aliases { get; set; } = [];
}

public sealed class CatalogAppIdFix
{
    public string Id { get; set; } = "";
    public List<int> SteamAppIds { get; set; } = [];
}

public sealed class CatalogBlockItem
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Reason { get; set; }
}

public sealed class CatalogUeExtendedOptOut
{
    public int? SteamAppId { get; set; }
    public string? Name { get; set; }
    public string? Reason { get; set; }
}

public sealed class CatalogEngineIniHint
{
    public int? SteamAppId { get; set; }
    public string? Name { get; set; }
    public string Path { get; set; } = "";
}
