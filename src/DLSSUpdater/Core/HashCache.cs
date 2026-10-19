using System.Globalization;
using System.Text;

namespace DLSSUpdater.Core;

/// <summary>
/// SHA-256 memo keyed by path + size + mtime. <see cref="Load"/> / <see cref="Save"/> keep it across launches, so
/// startup doesn't re-hash every game's DLLs and the NR runtime (seconds of UI-thread time on a big library).
/// </summary>
public static class HashCache
{
    private static readonly Dictionary<(string, long, DateTime), string> Cache = new();
    private static bool _dirty;

    private static string FilePath => Path.Combine(AppPaths.Cache, "hashes.tsv");

    public static string Get(string path)
    {
        if (Demo.Active && Demo.HashOf(path) is { } demo) return demo;
        var fi = new FileInfo(path);
        var key = (fi.FullName.ToLowerInvariant(), fi.Length, fi.LastWriteTimeUtc);
        lock (Cache)
            if (Cache.TryGetValue(key, out var h)) return h;
        var hash = FileUtil.Sha256(path);
        lock (Cache)
        {
            Cache[key] = hash;
            _dirty = true;
        }
        return hash;
    }

    /// <summary>Reads the saved hashes; entries whose file is gone or changed since are dropped.</summary>
    public static void Load()
    {
        string[] lines;
        try { lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath) : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn($"Hash cache unreadable, starting empty: {e.Message}"); return; }
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length != 4
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
                continue;
            var fi = new FileInfo(parts[3]);
            var mtime = new DateTime(ticks, DateTimeKind.Utc);
            if (!fi.Exists || fi.Length != length || fi.LastWriteTimeUtc != mtime) continue;
            lock (Cache) Cache.TryAdd((parts[3], length, mtime), parts[0]);
        }
    }

    public static void Save()
    {
        var sb = new StringBuilder();
        lock (Cache)
        {
            if (!_dirty) return;
            foreach (var ((path, length, mtime), sha) in Cache)
                sb.Append(sha).Append('\t').Append(length.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(mtime.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(path).Append('\n');
            _dirty = false;
        }
        try { FileUtil.AtomicWriteText(FilePath, sb.ToString()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn($"Hash cache not saved: {e.Message}"); }
    }
}
