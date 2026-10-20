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

    // The add-on folder keeps the pre-rename name on purpose: the launcher and older versions look for it there.
    static string ManifestPath => Path.Combine(AddonsDir, "dlssupdater", "addon.json");

    /// <summary>Unregisters only when addon.json launches this exe, so uninstalling one copy never disconnects another.</summary>
    public static void UnregisterIfThisExe()
    {
        if (!File.Exists(ManifestPath) || Environment.ProcessPath is not { } exe) return;
        var registered = JsonNode.Parse(File.ReadAllText(ManifestPath))?["exe"]?.GetValue<string>();
        if (registered is not null && IsThisExe(registered, exe))
            Unregister();
    }

    /// <summary>
    /// True for the running exe itself, or for the pre-rename DLSSUpdater.exe in the same folder: after an upgrade
    /// the old exe is gone and the new one runs the cleanup for the link the old one registered.
    /// </summary>
    internal static bool IsThisExe(string registered, string runningExe)
    {
        var reg = Path.GetFullPath(registered);
        var running = Path.GetFullPath(runningExe);
        if (string.Equals(reg, running, StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(Path.GetFileName(reg), "DLSSUpdater.exe", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetDirectoryName(reg), Path.GetDirectoryName(running), StringComparison.OrdinalIgnoreCase);
    }

    static void Unregister()
    {
        var dir = Path.GetDirectoryName(ManifestPath)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Log.Info("Removed old add-on link");
    }
}
