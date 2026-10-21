using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>An Unreal Engine.ini: where it is (or will be created) and how it was found.</summary>
public sealed record EngineIniTarget(string Path, bool Exists, string Source);

/// <summary>Finds the game's Engine.ini. Pure apart from looking at folders; the roots are passed in so tests use temp folders.</summary>
public static class EngineIniLocator
{
    private static readonly string[] Platforms = ["WinGDK", "Windows", "WindowsNoEditor"];

    /// <param name="overridePath">The catalog's hint for this game: a folder or an .ini file, relative to %LOCALAPPDATA% unless rooted; %VARIABLES% are expanded.</param>
    public static EngineIniTarget? Resolve(GameInfo game, string? overridePath, string localAppData, string documents)
    {
        if (FromOverride(game, overridePath, localAppData, documents) is { } hinted) return hinted;

        var project = game.Tech.UeProject;
        if (string.IsNullOrWhiteSpace(project)) return null;

        // The folder above Binaries is the project; some games keep their AppData folder under the game folder's name instead.
        var names = new[] { project, System.IO.Path.GetFileName(game.Root.TrimEnd('\\', '/')) }
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var name in names)
            if (FirstConfigDir(System.IO.Path.Combine(localAppData, name)) is { } dir) return At(dir, "AppData");

        var stripped = game.Name.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
        foreach (var gameName in new[] { game.Name, stripped }.Distinct(StringComparer.OrdinalIgnoreCase))
            if (FirstConfigDir(System.IO.Path.Combine(documents, "My Games", gameName)) is { } dir) return At(dir, "Documents");

        var projectDir = game.Tech.ModFolder is { } mod ? System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(mod)) : null;
        if (projectDir is not null && FirstConfigDir(projectDir) is { } inGame) return At(inGame, "game folder");
        if (Directory.Exists(game.Root))
            foreach (var sibling in SafeDirectories(game.Root))
                if (FirstConfigDir(sibling) is { } other) return At(other, "game folder");

        // Nothing yet: the game creates its config where the engine puts it, which is AppData.
        var platform = game.Tech.ModFolder is { } m && System.IO.Path.GetFileName(m).Equals("WinGDK", StringComparison.OrdinalIgnoreCase) ? "WinGDK" : "Windows";
        return new EngineIniTarget(System.IO.Path.Combine(localAppData, project, "Saved", "Config", platform, "Engine.ini"), false, "new, in AppData");
    }

    private static EngineIniTarget? FromOverride(GameInfo game, string? hint, string localAppData, string documents)
    {
        if (string.IsNullOrWhiteSpace(hint)) return null;
        var path = Environment.ExpandEnvironmentVariables(hint.Trim());
        path = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(localAppData, path);
        try { path = System.IO.Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return null; }
        var file = path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ? path : System.IO.Path.Combine(path, "Engine.ini");
        // Only places the install record may name: AppData, Documents or the game.
        var allowed = new[] { localAppData, documents, game.Root }.Any(root => FileUtil.IsUnder(file, root));
        return allowed ? new EngineIniTarget(file, File.Exists(file), "the catalog's path for this game") : null;
    }

    private static string? FirstConfigDir(string projectRoot)
    {
        var config = System.IO.Path.Combine(projectRoot, "Saved", "Config");
        foreach (var p in Platforms)
        {
            var dir = System.IO.Path.Combine(config, p);
            if (Directory.Exists(dir)) return dir;
        }
        return null;
    }

    private static EngineIniTarget At(string dir, string source)
    {
        var file = System.IO.Path.Combine(dir, "Engine.ini");
        return new EngineIniTarget(file, File.Exists(file), source);
    }

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try { return Directory.EnumerateDirectories(root).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}

/// <summary>Where ReShade looks for add-ons: <c>[ADDON] AddonPath</c> in its ini, else its own folder.</summary>
public static class ReShadeAddonPath
{
    public const string IniName = "ReShade.ini";

    /// <summary>
    /// The folder an add-on goes into. The first entry of AddonPath counts (a list is comma or semicolon separated); a relative
    /// one is relative to the folder ReShade sits in; a trailing wildcard (<c>\**</c>) is dropped. A path outside the game and the
    /// install folder is not one we may write to, so the install folder is used then.
    /// </summary>
    public static string Resolve(string targetDir, string gameRoot)
    {
        var ini = System.IO.Path.Combine(targetDir, IniName);
        string? value = null;
        try { value = File.Exists(ini) ? IniFile.Load(ini).Get("ADDON", "AddonPath") : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return FromValue(value, targetDir, gameRoot);
    }

    internal static string FromValue(string? value, string targetDir, string gameRoot)
    {
        foreach (var entry in (value ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = Environment.ExpandEnvironmentVariables(entry.Trim('"')).TrimEnd('\\', '/', '*');
            if (p.Length == 0 || p == ".") continue;
            string full;
            try { full = FileUtil.Normalize(System.IO.Path.IsPathRooted(p) ? p : System.IO.Path.Combine(targetDir, p)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { continue; }
            if (FileUtil.IsUnder(full, gameRoot) || FileUtil.IsUnder(full, targetDir) || full.Equals(FileUtil.Normalize(targetDir), StringComparison.OrdinalIgnoreCase))
                return full;
        }
        return FileUtil.Normalize(targetDir);
    }
}
