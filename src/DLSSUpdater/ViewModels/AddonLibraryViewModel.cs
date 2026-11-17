using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// The Settings row of one add-on library (ShortFuse's or Lecram's): the library of imported and downloaded builds,
/// the default build, a name the user gives the selected build, and import / remove.
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
        // Check now is a run like any other: off while a scan, check or install is going.
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsBusy)) CheckNowCommand.NotifyCanExecuteChanged();
        };
        Reload();
    }

    private AppSettings Settings => _main.S.Settings;
    private ComponentStore Store => _main.S.Store;
    public bool CanCheckUpdates => CanCheck(_kind, BuildFlags.LecramChannel);

    /// <summary>Only Lecram has a channel, and only the private build is allowed to use it.</summary>
    internal static bool CanCheck(AddonKind kind, bool channelEnabled) => kind == AddonKind.Lecram && channelEnabled;

    /// <summary>Lecram's row has the automatic check switch in the private build (MediaFire); public builds import it, so there is nothing to switch.</summary>
    public bool HasAutoDownload => CanCheckUpdates;
    public string AutoDownloadLabel => "Check for new builds automatically";

    public bool AutoDownload
    {
        get => Settings.AutoDownloadLecram;
        set
        {
            if (value == Settings.AutoDownloadLecram) return;
            Settings.AutoDownloadLecram = value;
            Settings.Save();
            OnPropertyChanged();
            CheckNowCommand.NotifyCanExecuteChanged();
        }
    }
    public string LastResult => Settings.LecramLastResult;

    private bool CanCheckNow() => CanCheckUpdates && !_main.IsBusy;

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private async Task CheckNow()
    {
        await _main.CheckLecramNowAsync();
        await _changed();
    }

    public string Title => ComponentStore.LibraryTitle(ComponentStore.KindOf(_kind));
    public string FileNames => _kind switch
    {
        AddonKind.RenoDx => ComponentStore.RenoDxFile,
        _ => $"{ComponentStore.Lecram10File} (v10 and older) · {ComponentStore.Lecram11File} (v11 and newer)",
    };
    public string AutomationName => $"Default {Title} version";

    public ObservableCollection<DlssChoice> Choices { get; } = [];

    private int _buildCount;
    public bool HasBuilds => _buildCount > 0;

    /// <summary>Lecram's first choice: no pinned build, the default follows the newest one in the library.</summary>
    private bool HasNewestChoice => ComponentStore.FollowsNewest(_kind);

    /// <summary>The default build; games without their own pick install it.</summary>
    public DlssChoice? Selected
    {
        get => HasNewestChoice && Settings.AddonId(_kind) is null
            ? Choices.FirstOrDefault()
            : Choices.FirstOrDefault(c => c.Tag is not null && c.Tag == SelectedBuild?.Id);
        set
        {
            if (_reloading || value is null || (value.Tag is null && !HasNewestChoice) || value.Tag == Settings.AddonId(_kind)) return;
            if (CatalogOffer.IsOffer(value.Tag)) { _ = DownloadAsync(value.Tag!); return; }
            Settings.SetAddonId(_kind, value.Tag);
            Settings.Save();
            _ = _changed();
        }
    }

    /// <summary>The tooltip of the default picker; names the "· download" entries only where this add-on has them.</summary>
    public string PickerTip => CatalogKind is null || (_kind == AddonKind.Lecram && BuildFlags.LecramCatalogOff)
        ? "Default build: games without their own pick install it"
        : "Default build: games without their own pick install it. Builds marked download are fetched when picked.";

    /// <summary>The older catalog builds of this add-on; Lecram has none while its catalog downloads are off (BuildFlags.LecramCatalogOff).</summary>
    private CatalogBuildKind? CatalogKind => _kind == AddonKind.RenoDx ? CatalogBuildKind.RenoDx : CatalogBuildKind.Lecram;

    /// <summary>A "· download" entry picked as the default: the build comes first, then it becomes the default. If it doesn't come the list goes back to the current default.</summary>
    private async Task DownloadAsync(string offerTag)
    {
        var picked = await _main.PickCatalogBuildAsync(CatalogKind!.Value, offerTag, id => { Settings.SetAddonId(_kind, id); Settings.Save(); });
        if (picked) await _changed();
        else Reload();
    }

    /// <summary>"Newest · 5.0.0 (2026-10-20)"; the build's own label may already end in a parenthesis, so no second pair.</summary>
    internal static string NewestLabel(string newestBuild) => $"Newest · {newestBuild}";

    /// <summary>The public build imports Lecram's add-on, so new builds only come in by import. No author URL is known to the app, so there is no link.</summary>
    public bool IsImportOnly => _kind == AddonKind.Lecram && !CanCheckUpdates;
    public string ImportOnlyNote => "Import only: get new builds from the author's page.";

    internal static string NoLecramBuilds(bool channelEnabled) => channelEnabled
        ? $"No builds yet. Use Check now to fetch the latest build, or import your own copy ({ComponentStore.Lecram10File} for v10 and older, {ComponentStore.Lecram11File} for v11 and newer, or the author's setup exe or add-on zip)."
        : $"No builds yet. Import the add-on ({ComponentStore.Lecram10File} for v10 and older, {ComponentStore.Lecram11File} for v11 and newer, or the author's setup exe or add-on zip).";

    private AddonBuild? SelectedBuild => Store.AddonBuildFor(_kind, null);

    /// <summary>The user's name for the selected build, edited in a text box and applied with Save name.</summary>
    [ObservableProperty] private string _labelText = "";

    /// <summary>The name box and Save name only show after Rename…</summary>
    [ObservableProperty] private bool _isRenaming;

    [RelayCommand]
    private void ToggleRename() => IsRenaming = !IsRenaming;

    public string Info
    {
        get
        {
            if (SelectedBuild is not { } build)
                return _kind == AddonKind.RenoDx
                    ? $"Not in the library yet. ShortFuse's {ComponentStore.RenoDxFile} is downloaded and installed automatically at the next update check, or import it here."
                    : NoLecramBuilds(BuildFlags.LecramChannel);
            var path = ComponentStore.AddonPathOf(_kind, build);
            var generation = build.Generation switch
            {
                LecramGeneration.V11 => "v11 and newer  ·  ",
                LecramGeneration.V10 => "v10 and older  ·  ",
                _ => _kind == AddonKind.RenoDx && build.FileVersion.Length > 0 ? $"File version {build.FileVersion}  ·  " : "",
            };
            var size = File.Exists(path) ? $"{new FileInfo(path).Length / 1048576.0:0.0} MB  ·  " : "";
            var count = _buildCount > 1 ? $"  ·  {_buildCount} builds in the library" : "";
            return $"{generation}{LecramHostInfo(build)}{size}SHA-256 {build.Id[..12]}…{count}";
        }
    }

    /// <summary>Whether a v11-generation Lecram build carries NR's host, which Direct3D 9, Direct3D 10, OpenGL and 32-bit games need.</summary>
    internal static string LecramHostInfo(AddonBuild build) => build.Generation != LecramGeneration.V11 ? ""
        : ComponentStore.LecramHasHost(build) ? "with NR's host and 32-bit add-on  ·  "
        : "64-bit add-on only (no NR host)  ·  ";

    /// <summary>Rebuilds the list from the library and the selection from the settings.</summary>
    public void Reload()
    {
        _reloading = true;
        Choices.Clear();
        var builds = Store.AddonBuilds(_kind);
        _buildCount = builds.Count;
        if (HasNewestChoice && builds.Count > 0) Choices.Add(new DlssChoice(null, NewestLabel(builds[0].Label)));
        var offers = CatalogKind is { } kind ? _main.S.CatalogOffers(kind).Select(o => o.ToEntry()) : [];
        foreach (var c in BuildPickerList.Merge(builds.Select(b => b.ToPickerEntry()), offers)) Choices.Add(new DlssChoice(c.Tag, c.Label));
        LabelText = SelectedBuild?.UserLabel ?? "";
        IsRenaming = false;
        _reloading = false;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasBuilds));
        OnPropertyChanged(nameof(Info));
        OnPropertyChanged(nameof(LastResult));
        OnPropertyChanged(nameof(AutoDownload));
        CheckNowCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task Import()
    {
        var filter = _kind == AddonKind.RenoDx
            ? "RenoDX DLSS add-on|renodx-dlss.addon64;*.addon64"
            : "Lecram DLSS5 add-on, setup or add-on zip|renodx-dlss5.addon64;dlss5-nr.addon64;*.addon64;DLSS5-NR-*.exe;*.zip";
        var dlg = new OpenFileDialog { Filter = filter, Title = $"Select {Title}" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FileName);
    }

    private async Task ImportFrom(string source)
    {
        try
        {
            var build = await Task.Run(() => _kind == AddonKind.RenoDx ? Store.ImportRenoDx(source) : Store.ImportLecram(source));
            if (Store.AdoptImportedAddon(Settings, _kind, build)) Settings.Save();
            else if (SelectedBuild is { } current && current.Id != build.Id) Log.Info($"Imported {Title} {build.Name}; the default stays on the newer {current.Name}");
            await _changed();
            await _main.AutoCleanUpAfterImportAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { Log.Error($"Import of {Title} failed", ex); Views.Dialog.Show("Import failed", UserErrors.Message(ex)); }
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
        { Log.Error($"Could not rename the {Title} build", ex); Views.Dialog.Show("Rename failed", UserErrors.Message(ex)); }
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
