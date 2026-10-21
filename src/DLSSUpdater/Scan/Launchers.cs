using System.Text.Json;
using Microsoft.Win32;
using DLSSUpdater.Core;

namespace DLSSUpdater.Scan;

/// <summary>Finds installed games from the launchers' own records. Every source is best-effort.</summary>
public static class Launchers
{
    public static IEnumerable<GameEntry> All() =>
        Safe("Steam", Steam)
            .Concat(Safe("Epic", Epic))
            .Concat(Safe("GOG", Gog))
            .Concat(Safe("EA", Ea))
            .Concat(Safe("Ubisoft", Ubisoft))
            .Concat(Safe("Xbox", Xbox));

    private static IEnumerable<GameEntry> Safe(string name, Func<IEnumerable<GameEntry>> source)
    {
        try { return source().ToList(); }
        catch (Exception ex)
        {
            Log.Error($"{name} library scan failed", ex);
            return [];
        }
    }

    // ---------- Steam ----------

    private static readonly HashSet<string> SteamSkipIds =
    [
        "228980",  // Steamworks Common Redistributables
        "250820",  // SteamVR
        "431960",  // Wallpaper Engine
        "1070560", "1391110", "1628350", "1826330", // Steam Linux Runtime
    ];

    private static readonly string[] SteamSkipNames = ["Proton", "Steam Linux Runtime", "Steamworks", "Redistributable", "SteamVR"];

    public static IEnumerable<GameEntry> Steam()
    {
        var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                        ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (string.IsNullOrEmpty(steamPath)) return [];
        steamPath = Path.GetFullPath(steamPath.Replace('/', '\\'));

        var libraries = new List<string> { steamPath };
        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(vdfPath))
            {
                var root = VdfNode.Parse(File.ReadAllText(vdfPath));
                var folders = root.Child("libraryfolders") ?? root;
                foreach (var lib in folders.Children.Values)
                    if (lib["path"] is { } p) libraries.Add(p);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Steam library list unreadable ({ex.Message}), scanning the main library only");
        }
        return SteamLibraries(libraries);
    }

    /// <summary>Games in the given Steam libraries. One unreadable library or manifest only drops itself.</summary>
    internal static List<GameEntry> SteamLibraries(IEnumerable<string> libraries)
    {
        var games = new List<GameEntry>();
        foreach (var lib in libraries.Select(FileUtil.Normalize).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { games.AddRange(SteamLibrary(lib)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Info($"Steam library {lib} skipped ({ex.Message})");
            }
        }
        return games;
    }

    private static List<GameEntry> SteamLibrary(string lib)
    {
        var games = new List<GameEntry>();
        var apps = Path.Combine(lib, "steamapps");
        if (!Directory.Exists(apps)) return games;
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
        {
            try
            {
                var state = VdfNode.Parse(File.ReadAllText(acf)).Child("AppState");
                if (state is null) continue;
                var id = state["appid"] ?? "";
                var name = state["name"] ?? "";
                var dir = state["installdir"];
                if (dir is null || SteamSkipIds.Contains(id)) continue;
                if (SteamSkipNames.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
                var path = Path.Combine(apps, "common", dir);
                listed.Add(dir);
                if (Directory.Exists(path)) games.Add(new GameEntry(name.Length > 0 ? name : dir, path, "Steam", int.TryParse(id, out var appId) && appId > 0 ? appId : null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        // Folders without a manifest (copied in, or kept after a Steam reinstall) still count if they hold an exe.
        var common = Path.Combine(apps, "common");
        if (!Directory.Exists(common)) return games;
        foreach (var dir in Directory.EnumerateDirectories(common))
        {
            var folder = Path.GetFileName(dir);
            if (listed.Contains(folder) || SteamSkipNames.Any(s => folder.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            if (folder.Equals("wallpaper_engine", StringComparison.OrdinalIgnoreCase) || folder.Equals("Steam Controller Configs", StringComparison.OrdinalIgnoreCase)) continue;
            if (HasExe(dir)) games.Add(new GameEntry(folder, dir, "Steam"));
        }
        return games;
    }

    private static bool HasExe(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true }).Any();
        }
        catch (IOException) { return false; }
    }

    // ---------- Epic ----------

    public static IEnumerable<GameEntry> Epic() =>
        EpicManifests(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests"));

    /// <summary>Games in Epic's *.item manifests. A damaged or unreadable manifest only drops itself.</summary>
    internal static List<GameEntry> EpicManifests(string dir)
    {
        var games = new List<GameEntry>();
        if (!Directory.Exists(dir)) return games;
        foreach (var item in Directory.EnumerateFiles(dir, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(item));
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object) continue;
                if (r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) continue;
                if (r.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array &&
                    !cats.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == "games")) continue;
                var name = StringOf(r, "DisplayName");
                var loc = StringOf(r, "InstallLocation");
                if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc))
                    games.Add(new GameEntry(name ?? Path.GetFileName(loc), loc, "Epic"));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        }
        return games;
    }

    private static string? StringOf(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ---------- GOG ----------

    public static IEnumerable<GameEntry> Gog()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (key is null) yield break;
        foreach (var sub in key.GetSubKeyNames())
        {
            using var g = key.OpenSubKey(sub);
            var path = g?.GetValue("path") as string;
            var name = g?.GetValue("gameName") as string;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                yield return new GameEntry(name ?? Path.GetFileName(path), path, "GOG");
        }
    }

    // ---------- EA ----------

    private static readonly HashSet<string> EaSkip = new(StringComparer.OrdinalIgnoreCase)
        { "EA Desktop", "EA Core", "EADM", "Origin", "EA app" };

    public static IEnumerable<GameEntry> Ea()
    {
        foreach (var hive in new[] { @"SOFTWARE\WOW6432Node\EA Games", @"SOFTWARE\WOW6432Node\Origin Games", @"SOFTWARE\WOW6432Node\Electronic Arts", @"SOFTWARE\EA Games" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(hive);
            if (key is null) continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                if (EaSkip.Contains(sub)) continue;
                using var g = key.OpenSubKey(sub);
                var path = (g?.GetValue("Install Dir") ?? g?.GetValue("InstallDir")) as string;
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;
                var name = g?.GetValue("DisplayName") as string ?? sub;
                yield return new GameEntry(name, path, "EA");
            }
        }
    }

    // ---------- Ubisoft ----------

    public static IEnumerable<GameEntry> Ubisoft()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (key is null) yield break;
        foreach (var sub in key.GetSubKeyNames())
        {
            using var g = key.OpenSubKey(sub);
            var path = (g?.GetValue("InstallDir") as string)?.Replace('/', '\\');
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                yield return new GameEntry(Path.GetFileName(path.TrimEnd('\\')), path, "Ubisoft");
        }
    }

    // ---------- Xbox / Microsoft Store (moddable XboxGames folders only) ----------

    public static IEnumerable<GameEntry> Xbox()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;
            var dir = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
            if (!Directory.Exists(dir)) continue;
            foreach (var game in Directory.EnumerateDirectories(dir))
            {
                if (Path.GetFileName(game).Equals("GameSave", StringComparison.OrdinalIgnoreCase)) continue;
                var content = Path.Combine(game, "Content");
                yield return new GameEntry(Path.GetFileName(game), Directory.Exists(content) ? content : game, "Xbox");
            }
        }
    }
}
