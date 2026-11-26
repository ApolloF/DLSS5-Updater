using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>What the advisor knows about the Lecram build a game would use: its name, whether it carries NR's host, whether it runs without DLSS.</summary>
public sealed record LecramBuildInfo(string Name, bool Host, bool RunsWithoutDlss)
{
    public static LecramBuildInfo Of(AddonBuild build) =>
        new(build.Name, ComponentStore.LecramHasHost(build), LecramRoute.RunsWithoutDlss(build.Generation, build.DetectedLabel));
}

/// <summary>
/// Which games Lecram's add-on can serve and how (docs/addons/lecram-dlss5.md, "Host and 32-bit"). 64-bit Direct3D 11, Direct3D 12 and
/// Vulkan games run NR in the game; Direct3D 9, Direct3D 10, OpenGL and every 32-bit game run it through NR's 64-bit host, which the
/// author ships from v12.9. 32-bit Vulkan and 32-bit Direct3D 12 are not supported by the author yet (README).
/// </summary>
public static class LecramRoute
{
    /// <summary>
    /// The first build that runs in games without their own DLSS: v12.0.0's release notes ("with or without DLSS") and README (games
    /// without DLSS get NR on the game's motion vectors where it finds them, else NVIDIA Optical Flow, <c>NROpticalFlow</c>).
    /// </summary>
    public const string FirstWithoutDlssBuild = "v12.0.0";

    /// <summary>The first checked build that ships NR's host and the 32-bit add-on; named in the texts that ask for one.</summary>
    public const string FirstHostBuild = "v12.9.9";

    /// <summary>INSTALL.txt (v13.4.0 to v15.2.0, unchanged): "Needs ReShade 6.8 or newer with add-on support in the game".</summary>
    public static readonly Version HostReShade = new(6, 8);

    /// <summary>Whether a build of <paramref name="generation"/> labelled <paramref name="label"/> runs in a game without its own DLSS; a v11 build without a readable label doesn't.</summary>
    public static bool RunsWithoutDlss(LecramGeneration? generation, string? label) =>
        generation == LecramGeneration.V11 && !string.IsNullOrWhiteSpace(label) && ComponentStore.IsLecramVersionLabel(label)
        && BuildOrder.Key(label).CompareTo(BuildOrder.Key(FirstWithoutDlssBuild)) >= 0;

    /// <summary>The game runs NR through the host: 32-bit, or Direct3D 9, Direct3D 10 or OpenGL.</summary>
    public static bool NeedsHost(GameTech tech) =>
        tech.Is32Bit || tech.PrimaryApi is GraphicsApis.Dx9 or GraphicsApis.Dx10 or GraphicsApis.OpenGL;

    /// <summary>The author's README: "Not yet: 32-bit Vulkan and 32-bit Direct3D 12 games."</summary>
    public static bool Unsupported(GameTech tech) => tech.Is32Bit && tech.PrimaryApi is GraphicsApis.Vulkan or GraphicsApis.Dx12;

    /// <summary>"32-bit DX11", "DX9", ...: how the texts name the game.</summary>
    public static string GameKind(GameTech tech)
    {
        var api = GraphicsApiInfo.Label(tech.PrimaryApi);
        return tech.Is32Bit ? (api.Length > 0 ? $"32-bit {api}" : "32-bit") : api;
    }

    /// <summary>Why <paramref name="build"/> can't be installed in this game, or null when it can.</summary>
    /// <param name="hasHost">The build carries the 32-bit add-on and NR's host (<see cref="ComponentStore.LecramHostOf"/>).</param>
    /// <param name="nativeDlss">The game ships its own DLSS (<see cref="Installer.HasNativeDlss"/>).</param>
    public static string? Refusal(GameTech tech, AddonBuild build, bool hasHost, bool nativeDlss)
    {
        if (Unsupported(tech))
            return $"Lecram's DLSS5 add-on doesn't run in {GameKind(tech)} games yet (the author's README: not yet 32-bit Vulkan and 32-bit Direct3D 12).";
        if (NeedsHost(tech) && !hasHost)
            return $"Lecram's DLSS5 add-on runs {GameKind(tech)} games through NR's host, and the build {build.Name} has none. " +
                   $"Import the author's DLSS5 NR setup exe or add-on zip of {FirstHostBuild} or newer (a bare {ComponentStore.Lecram11File} brings no host).";
        if (!nativeDlss && !RunsWithoutDlss(build.Generation, build.DetectedLabel))
            return $"Lecram's DLSS5 add-on {build.Name} needs a game with native DLSS. {FirstWithoutDlssBuild} and newer also run in games without it.";
        return null;
    }
}
