using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

// The `addons` and `shaderPacks` sections of catalog.json (see CatalogModel.cs and the catalog repository's README).

public sealed class CatalogAddons
{
    public string? Updated { get; set; }
    public List<CatalogAddon> Addons { get; set; } = [];

    public CatalogAddon? Find(string id) => Addons.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One package of crosire's Addons.ini.</summary>
public sealed class CatalogAddon
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? RepositoryUrl { get; set; }
    public string? EffectInstallPath { get; set; }
    /// <summary>SPDX id of the repository the files are published from, when GitHub reports one.</summary>
    public string? License { get; set; }
    /// <summary>The app manages this add-on as a component of its own (e.g. "renodx", "display-commander"); the catalog lists no files.</summary>
    public string? ManagedBy { get; set; }
    /// <summary>Why the app can't install the package itself (no download, or files it doesn't place).</summary>
    public string? Manual { get; set; }
    public List<CatalogBinary> Files { get; set; } = [];
    /// <summary>Added by the user (<see cref="ExtrasLibrary"/>); never in the catalog.</summary>
    [JsonIgnore] public bool Imported { get; init; }

    /// <summary>The app can download and install it: not managed elsewhere, not manual, and at least one file with a hash.</summary>
    public bool IsInstallable => ManagedBy is null && Manual is null && Files.Any(f => f.IsInstallable);

    /// <summary>Mirrored copies carry the licence notice in the catalog repository's release notes; pinned ones come from the author.</summary>
    public bool IsMirrored => Files.Count > 0 && Files.All(f => !string.IsNullOrWhiteSpace(f.MirrorUrl));

    /// <summary>"Managed under RenoDX HDR mods" style label for <see cref="ManagedBy"/>; null when the add-on is not managed elsewhere.</summary>
    public string? ManagedLabel => ManagedBy is null ? null : ManagedNames.GetValueOrDefault(ManagedBy, ManagedBy);

    private static readonly Dictionary<string, string> ManagedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["renodx"] = "RenoDX HDR mods",
        ["display-commander"] = "Display Commander (frame limiters)",
        ["relimiter"] = "ReLimiter (frame limiters)",
        ["dfc"] = "Deep Fried Chicken",
        ["lecram-dlss5"] = "Lecram DLSS5",
        ["gitc-uplift"] = "GITC Uplift",
        ["dlss5-feeder"] = "DLSS5 Feeder",
    };
}

/// <summary>One file of an add-on download the app installs.</summary>
public sealed class CatalogPayloadItem
{
    /// <summary>Path inside the zip (the file name for a plain binary).</summary>
    public string Entry { get; set; } = "";
    /// <summary>"addon" (into the add-on folder) or "effect" (below the game's Shaders folder).</summary>
    public string Kind { get; set; } = "";
    /// <summary>"x64", "x86" or "both" (add-ons only).</summary>
    public string? Arch { get; set; }
    /// <summary>Effects only: path below the Shaders folder, with '/' separators.</summary>
    public string? Dest { get; set; }
}

public sealed class CatalogPackLayout
{
    /// <summary>Zip prefix of the Shaders folder, ending in '/'.</summary>
    public string ShadersDir { get; set; } = "";
    public string? TexturesDir { get; set; }
    /// <summary>File names of the effects (.fx) in the Shaders folder, sorted; presets name their effects by file name.</summary>
    public List<string> Effects { get; set; } = [];
    public int ShaderFiles { get; set; }
    public int TextureFiles { get; set; }
}

public sealed class CatalogShaderPacks
{
    public string? Updated { get; set; }
    public List<CatalogShaderPack> Packs { get; set; } = [];

    public CatalogShaderPack? Find(string id) => Packs.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

public sealed class CatalogShaderPack
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Repo { get; set; } = "";
    public string Homepage { get; set; } = "";
    /// <summary>"hdr", "general", "color", "depth-3d" or "cinematic".</summary>
    public string Category { get; set; } = "";
    /// <summary>Pre-selected until the user chooses (the HDR essentials).</summary>
    public bool Default { get; set; }
    /// <summary>"branch" or "release-asset".</summary>
    public string Source { get; set; } = "";
    public string Ref { get; set; } = "";
    public string? Commit { get; set; }
    /// <summary>The release tag, or the first seven characters of the commit.</summary>
    public string Version { get; set; } = "";
    public string? License { get; set; }
    public CatalogBinary? File { get; set; }
    /// <summary>Added by the user (<see cref="ExtrasLibrary"/>); never in the catalog.</summary>
    [JsonIgnore] public bool Imported { get; init; }

    public CatalogPackLayout? Layout => File?.Layout;

    /// <summary>Only a pack with a hash and a layout can be installed.</summary>
    public bool IsInstallable => File is { IsInstallable: true, Layout: not null };

    public bool IsMirrored => !string.IsNullOrWhiteSpace(File?.MirrorUrl);

    /// <summary>Lower-case effect file names the pack holds.</summary>
    public IEnumerable<string> EffectNames => Layout?.Effects ?? [];
}
