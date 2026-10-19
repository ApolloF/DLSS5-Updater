namespace DLSSUpdater.Core;

/// <summary>Decides which exception text is safe and useful to show in a dialog.</summary>
public static class UserErrors
{
    public const string Generic = "Something went wrong. The details are in the log (Log, bottom right).";

    /// <summary>
    /// True for the exceptions the app throws itself with a message written for the user.
    /// Anything else (framework or library errors) carries technical text that belongs in the log.
    /// </summary>
    public static bool IsCurated(Exception ex) =>
        ex is not ObjectDisposedException
        && ex is InvalidOperationException or InvalidDataException or FileNotFoundException
            or GameRunningException or UnsafeManifestException;

    /// <summary>The exception's own message when it is curated, otherwise <see cref="Generic"/>.</summary>
    public static string Message(Exception ex) =>
        IsCurated(ex) && !string.IsNullOrWhiteSpace(ex.Message) ? ex.Message : Generic;
}
