using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// Settings → ReShade add-ons → ReShade: the default build or channel, the library of builds (import, older versions, name,
/// remove), the ReShade.ini template and the Vulkan layer. Logic lives in <see cref="ComponentStore"/>, <see cref="ReShadeIniTemplate"/>
/// and <see cref="VulkanLayer"/>; this only asks and shows.
/// </summary>
public sealed partial class ReShadeSettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Func<Task> _changed;
    private readonly IVulkanLayerHost _layerHost = new WindowsVulkanLayerHost();
    private bool _reloading;

    public ReShadeSettingsViewModel(MainViewModel main, Func<Task> changed)
    {
        _main = main;
        _changed = changed;
        Reload();
    }

    private AppSettings Settings => _main.S.Settings;
    private ComponentStore Store => _main.S.Store;

    public ObservableCollection<DlssChoice> DefaultChoices { get; } = [];
    public ObservableCollection<DlssChoice> BuildChoices { get; } = [];
    public ObservableCollection<string> LegacyVersions { get; } = [];

    [ObservableProperty] private DlssChoice? _defaultChoice;
    [ObservableProperty] private DlssChoice? _selectedBuildChoice;
    [ObservableProperty] private string _labelText = "";
    [ObservableProperty] private string? _selectedLegacy;
    [ObservableProperty] private string _layerText = "";
    [ObservableProperty] private bool _layerInstalled;
    [ObservableProperty] private bool _layerRemovable;

    public bool AutoDownload
    {
        get => Settings.AutoDownloadReShade;
        set
        {
            if (value == Settings.AutoDownloadReShade) return;
            Settings.AutoDownloadReShade = value;
            Settings.Save();
            OnPropertyChanged();
            _ = _changed();
        }
    }

    public bool HasBuilds => Store.ReShadeBuilds.Count > 0;

    private ReShadeBuild? SelectedBuild => Store.ReShadeBuilds.FirstOrDefault(b => b.Id == SelectedBuildChoice?.Tag);

    /// <summary>What the default stands for now, and what the selected build was checked against.</summary>
    public string Info
    {
        get
        {
            var lines = new List<string>();
            var follows = Store.DefaultReShadeBuild();
            lines.Add(follows is null
                ? (Settings.AutoDownloadReShade ? "Nothing downloaded yet: the newest Stable add-on build from reshade.me is fetched on the first install." : "Nothing imported, and automatic download is off.")
                : $"Default: {follows.Label} · {Bits(follows)}");
            if (SelectedBuild is { } b)
                lines.Add($"{b.Label} · {Bits(b)} · {b.VerificationText}");
            return string.Join(Environment.NewLine, lines);
        }
    }

    private static string Bits(ReShadeBuild b) => b.Has64 && b.Has32 ? "64-bit and 32-bit" : b.Has64 ? "64-bit only" : "32-bit only";

    public void Reload()
    {
        _reloading = true;
        var pin = Settings.ReShadeId;
        DefaultChoices.Clear();
        DefaultChoices.Add(new DlssChoice(null, "Newest Stable (add-on build)"));
        foreach (var c in new[] { ReShadeChannel.StableNoAddons, ReShadeChannel.Nightly })
            DefaultChoices.Add(new DlssChoice(ReShadePick.ForChannel(c), "Newest " + ReShadeBuild.ChannelLabel(c).ToLowerInvariant() + (c == ReShadeChannel.Nightly ? " (CI build)" : "")));
        foreach (var b in Store.ReShadeBuilds) DefaultChoices.Add(new DlssChoice(b.Id, b.Label));
        if (pin is not null && DefaultChoices.All(c => c.Tag != pin)) DefaultChoices.Add(new DlssChoice(pin, "Missing ReShade build · import again"));
        DefaultChoice = DefaultChoices.FirstOrDefault(c => c.Tag == pin) ?? DefaultChoices[0];

        var selected = SelectedBuildChoice?.Tag;
        BuildChoices.Clear();
        foreach (var b in Store.ReShadeBuilds) BuildChoices.Add(new DlssChoice(b.Id, b.Label));
        SelectedBuildChoice = BuildChoices.FirstOrDefault(c => c.Tag == selected) ?? BuildChoices.FirstOrDefault();
        LabelText = SelectedBuild?.UserLabel ?? "";
        _reloading = false;
        OnPropertyChanged(nameof(HasBuilds));
        OnPropertyChanged(nameof(Info));
        OnPropertyChanged(nameof(AutoDownload));
        RefreshLayer();
    }

    partial void OnDefaultChoiceChanged(DlssChoice? value)
    {
        if (_reloading || value is null) return;
        Settings.ReShadeId = value.Tag;
        Settings.Save();
        OnPropertyChanged(nameof(Info));
        _ = _changed();
    }

    partial void OnSelectedBuildChoiceChanged(DlssChoice? value)
    {
        if (_reloading) return;
        LabelText = SelectedBuild?.UserLabel ?? "";
        OnPropertyChanged(nameof(Info));
    }

    // ---------- library ----------

    [RelayCommand]
    private async Task Import()
    {
        var dlg = new OpenFileDialog { Filter = "ReShade|ReShade64.dll;ReShade32.dll;ReShade_Setup*.exe;*.zip|All files|*.*", Title = "Select ReShade64.dll, ReShade32.dll, a ReShade setup or a .zip" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FileName);
    }

    [RelayCommand]
    private async Task ImportFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Select the folder with ReShade64.dll and ReShade32.dll" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FolderName);
    }

    private async Task ImportFrom(string source)
    {
        try
        {
            var build = await Task.Run(() => Store.ImportReShade(source));
            SelectedBuildChoice = null;
            await _changed();
            SelectedBuildChoice = BuildChoices.FirstOrDefault(c => c.Tag == build.Id);
            await _main.AutoCleanUpAfterImportAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { Log.Error("Import of ReShade failed", ex); Views.Dialog.Show("Import failed", UserErrors.Message(ex)); }
    }

    [RelayCommand]
    private async Task Rename()
    {
        if (SelectedBuild is not { } build) return;
        try
        {
            Store.RenameReShade(build.Id, LabelText);
            await _changed();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        { Log.Error("Could not rename the ReShade build", ex); Views.Dialog.Show("Rename failed", UserErrors.Message(ex)); }
    }

    [RelayCommand]
    private async Task Remove()
    {
        if (SelectedBuild is not { } build) return;
        if (!Views.Dialog.Confirm("Remove from library", $"Remove ReShade {build.Label} from the library? Games that already have it keep their copy.", "Remove", danger: true)) return;
        try
        {
            Store.RemoveReShade(build.Id);
            if (Settings.ReShadeId == build.Id) Settings.ReShadeId = null;
            foreach (var g in Settings.Games.Values.Where(g => g.ReShadeId == build.Id)) g.ReShadeId = null;
            Settings.Save();
            Log.Info($"Removed ReShade {build.Label} from the library");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log.Error("Could not remove the ReShade build", ex); }
        await _changed();
    }

    [RelayCommand]
    private async Task ListOlderVersions()
    {
        var versions = await Store.ReShadeLegacyVersionsAsync(default);
        LegacyVersions.Clear();
        foreach (var v in versions) LegacyVersions.Add(v);
        SelectedLegacy = LegacyVersions.FirstOrDefault();
        if (versions.Count == 0) Views.Dialog.Show("Older ReShade versions", "The list of versions couldn't be loaded (GitHub unreachable or rate-limited). Try again later.");
    }

    [RelayCommand]
    private async Task DownloadLegacy()
    {
        if (SelectedLegacy is not { } version) return;
        await _main.RunJob($"Downloading ReShade {version}", async ct =>
        {
            await Store.DownloadReShadeLegacyAsync(version, _main.TransferProgress(), ct);
            await _changed();
        });
    }

    // ---------- template ----------

    public string TemplatePath => AppPaths.ReShadeTemplate;

    [RelayCommand]
    private void EditTemplate()
    {
        try { Process.Start(new ProcessStartInfo(ReShadeIniTemplate.EnsureFile()) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Log.Error("Could not open the ReShade.ini template", ex); Views.Dialog.Show("Template", $"Could not open {AppPaths.ReShadeTemplate}: {UserErrors.Message(ex)}"); }
    }

    [RelayCommand]
    private void ResetTemplate()
    {
        if (!Views.Dialog.Confirm("Reset template", "Put the shipped ReShade.ini template back? Your edits to it are lost; games keep their ReShade.ini.", "Reset", danger: true)) return;
        try { ReShadeIniTemplate.Reset(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log.Error("Could not reset the ReShade.ini template", ex); }
    }

    // ---------- Vulkan layer ----------

    public void RefreshLayer()
    {
        var status = VulkanLayer.Status(_layerHost);
        LayerText = status.Text;
        LayerInstalled = status.IsInstalled;
        LayerRemovable = status.State is VulkanLayerState.Installed or VulkanLayerState.Incomplete;
    }

    [RelayCommand]
    private async Task InstallLayer()
    {
        if (!Views.Dialog.Confirm("Install the Vulkan layer",
                "ReShade reaches Vulkan games through a layer registered for the whole PC (C:\\ProgramData\\ReShade and one HKLM registry entry). " +
                "Windows will ask for administrator rights for this step only; DLSS5 Updater itself stays unelevated.", "Install")) return;
        await _main.RunJob("Installing the ReShade Vulkan layer", async ct =>
        {
            var file = await Store.EnsureReShadeAsync(null, 64, _main.TransferProgress(), ct);
            try { await Store.EnsureReShadeAsync(file.Build.Id, 32, _main.TransferProgress(), ct); }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or HttpRequestException)
            { Log.Info($"Vulkan layer: no 32-bit ReShade for this build ({ex.Message}); 64-bit games only"); }
            var build = Store.ReShadeBuilds.First(b => b.Id == file.Build.Id);
            await RunElevated(VulkanLayerCommand.InstallArguments(ComponentStore.ReShadeBuildDir(build), build), "installed");
        });
    }

    [RelayCommand]
    private async Task RemoveLayer()
    {
        if (!Views.Dialog.Confirm("Remove the Vulkan layer", "Vulkan games stop loading ReShade until it is installed again. Windows will ask for administrator rights for this step only.", "Remove", danger: true)) return;
        await _main.RunJob("Removing the ReShade Vulkan layer", _ => RunElevated(VulkanLayerCommand.RemoveArguments, "removed"));
    }

    private async Task RunElevated(IReadOnlyList<string> arguments, string done)
    {
        try
        {
            await Task.Run(() => VulkanLayerCommand.RunElevated(arguments));
            Log.Info($"ReShade Vulkan layer {done}");
        }
        catch (OperationCanceledException) { Log.Info("Vulkan layer: the administrator prompt was declined, nothing changed"); }
        catch (VulkanLayerException ex) { Log.Error("Vulkan layer", ex); Views.Dialog.Show("Vulkan layer", ex.Message); }
        RefreshLayer();
    }
}
