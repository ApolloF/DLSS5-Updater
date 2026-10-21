using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// Settings → Luma: the library of Luma zips (downloaded by the games that use them, or imported), the import and the removal. There is
/// no default build: every game's mod is its own zip, picked on the game page. Logic lives in <see cref="ComponentStore"/>.
/// </summary>
public sealed partial class LumaSettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Func<Task> _changed;

    public LumaSettingsViewModel(MainViewModel main, Func<Task> changed)
    {
        _main = main;
        _changed = changed;
        Reload();
    }

    public ObservableCollection<DlssChoice> BuildChoices { get; } = [];

    [ObservableProperty] private DlssChoice? _selectedBuildChoice;
    [ObservableProperty] private string _status = "";

    public bool HasBuilds => BuildChoices.Count > 0;

    public string Info => HasBuilds
        ? "Luma zips are downloaded when a game that uses one is installed, checked against the SHA-256 in the catalog. Imported zips stay until you remove them."
        : "Nothing downloaded or imported yet: a game's Luma zip is downloaded the first time you install it.";

    public void Reload()
    {
        var selected = SelectedBuildChoice?.Tag;
        BuildChoices.Clear();
        foreach (var b in _main.S.Store.LumaBuilds) BuildChoices.Add(new DlssChoice(b.Id, b.Label + (b.Imported ? "" : $" ({b.Arch})")));
        SelectedBuildChoice = BuildChoices.FirstOrDefault(c => c.Tag == selected) ?? BuildChoices.FirstOrDefault();
        OnPropertyChanged(nameof(HasBuilds));
        OnPropertyChanged(nameof(Info));
    }

    [RelayCommand]
    private async Task Import()
    {
        var dlg = new OpenFileDialog { Filter = "Luma zip|*.zip|All files|*.*", Title = "Select a Luma zip (Luma-<Game>.zip)" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var build = await Task.Run(() => _main.S.Store.ImportLumaBuild(dlg.FileName));
            SelectedBuildChoice = null;
            Status = $"Imported {build.Label}.";
            await _changed();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private async Task Remove()
    {
        if (SelectedBuildChoice?.Tag is not { } id || _main.S.Store.LumaBuildFor(id) is not { } build) return;
        var settings = _main.S.Settings;
        foreach (var g in settings.Games.Values)
            if (g.BuildPick(ComponentRegistry.Luma)?.Equals(id, StringComparison.OrdinalIgnoreCase) == true)
            {
                g.SetBuildPick(ComponentRegistry.Luma, null);
                if (g.LumaMod == LumaManual.CustomMod) g.SetLumaMod(null);
            }
        _main.S.Store.RemoveLuma(id);
        settings.Save();
        Status = $"Removed {build.Label}.";
        await _changed();
    }
}
