using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>What the game page and the install confirmation need to know about ReShade in one game, without touching it.</summary>
public static class ReShadeInstall
{
    /// <summary>
    /// The one-line warning for the dll ReShade is about to replace, or null when the slot is empty, ours or ReShade already.
    /// A dll the install record owns is ours (an update), whatever it is called.
    /// </summary>
    public static string? ForeignWarning(GameInfo game, string targetDir, string fileName, InstallManifest? m)
    {
        var full = Path.Combine(targetDir, fileName);
        if (!File.Exists(full)) return null;
        if (m is not null && m.Owns(InstallManifest.Entry(FileUtil.Normalize(full), FileUtil.Normalize(targetDir), FileUtil.Normalize(game.Root)))) return null;
        return ForeignDll.Warning(fileName, ForeignDll.Identify(full));
    }

    /// <summary>The file name or layer ReShade takes in this game under the install options (the same rule the installer follows).</summary>
    public static ReShadeTarget TargetFor(GameInfo game, InstallOptions o) =>
        o.ReShadeVulkan ? new(null, true, game.Effective.Bits == 32 ? 32 : 64, "Vulkan layer")
        : ReShadeNaming.Resolve(game.Effective, optiScalerLoads: !o.ReShadeOnly, manualName: o.ReShadeOnly ? o.Proxy : null);
}
