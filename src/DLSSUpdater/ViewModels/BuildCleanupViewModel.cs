using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

/// <summary>One library in the cleanup area: how much it holds and how many of its newest builds stay.</summary>
public sealed partial class LibraryRowViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _changed;

    public LibraryRowViewModel(LibraryKind kind, AppSettings settings, Action changed)
    {
        Kind = kind;
        _settings = settings;
        _changed = changed;
    }

    public LibraryKind Kind { get; }
    public string Title => ComponentStore.LibraryTitle(Kind);
    public static IReadOnlyList<int> KeepChoices { get; } = Enumerable.Range(1, BuildCleanup.MaxKeep).ToList();

    [ObservableProperty] private string _summary = "";

    public int Keep
    {
        get => _settings.KeepBuildsFor(Kind);
        set
        {
            if (value == Keep) return;
            _settings.KeepBuilds[Kind.ToString()] = Math.Clamp(value, 1, BuildCleanup.MaxKeep);
            _settings.Save();
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>The first-run setup changed the setting: show it.</summary>
    public void SettingsChanged() => OnPropertyChanged(nameof(Keep));
}

/// <summary>
/// The cleanup area of Settings: per library how many newest builds stay, what a cleanup would free, the manual
/// cleanup and the opt-in automatic one. The deciding happens in <see cref="BuildCleanup"/> and <see cref="ComponentStore"/>.
/// </summary>
public sealed partial class BuildCleanupViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private int _refresh;

    public BuildCleanupViewModel(MainViewModel main)
    {
        _main = main;
        Rows = new(Enum.GetValues<LibraryKind>().Select(k => new LibraryRowViewModel(k, main.S.Settings, () => _ = RefreshAsync())));
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsBusy)) CleanCommand.NotifyCanExecuteChanged();
        };
    }

    public ObservableCollection<LibraryRowViewModel> Rows { get; }

    public bool AutoClean
    {
        get => _main.S.Settings.AutoCleanBuilds;
        set
        {
            if (value == _main.S.Settings.AutoCleanBuilds) return;
            _main.S.Settings.AutoCleanBuilds = value;
            _main.S.Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>The first-run setup changed these settings: show them and measure again.</summary>
    public void SettingsChanged()
    {
        OnPropertyChanged(nameof(AutoClean));
        foreach (var row in Rows) row.SettingsChanged();
        _ = RefreshAsync();
    }

    [ObservableProperty] private string _freeable = "";

    /// <summary>Which libraries the automatic cleanup skips now and why; empty when it skips none.</summary>
    [ObservableProperty] private string _autoSkipped = "";

    private bool CanClean() => !_main.IsBusy;

    [RelayCommand(CanExecute = nameof(CanClean))]
    private Task Clean() => _main.CleanUpBuildsAsync();

    /// <summary>Re-reads sizes and what a cleanup would free, off the UI thread; a newer refresh wins over a slower older one.</summary>
    public async Task RefreshAsync()
    {
        var run = ++_refresh;
        var store = _main.S.Store;
        var settings = _main.S.Settings;
        var dirs = _main.InstallDirs();
        try
        {
            var (summaries, plan, skipped) = await Task.Run(() =>
            {
                var records = MainViewModel.ReadRecords(dirs, settings);
                var summaries = Rows.Select(r =>
                {
                    var builds = store.Library(r.Kind);
                    var size = builds.Sum(b => BuildCleanup.SizeOf(b.Dir));
                    return builds.Count == 0 ? "none kept" : $"{builds.Count} build{(builds.Count == 1 ? "" : "s")}  ·  {BuildCleanup.FormatBytes(size)}";
                }).ToList();
                return (summaries, records.Unknown ? null : store.PlanCleanup(settings, records.Records), records.AutoSkipNote());
            });
            if (run != _refresh) return;
            for (var i = 0; i < Rows.Count; i++) Rows[i].Summary = summaries[i];
            AutoSkipped = skipped ?? "";
            Freeable = plan is null ? "An install record or the list of known installs can't be read, so nothing is cleaned up. The details are in the log."
                : plan.Count == 0 ? "Nothing to clean up."
                : $"Clean up frees {BuildCleanup.FormatBytes(plan.Sum(p => p.Bytes))} ({plan.Count} item{(plan.Count == 1 ? "" : "s")}).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Log.Error("Could not measure the build libraries", ex); }
    }
}
