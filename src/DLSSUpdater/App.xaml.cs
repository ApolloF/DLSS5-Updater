using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using DLSSUpdater.Core;

namespace DLSSUpdater;

public partial class App : Application
{
    /// <summary>Passed by <see cref="RestartElevated"/>: wait for the old process to let go of the instance lock.</summary>
    private const string RestartedArg = "--restarted";

    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--unregister-addon"))
        {
            // Run by the uninstaller so old add-on links from earlier versions get cleaned up; only removes the link when it points at this exe.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            // Don't move anything here, and don't let the log line create an empty new folder next to the old data.
            if (!Directory.Exists(AppPaths.Root) && Directory.Exists(AppPaths.LegacyRoot)) AppPaths.Root = AppPaths.LegacyRoot;
            try { Addon.AddonRegistration.UnregisterIfThisExe(); }
            catch (Exception ex) { Log.Error("Remove old add-on link", ex); }
            Shutdown();
            return;
        }
        // Made-up games for screenshots and trying the interface; see Core/Demo.cs. The demo has its own temp data folder,
        // so it may run next to a normal instance, but only one demo at a time (each start wipes that folder).
        var demo = e.Args.Contains("--demo");
        _instance = SingleInstance.TryAcquire(demo ? SingleInstance.DemoName : SingleInstance.DefaultName,
            e.Args.Contains(RestartedArg) ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);
        if (_instance is null)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (demo)
            {
                // A screenshot run is unattended: exit with an error code instead of waiting on a dialog.
                if (Demo.ScreenshotArg(e.Args) is null) Views.Dialog.Show("DLSS5 Updater", "A DLSS5 Updater demo is already running.");
                Shutdown(1);
            }
            else
            {
                if (!SingleInstance.ActivateOther()) Views.Dialog.Show("DLSS5 Updater", "DLSS5 Updater is already running.");
                Shutdown();
            }
            return;
        }
        // Before anything logs: Log creates the data folder, which would block the move.
        if (!demo)
        {
            var legacy = AppPaths.LegacyRoot;
            var target = AppPaths.Root;
            var pending = !Directory.Exists(target) && Directory.Exists(legacy);
            AppPaths.Root = AppPaths.Migrate(legacy, target);
            if (pending)
            {
                if (AppPaths.Root == target) Log.Info($"Moved data folder from {legacy} to {target}");
                else Log.Warn($"Could not move {legacy} to {target}; using the old folder this time.");
            }
            else if (Directory.Exists(legacy) && Directory.EnumerateFileSystemEntries(legacy).Any())
                Log.Warn($"Both {legacy} and {target} exist; using {target}. Settings in the old folder are not read.");
        }
        DispatcherUnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Background task failed", args.Exception);
            args.SetObserved();
        };
        if (!demo && StartPendingUpdate()) return;
        // Inside the handler's reach: a locked demo folder shows a message instead of crashing the process.
        if (demo) Demo.Enable(screenshotDir: Demo.ScreenshotArg(e.Args));
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Before base.OnExit raises Exit: a self-update started from there needs the lock gone (setup checks AppMutex).
        ReleaseInstance();
        base.OnExit(e);
    }

    private void ReleaseInstance()
    {
        _instance?.Dispose();
        _instance = null;
    }

    /// <summary>
    /// Automatic updates: an update downloaded and verified last session that didn't get to install on exit installs
    /// now, before the window opens; setup or the new portable exe starts the app again. True when this process should end.
    /// </summary>
    private bool StartPendingUpdate()
    {
        if (Environment.ProcessPath is not { } exe) return false;
        if (!AppUpdater.ApplyPending(AppSettings.Load(), exe, relaunch: true, ReleaseInstance))
        {
            // Released just before a start that then failed: take the lock back, or leave if another copy got it.
            _instance ??= SingleInstance.TryAcquire(SingleInstance.DefaultName, TimeSpan.Zero);
            if (_instance is not null) return false;
        }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Shutdown();
        return true;
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled", e.Exception);
        Views.Dialog.Show("Something went wrong", "DLSS5 Updater kept running. The details are in the log.");
        e.Handled = true;
        // Failed before the main window was up: nothing would ever close the process.
        if (MainWindow is not { IsLoaded: true }) Shutdown(1);
    }

    public static void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, RestartedArg) { UseShellExecute = true, Verb = "runas" });
            Current.Shutdown();
        }
        catch (Win32Exception)
        {
            // UAC prompt declined.
        }
    }
}
