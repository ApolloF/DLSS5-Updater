using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DLSSUpdater.Core;

/// <summary>
/// One DLSS5 Updater per Windows session: two copies would write the same settings, caches and game folders at once.
/// Held through a named mutex for the life of the process.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // The mutex names keep the pre-rename name on purpose: an old and a new copy must never run at once,
    // because the first start of a new copy moves the old data folder.
    public const string DefaultName = @"Local\DLSSUpdater.SingleInstance";
    /// <summary>The demo (--demo) uses its own temp data folder, so it only excludes other demos.</summary>
    public const string DemoName = @"Local\DLSSUpdater.Demo";
    /// <summary>A start on a copied data folder (--data-root) only excludes other such starts.</summary>
    public const string TempRootName = @"Local\DLSSUpdater.TempRoot";
    private const string LegacyProcessName = "DLSSUpdater";

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// Takes the instance lock, waiting up to <paramref name="wait"/> (a restart waits for the old process to exit).
    /// Null when another instance holds it.
    /// </summary>
    public static SingleInstance? TryAcquire(string name, TimeSpan wait)
    {
        Mutex mutex;
        try { mutex = new Mutex(false, name); }
        catch (UnauthorizedAccessException) { return null; } // created by an elevated instance
        try
        {
            if (mutex.WaitOne(wait)) return new SingleInstance(mutex);
        }
        catch (AbandonedMutexException)
        {
            // The previous instance ended without releasing it (crash); the lock is ours now.
            return new SingleInstance(mutex);
        }
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { } // not owned by this thread; the OS releases it at exit
        _mutex.Dispose();
    }

    /// <summary>Brings the window of the other running instance to the front. False when there is none to show.</summary>
    public static bool ActivateOther()
    {
        using var self = Process.GetCurrentProcess();
        // The pre-rename DLSSUpdater.exe holds the same mutex, so it may be the copy that is running.
        foreach (var p in new[] { self.ProcessName, LegacyProcessName }.Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(Process.GetProcessesByName))
        {
            using (p)
            {
                try
                {
                    if (p.Id == self.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                    var handle = p.MainWindowHandle;
                    if (IsIconic(handle)) ShowWindow(handle, SwRestore);
                    return SetForegroundWindow(handle);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            }
        }
        return false;
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
}
