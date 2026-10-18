using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// The Settings row of one import-only add-on (ShortFuse's or Lecram's): the library of imported builds, the default build,
/// a name the user gives the selected build, and import / remove.
/// </summary>
public sealed partial class AddonLibraryViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly AddonKind _kind;
    private readonly Func<Task> _changed;
    private bool _reloading;

    /// <param name="changed">Runs after the library or the default changed, so games, options and warnings follow.</param>
    public AddonLibraryViewModel(MainViewModel main, AddonKind kind, Func<Task> changed)
    {
        _main = main;
        _kind = kind;
        _changed = changed;
        Reload();
    }

    private AppSettings Settings => _main.S.Settings;
    private ComponentStore Store => _main.S.Store;
    private bool IsRenoDx => _kind == AddonKind.RenoDx;

    public string Title => IsRenoDx ? "ShortFuse RenoDX DLSS" : "Lecram DLSS5 Neural Rendering";
    public string FileNames => IsRenoDx ? ComponentStore.RenoDxFile : $"{ComponentStore.Lecram10File} (v10 and older) · {ComponentStore.Lecram11File} (v11 and newer)";
    public string AutomationName => $"Default {Title} version";

    public ObservableCollection<DlssChoice> Choices { get; } = [];

    public bool HasBuilds => Choices.Count > 0;

    /// <summary>The default build; games without their own pick install it.</summary>
    public DlssChoice? Selected
    {
        get => Choices.FirstOrDefault(c => c.Tag == SelectedBuild?.Id);
        set
        {
            if (_reloading || value?.Tag is not { } id || id == Settings.AddonId(_kind)) return;
            Settings.SetAddonId(_kind, id);
            Settings.Save();
            _ = _changed();
        }
    }

    private AddonBuild? SelectedBuild => Store.AddonBuildFor(_kind, null);

    /// <summary>The user's name for the selected build, edited in a text box and applied with Save name.</summary>
    [ObservableProperty] private string _labelText = "";

    public string Info
    {
        get
        {
            if (SelectedBuild is not { } build)
                return IsRenoDx
                    ? $"Not imported. ShortFuse's {ComponentStore.RenoDxFile} isn't published on GitHub; get it from ShortFuse's RenoDX Discord and import it here."
                    : $"Not imported. Lecram's DLSS5 add-on is distributed by the author to supporters; import your copy here ({ComponentStore.Lecram10File} for v10 and older, {ComponentStore.Lecram11File} for v11 and newer).";
            var path = ComponentStore.AddonPathOf(_kind, build);
            var generation = build.Generation switch { LecramGeneration.V11 => "v11 and newer  ·  ", LecramGeneration.V10 => "v10 and older  ·  ", _ => "" };
            var size = File.Exists(path) ? $"{new FileInfo(path).Length / 1048576.0:0.0} MB  ·  " : "";
            var count = Choices.Count > 1 ? $"  ·  {Choices.Count} builds in the library" : "";
            return $"{generation}{size}SHA-256 {build.Id[..12]}…{count}";
        }
    }

    /// <summary>Rebuilds the list from the library and the selection from the settings.</summary>
    public void Reload()
    {
        _reloading = true;
        Choices.Clear();
        foreach (var b in Store.AddonBuilds(_kind)) Choices.Add(new DlssChoice(b.Id, b.Label));
        LabelText = SelectedBuild?.UserLabel ?? "";
        _reloading = false;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasBuilds));
        OnPropertyChanged(nameof(Info));
    }

    [RelayCommand]
    private async Task Import()
    {
        var filter = IsRenoDx
            ? "RenoDX DLSS add-on|renodx-dlss.addon64;*.addon64"
            : "Lecram DLSS5 add-on|renodx-dlss5.addon64;dlss5-nr.addon64;*.addon64";
        var dlg = new OpenFileDialog { Filter = filter, Title = $"Select {Title}" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var build = await Task.Run(() => IsRenoDx ? Store.ImportRenoDx(dlg.FileName) : Store.ImportLecram(dlg.FileName));
            if (Store.AdoptImportedAddon(Settings, _kind, build)) Settings.Save();
            else Log.Info($"Imported {Title} {build.Name}; the default stays on the newer {SelectedBuild?.Name}");
            await _changed();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { Log.Error($"Import of {Title} failed", ex); Views.Dialog.Show("Import failed", ex.Message); }
    }

    [RelayCommand]
    private async Task Rename()
    {
        if (SelectedBuild is not { } build) return;
        try
        {
            Store.RenameAddon(_kind, build.Id, LabelText);
            await _changed();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        { Log.Error($"Could not rename the {Title} build", ex); Views.Dialog.Show("Rename failed", ex.Message); }
    }

    [RelayCommand]
    private async Task Remove()
    {
        if (SelectedBuild is not { } build) return;
        if (!Views.Dialog.Confirm("Remove from library", $"Remove {build.Label} from the {Title} library? Games that already have it keep their copy.", "Remove", danger: true)) return;
        try
        {
            Store.RemoveAddon(_kind, build.Id);
            if (Settings.AddonId(_kind) == build.Id) Settings.SetAddonId(_kind, null);
            Settings.Save();
            Log.Info($"Removed {Title} {build.Label} from the library");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log.Error($"Could not remove the {Title} build", ex); }
        await _changed();
    }
}
