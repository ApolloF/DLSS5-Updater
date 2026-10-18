using System.Text.Json.Nodes;
using DLSSUpdater.Core;

namespace DLSSUpdater.Addon;

/// <summary>
/// Cleans up the Seaglass add-on link that older versions wrote. The add-on itself lives on the
/// feature/seaglass-addon branch; the uninstaller still calls --unregister-addon for stale links.
/// </summary>
public static class AddonRegistration
{
    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// Seaglass's add-ons folder. Seaglass was called WaterLauncher before 1.5; a WaterLauncher
    /// that hasn't updated yet still has its own folder.
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
        Log.Info("Disconnected from Seaglass");
    }
}
