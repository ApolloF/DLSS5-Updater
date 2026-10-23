namespace DLSSUpdater.Core;

/// <summary>
/// Compile-time switches of this build (see LecramChannel in DLSSUpdater.csproj). Readonly fields, not consts, so the code
/// behind a switch still compiles in both variants without unreachable-code warnings.
/// </summary>
public static class BuildFlags
{
    /// <summary>The owner's private build: fetches Lecram's builds from his MediaFire channel and never self-updates from the public release.</summary>
    public static readonly bool LecramChannel =
#if LECRAM_CHANNEL
        true;
#else
        false;
#endif

    /// <summary>
    /// Off for now: public builds import Lecram and never download it from the catalog mirror (no automatic check, no "· download"
    /// entries in the build pickers). Set it to true to bring the public download back; the code behind it stays.
    /// </summary>
    public static readonly bool LecramCatalog = false;

    /// <summary>True when this build fetches no Lecram builds from the catalog mirror: the private build has its own channel, the public one imports.</summary>
    public static bool LecramCatalogOff => LecramChannel || !LecramCatalog;
}
