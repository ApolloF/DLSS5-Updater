using System.Collections.Concurrent;
using System.Text.Json;
using DLSSUpdater.Core;

namespace DLSSUpdater.Scan;

/// <summary>What a PE scan of one game exe found. Valid while the exe keeps its last write time.</summary>
public sealed class PeScanResult
{
    public long WriteTicks { get; set; }
    /// <summary>32 or 64, 0 when the exe could not be read.</summary>
    public int Bits { get; set; }
    public GraphicsApis Apis { get; set; }
    /// <summary>The exe (or a dll scanned for it) links dxgi.dll, which alone hints at DX12.</summary>
    public bool Dxgi { get; set; }
}

/// <summary>
/// Remembers PE scan results in <see cref="AppPaths.DetectCacheFile"/>, one entry per exe path, so a rescan only reads exes
/// whose last write time changed. Entries of exes that no longer exist are dropped when the cache is saved.
/// </summary>
internal static class DetectCache
{
    private static readonly object Gate = new();
    private static ConcurrentDictionary<string, PeScanResult>? _entries;
    private static string? _loadedFrom;
    private static bool _dirty;

    private static ConcurrentDictionary<string, PeScanResult> Entries()
    {
        lock (Gate)
        {
            var file = AppPaths.DetectCacheFile;
            if (_entries is not null && _loadedFrom == file) return _entries;
            _entries = new ConcurrentDictionary<string, PeScanResult>(StringComparer.OrdinalIgnoreCase);
            _loadedFrom = file;
            _dirty = false;
            try
            {
                if (File.Exists(file) &&
                    JsonSerializer.Deserialize(File.ReadAllText(file), JsonCtx.Default.DictionaryStringPeScanResult) is { } saved)
                    foreach (var (k, v) in saved) _entries[k] = v;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
            return _entries;
        }
    }

    public static PeScanResult? Get(string exe, long writeTicks) =>
        Entries().TryGetValue(exe, out var r) && r.WriteTicks == writeTicks ? r : null;

    public static void Put(string exe, PeScanResult result)
    {
        Entries()[exe] = result;
        _dirty = true;
    }

    public static void Save()
    {
        ConcurrentDictionary<string, PeScanResult> entries;
        string file;
        lock (Gate)
        {
            if (!_dirty || _entries is null) return;
            entries = _entries;
            file = _loadedFrom!;
            _dirty = false;
        }
        try
        {
            foreach (var key in entries.Keys)
                if (!File.Exists(key)) entries.TryRemove(key, out _);
            FileUtil.AtomicWriteText(file, JsonSerializer.Serialize(entries.ToDictionary(e => e.Key, e => e.Value), JsonCtx.Default.DictionaryStringPeScanResult));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Forgets what is in memory so the next lookup reads the file again (tests).</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            _entries = null;
            _loadedFrom = null;
            _dirty = false;
        }
    }
}
