using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// Settings → DXVK: which build installs by default ("Auto" follows the newest Stable release; a channel or one build can be pinned), the library
/// of builds (downloaded or imported), the download of a channel's newest build and the import. Logic lives in <see cref="DxvkLibrary"/>.
/// </summary>
public sealed partial class DxvkSettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Func<Task> _changed;
    private bool _reloading;

    public DxvkSettingsViewModel(MainViewModel main, Func<Task> changed)
    {
        _main = main;
        _changed = changed;
        foreach (var c in Enum.GetValues<DxvkChannel>()) ChannelChoices.Add(new DlssChoice(DxvkChannels.Id(c), DxvkChannels.Label(c)));
        DownloadChannel = ChannelChoices[0];
        Reload();
    }

    private AppSettings Settings => _main.S.Settings;

    public ObservableCollection<DlssChoice> DefaultChoices { get; } = [];
    public ObservableCollection<DlssChoice> BuildChoices { get; } = [];
    public ObservableCollection<DlssChoice> ChannelChoices { get; } = [];

    [ObservableProperty] private DlssChoice? _defaultChoice;
    [ObservableProperty] private DlssChoice? _selectedBuildChoice;
    [ObservableProperty] private DlssChoice? _downloadChannel;
    [ObservableProperty] private string _status = "";

    public bool HasBuilds => BuildChoices.Count > 0;
    public bool IsManual => DefaultChoice?.Tag is not null;

    private LibraryBuild? SelectedBuild => DxvkLibrary.Builds().FirstOrDefault(b => b.Id == SelectedBuildChoice?.Tag);

    /// <summary>What the default stands for now.</summary>
    public string Info
    {
        get
        {
            var pin = Settings.DefaultBuild(ComponentRegistry.Dxvk);
            var auto = DxvkLibrary.Choose(null, pin);
            return auto is null
                ? "Nothing downloaded or imported yet: the newest release is downloaded from GitHub on the first install."
                : $"Installs {auto.Label}{(IsManual ? " (your choice)" : " (Auto follows the newest Stable release)")}. Imported builds stay until you remove them; downloaded ones are cleaned up like the other libraries.";
        }
    }

    public void Reload()
    {
        _reloading = true;
        var pin = Settings.DefaultBuild(ComponentRegistry.Dxvk);
        var builds = DxvkLibrary.Builds();
        DefaultChoices.Clear();
        DefaultChoices.Add(new DlssChoice(null, DxvkLibrary.AutoLabel(null)));
        foreach (var c in Enum.GetValues<DxvkChannel>().Where(c => c != DxvkChannel.Stable))
            DefaultChoices.Add(new DlssChoice(DxvkChannels.Id(c), $"Newest {DxvkChannels.Label(c)}"));
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
        Settings.SetDefaultBuild(ComponentRegistry.Dxvk, value.Tag);
        Settings.Save();
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(Info));
        _ = _changed();
    }

    /// <summary>Back to following the newest Stable release.</summary>
    [RelayCommand]
    private void ResetToAuto() => DefaultChoice = DefaultChoices.FirstOrDefault();

    // ---------- library ----------

    [RelayCommand]
    private async Task Import()
    {
        var dlg = new OpenFileDialog { Filter = "DXVK release|*.tar.gz;*.tgz;*.zip;*.7z|All files|*.*", Title = "Select a DXVK release (.tar.gz, .zip or .7z)" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FileName);
    }

    [RelayCommand]
    private async Task ImportFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Select the extracted DXVK folder (the one with x32 and x64)" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FolderName);
    }

    private async Task ImportFrom(string source)
    {
        try
        {
            var build = await Task.Run(() => DxvkLibrary.Import(source));
            SelectedBuildChoice = null;
            Status = $"Imported {build.Label}.";
            await _changed();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            Status = ex.Message;
        }
    }

    /// <summary>Downloads the newest build of the chosen channel into the library now (installs do it on their own when the library lacks it).</summary>
    [RelayCommand]
    private async Task Download()
    {
        var channel = DxvkChannels.Parse(DownloadChannel?.Tag) ?? DxvkChannel.Stable;
        try
        {
            var library = new DxvkLibrary(_main.Gh);
            var latest = await library.LatestAsync(channel, default);
            if (latest is null) { Status = $"No {DxvkChannels.Label(channel)} build with a published SHA-256 was found (offline, or GitHub has none)."; return; }
            var build = await library.EnsureAsync(latest, null, default);
            Status = $"{build.Label} is in the library.";
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
        DxvkLibrary.Remove(build, Settings);
        Settings.Save();
        Status = $"Removed {build.Label}.";
        await _changed();
    }
}
