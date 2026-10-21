using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// Settings → ReShade add-ons → DLSS5-Feeder: which build installs by default ("Auto" follows the newest), the library of builds
/// (downloaded or imported) and the import. Logic lives in <see cref="FeederLibrary"/>; this only asks and shows.
/// </summary>
public sealed partial class FeederSettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Func<Task> _changed;
    private bool _reloading;

    public FeederSettingsViewModel(MainViewModel main, Func<Task> changed)
    {
        _main = main;
        _changed = changed;
        Reload();
    }

    private AppSettings Settings => _main.S.Settings;

    public ObservableCollection<DlssChoice> DefaultChoices { get; } = [];
    public ObservableCollection<DlssChoice> BuildChoices { get; } = [];

    [ObservableProperty] private DlssChoice? _defaultChoice;
    [ObservableProperty] private DlssChoice? _selectedBuildChoice;
    [ObservableProperty] private string _status = "";

    public bool HasBuilds => BuildChoices.Count > 0;
    public bool IsManual => DefaultChoice?.Tag is not null;

    private LibraryBuild? SelectedBuild => FeederLibrary.Builds().FirstOrDefault(b => b.Id == SelectedBuildChoice?.Tag);

    /// <summary>What the default stands for now.</summary>
    public string Info
    {
        get
        {
            var auto = FeederLibrary.Choose(Settings.DefaultBuild(ComponentRegistry.Feeder));
            return auto is null
                ? "Nothing downloaded or imported yet: the newest release is downloaded from GitHub on the first install."
                : $"Installs {auto.Label}{(IsManual ? " (your choice)" : " (Auto follows the newest build)")}. Imported builds stay until you remove them; downloaded ones are cleaned up like the other libraries.";
        }
    }

    public void Reload()
    {
        _reloading = true;
        var pin = Settings.DefaultBuild(ComponentRegistry.Feeder);
        var builds = FeederLibrary.Builds();
        DefaultChoices.Clear();
        DefaultChoices.Add(new DlssChoice(null, FeederLibrary.AutoLabel(null)));
        foreach (var b in builds) DefaultChoices.Add(new DlssChoice(b.Id, b.Label));
        if (pin is not null && DefaultChoices.All(c => c.Tag != pin)) DefaultChoices.Add(new DlssChoice(pin, $"{pin} · not in the library"));
        DefaultChoice = DefaultChoices.FirstOrDefault(c => c.Tag == pin) ?? DefaultChoices[0];

        var selected = SelectedBuildChoice?.Tag;
        BuildChoices.Clear();
        foreach (var b in builds) BuildChoices.Add(new DlssChoice(b.Id, b.Label));
        SelectedBuildChoice = BuildChoices.FirstOrDefault(c => c.Tag == selected) ?? BuildChoices.FirstOrDefault();
        _reloading = false;
        OnPropertyChanged(nameof(HasBuilds));
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(Info));
    }

    partial void OnDefaultChoiceChanged(DlssChoice? value)
    {
        if (_reloading || value is null) return;
        Settings.SetDefaultBuild(ComponentRegistry.Feeder, value.Tag);
        Settings.Save();
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(Info));
        _ = _changed();
    }

    /// <summary>Back to following the newest build.</summary>
    [RelayCommand]
    private void ResetToAuto() => DefaultChoice = DefaultChoices.FirstOrDefault();

    // ---------- library ----------

    [RelayCommand]
    private async Task Import()
    {
        var dlg = new OpenFileDialog { Filter = "DLSS5-Feeder|*.zip;dlss5-feed.addon64|All files|*.*", Title = "Select the DLSS5-Feeder release .zip or dlss5-feed.addon64" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FileName);
    }

    [RelayCommand]
    private async Task ImportFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Select the extracted DLSS5-Feeder release folder" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FolderName);
    }

    private async Task ImportFrom(string source)
    {
        try
        {
            var build = await Task.Run(() => FeederLibrary.Import(source));
            SelectedBuildChoice = null;
            Status = $"Imported DLSS5-Feeder {build.Version}.";
            await _changed();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }
    }

    /// <summary>Downloads the newest release into the library now (installs do it on their own when the library is empty).</summary>
    [RelayCommand]
    private async Task Download()
    {
        try
        {
            var latest = await new FeederLibrary(_main.Gh).LatestAsync(Settings.IncludePrereleases, default);
            if (latest is null) { Status = "No release with a published SHA-256 was found (offline, or GitHub has none)."; return; }
            var build = await new FeederLibrary(_main.Gh).EnsureAsync(latest, null, default);
            Status = $"DLSS5-Feeder {build.Label} is in the library.";
            await _changed();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or HttpRequestException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private async Task Remove()
    {
        if (SelectedBuild is not { } build) return;
        FeederLibrary.Remove(build, Settings);
        Settings.Save();
        Status = $"Removed DLSS5-Feeder {build.Label}.";
        await _changed();
    }
}
