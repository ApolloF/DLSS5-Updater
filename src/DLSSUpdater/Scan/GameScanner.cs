using System.Text.Json;
using DLSSUpdater.Core;

namespace DLSSUpdater.Scan;

public static class GameScanner
{
    internal static readonly string[] SourcePriority = ["Steam", "Epic", "GOG", "EA", "Ubisoft", "Battle.net", "Rockstar", "Xbox", "Manual", "Library"];

    /// <summary>
    /// All known game folders, deduplicated by path; launcher names win over manual entries. Hidden games are left out,
    /// or with <paramref name="hiddenOnly"/> they are all it returns.
    /// </summary>
    public static List<GameEntry> Discover(AppSettings settings, bool hiddenOnly = false)
    {
        var entries = Launchers.All().ToList();

        foreach (var path in settings.ManualGames.Where(Directory.Exists))
            entries.Add(new GameEntry(Path.GetFileName(path.TrimEnd('\\')), path, "Manual"));

        foreach (var root in settings.LibraryRoots.Where(Directory.Exists))
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var attrs = File.GetAttributes(dir);
                    if ((attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    entries.Add(new GameEntry(Path.GetFileName(dir), dir, "Library"));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Library root {root} unreadable", ex);
            }
        }

        var hidden = new HashSet<string>(settings.HiddenGames, StringComparer.OrdinalIgnoreCase);
        return entries
            .Where(e => hidden.Contains(GameInfo.MakeId(e.Root)) == hiddenOnly)
            .GroupBy(e => GameInfo.MakeId(e.Root))
            .Select(g => g.OrderBy(e => Array.IndexOf(SourcePriority, e.Source)).First())
            .ToList();
    }

    /// <summary>The install folders of hidden games, which the list doesn't show; the cleanup's first run reads their records too.</summary>
    public static List<string> HiddenInstallDirs(AppSettings settings) =>
        settings.HiddenGames.Count == 0 ? [] : InstallDirs(Discover(settings, hiddenOnly: true));

    internal static List<string> InstallDirs(IEnumerable<GameEntry> entries) => entries.SelectMany(e => Inspect(e).Installs).ToList();

    public static GameInfo Inspect(GameEntry entry, TechOverride? manual = null)
    {
        var game = new GameInfo
        {
            Manual = manual,
            Id = GameInfo.MakeId(entry.Root),
            Name = entry.Name,
            Source = entry.Source,
            Root = FileUtil.Normalize(entry.Root),
            SteamAppId = entry.SteamAppId,
        };
        try { GameInspector.Inspect(game); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Scan of {entry.Root} failed", ex);
        }
        return game;
    }

    public static async Task<List<GameInfo>> ScanAsync(AppSettings settings, IProgress<int>? progress, CancellationToken ct)
    {
        var entries = await Task.Run(() => Discover(settings), ct);
        var results = new GameInfo[entries.Count];
        var done = 0;
        // Disk-bound; a few workers keep both SSDs and HDDs busy without thrashing.
        await Parallel.ForEachAsync(Enumerable.Range(0, entries.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            (i, _) =>
            {
                results[i] = Inspect(entries[i], settings.Games.GetValueOrDefault(GameInfo.MakeId(entries[i].Root))?.Tech);
                progress?.Report(Interlocked.Increment(ref done) * 100 / Math.Max(entries.Count, 1));
                return ValueTask.CompletedTask;
            });
        var list = results.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        SaveCache(list);
        DetectCache.Save();
        return list;
    }

    public static List<GameInfo> LoadCache()
    {
        try
        {
            if (File.Exists(AppPaths.GamesFile))
                return JsonSerializer.Deserialize(File.ReadAllText(AppPaths.GamesFile), JsonCtx.Default.ListGameInfo) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return [];
    }

    public static void SaveCache(List<GameInfo> games)
    {
        try { FileUtil.AtomicWriteText(AppPaths.GamesFile, JsonSerializer.Serialize(games, JsonCtx.Default.ListGameInfo)); }
        catch (IOException) { }
    }
}
