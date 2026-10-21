using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// Settings → Frame limit: the method and cap a game gets when it has no choice of its own, and the library of the two add-on limiters
/// (import a file, a folder or a .zip you got from the author; remove a build). Games follow these until they pick their own.
/// </summary>
public sealed partial class FrameLimitSettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Func<Task> _changed;
    private bool _filling;

    public FrameLimitSettingsViewModel(MainViewModel main, Func<Task> changed)
    {
        _main = main;
        _changed = changed;
        ReLimiter = new LimiterLibraryRow(main, ComponentRegistry.ReLimiter, changed);
        DisplayCommander = new LimiterLibraryRow(main, ComponentRegistry.DisplayCommander, changed);
        Reload();
    }

    private AppSettings Settings => _main.S.Settings;

    public LimiterLibraryRow ReLimiter { get; }
    public LimiterLibraryRow DisplayCommander { get; }

    /// <summary>"Auto" (the driver limiter on NVIDIA cards) or a method every game without its own choice uses.</summary>
    public ObservableCollection<DlssChoice> MethodChoices { get; } = [];

    [ObservableProperty] private DlssChoice? _methodChoice;

    partial void OnMethodChoiceChanged(DlssChoice? value)
    {
        if (_filling || value is null) return;
        FrameLimitSettings.SetMethod(Settings, LimitMethods.Parse(value.Tag));
        Save();
    }

    public string FpsText
    {
        get => (FrameLimitSettings.Fps(Settings.PickOf(ComponentRegistry.FrameLimit)) ?? FrameLimitRules.DefaultFps).ToString();
        set
        {
            if (!int.TryParse(value.Trim(), out var fps) || !FrameLimitRules.ValidFps(fps))
            {
                OnPropertyChanged();
                return;
            }
            FrameLimitSettings.SetFps(Settings, fps);
            Save();
        }
    }

    public string FpsHint => $"{FrameLimitRules.MinFps} to {FrameLimitRules.MaxFps}";

    public string Explanation =>
        "Used by every game that has no choice of its own on its page. Auto picks the NVIDIA driver limiter on NVIDIA cards (nothing is placed in the game). " +
        "Nothing changes in a game until you press Apply on its page.";

    private void Save()
    {
        Settings.Save();
        foreach (var g in _main.Games) g.RefreshHdr();
    }

    public void Reload()
    {
        _filling = true;
        MethodChoices.Clear();
        MethodChoices.Add(new DlssChoice(null, "Auto (NVIDIA driver limiter on NVIDIA cards)"));
        foreach (var m in LimitMethods.All) MethodChoices.Add(new DlssChoice(LimitMethods.Id(m), LimitMethods.Label(m)));
        var own = FrameLimitSettings.Method(Settings.PickOf(ComponentRegistry.FrameLimit));
        MethodChoice = MethodChoices.FirstOrDefault(c => own is not null && c.Tag == LimitMethods.Id(own.Value)) ?? MethodChoices[0];
        _filling = false;
        OnPropertyChanged(nameof(FpsText));
        ReLimiter.Reload();
        DisplayCommander.Reload();
    }
}

/// <summary>The Settings row of one limiter add-on: the builds in the library, import and remove.</summary>
public sealed partial class LimiterLibraryRow : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly string _id;
    private readonly Func<Task> _changed;

    public LimiterLibraryRow(MainViewModel main, string id, Func<Task> changed)
    {
        _main = main;
        _id = id;
        _changed = changed;
        Reload();
    }

    private ComponentStore Store => _main.S.Store;

    public string Title => ComponentRegistry.Find(_id)?.Name ?? _id;
    public string FileNames => LimiterFiles.Names(_id);
    public string AutomationName => $"{Title} builds in the library";

    public ObservableCollection<DlssChoice> Choices { get; } = [];

    [ObservableProperty] private DlssChoice? _selected;

    public bool HasBuilds => Choices.Count > 0;

    public string Info => HasBuilds
        ? $"{Choices.Count} build{(Choices.Count == 1 ? "" : "s")} in the library. Downloaded builds come from the catalog (checked against its SHA-256); imported ones are copied as they are and only go when you remove them."
        : $"Nothing in the library yet. It is downloaded from the catalog when a game needs it, or import {FileNames} (a file, the release folder or its .zip).";

    public void Reload()
    {
        Choices.Clear();
        foreach (var b in Store.LimiterBuilds.Where(b => b.LimiterId == _id))
            Choices.Add(new DlssChoice(b.Id, $"{b.Version}{(b.Arch == "x86" ? " (32-bit)" : " (64-bit)")}{(b.Imported ? " · imported" : "")}"));
        Selected = Choices.FirstOrDefault();
        OnPropertyChanged(nameof(HasBuilds));
        OnPropertyChanged(nameof(Info));
    }

    [RelayCommand]
    private async Task Import()
    {
        var dlg = new OpenFileDialog { Filter = $"{Title}|{string.Join(';', LimiterFiles.All.Where(f => f.LimiterId == _id).Select(f => f.Name))};*.zip", Title = $"Select {FileNames} or a .zip of the release" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FileName);
    }

    [RelayCommand]
    private async Task ImportFolder()
    {
        var dlg = new OpenFolderDialog { Title = $"Select the folder with {FileNames}" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FolderName);
    }

    private async Task ImportFrom(string source)
    {
        try
        {
            var builds = await Task.Run(() => Store.ImportLimiter(source));
            // A folder or a zip may hold the other limiter too; each file lands in its own library.
            Log.Info($"Imported {string.Join(", ", builds.Select(b => b.Label))}");
            await _changed();
            await _main.AutoCleanUpAfterImportAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FileNotFoundException)
        { Log.Error($"Import of {Title} failed", ex); Views.Dialog.Show("Import failed", UserErrors.Message(ex)); }
    }

    [RelayCommand]
    private async Task Remove()
    {
        if (Selected?.Tag is not { } id || Store.LimiterBuildFor(id) is not { } build) return;
        if (!Views.Dialog.Confirm("Remove from library", $"Remove {build.Label} from the library? Games that already have it keep their copy.", "Remove", danger: true)) return;
        try
        {
            Store.RemoveLimiter(id);
            Log.Info($"Removed {build.Label} from the library");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log.Error($"Could not remove the {Title} build", ex); }
        await _changed();
    }
}
