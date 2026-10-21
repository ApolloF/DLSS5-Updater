namespace DLSSUpdater.Core;

public static class AppPaths
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string Root { get; set; } = Path.Combine(Local, "DLSS5Updater");

    /// <summary>Data folder of versions before the rename to DLSS5 Updater.</summary>
    public static string LegacyRoot => Path.Combine(Local, "DLSSUpdater");

    /// <summary>
    /// Returns the data folder to use. Moves <paramref name="legacy"/> to <paramref name="root"/> when only the old
    /// one exists; if the move fails (a locked file, for example) the old folder stays in use and the move is retried next start.
    /// </summary>
    public static string Migrate(string legacy, string root)
    {
        if (Directory.Exists(root) || !Directory.Exists(legacy)) return root;
        try
        {
            Directory.Move(legacy, root);
            return root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return legacy;
        }
    }

    public static string Cache => Path.Combine(Root, "cache");
    public static string Components => Path.Combine(Root, "components");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string GamesFile => Path.Combine(Root, "games.json");
    public static string DetectCacheFile => Path.Combine(Root, "detect-cache.json");
    public static string ApiCacheDir => Path.Combine(Cache, "api");
    public static string LegacyApiCacheFile => Path.Combine(Cache, "api.json");
    /// <summary>Last good copy of the catalog repository's catalog.json, for when GitHub can't be reached.</summary>
    public static string CatalogFile => Path.Combine(Cache, "catalog.json");
    public static string LogFile => Path.Combine(Root, "log.txt");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Components);
    }
}
