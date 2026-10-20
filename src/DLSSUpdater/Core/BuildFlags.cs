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
}
