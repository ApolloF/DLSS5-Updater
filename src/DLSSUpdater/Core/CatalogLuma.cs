namespace DLSSUpdater.Core;

/// <summary>
/// The catalog's <c>luma</c> section: the Luma Framework (Filoppi/Luma-Framework) mods the wiki lists, the generic Unreal Engine mod
/// with its game table, and the pinned release zips. The licence is NOASSERTION, so the files are the author's own URLs plus a hash.
/// </summary>
public sealed class CatalogLuma
{
    public string? Updated { get; set; }
    /// <summary>The upstream release tag the zips come from ("latest-755").</summary>
    public string Release { get; set; } = "";
    public List<CatalogLumaPackage> Packages { get; set; } = [];
    public List<CatalogLumaMod> Mods { get; set; } = [];
    /// <summary>Id of the package that is the generic Unreal Engine mod.</summary>
    public string UnrealPackage { get; set; } = "Unreal_Engine";
    public List<CatalogLumaUnrealGame> UnrealGames { get; set; } = [];

    public CatalogLumaPackage? PackageOf(string? id) =>
        id is null ? null : Packages.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One release zip: a file per bitness (<c>x64</c> = Luma-&lt;id&gt;.zip, <c>x86</c> = Luma-&lt;id&gt;-x32.zip).</summary>
public sealed class CatalogLumaPackage
{
    public string Id { get; set; } = "";
    public List<CatalogBinary> Files { get; set; } = [];

    /// <summary>
    /// The file for a game of <paramref name="bits"/> (32, 64, or 0 when the exe couldn't be read: x64 first, else x86). Only files with
    /// a hash count.
    /// </summary>
    public CatalogBinary? FileFor(int bits)
    {
        var usable = Files.Where(f => f.IsInstallable).ToList();
        CatalogBinary? Of(string arch) => usable.FirstOrDefault(f => f.Arch.Equals(arch, StringComparison.OrdinalIgnoreCase));
        return bits switch { 32 => Of("x86"), 64 => Of("x64"), _ => Of("x64") ?? Of("x86") };
    }
}

public sealed class CatalogLumaMod
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Authors { get; set; } = [];
    /// <summary>"working", "in-progress", or null for a release zip the wiki doesn't list.</summary>
    public string? Status { get; set; }
    public string? Notes { get; set; }
    public CatalogLinks? Links { get; set; }
    /// <summary>The package the mod is in; null when the author publishes it elsewhere (Nexus Mods).</summary>
    public string? Package { get; set; }
    public bool RequiresDgVoodoo { get; set; }
    public List<string> DgVoodooVersions { get; set; } = [];
    public List<string> DgVoodooSetup { get; set; } = [];
    public string? FeaturesUrl { get; set; }
}

/// <summary>A row of the wiki's generic Unreal Engine table.</summary>
public sealed class CatalogLumaUnrealGame
{
    public string Name { get; set; } = "";
    /// <summary>"yes", "blocked", "in-progress" or null (empty cell).</summary>
    public string? Dlss { get; set; }
    public string? Hdr { get; set; }
    public string? UeVersion { get; set; }
    public string? Notes { get; set; }
    public List<CatalogEngineIniKey> EngineIni { get; set; } = [];
    public List<string> LaunchArgs { get; set; } = [];

    /// <summary>The wiki marks DLSS/FSR blocked and has no HDR for the game: the generic mod does nothing useful there.</summary>
    public bool Blocked => Dlss == "blocked" && Hdr != "yes";
}
