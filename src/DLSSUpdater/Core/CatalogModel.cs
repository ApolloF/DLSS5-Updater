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
    /// <summary>"x64" or "x86".</summary>
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
