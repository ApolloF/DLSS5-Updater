using System.Text.Json.Nodes;
using DLSSUpdater.Core;

namespace DLSSUpdater.Addon;

/// <summary>
/// Cleans up the launcher add-on link that older versions wrote. The uninstaller calls
/// --unregister-addon to remove stale links.
/// </summary>
public static class AddonRegistration
{
    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// The launcher's add-ons folder. Older launcher versions used the WaterLauncher folder name.
    /// </summary>
    static string AddonsDir =>
        !Directory.Exists(Path.Combine(Local, "Seaglass")) && Directory.Exists(Path.Combine(Local, "WaterLauncher"))
            ? Path.Combine(Local, "WaterLauncher", "addons")
            : Path.Combine(Local, "Seaglass", "addons");

    static string ManifestPath => Path.Combine(AddonsDir, "dlssupdater", "addon.json");

    /// <summary>Unregisters only when addon.json launches this exe, so uninstalling one copy never disconnects another.</summary>
    public static void UnregisterIfThisExe()
    {
        if (!File.Exists(ManifestPath) || Environment.ProcessPath is not { } exe) return;
        var registered = JsonNode.Parse(File.ReadAllText(ManifestPath))?["exe"]?.GetValue<string>();
        if (registered is not null && string.Equals(Path.GetFullPath(registered), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
            Unregister();
    }

    static void Unregister()
    {
        var dir = Path.GetDirectoryName(ManifestPath)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Log.Info("Removed old add-on link");
    }
}
