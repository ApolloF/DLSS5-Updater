using System.IO.Compression;

namespace DLSSUpdater.Core;

/// <summary>
/// Safe extraction of a downloaded zip into an empty staging folder: every entry stays below the folder, nothing is a link,
/// and the number and size of the entries are capped (a hash-checked zip is still untrusted data).
/// </summary>
public static class ZipStage
{
    public const int MaxEntries = 40_000;
    public const long MaxEntryBytes = 512L << 20;
    public const long MaxTotalBytes = 3L << 30;

    /// <summary>The entry name with '/' separators and no leading slash; null when the entry would leave the folder.</summary>
    public static string? SafeName(string entry)
    {
        var name = entry.Replace('\\', '/').TrimStart('/');
        if (name.Length == 0 || name.Contains(':') || name.Any(char.IsControl)) return null;
        var parts = name.Split('/');
        return parts.Any(p => p is ".." or ".") ? null : name;
    }

    /// <summary>Extracts the entries <paramref name="include"/> accepts (all by default) and returns their names.</summary>
    /// <exception cref="InvalidDataException">An entry leaves the folder or a limit is passed; the caller deletes the folder.</exception>
    public static IReadOnlyList<string> Extract(string zipPath, string destDir, Func<string, bool>? include = null)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > MaxEntries) throw new InvalidDataException($"{Path.GetFileName(zipPath)} has {zip.Entries.Count} entries, more than the {MaxEntries} allowed");
        Directory.CreateDirectory(destDir);
        var root = FileUtil.Normalize(destDir);
        var names = new List<string>();
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            var name = SafeName(entry.FullName) ?? throw new InvalidDataException($"{Path.GetFileName(zipPath)}: entry \"{entry.FullName}\" leaves the folder it is extracted into");
            if (include is not null && !include(name)) continue;
            var dest = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!FileUtil.IsUnder(dest, root)) throw new InvalidDataException($"{Path.GetFileName(zipPath)}: entry \"{entry.FullName}\" leaves the folder it is extracted into");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using (var input = entry.Open())
            using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                total += CopyLimited(input, output, Math.Min(MaxEntryBytes, MaxTotalBytes - total), entry.FullName);
            names.Add(name);
        }
        return names;
    }

    private static long CopyLimited(Stream input, Stream output, long limit, string name)
    {
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            written += read;
            if (written > limit) throw new InvalidDataException($"\"{name}\" is larger than the size limit");
            output.Write(buffer, 0, read);
        }
        return written;
    }
}
