using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using DLSSUpdater.Views;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// Updates of DLSS5 Updater itself: the notice in the title bar and the Settings section. Checking and downloading run
/// beside other work; installing waits for the busy gate and happens as the app exits, so it never cuts into a job.
/// </summary>
public sealed partial class AppUpdateViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly AppUpdater _updater;
    private readonly string? _exe = Environment.ProcessPath;
    private AppSettings Settings => _main.S.Settings;
    /// <summary>A verified file to install as the app exits, from Update now.</summary>
    private (AppInstallKind Kind, string Path, string Sha)? _installOnExit;
    private Task? _background;

    public AppUpdateViewModel(MainViewModel main)
    {
        _main = main;
        _updater = new AppUpdater(main.Gh);
        Kind = _exe is null ? AppInstallKind.Portable : AppUpdate.DetectKind(_exe);
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsBusy)) UpdateNowCommand.NotifyCanExecuteChanged();
        };
        // Base OnExit releases the instance lock first (App.OnExit), so setup can start right away.
        if (Application.Current is { } app) app.Exit += (_, _) => OnAppExit();
    }

    public AppInstallKind Kind { get; }

    public string KindText => Kind == AppInstallKind.Installed
        ? "Installed copy: updates run the new setup quietly."
        : "Portable copy: updates replace the exe in its folder.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(CanInstallUpdate), nameof(ShowPrivateNotice), nameof(PrivateNotice), nameof(UpdateText), nameof(UpdateName))]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand), nameof(ShowNotesCommand))]
    private AppRelease? _available;

    /// <summary>The available update is downloaded and verified; it installs when the app closes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateText), nameof(UpdateName))]
    private bool _ready;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand), nameof(CheckNowCommand))]
    private bool _working;

    public bool HasUpdate => Available is not null;
    /// <summary>False in the private build: it must not replace itself with the public release.</summary>
    public bool CanInstall { get; } = AppUpdate.CanSelfInstall(BuildFlags.LecramChannel);
    public bool CanInstallUpdate => HasUpdate && CanInstall;
    public bool ShowPrivateNotice => HasUpdate && !CanInstall;
    public string PrivateNotice => Available is { } r ? AppUpdate.PrivateBuildNotice(r) : "";
    public string UpdateText => Available is null ? "" : Ready ? $"Restart to update to {Available.Label}" : $"Update to {Available.Label}";
    public string UpdateName => $"DLSS5 Updater {Available?.Label} is available. {UpdateText}";

    public string LastResult => Settings.AppUpdateLastResult;

    public bool CheckAppUpdates
    {
        get => Settings.CheckAppUpdates;
        set
        {
            Settings.CheckAppUpdates = value;
            Settings.Save();
        }
    }

    public bool AutoInstall
    {
        get => Settings.AutoInstallAppUpdates;
        set
        {
            Settings.AutoInstallAppUpdates = value;
            Settings.Save();
            if (value && Available is not null) StartBackgroundDownload();
        }
    }

    /// <summary>The first-run setup changed these settings: show them, and start the automatic download if one is due.</summary>
    public void SettingsChanged()
    {
        OnPropertyChanged(nameof(CheckAppUpdates));
        OnPropertyChanged(nameof(AutoInstall));
        if (AutoInstall && Available is not null) StartBackgroundDownload();
    }

    /// <summary>After the startup run: tidy up, then check if due (otherwise show what the last check cached).</summary>
    public async Task StartupAsync()
    {
        if (Demo.Active)
        {
            // DLSSU_DEMO_APPUPDATE=1 pretends a newer version is out, for screenshots of the notice.
            if (Environment.GetEnvironmentVariable("DLSSU_DEMO_APPUPDATE") == "1") Available = DemoRelease();
            return;
        }
        NoteFinishedInstall();
        if (_exe is not null) AppUpdate.Cleanup(_exe, AppUpdate.ParseVersion(Settings.AppUpdateStaged));
        if (!Settings.CheckAppUpdates) return;
        if (AppUpdate.IsDue(Settings.AppUpdateLastCheck, DateTime.UtcNow)) await CheckAsync();
        else Show(AppUpdate.Newest(_main.Gh.CachedReleases(AppUpdate.Repo), AppUpdate.Current));
    }

    /// <summary>Logs whether the install started last time took, and forgets what it staged.</summary>
    private void NoteFinishedInstall()
    {
        if (Settings.AppUpdateTried is not { } tried) return;
        if (AppUpdate.ParseVersion(tried) is { } v && v <= AppUpdate.Current)
        {
            Log.Info($"DLSS5 Updater was updated to v{AppUpdate.Current.ToString(3)}");
            Settings.AppUpdateTried = Settings.AppUpdateStaged = Settings.AppUpdateStagedSha256 = null;
            Settings.Save();
        }
        else Log.Warn($"The update to {tried} didn't install. Use Update in the title bar to try again.");
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckNow() => Demo.Active ? Task.CompletedTask : CheckAsync();

    private bool CanCheck() => !Working;

    private async Task CheckAsync()
    {
        Working = true;
        try
        {
            var result = await _updater.CheckAsync(AppUpdate.Current, CancellationToken.None);
            if (!result.Failed) Settings.AppUpdateLastCheck = DateTime.UtcNow;
            Settings.AppUpdateLastResult = result.Message;
            Settings.Save();
            OnPropertyChanged(nameof(LastResult));
            if (!result.Failed) Show(result.Update);
        }
        finally { Working = false; }
    }

    private void Show(AppRelease? update)
    {
        if (update is not null && update.Tag != Available?.Tag) Log.Info($"DLSS5 Updater {update.Label} is available (click Update in the title bar)");
        Available = update;
        Ready = update is not null && Settings.AppUpdateStaged == update.Tag && Settings.AppUpdateStagedSha256 is not null
                && _exe is not null && File.Exists(AppUpdate.StagedPath(Kind, _exe, update.Version));
        if (update is not null && !Ready && Settings.AutoInstallAppUpdates) StartBackgroundDownload();
    }

    /// <summary>Automatic mode: download and verify quietly; it installs when the app closes or on the next start.</summary>
    private void StartBackgroundDownload()
    {
        if (!CanInstall || _background is not null || Working || Ready || Available is not { } r || _exe is null) return;
        _background = DownloadInBackground(r, _exe);
    }

    private async Task DownloadInBackground(AppRelease r, string exe)
    {
        Working = true;
        try
        {
            var (_, sha) = await _updater.DownloadAsync(r, Kind, exe, null, CancellationToken.None);
            Stage(r, sha);
            Settings.AppUpdateLastResult = $"{r.Label} is downloaded and installs when DLSS5 Updater closes.";
            Log.Info($"DLSS5 Updater {r.Label} downloaded and verified; it installs when the app closes");
        }
        catch (Exception ex) when (ex is AppUpdateException or HttpRequestException or IOException or TaskCanceledException)
        {
            Log.Warn($"DLSS5 Updater {r.Label} download: {ex.Message}");
            Settings.AppUpdateLastResult = ex is AppUpdateException ? ex.Message : $"{r.Label} couldn't be downloaded. Trying again later.";
        }
        finally
        {
            Settings.Save();
            OnPropertyChanged(nameof(LastResult));
            Working = false;
            _background = null;
        }
    }

    private void Stage(AppRelease r, string sha)
    {
        Settings.AppUpdateStaged = r.Tag;
        Settings.AppUpdateStagedSha256 = sha;
        Settings.AppUpdateTried = null;
        Ready = true;
    }

    private bool CanUpdateNow() => CanInstall && Available is not null && !Working && !_main.IsBusy;

    /// <summary>Downloads (unless already staged) and verifies inside the busy gate, then closes the app to install and restart.</summary>
    [RelayCommand(CanExecute = nameof(CanUpdateNow))]
    private async Task UpdateNow()
    {
        if (Available is not { } r || _exe is not { } exe) return;
        if (Demo.Active)
        {
            Log.Info($"Demo: DLSS5 Updater would now update to {r.Label} and restart");
            return;
        }
        var staged = AppUpdate.StagedPath(Kind, exe, r.Version);
        string? sha = Ready ? Settings.AppUpdateStagedSha256 : null;
        Working = true;
        try
        {
            if (sha is null)
                await _main.RunJob($"Updating DLSS5 Updater to {r.Label}", async ct =>
                {
                    (_, sha) = await _updater.DownloadAsync(r, Kind, exe, _main.TransferProgress(), ct);
                    Stage(r, sha);
                    Settings.Save();
                });
        }
        finally { Working = false; }
        // The run failed, was cancelled or skipped (it logged why); the gate is free again here.
        if (sha is null || _main.IsBusy) return;
        _installOnExit = (Kind, staged, sha);
        Application.Current.Shutdown();
    }

    [RelayCommand(CanExecute = nameof(HasUpdate))]
    private void ShowNotes()
    {
        if (Available is not { } r) return;
        var published = r.Published is { } p ? $"Released {p.ToLocalTime():d MMM yyyy}\n\n" : "";
        if (!CanInstall)
        {
            Dialog.Show($"What's new in DLSS5 Updater {r.Label}", published + AppUpdate.PlainNotes(r.Notes) + "\n\n" + AppUpdate.PrivateBuildNotice(r));
            return;
        }
        if (Dialog.Confirm($"What's new in DLSS5 Updater {r.Label}", published + AppUpdate.PlainNotes(r.Notes),
                Ready ? "Restart and update" : "Update now")
            && UpdateNowCommand.CanExecute(null))
            _ = UpdateNowCommand.ExecuteAsync(null);
    }

    /// <summary>Update now's file with a restart; otherwise automatic mode's staged update, quietly, when no job was cut off.</summary>
    private void OnAppExit()
    {
        if (Demo.Active || _exe is null) return;
        if (_installOnExit is { } now)
        {
            Settings.AppUpdateTried = Settings.AppUpdateStaged;
            Settings.Save();
            AppUpdater.Apply(now.Kind, _exe, now.Path, now.Sha, relaunch: true);
        }
        else if (CanInstall && !_main.IsBusy && _background is null) AppUpdater.ApplyPending(Settings, _exe, relaunch: false);
    }

    private static AppRelease DemoRelease()
    {
        var c = AppUpdate.Current;
        var v = new Version(c.Major, c.Minor + 1, 0);
        var tag = "v" + v.ToString(3);
        const string notes = "## What's new\n- Checks GitHub for new DLSS5 Updater versions and updates itself\n- Optional automatic updates, installed when the app closes\n- Downloads are checked against SHA256SUMS before anything runs";
        return new AppRelease(v, tag, notes, DateTime.UtcNow.AddDays(-1), new GhRelease { TagName = tag, Body = notes });
    }
}
