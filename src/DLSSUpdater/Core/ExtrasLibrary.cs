
using System.Security.Cryptography;
using System.Text;

namespace DLSSUpdater.Core;

/// <summary>
/// Add-ons and shader packs the user imported (a file, a folder or a .zip), kept byte for byte under
/// components\reshade-extras\imported and never cleaned up automatically. They are offered next to the catalog's, with ids
/// starting with <see cref="Prefix"/>, and installed the same way.
/// </summary>
public static class ExtrasLibrary
{
    public const string Prefix = "imported:";
    private const string InfoFile = ".import.txt";

    public static string Root => Path.Combine(AppPaths.Components, "reshade-extras", "imported");
    /// <summary>Verified downloads, unpacked; safe to delete, they are fetched again when needed.</summary>
    public static string CacheRoot => Path.Combine(AppPaths.Cache, "reshade-extras");
    public static string AddonsRoot => Path.Combine(Root, "addons");
    public static string PacksRoot => Path.Combine(Root, "packs");

    public static bool IsImported(string id) => id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static string Slug(string name)
    {
        var s = new string(name.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray()).Trim('-', '.');
        return s.Length == 0 ? "import" : s.Length > 60 ? s[..60] : s;
    }

    public static string DirOf(string id) =>
        Path.Combine(Directory.Exists(Path.Combine(AddonsRoot, id[Prefix.Length..])) ? AddonsRoot : PacksRoot, id[Prefix.Length..]);

    // ---- add-ons ----

    /// <summary>Imports an add-on file (.addon, .addon32, .addon64) or a .zip that holds some; the id of the entry.</summary>
    /// <exception cref="InvalidDataException">The file holds no add-on, or the zip is not safe to unpack.</exception>
    public static string ImportAddon(string source)
    {
        var name = Path.GetFileNameWithoutExtension(source);
        var id = Prefix + Slug(name);
        var dir = Path.Combine(AddonsRoot, Slug(name));
        var staging = dir + ".new";
        FileUtil.DeleteDirectory(staging);
        try
        {
            Directory.CreateDirectory(staging);
            if (Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipStage.Extract(source, staging, n => ExtrasFiles.IsAddonName(n));
                if (Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).FirstOrDefault() is null)
                    throw new InvalidDataException($"{Path.GetFileName(source)} holds no .addon, .addon32 or .addon64 file.");
            }
            else if (ExtrasFiles.IsAddonName(source)) File.Copy(source, Path.Combine(staging, Path.GetFileName(source)), true);
            else throw new InvalidDataException($"{Path.GetFileName(source)} is not an add-on (.addon, .addon32, .addon64) or a .zip.");
            FileUtil.AtomicWriteText(Path.Combine(staging, InfoFile), $"name={name}\r\n");
            Replace(staging, dir);
            Log.Info($"Imported ReShade add-on {name}");
            return id;
        }
        finally { FileUtil.DeleteDirectory(staging); }
    }

    public static IReadOnlyList<CatalogAddon> Addons()
    {
        var list = new List<CatalogAddon>();
        if (!Directory.Exists(AddonsRoot)) return list;
        foreach (var dir in Directory.EnumerateDirectories(AddonsRoot).Where(d => !d.EndsWith(".new", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Where(f => ExtrasFiles.IsAddonName(f)).Order(StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0) continue;
            var payload = files.Select(f => new CatalogPayloadItem
            {
                Entry = Path.GetRelativePath(dir, f).Replace('\\', '/'),
                Kind = "addon",
                Arch = Path.GetExtension(f).Equals(".addon32", StringComparison.OrdinalIgnoreCase) ? "x86" : "x64",
            }).ToList();
            var slug = Path.GetFileName(dir);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", files.Select(f => FileUtil.Sha256(f)))))).ToLowerInvariant();
            list.Add(new CatalogAddon
            {
                Id = Prefix + slug, Name = slug, Imported = true, Description = "Imported by you.",
                Files = [new CatalogBinary { Name = slug, Arch = "both", Url = "imported://" + slug, Sha256 = hash, Payload = payload }],
            });
        }
        return list;
    }

    // ---- shader packs ----

    /// <summary>Imports a folder or .zip that holds shaders (a Shaders folder with .fx files, or .fx files directly).</summary>
    public static string ImportPack(string source)
    {
        var isZip = File.Exists(source) && Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase);
        if (!isZip && !Directory.Exists(source)) throw new InvalidDataException($"{Path.GetFileName(source)} is neither a folder nor a .zip.");
        var name = isZip ? Path.GetFileNameWithoutExtension(source) : Path.GetFileName(FileUtil.Normalize(source));
        var slug = Slug(name);
        var dir = Path.Combine(PacksRoot, slug);
        var staging = dir + ".new";
        var unpacked = dir + ".unpack";
        FileUtil.DeleteDirectory(staging);
        FileUtil.DeleteDirectory(unpacked);
        try
        {
            var from = source;
            if (isZip)
            {
                ZipStage.Extract(source, unpacked);
                from = unpacked;
            }
            var (shaders, textures) = FindShaderRoot(from) ?? throw new InvalidDataException($"{name} holds no .fx shaders.");
            Copy(shaders, Path.Combine(staging, "Shaders"));
            if (textures is not null) Copy(textures, Path.Combine(staging, "Textures"));
            FileUtil.AtomicWriteText(Path.Combine(staging, InfoFile), $"name={name}\r\nsha256={TreeHash(staging)}\r\n");
            Replace(staging, dir);
            Log.Info($"Imported shader pack {name}");
            return Prefix + slug;
        }
        finally
        {
            FileUtil.DeleteDirectory(staging);
            FileUtil.DeleteDirectory(unpacked);
        }
    }

    public static IReadOnlyList<CatalogShaderPack> Packs()
    {
        var list = new List<CatalogShaderPack>();
        if (!Directory.Exists(PacksRoot)) return list;
        foreach (var dir in Directory.EnumerateDirectories(PacksRoot).Where(d => d.EndsWith(".new") == false && d.EndsWith(".unpack") == false).Order(StringComparer.OrdinalIgnoreCase))
        {
            var info = Path.Combine(dir, InfoFile);
            var sha = File.Exists(info) ? File.ReadAllLines(info).Select(l => l.Split('=', 2)).FirstOrDefault(p => p[0] == "sha256" && p.Length == 2)?[1] : null;
            if (sha is not { Length: 64 } || !Directory.Exists(Path.Combine(dir, "Shaders"))) continue;
            var slug = Path.GetFileName(dir);
            var effects = Directory.EnumerateFiles(Path.Combine(dir, "Shaders"), "*.fx", SearchOption.AllDirectories).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase).ToList();
            list.Add(new CatalogShaderPack
            {
                Id = Prefix + slug, Name = slug, Imported = true, Description = "Imported by you.", Category = "general", Source = "imported", Version = "imported",
                File = new CatalogBinary
                {
                    Name = slug, Url = "imported://" + slug, Sha256 = sha,
                    Layout = new CatalogPackLayout { ShadersDir = "Shaders/", TexturesDir = Directory.Exists(Path.Combine(dir, "Textures")) ? "Textures/" : null, Effects = effects! },
                },
            });
        }
        return list;
    }

    /// <summary>Deletes an import. Only the user asks for this; nothing cleans the imports up by itself.</summary>
    public static void Remove(string id)
    {
        if (!IsImported(id)) return;
        FileUtil.DeleteDirectory(Path.Combine(AddonsRoot, id[Prefix.Length..]));
        FileUtil.DeleteDirectory(Path.Combine(PacksRoot, id[Prefix.Length..]));
    }

    /// <summary>The Shaders folder (and the Textures folder beside it) inside a folder: the shallowest "Shaders" that holds an .fx, else the folder itself when it holds .fx files.</summary>
    public static (string Shaders, string? Textures)? FindShaderRoot(string dir)
    {
        var walk = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var shaders = Directory.EnumerateDirectories(dir, "Shaders", walk)
            .Where(d => Directory.EnumerateFiles(d, "*.fx", walk).Any())
            .OrderBy(d => d.Count(c => c == Path.DirectorySeparatorChar)).FirstOrDefault();
        if (shaders is null)
            return Directory.EnumerateFiles(dir, "*.fx", walk).Any() ? (dir, null) : null;
        var textures = Path.Combine(Path.GetDirectoryName(shaders)!, "Textures");
        return (shaders, Directory.Exists(textures) ? textures : null);
    }

    private static void Copy(string from, string to)
    {
        var walk = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(from, "*", walk))
        {
            var dest = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }

    private static string TreeHash(string dir)
    {
        var sb = new StringBuilder();
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != InfoFile).Order(StringComparer.OrdinalIgnoreCase))
            sb.Append(Path.GetRelativePath(dir, f).Replace('\\', '/').ToLowerInvariant()).Append(' ').Append(FileUtil.Sha256(f)).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private static void Replace(string staging, string dir)
    {
        FileUtil.DeleteDirectory(dir);
        Directory.CreateDirectory(Path.GetDirectoryName(dir)!);
        Directory.Move(staging, dir);
    }
}
