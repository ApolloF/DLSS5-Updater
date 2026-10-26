namespace DLSSUpdater.Core;

/// <summary>One thing the planner left out, with the reason shown to the user.</summary>
public sealed record ExtrasSkip(string Id, string Reason);

/// <summary>An add-on's download after it was verified and unpacked into <see cref="Dir"/>.</summary>
public sealed record AddonSource(CatalogAddon Addon, CatalogBinary File, string Dir);

/// <summary>What a set of add-ons puts into a game: files, what was left out, and the hash tokens an update check compares.</summary>
public sealed class AddonPlan
{
    public List<(string Source, string Dest)> Files { get; } = [];
    public List<ExtrasSkip> Skips { get; } = [];
    /// <summary>Add-on id -> hash token (<see cref="ReShadeAddonPlanner.Token"/>) of the ones placed.</summary>
    public Dictionary<string, string> Placed { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasEffects { get; set; }
}

/// <summary>Names the app never places from a download, whatever the catalog says.</summary>
public static class ExtrasFiles
{
    private static readonly HashSet<string> Executable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".com", ".scr", ".msi", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".lnk", ".jar", ".sys", ".cpl", ".reg", ".hta", ".url", ".pif",
    };

    public static bool IsExecutableName(string name) => Executable.Contains(Path.GetExtension(name));

    public static bool IsAddonName(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".addon" or ".addon32" or ".addon64";

    /// <summary>A relative path with '/' or '\' separators that stays inside its folder and has no hidden or executable file; null when it doesn't.</summary>
    public static string? SafeRelative(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(p => p is "." or ".." || p.StartsWith('.') || p.Contains(':') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return null;
        return IsExecutableName(parts[^1]) ? null : string.Join(Path.DirectorySeparatorChar, parts);
    }
}

/// <summary>
/// Turns the chosen add-ons into files for a game: the add-on binaries into the folder ReShade loads add-ons from (32-bit or
/// 64-bit ones to match the game), effect files below the shader folder. An add-on with a file that another add-on or component
/// already owns is left out whole, with the reason, instead of being half installed.
/// </summary>
public static class ReShadeAddonPlanner
{
    public static string ArchFor(int bits) => bits == 32 ? "x86" : "x64";

    private static bool Fits(string? arch, string want) =>
        string.IsNullOrEmpty(arch) || arch.Equals("both", StringComparison.OrdinalIgnoreCase) || arch.Equals(want, StringComparison.OrdinalIgnoreCase);

    /// <summary>The downloads of an add-on that serve a game of this bitness.</summary>
    public static IReadOnlyList<CatalogBinary> FilesFor(CatalogAddon addon, int bits) =>
        addon.Files.Where(f => f.IsInstallable && Fits(f.Arch, ArchFor(bits))).ToList();

    /// <summary>The hashes of those downloads; an update check compares this with what the install record kept.</summary>
    public static string Token(CatalogAddon addon, int bits) =>
        string.Join("+", FilesFor(addon, bits).Select(f => f.Sha256!.ToLowerInvariant()).Order(StringComparer.Ordinal));

    /// <summary>Why the app can't install the add-on in a game of this bitness, or null.</summary>
    public static string? WhyNot(CatalogAddon addon, int bits)
    {
        if (addon.ManagedLabel is { } managed) return $"managed as {managed}; use that instead";
        if (addon.Manual is { } manual) return manual;
        if (addon.Files.Count == 0 || !addon.Files.Any(f => f.IsInstallable)) return "the catalog has no verified download for it";
        return FilesFor(addon, bits).Count == 0 ? $"there is no {bits}-bit build of it" : null;
    }

    /// <param name="otherOwner">Who else owns a destination file (a name for the message), or null.</param>
    public static AddonPlan Plan(IEnumerable<AddonSource> sources, string addonDir, string effectsDir, int bits, Func<string, string?> otherOwner)
    {
        var plan = new AddonPlan();
        var want = ArchFor(bits);
        var planned = new Dictionary<string, (string Source, string Id)>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in sources.GroupBy(s => s.Addon.Id, StringComparer.OrdinalIgnoreCase))
        {
            var addon = group.First().Addon;
            var items = new List<(string Source, string Dest, bool Effect)>();
            string? problem = null;
            foreach (var src in group)
            {
                var payload = src.File.Payload is { Count: > 0 } p ? p : [new CatalogPayloadItem { Entry = src.File.Name, Kind = "addon", Arch = src.File.Arch }];
                foreach (var item in payload)
                {
                    var effect = item.Kind.Equals("effect", StringComparison.OrdinalIgnoreCase);
                    if (!effect && !Fits(item.Arch ?? src.File.Arch, want)) continue;
                    var dest = Destination(item, effect, addonDir, effectsDir);
                    var source = SourcePath(src.Dir, item.Entry);
                    if (dest is null) { problem = $"{item.Entry} is not a file the app places"; break; }
                    if (source is null) { problem = $"{item.Entry} is missing from the download"; break; }
                    items.Add((source, dest, effect));
                }
                if (problem is not null) break;
            }
            if (problem is null && items.Count == 0) problem = $"it has nothing for a {bits}-bit game";
            if (problem is null)
                foreach (var (source, dest, _) in items)
                {
                    if (planned.TryGetValue(dest, out var first) && first.Id != addon.Id && !SameContent(first.Source, source))
                        problem = $"{Path.GetFileName(dest)} is also placed by {first.Id}";
                    else if (otherOwner(dest) is { } owner)
                        problem = $"{Path.GetFileName(dest)} belongs to {owner}";
                    if (problem is not null) break;
                }
            if (problem is not null)
            {
                plan.Skips.Add(new ExtrasSkip(addon.Id, $"{addon.Name}: {problem}"));
                continue;
            }
            foreach (var (source, dest, effect) in items)
            {
                if (planned.TryAdd(dest, (source, addon.Id))) plan.Files.Add((source, dest));
                plan.HasEffects |= effect;
            }
            plan.Placed[addon.Id] = Token(addon, bits);
        }
        return plan;
    }

    private static string? Destination(CatalogPayloadItem item, bool effect, string addonDir, string effectsDir)
    {
        if (!effect)
        {
            var leaf = Path.GetFileName(item.Entry.Replace('/', '\\'));
            return leaf.Length > 0 && !leaf.StartsWith('.') && ExtrasFiles.IsAddonName(leaf) && leaf.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                ? Path.Combine(addonDir, leaf) : null;
        }
        return ExtrasFiles.SafeRelative(item.Dest ?? "") is { } rel ? Path.Combine(effectsDir, rel) : null;
    }

    private static string? SourcePath(string dir, string entry)
    {
        if (ZipStage.SafeName(entry) is not { } name) return null;
        var full = Path.GetFullPath(Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar)));
        return FileUtil.IsUnder(full, dir) && File.Exists(full) ? full : null;
    }

    private static bool SameContent(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && FileUtil.Sha256(a).Equals(FileUtil.Sha256(b), StringComparison.OrdinalIgnoreCase);
}
