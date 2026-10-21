namespace DLSSUpdater.Core;

/// <summary>The shader folder the app deploys packs into, and whether it is ours or the user's.</summary>
/// <param name="Name">Folder name below the install folder.</param>
/// <param name="UserFolderKept">A folder the user already had is kept as it is; the packs go to <see cref="ReShadeLayout.ManagedFolder"/>.</param>
public sealed record ShaderFolder(string Name, string Dir, bool UserFolderKept)
{
    public string ShadersDir => Path.Combine(Dir, "Shaders");
    public string TexturesDir => Path.Combine(Dir, "Textures");
}

/// <summary>Where ReShade looks for add-ons, shaders and presets in a game, and the ReShade.ini keys that point there.</summary>
public static class ReShadeLayout
{
    public const string ShadersFolder = "reshade-shaders";
    public const string ManagedFolder = "reshade-shaders-managed";
    public const string PresetsFolder = "reshade-presets";
    /// <summary>Written into a shader folder the app created, so a later run knows it may manage it.</summary>
    public const string Marker = ".dlss5updater-shaders.json";

    public static string IniPath(string target) => Path.Combine(target, "ReShade.ini");

    /// <summary>
    /// The folder ReShade loads add-ons from: <c>[ADDON] AddonPath</c> of the game's ReShade.ini (one path, relative to the
    /// folder of the ini), else the install folder. A path that leaves the game is refused.
    /// </summary>
    public static (string? Dir, string? Refusal) AddonDir(string target, string gameRoot)
    {
        target = FileUtil.Normalize(target);
        var value = ReadIni(target)?.Get("ADDON", "AddonPath");
        if (string.IsNullOrWhiteSpace(value)) return (target, null);
        string full;
        try { full = FileUtil.Normalize(Path.Combine(target, value.Trim().Trim('"'))); }
        catch (ArgumentException) { return (null, $"[ADDON] AddonPath in ReShade.ini (\"{value}\") is not a valid path"); }
        return FileUtil.IsUnder(full, gameRoot)
            ? (full, null)
            : (null, $"[ADDON] AddonPath in ReShade.ini points outside the game ({value}); add-ons are only placed inside the game folder");
    }

    /// <summary>
    /// <c>reshade-shaders</c> when it is ours (missing, marked, or holding only files this app placed), else the managed folder
    /// next to the user's own, which stays untouched.
    /// </summary>
    public static ShaderFolder ResolveShaderFolder(string target, string gameRoot, InstallManifest? manifest)
    {
        target = FileUtil.Normalize(target);
        var dir = Path.Combine(target, ShadersFolder);
        if (!Directory.Exists(dir) || File.Exists(Path.Combine(dir, Marker)) || OnlyOurs(dir, target, gameRoot, manifest))
            return new ShaderFolder(ShadersFolder, dir, false);
        return new ShaderFolder(ManagedFolder, Path.Combine(target, ManagedFolder), true);
    }

    private static bool OnlyOurs(string dir, string target, string gameRoot, InstallManifest? manifest)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(dir, "*", options))
        {
            var entry = InstallManifest.Entry(file, target, gameRoot);
            if (manifest is null || !manifest.InstalledComponents().Any(c => c.FileAt(entry) is not null)) return false;
        }
        return true;
    }

    public static IniFile? ReadIni(string target)
    {
        try { return File.Exists(IniPath(target)) ? IniFile.Load(IniPath(target)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The search path entry for one subfolder of a shader folder: <c>.\reshade-shaders\Shaders\**</c>.</summary>
    public static string SearchEntry(ShaderFolder folder, string sub) => $@".\{folder.Name}\{sub}\**";

    /// <summary>
    /// <paramref name="existing"/> (a comma list as ReShade writes it) with <paramref name="entry"/> added when no entry
    /// names the same folder; the list unchanged otherwise.
    /// </summary>
    public static string MergeSearchPath(string? existing, string entry)
    {
        var items = (existing ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return items.Any(i => SameFolder(i, entry)) ? string.Join(",", items) : string.Join(",", [.. items, entry]);
    }

    private static string Canon(string path) =>
        path.Replace('/', '\\').Trim().TrimEnd('*').TrimEnd('\\').TrimStart('.').TrimStart('\\').ToLowerInvariant();

    private static bool SameFolder(string a, string b) => Canon(a) == Canon(b);

    /// <summary>The ReShade.ini edits that put the folder on the effect and texture search paths; empty when both are already there.</summary>
    public static IReadOnlyList<(string File, string Section, string Key, string Value)> SearchPathEdits(string target, ShaderFolder folder, bool effects, bool textures)
    {
        var ini = ReadIni(target);
        var edits = new List<(string, string, string, string)>();
        void One(string key, string sub)
        {
            // A key the ini lacks is written out (ReShade's own default for it differs between versions).
            var inIni = ini?.Get("GENERAL", key);
            var current = inIni ?? ReShadeIniTemplate.Load().FirstOrDefault(o => o.Section == "GENERAL" && o.Key == key)?.Value;
            var merged = MergeSearchPath(current, SearchEntry(folder, sub));
            if (inIni is null || merged != inIni) edits.Add((IniPath(target), "GENERAL", key, merged));
        }
        if (effects) One("EffectSearchPaths", "Shaders");
        if (textures) One("TextureSearchPaths", "Textures");
        return edits;
    }
}
