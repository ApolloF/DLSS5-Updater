using System.Text.Json;

namespace DLSSUpdater.Core;

/// <summary>A shader pack's download after it was verified and unpacked into <see cref="Dir"/> (the zip's own folder layout).</summary>
public sealed record PackSource(CatalogShaderPack Pack, string Dir);

public sealed class ShaderPlan
{
    public List<(string Source, string Dest)> Files { get; } = [];
    public List<ExtrasSkip> Skips { get; } = [];
    /// <summary>Pack ids in the order they were placed, with the token an update check compares (see <see cref="ShaderPackPlanner.Token"/>).</summary>
    public Dictionary<string, string> Placed { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Turns the chosen shader packs into files below the managed shader folder: the pack's Shaders and Textures folders keep their
/// layout. Where two packs have the same file the first in the list wins (an identical copy is fine; a different one is
/// reported). Hidden and executable files in a pack are never placed.
/// </summary>
public static class ShaderPackPlanner
{
    /// <summary>The hash and pin a pack was built from; an update check compares it with the install record.</summary>
    public static string Token(CatalogShaderPack pack) => $"{pack.File?.Sha256?.ToLowerInvariant()}";

    public static ShaderPlan Plan(IEnumerable<PackSource> sources, ShaderFolder folder, Func<string, string?> otherOwner)
    {
        var plan = new ShaderPlan();
        var planned = new Dictionary<string, (string Source, string PackId)>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var src in sources)
        {
            if (src.Pack.Layout is not { } layout)
            {
                plan.Skips.Add(new ExtrasSkip(src.Pack.Id, $"{src.Pack.Name}: the catalog has no layout for it"));
                continue;
            }
            foreach (var (zipDir, destDir) in new[] { (layout.ShadersDir, folder.ShadersDir), (layout.TexturesDir ?? "", folder.TexturesDir) })
            {
                if (zipDir.Length == 0) continue;
                var from = Path.GetFullPath(Path.Combine(src.Dir, zipDir.Replace('/', Path.DirectorySeparatorChar)));
                if (!FileUtil.IsUnder(from, src.Dir) || !Directory.Exists(from)) continue;
                foreach (var file in Directory.EnumerateFiles(from, "*", options).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var rel = ExtrasFiles.SafeRelative(Path.GetRelativePath(from, file));
                    if (rel is null) continue;
                    var dest = Path.Combine(destDir, rel);
                    if (planned.TryGetValue(dest, out var first))
                    {
                        if (first.PackId != src.Pack.Id && !Same(first.Source, file))
                            plan.Skips.Add(new ExtrasSkip(src.Pack.Id, $"{src.Pack.Name}: {rel} differs from the one in {first.PackId}, which is kept"));
                        continue;
                    }
                    if (otherOwner(dest) is { } owner)
                    {
                        plan.Skips.Add(new ExtrasSkip(src.Pack.Id, $"{src.Pack.Name}: {rel} belongs to {owner}, which is kept"));
                        continue;
                    }
                    planned[dest] = (file, src.Pack.Id);
                    plan.Files.Add((file, dest));
                }
            }
            plan.Placed[src.Pack.Id] = Token(src.Pack);
        }
        return plan;
    }

    /// <summary>The marker file content: which packs the folder holds, written next to them so a later run knows the folder is ours.</summary>
    public static string MarkerText(IEnumerable<CatalogShaderPack> packs) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["app"] = "DLSS5 Updater",
            ["note"] = "This folder is managed by DLSS5 Updater. Put your own shaders in a folder of their own.",
            ["packs"] = packs.Select(p => new { p.Id, p.Version, p.Repo }).ToList(),
        }, new JsonSerializerOptions { WriteIndented = true });

    private static bool Same(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && FileUtil.Sha256(a).Equals(FileUtil.Sha256(b), StringComparison.OrdinalIgnoreCase);
}
