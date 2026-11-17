using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// Settings → Local components → HSF: the imported releases (the .zip or the extracted folder) and the removal. HSF has a private licence,
/// so there is no download and no default pick: installs use the newest import. Logic lives in <see cref="HsfLibrary"/>.
/// </summary>
public sealed partial class HsfSettingsViewModel : ObservableObject
{
    private readonly Func<Task> _changed;

    public HsfSettingsViewModel(Func<Task> changed)
    {
        _changed = changed;
        Reload();
    }

    public ObservableCollection<DlssChoice> BuildChoices { get; } = [];

    [ObservableProperty] private DlssChoice? _selectedBuildChoice;
    [ObservableProperty] private string _status = "";

    public bool HasBuilds => BuildChoices.Count > 0;

    public string Info => HasBuilds
        ? $"Installs use {BuildChoices[0].Label}, the newest import. Imported releases stay until you remove them."
        : "HSF's licence is private and invite-only, so the app never downloads it: import the release you were given (the .zip or the extracted folder).";

    public void Reload()
    {
        var selected = SelectedBuildChoice?.Tag;
        BuildChoices.Clear();
        foreach (var b in HsfLibrary.Builds()) BuildChoices.Add(new DlssChoice(b.Id, b.Label));
        SelectedBuildChoice = BuildChoices.FirstOrDefault(c => c.Tag == selected) ?? BuildChoices.FirstOrDefault();
        OnPropertyChanged(nameof(HasBuilds));
        OnPropertyChanged(nameof(Info));
    }

    [RelayCommand]
    private async Task Import()
    {
        var dlg = new OpenFileDialog { Filter = "HSF release|*.zip;hsf.addon64|All files|*.*", Title = "Select the HSF release .zip or hsf.addon64" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FileName);
    }

    [RelayCommand]
    private async Task ImportFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Select the extracted HSF release folder" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FolderName);
    }

    internal async Task ImportFrom(string source)
    {
        try
        {
            var build = await Task.Run(() => HsfLibrary.Import(source));
            SelectedBuildChoice = null;
            Status = $"Imported HSF {build.Label}.";
            await _changed();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private async Task Remove()
    {
        if (HsfLibrary.Builds().FirstOrDefault(b => b.Id == SelectedBuildChoice?.Tag) is not { } build) return;
        HsfLibrary.Remove(build);
        Status = $"Removed HSF {build.Label}.";
        await _changed();
    }
}
