using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// The Settings row of one import-only add-on (ShortFuse's, Lecram's or Deep Fried Chicken): the library of imported builds,
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
    private bool IsDfc => _kind == AddonKind.Dfc;
    public bool CanCheckUpdates => CanCheck(_kind, BuildFlags.LecramChannel);

    /// <summary>Only Lecram has a channel, and only the private build is allowed to use it.</summary>
    internal static bool CanCheck(AddonKind kind, bool channelEnabled) => kind == AddonKind.Lecram && channelEnabled;

    /// <summary>Lecram's row has the automatic download switch: the MediaFire check in the private build, the catalog mirror otherwise.</summary>
    public bool HasAutoDownload => _kind == AddonKind.Lecram;
    public string AutoDownloadLabel => CanCheckUpdates ? "Check for new builds automatically" : "Download Lecram's add-on from the catalog";

    /// <summary>Deep Fried Chicken ships as a folder: its row also imports the extracted release folder.</summary>
    public bool CanImportFolder => IsDfc;

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
        AddonKind.Lecram => $"{ComponentStore.Lecram10File} (v10 and older) · {ComponentStore.Lecram11File} (v11 and newer)",
        _ => $"{ComponentStore.DfcFile} with its 64-bit folder",
    };
    public string AutomationName => $"Default {Title} version";

    public ObservableCollection<DlssChoice> Choices { get; } = [];

    private int _buildCount;
    public bool HasBuilds => _buildCount > 0;

    /// <summary>Lecram's and Deep Fried Chicken's first choice: no pinned build, the default follows the newest one in the library.</summary>
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

    /// <summary>The tooltip of the default picker; names the "(download)" entries only where this add-on has them.</summary>
    public string PickerTip => CatalogKind is null || (_kind == AddonKind.Lecram && BuildFlags.LecramChannel)
        ? "Default build: games without their own pick install it"
        : "Default build: games without their own pick install it. Builds marked (download) are older catalog builds; picking one downloads it first.";

    /// <summary>The older catalog builds of this add-on; Deep Fried Chicken has none, and Lecram none in the private build.</summary>
    private CatalogBuildKind? CatalogKind => _kind switch { AddonKind.RenoDx => CatalogBuildKind.RenoDx, AddonKind.Lecram => CatalogBuildKind.Lecram, _ => null };

    /// <summary>"(download)" picked as the default: the build comes first, then it becomes the default. If it doesn't come the list goes back to the current default.</summary>
    private async Task DownloadAsync(string offerTag)
    {
        var picked = await _main.PickCatalogBuildAsync(CatalogKind!.Value, offerTag, id => { Settings.SetAddonId(_kind, id); Settings.Save(); });
        if (picked) await _changed();
        else Reload();
    }

    internal static string NewestLabel(string newestBuild) => $"Newest ({newestBuild})";

    internal static string NoLecramBuilds(bool channelEnabled) => channelEnabled
        ? $"No builds yet. Use Check now to fetch the latest v11 build, or import your own copy ({ComponentStore.Lecram10File} for v10 and older, {ComponentStore.Lecram11File} for v11 and newer)."
        : $"No builds yet. They are downloaded and installed automatically at the next update check, or import the add-on ({ComponentStore.Lecram10File} for v10 and older, {ComponentStore.Lecram11File} for v11 and newer).";

    private const string NoDfcBuilds =
        "Not imported. Its licence is personal use only, so import your own copy: extract the release and choose its folder (Folder…), " +
        "or choose deep-fried-chicken.addon64 or a .zip of the release (Import…). The whole 64-bit folder is imported. 32-bit games are not supported.";

    /// <summary>The keys the app writes into deep-fried-chicken.cfg, which a build's default cfg is checked against.</summary>
    private static IEnumerable<string> DfcKeys => GameOptionViewModel.Definitions.Dfc.Select(o => o.Key);

    /// <summary>The default Deep Fried Chicken build, and whether it differs from the tested release (details in the log).</summary>
    private static string DfcInfo(AddonBuild build, DfcCompatReport? compat, int buildCount)
    {
        var warning = compat is { HasIssues: true }
            ? $"  ·  not the tested version ({DfcConfig.TestedVersion}, config schema {DfcConfig.TestedSchema}); see the log" : "";
        var count = buildCount > 1 ? $"  ·  {buildCount} builds in the library" : "";
        return $"Default {build.Name}  ·  SHA-256 {build.Id[..12]}…{warning}{count}";
    }

    private AddonBuild? SelectedBuild => Store.AddonBuildFor(_kind, null);

    /// <summary>The user's name for the selected build, edited in a text box and applied with Save name.</summary>
    [ObservableProperty] private string _labelText = "";

    public string Info
    {
        get
        {
            if (SelectedBuild is not { } build)
                return _kind switch
                {
                    AddonKind.RenoDx => $"Not in the library yet. ShortFuse's {ComponentStore.RenoDxFile} is downloaded and installed automatically at the next update check, or import it here.",
                    AddonKind.Lecram => NoLecramBuilds(BuildFlags.LecramChannel),
                    _ => NoDfcBuilds,
                };
            if (IsDfc) return DfcInfo(build, Store.DfcCompat(DfcKeys, build.Id), _buildCount);
            var path = ComponentStore.AddonPathOf(_kind, build);
            var generation = build.Generation switch { LecramGeneration.V11 => "v11 and newer  ·  ", LecramGeneration.V10 => "v10 and older  ·  ", _ => "" };
            var size = File.Exists(path) ? $"{new FileInfo(path).Length / 1048576.0:0.0} MB  ·  " : "";
            var count = _buildCount > 1 ? $"  ·  {_buildCount} builds in the library" : "";
            return $"{generation}{size}SHA-256 {build.Id[..12]}…{count}";
        }
    }

    /// <summary>Rebuilds the list from the library and the selection from the settings.</summary>
    public void Reload()
    {
        _reloading = true;
        Choices.Clear();
        var builds = Store.AddonBuilds(_kind);
        _buildCount = builds.Count;
        if (HasNewestChoice && builds.Count > 0) Choices.Add(new DlssChoice(null, NewestLabel(builds[0].Label)));
        foreach (var b in builds) Choices.Add(new DlssChoice(b.Id, b.Label));
        if (CatalogKind is { } kind) foreach (var o in _main.S.CatalogOffers(kind)) Choices.Add(new DlssChoice(o.Tag, o.ChoiceLabel));
        LabelText = SelectedBuild?.UserLabel ?? "";
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
        var filter = _kind switch
        {
            AddonKind.RenoDx => "RenoDX DLSS add-on|renodx-dlss.addon64;*.addon64",
            AddonKind.Lecram => "Lecram DLSS5 add-on|renodx-dlss5.addon64;dlss5-nr.addon64;*.addon64",
            _ => "Deep Fried Chicken|deep-fried-chicken.addon64;*.addon64;*.zip",
        };
        var dlg = new OpenFileDialog { Filter = filter, Title = IsDfc ? "Select deep-fried-chicken.addon64 or a .zip of the release" : $"Select {Title}" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FileName);
    }

    [RelayCommand]
    private async Task ImportFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Select the extracted Deep Fried Chicken folder (or its 64-bit folder)" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FolderName);
    }

    private async Task ImportFrom(string source)
    {
        try
        {
            var build = await Task.Run(() => _kind switch
            {
                AddonKind.RenoDx => Store.ImportRenoDx(source),
                AddonKind.Lecram => Store.ImportLecram(source),
                _ => Store.ImportDfc(source),
            });
            if (Store.AdoptImportedAddon(Settings, _kind, build)) Settings.Save();
            else if (SelectedBuild is { } current && current.Id != build.Id) Log.Info($"Imported {Title} {build.Name}; the default stays on the newer {current.Name}");
            await _changed();
            if (IsDfc && Store.DfcCompat(DfcKeys, build.Id) is { HasIssues: true } compat)
                Views.Dialog.Show("Deep Fried Chicken config check", compat.Summary + Environment.NewLine + Environment.NewLine + "Settings that still exist are applied normally.");
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
