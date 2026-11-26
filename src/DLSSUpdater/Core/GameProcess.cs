using System.Text.RegularExpressions;

namespace DLSSUpdater.Core;

/// <summary>
/// What the logs our proxies leave in an install folder say about the process they ran in. Some games load the proxy from a folder
/// other than their exe's (Phantom Blade Zero runs platform\steam\PBZ.exe and loads dxgi.dll from ProjectPBZ\Binaries\Win64), and
/// OptiScaler looks for ReShade64.dll and its OptiScaler\ plugin folder beside the process exe (Util::ReShadePath, Util::DllPath),
/// so there ReShade and F5's frame generation never load.
/// </summary>
public static partial class GameProcess
{
    private const int OptiScalerHeadLines = 200;

    /// <summary>The folder of the exe that loaded the proxy in <paramref name="targetDir"/>, from ReShade.log or OptiScaler.log; null when neither says.</summary>
    public static string? ExeDirFromLogs(string targetDir)
    {
        try
        {
            var reshade = Path.Combine(targetDir, "ReShade.log");
            if (File.Exists(reshade) && ReadHead(reshade, 1).FirstOrDefault() is { } first && ReShadeInit().Match(first) is { Success: true } r
                && FileUtil.Normalize(Path.GetDirectoryName(r.Groups["dll"].Value)!).Equals(FileUtil.Normalize(targetDir), StringComparison.OrdinalIgnoreCase))
                return FileUtil.Normalize(Path.GetDirectoryName(r.Groups["exe"].Value)!);
            var opti = Path.Combine(targetDir, "OptiScaler.log");
            if (File.Exists(opti))
                foreach (var line in ReadHead(opti, OptiScalerHeadLines))
                    if (OptiDllPath().Match(line) is { Success: true } o) return FileUtil.Normalize(o.Groups["dir"].Value.Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return null;
    }

    /// <summary>
    /// Why OptiScaler in <paramref name="targetDir"/> can't load ReShade and its plugins: the game's exe runs from another folder.
    /// Null when the logs don't say so.
    /// </summary>
    public static string? OptiScalerFolderWarning(string targetDir, string? gameRoot = null)
    {
        if (ExeDirFromLogs(targetDir) is not { } exeDir || exeDir.Equals(FileUtil.Normalize(targetDir), StringComparison.OrdinalIgnoreCase)) return null;
        var shown = gameRoot is not null && FileUtil.IsUnder(exeDir, gameRoot) ? Path.GetRelativePath(gameRoot, exeDir) : exeDir;
        return $"This game runs from {shown}, not from this folder. OptiScaler loads ReShade (and the add-ons in it, such as MFG Unlock) and its own frame generation plugins from the exe's folder, so here they don't load. Pick the install folder {shown} for OptiScaler, or use ReShade + add-ons mode here.";
    }

    private static IEnumerable<string> ReadHead(string path, int lines)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        for (var i = 0; i < lines && reader.ReadLine() is { } line; i++) yield return line;
    }

    [GeneratedRegex(@"loaded from '(?<dll>[^']+)' into '(?<exe>[^']+)'")]
    private static partial Regex ReShadeInit();

    [GeneratedRegex(@"Setting DllPath to (?<dir>.+)$")]
    private static partial Regex OptiDllPath();
}
