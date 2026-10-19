namespace DLSSUpdater.Core;

/// <summary>Decides which exception text is safe and useful to show in a dialog.</summary>
public static class UserErrors
{
    public const string Generic = "Something went wrong. The details are in the log (Log, bottom right).";

    /// <summary>
    /// True for the exceptions the app throws itself with a message written for the user, and for the
    /// general-purpose types it uses for that (<see cref="InvalidOperationException"/>, <see cref="InvalidDataException"/>,
    /// <see cref="FileNotFoundException"/>). Those types are also thrown by the framework, whose technical text
    /// ("Sequence contains no elements") then shows too; every other type is hidden and only logged.
    /// </summary>
    public static bool IsCurated(Exception ex) =>
        ex is not ObjectDisposedException
        && ex is InvalidOperationException or InvalidDataException or FileNotFoundException
            or GameRunningException or GameFileInUseException or UnsafeManifestException;

    /// <summary>The exception's own message when it is curated, otherwise <see cref="Generic"/>.</summary>
    public static string Message(Exception ex) =>
        IsCurated(ex) && !string.IsNullOrWhiteSpace(ex.Message) ? ex.Message : Generic;
}
