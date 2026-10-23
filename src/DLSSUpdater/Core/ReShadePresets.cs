namespace DLSSUpdater.Core;

/// <summary>What a ReShade preset file asks for.</summary>
/// <param name="Effects">File names (lower case, sorted, no duplicates) of the effects its Techniques come from.</param>
public sealed record PresetCheck(bool Valid, string? Problem, IReadOnlyList<string> Techniques, IReadOnlyList<string> Effects);

/// <summary>A preset in the library.</summary>
public sealed record PresetInfo(string Name, string Path, PresetCheck Check);

/// <summary>
/// ReShade presets the user imported: validated (a root-level <c>Techniques=</c> list with at least one <c>Name@File.fx</c>),
/// kept in the presets library under the data folder, and applied to a game by copying the file in and pointing
/// <c>[GENERAL] PresetPath</c> of its ReShade.ini at the copy.
/// </summary>
public static class ReShadePresets
{
    public const long MaxBytes = 2 << 20;

    public static string LibraryDir => System.IO.Path.Combine(AppPaths.Root, "reshade-presets");

    public static string PathOf(string name) => System.IO.Path.Combine(LibraryDir, name + ".ini");

    public static string SafeName(string name)
    {
        var s = new string(name.Trim().Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim('.', ' ', '-');
        return s.Length == 0 ? "preset" : s.Length > 80 ? s[..80] : s;
    }

    public static PresetCheck Check(string text)
    {
        if (text.Contains('\0')) return Invalid("not a text file");
        var techniques = IniFile.Parse(text).Entries()
            .Where(e => e.Section.Length == 0 && e.Key.Equals("Techniques", StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => e.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        var good = techniques.Where(t => EffectOf(t) is not null).ToList();
        if (techniques.Count == 0) return Invalid("no Techniques= line, so it is not a ReShade preset");
        if (good.Count == 0) return Invalid("its Techniques= line has no Name@File.fx entry");
        return new PresetCheck(true, null, good, good.Select(t => EffectOf(t)!).Distinct().Order(StringComparer.Ordinal).ToList());
    }

    private static PresetCheck Invalid(string problem) => new(false, problem, [], []);

    /// <summary>"AO@qUINT_mxao.fx" -> "quint_mxao.fx"; null when the entry isn't that shape.</summary>
    private static string? EffectOf(string technique)
    {
        var at = technique.IndexOf('@');
        if (at <= 0) return null;
        var file = technique[(at + 1)..].Trim();
        return file.EndsWith(".fx", StringComparison.OrdinalIgnoreCase) && file.Length > 3 && file.IndexOfAny(['/', '\\', ':']) < 0 ? file.ToLowerInvariant() : null;
    }

    /// <summary>Copies a preset file into the library (a second import under the same name replaces it) and returns its name.</summary>
    /// <exception cref="InvalidDataException">The file is not a ReShade preset; nothing is stored.</exception>
    public static string Import(string source)
    {
        var info = new FileInfo(source);
        if (!info.Exists) throw new FileNotFoundException("The preset file is not there.", source);
        if (info.Length > MaxBytes) throw new InvalidDataException($"{info.Name} is {info.Length / 1024} KB; presets are small text files.");
        var text = File.ReadAllText(source);
        var check = Check(text);
        if (!check.Valid) throw new InvalidDataException($"{info.Name} is not a ReShade preset: {check.Problem}.");
        var name = SafeName(System.IO.Path.GetFileNameWithoutExtension(source));
        Directory.CreateDirectory(LibraryDir);
        File.Copy(source, PathOf(name), true);
        Log.Info($"Imported ReShade preset {name} ({check.Effects.Count} effect file(s))");
        return name;
    }

    public static IReadOnlyList<PresetInfo> List()
    {
        if (!Directory.Exists(LibraryDir)) return [];
        var list = new List<PresetInfo>();
        foreach (var file in Directory.EnumerateFiles(LibraryDir, "*.ini").Order(StringComparer.OrdinalIgnoreCase))
        {
            try { list.Add(new PresetInfo(System.IO.Path.GetFileNameWithoutExtension(file), file, Check(File.ReadAllText(file)))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Preset {file} unreadable ({ex.Message})"); }
        }
        return list;
    }

    public static PresetInfo? Find(string? name) => name is null ? null : List().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static void Delete(string name) => FileUtil.TryDeleteFile(PathOf(name));

    /// <summary>The effect files (lower case) the game can load now: every .fx below the folders on its effect search path and our shader folders.</summary>
    public static HashSet<string> InstalledEffects(string target, string gameRoot)
    {
        target = FileUtil.Normalize(target);
        var folders = new List<string> { System.IO.Path.Combine(target, ReShadeLayout.ShadersFolder, "Shaders"), System.IO.Path.Combine(target, ReShadeLayout.ManagedFolder, "Shaders") };
        var configured = ReShadeLayout.ReadIni(target)?.Get("GENERAL", "EffectSearchPaths");
        foreach (var item in (configured ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var path = item.TrimEnd('*').TrimEnd('\\', '/');
            if (path.Length == 0) continue;
            try { folders.Add(FileUtil.Normalize(System.IO.Path.Combine(target, path))); }
            catch (ArgumentException) { }
        }
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var walk = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var dir in folders.Distinct(StringComparer.OrdinalIgnoreCase).Where(d => FileUtil.IsUnder(d, gameRoot) && Directory.Exists(d)))
            foreach (var file in Directory.EnumerateFiles(dir, "*.fx", walk))
                found.Add(System.IO.Path.GetFileName(file));
        return found;
    }

    /// <summary>The effect files the preset needs that the game doesn't have.</summary>
    public static IReadOnlyList<string> Missing(PresetCheck preset, IReadOnlyCollection<string> installedEffects) =>
        preset.Effects.Where(e => !installedEffects.Contains(e, StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Packs that hold the missing effects, fewest packs first (each step takes the pack with the most of what is still missing).
    /// What no pack has is left out.
    /// </summary>
    public static IReadOnlyList<CatalogShaderPack> SuggestPacks(IEnumerable<string> missing, IEnumerable<CatalogShaderPack> packs)
    {
        var left = new HashSet<string>(missing, StringComparer.OrdinalIgnoreCase);
        var candidates = packs.Where(p => p.IsInstallable).ToList();
        var chosen = new List<CatalogShaderPack>();
        while (left.Count > 0)
        {
            var best = candidates.Select(p => (Pack: p, Hits: p.EffectNames.Count(left.Contains))).Where(x => x.Hits > 0)
                .OrderByDescending(x => x.Hits).ThenBy(x => x.Pack.Default ? 0 : 1).FirstOrDefault();
            if (best.Pack is null) break;
            chosen.Add(best.Pack);
            candidates.Remove(best.Pack);
            left.ExceptWith(best.Pack.EffectNames);
        }
        return chosen;
    }
}
