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
                if (Demo.ScreenshotArg(e.Args) is null) Views.Dialog.Show("DLSS Updater", "A DLSS Updater demo is already running.");
                Shutdown(1);
            }
            else
            {
                if (!SingleInstance.ActivateOther()) Views.Dialog.Show("DLSS Updater", "DLSS Updater is already running.");
                Shutdown();
            }
            return;
        }
        if (demo) Demo.Enable(screenshotDir: Demo.ScreenshotArg(e.Args));
        DispatcherUnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Background task failed", args.Exception);
            args.SetObserved();
        };
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled", e.Exception);
        Views.Dialog.Show("Something went wrong", "DLSS Updater kept running. The details are in the log.");
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
