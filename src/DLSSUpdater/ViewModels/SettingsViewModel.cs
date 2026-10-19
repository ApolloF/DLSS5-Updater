using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private AppSettings Settings => _main.S.Settings;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        RenoDx = new AddonLibraryViewModel(main, AddonKind.RenoDx, AddonsChanged);
        Lecram = new AddonLibraryViewModel(main, AddonKind.Lecram, AddonsChanged);
        Overrides = new ObservableCollection<IniOverride>(Settings.IniOverrides);
        ReShadeOverrides = new ObservableCollection<IniOverride>(Settings.ReShadeOverrides);
        Folders = new ObservableCollection<FolderEntry>();

        OptiKeybinds = new(KeybindDef.Opti.Select(d => new KeybindViewModel(d, Overrides)));
        ReShadeKeybinds = new(KeybindDef.ReShadeKeys.Select(d => new KeybindViewModel(d, ReShadeOverrides)));

        ReShadeOptions = SettingsOptions.ReShade(ReShadeOverrides);
        MfgOptions = SettingsOptions.Mfg(ReShadeOverrides);
        RenoDxOptions = SettingsOptions.RenoDx(ReShadeOverrides);
        LecramOptions = SettingsOptions.Lecram(ReShadeOverrides);
        UpliftOptions = SettingsOptions.Uplift(ReShadeOverrides);
        DfcOptions = SettingsOptions.Dfc(ReShadeOverrides);
        NrOptions = SettingsOptions.Nr(Overrides);
        TonemapOptions = SettingsOptions.Tonemap(Overrides);
        HdrOptions = SettingsOptions.HdrOutput(Overrides);

        Reload();
        ApplyOptiSource();
        ApplyDfcCompat();
        ApplyLecramGeneration();
    }

    public AddonLibraryViewModel RenoDx { get; }
    public AddonLibraryViewModel Lecram { get; }
    public IReadOnlyList<string> ProxyNames => AppSettings.ProxyNames;
    public ObservableCollection<IniOverride> Overrides { get; }
    public ObservableCollection<IniOverride> ReShadeOverrides { get; }
    public ObservableCollection<FolderEntry> Folders { get; }
    public ObservableCollection<KeybindViewModel> OptiKeybinds { get; }
    public ObservableCollection<KeybindViewModel> ReShadeKeybinds { get; }
    public IReadOnlyList<IniOptionViewModel> ReShadeOptions { get; }
    public IReadOnlyList<IniOptionViewModel> MfgOptions { get; }
    public IReadOnlyList<IniOptionViewModel> RenoDxOptions { get; }
    public IReadOnlyList<IniOptionViewModel> LecramOptions { get; }
    public IReadOnlyList<IniOptionViewModel> UpliftOptions { get; }
    public IReadOnlyList<IniOptionViewModel> DfcOptions { get; }
    public IReadOnlyList<IniOptionViewModel> NrOptions { get; }
    public IReadOnlyList<IniOptionViewModel> TonemapOptions { get; }
    public IReadOnlyList<IniOptionViewModel> HdrOptions { get; }
    public IEnumerable<IniOptionViewModel> AllOptions => ReShadeOptions.Concat(MfgOptions).Concat(RenoDxOptions).Concat(LecramOptions).Concat(UpliftOptions).Concat(DfcOptions).Concat(NrOptions).Concat(TonemapOptions).Concat(HdrOptions);
    public IReadOnlyList<OptionChoice> IniModes => SettingsOptions.IniModes;

    public OptionChoice IniMode
    {
        get => IniModes.FirstOrDefault(m => m.Value == Settings.IniMode) ?? IniModes[0];
        set
        {
            if (value?.Value is null) return;
            Settings.IniMode = value.Value;
            Save();
        }
    }
    public ObservableCollection<DlssChoice> DlssChoices { get; } = [];

    [ObservableProperty] private string _tab = "General";

    partial void OnTabChanged(string value)
    {
        if (value == "ReShade") NotifyModShown();
    }

    private ModChoice? _pickedMod;

    public IReadOnlyList<ModChoice> ModChoices => ModPicker.AvailableInSettings;

    /// <summary>The mod whose defaults the ReShade tab shows. Follows the default install route until the user picks one; not saved.</summary>
    public ModChoice ModSelection
    {
        get => _pickedMod ?? ModPicker.Choice(ModChoices, DefaultMod);
        set
        {
            if (value is null) return;
            _pickedMod = value;
            NotifyModShown();
        }
    }

        private Mod DefaultMod => ModPicker.DefaultInSettings(Settings.DefaultMode == InstallMode.ReShadeOnly, Settings.InstallUplift, false, Settings.InstallLecram, Settings.InstallRenoDx);

    public bool ShowReShade => ModPicker.Shows(ModSelection.Mod, Mod.ReShade);
    public bool ShowMfg => ModPicker.Shows(ModSelection.Mod, Mod.Mfg);
    public bool ShowRenoDx => ModPicker.Shows(ModSelection.Mod, Mod.RenoDx);
    public bool ShowLecram => ModPicker.Shows(ModSelection.Mod, Mod.Lecram);
    public bool ShowDfc => ModPicker.Shows(ModSelection.Mod, Mod.Dfc);
    public bool ShowUplift => ModPicker.Shows(ModSelection.Mod, Mod.Uplift);

    private void NotifyModShown()
    {
        OnPropertyChanged(nameof(ModSelection));
        OnPropertyChanged(nameof(ShowReShade));
        OnPropertyChanged(nameof(ShowMfg));
        OnPropertyChanged(nameof(ShowRenoDx));
        OnPropertyChanged(nameof(ShowLecram));
        OnPropertyChanged(nameof(ShowDfc));
        OnPropertyChanged(nameof(ShowUplift));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeletePreset))]
    private string? _selectedPreset;

    public ObservableCollection<string> PresetNames { get; } = [];

    /// <summary>User presets can be deleted; built-in ones can't.</summary>
    public bool CanDeletePreset => SelectedPreset is { } n && Settings.Presets.Any(p => p.Name == n);

    partial void OnSelectedPresetChanged(string? value)
    {
        if (value is null || _reloading) return;
        var p = Settings.AllPresets.FirstOrDefault(x => x.Name == value);
        if (p is null) return;
        Replace(Overrides, p.Opti);
        Replace(ReShadeOverrides, p.ReShade);
        Commit();
        RefreshEditors();
        Log.Info($"Loaded preset '{value}'");
    }

    private bool _reloading;

    private static void Replace(ObservableCollection<IniOverride> target, IEnumerable<IniOverride> source)
    {
        target.Clear();
        foreach (var o in source) target.Add(new IniOverride(o.Section, o.Key, o.Value));
    }

    private static List<IniOverride> Copy(IEnumerable<IniOverride> list) =>
        list.Where(o => !string.IsNullOrWhiteSpace(o.Section) && !string.IsNullOrWhiteSpace(o.Key))
            .Select(o => new IniOverride(o.Section.Trim(), o.Key.Trim(), o.Value.Trim())).ToList();

    [RelayCommand]
    private void SavePreset()
    {
        var name = Views.Dialog.Prompt("Save preset",
            "Saves the current OptiScaler.ini and ReShade.ini settings, keybinds and options under a name. " +
            "Presets can be loaded here or assigned to single games.", SelectedPreset ?? "My preset");
        if (name is null) return;
        if (Settings.AllPresets.Any(p => p.BuiltIn && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Views.Dialog.Show("Save preset", $"'{name}' is built in and can't be replaced. Pick another name.");
            return;
        }
        if (GameConfig.GlobalNameError(Settings, name) is { } clash)
        {
            Views.Dialog.Show("Save preset", clash);
            return;
        }
        Settings.Presets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Settings.Presets.Add(new ConfigPreset { Name = name, Opti = Copy(Overrides), ReShade = Copy(ReShadeOverrides) });
        Save();
        ReloadPresets(name);
        _main.RefreshPresets();
        Log.Info($"Saved preset '{name}'");
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset is not { } name) return;
        if (Settings.AllPresets.Any(p => p.BuiltIn && p.Name == name))
        {
            Views.Dialog.Show("Delete preset", $"'{name}' is built in and can't be deleted.");
            return;
        }
        if (!Views.Dialog.Confirm("Delete preset", $"Delete preset '{name}'? Games using it fall back to the current settings.", "Delete", danger: true)) return;
        Settings.Presets.RemoveAll(p => p.Name == name);
        foreach (var g in Settings.Games.Values.Where(g => g.Preset == name)) g.Preset = null;
        Save();
        ReloadPresets(null);
        _main.RefreshPresets();
    }

    /// <summary>Re-reads the preset list after a game changed presets; keeps the selection if it still exists.</summary>
    public void RefreshPresetNames() =>
        ReloadPresets(SelectedPreset is { } sp && Settings.AllPresets.Any(p => p.Name == sp) ? sp : null);

    private void ReloadPresets(string? select)
    {
        _reloading = true;
        PresetNames.Clear();
        foreach (var p in Settings.AllPresets) PresetNames.Add(p.Name);
        SelectedPreset = select;
        _reloading = false;
    }
    [ObservableProperty] private KeybindViewModel? _capturing;

    // ---------- general ----------

    public IReadOnlyList<ModeChoice> Modes => ModeChoice.All;

    public ModeChoice DefaultMode
    {
        get => ModeChoice.All.First(m => m.Mode == Settings.DefaultMode);
        set
        {
            if (value is null) return;
            Settings.DefaultMode = value.Mode;
            Save();
        }
    }

    public string DefaultProxy
    {
        get => Settings.DefaultProxy;
        set { Settings.DefaultProxy = value; Save(); }
    }

    public bool IncludePrereleases
    {
        get => Settings.IncludePrereleases;
        set { Settings.IncludePrereleases = value; Save(); }
    }

    public IReadOnlyList<OptiSource> OptiBuilds => OptiSources.All;

    public OptiSource OptiBuild
    {
        get => OptiSources.For(Settings.OptiSource);
        set
        {
            if (value is null || value.Repo == Settings.OptiSource) return;
            Settings.OptiSource = value.Repo;
            Save();
            OnPropertyChanged();
            _main.OptiSourceChanged();
        }
    }

    /// <summary>Hides the options the chosen OptiScaler build doesn't have and switches the rest to its values.</summary>
    public void ApplyOptiSource()
    {
        var source = OptiBuild;
        var shared = GameOptionViewModel.Definitions.Nr.Concat(GameOptionViewModel.Definitions.Tonemap).Concat(GameOptionViewModel.Definitions.HdrOutput);
        foreach (var o in NrOptions.Concat(TonemapOptions).Concat(HdrOptions).Concat(shared))
            o.ApplySource(source);
    }

    public bool InstallReShade
    {
        get => Settings.InstallReShade;
        set { Settings.InstallReShade = value; Save(); }
    }

    public bool InstallMfgUnlock
    {
        get => Settings.InstallMfgUnlock;
        set { Settings.InstallMfgUnlock = value; Save(); }
    }

    public bool InstallDlssNr
    {
        get => Settings.InstallDlssNr;
        set { Settings.InstallDlssNr = value; Save(); }
    }

    public bool InstallStreamline
    {
        get => Settings.InstallStreamline;
        set { Settings.InstallStreamline = value; Save(); }
    }

    public bool AddMissingDlss
    {
        get => Settings.AddMissingDlss;
        set { Settings.AddMissingDlss = value; Save(); }
    }

    public string GitHubToken
    {
        get => Settings.GitHubToken ?? "";
        set { Settings.GitHubToken = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); Save(); }
    }

    public DlssChoice? SelectedDlss
    {
        get => DlssChoices.FirstOrDefault(c => c.Tag == Settings.DlssTag);
        set
        {
            if (value is null) return;
            Settings.DlssTag = value.Tag;
            Save();
        }
    }

    public ObservableCollection<DlssChoice> NrChoices { get; } = [];
    public DlssChoice? SelectedNr
    {
        get => NrChoices.FirstOrDefault(c => c.Tag == Settings.DlssNrId);
        set
        {
            if (value?.Tag is null || value.Tag == Settings.DlssNrId) return;
            Settings.SelectDlssNr(value.Tag);
            Save();
            _ = _main.RefreshLocalComponentsAsync();
            OnPropertyChanged(nameof(SelectedNr));
            OnPropertyChanged(nameof(SelectedNrHint));
            OnPropertyChanged(nameof(DlssNrInfo));
        }
    }

    /// <summary>Plain advice for the chosen NR build (known hashes only).</summary>
    public string? SelectedNrHint => _main.S.Store.DlssNrVersions.FirstOrDefault(v => v.Id == Settings.DlssNrId)?.Hint;

    [RelayCommand]
    private void UndoNrDefault()
    {
        if (Settings.PreviousDlssNrId is not { } id) return;
        SelectedNr = NrChoices.FirstOrDefault(c => c.Tag == id);
    }

    public bool InstallRenoDx
    {
        get => Settings.InstallRenoDx;
        set { Settings.InstallRenoDx = value; Save(); }
    }

    public bool InstallUplift
    {
        get => Settings.InstallUplift;
        set { Settings.InstallUplift = value; Save(); }
    }

    public bool InstallLecram
    {
        get => Settings.InstallLecram;
        set { Settings.InstallLecram = value; Save(); }
    }

    public string DlssNrInfo => Describe(_main.S.Store.DlssNrPath, _main.S.DlssNrSha, true);
    public string ReShadeInfo
    {
        get
        {
            var store = _main.S.Store;
            if (store.ReShadeImported) return "Imported  ·  " + Describe(store.ReShadePath, _main.S.ReShadeSha, false);
            if (!AutoDownloadReShade) return "Not imported";
            if (store.CurrentReShadePath is { } path) return "Downloaded from reshade.me (add-on build)  ·  " + Describe(path, _main.S.ReShadeSha, false);
            return store.ReShade is { } r ? $"Downloaded from reshade.me on first install (add-on build {r.Tag})" : "Downloaded from reshade.me on first install";
        }
    }

    public bool ReShadeImported => _main.S.Store.ReShadeImported;

    public string? LecramMessage => _main.S.Store.SfLecramMessage(_main.S.Store.RenoDxImported, _main.S.Store.LecramImported, Settings.RenoDxId, Settings.LecramId);
    public bool HasLecramMessage => LecramMessage is not null;
    public bool IsPairCrashed => _main.S.Store.PairTestFor(Settings.RenoDxId, Settings.LecramId)?.Status == AddonPairStatus.Crashed;

    public void RefreshAddonPairStatus()
    {
        OnPropertyChanged(nameof(LecramMessage));
        OnPropertyChanged(nameof(HasLecramMessage));
        OnPropertyChanged(nameof(IsPairCrashed));
    }

    /// <summary>Hides the options the default Lecram build's generation doesn't have and switches the rest to its values.</summary>
    public void ApplyLecramGeneration()
    {
        var generation = _main.S.Store.LecramBuildFor(null)?.Generation;
        foreach (var o in LecramOptions)
            o.ApplyGeneration(generation);
    }

    // ---------- add-on libraries ----------

    /// <summary>Runs after an import, rename, removal or new default: games, options and warnings follow.</summary>
    private async Task AddonsChanged()
    {
        await _main.RefreshLocalComponentsAsync();
        ReloadAddons();
    }

    private void ReloadAddons()
    {
        RenoDx.Reload();
        Lecram.Reload();
        ApplyLecramGeneration();
        RefreshAddonPairStatus();
    }

    public string DfcInfo
    {
        get
        {
            var store = _main.S.Store;
            return store.DfcImported
                ? $"Imported  ·  {store.DfcVersion}  ·  {HashCache.Get(store.DfcPath)[..12]}" +
                  (DfcWarning is null ? "" : $"  ·  not the tested version ({DfcConfig.TestedVersion}, config schema {DfcConfig.TestedSchema}); see the log")
                : "Not imported. Its licence is personal use only, so import your own copy: choose deep-fried-chicken.addon64 in the extracted package's 64-bit folder (the files next to it are imported too). 32-bit games are not supported.";
        }
    }

    /// <summary>Hides the DFC options its imported default cfg doesn't have (a different config schema).</summary>
    public void ApplyDfcCompat()
    {
        var missing = _main.S.Store.DfcCompat(DfcOptions.Select(o => o.Key))?.MissingKeys ?? [];
        foreach (var o in DfcOptions.Concat(GameOptionViewModel.Definitions.Dfc))
            o.IsAvailable = !missing.Contains(o.Key, StringComparer.OrdinalIgnoreCase);
    }

    public string? DfcWarning => _main.S.Store.DfcCompat(DfcOptions.Select(o => o.Key)) is { HasIssues: true } r ? r.Summary : null;

    public bool DfcImported => _main.S.Store.DfcImported;

    [RelayCommand]
    private Task ImportDfc() => Import(ComponentStore.DfcFile, "Deep Fried Chicken|deep-fried-chicken.addon64;*.addon64");

    [RelayCommand]
    private async Task RemoveDfc()
    {
        try { _main.S.Store.RemoveDfcImport(); }
        catch (IOException ex) { Log.Error("Could not remove the imported Deep Fried Chicken files", ex); }
        Log.Info($"Removed imported {ComponentStore.DfcFile}");
        await _main.RefreshLocalComponentsAsync();
        ApplyDfcCompat();
        OnPropertyChanged(nameof(DfcInfo));
        OnPropertyChanged(nameof(DfcImported));
    }

    public bool AutoDownloadReShade
    {
        get => Settings.AutoDownloadReShade;
        set
        {
            Settings.AutoDownloadReShade = value;
            Save();
            _ = RefreshReShadeAsync();
        }
    }

    /// <summary>Drops the imported ReShade64.dll so installs use the automatic download again.</summary>
    [RelayCommand]
    private async Task UseReShadeDownload()
    {
        try { File.Delete(_main.S.Store.ReShadePath); }
        catch (IOException ex) { Log.Error("Could not remove the imported ReShade64.dll", ex); }
        Log.Info("Removed imported ReShade64.dll; installs use the reshade.me download");
        await RefreshReShadeAsync();
    }

    private async Task RefreshReShadeAsync()
    {
        await _main.RefreshLocalComponentsAsync();
        OnPropertyChanged(nameof(ReShadeInfo));
        OnPropertyChanged(nameof(ReShadeImported));
        OnPropertyChanged(nameof(AutoDownloadReShade));
    }

    private static string Describe(string path, string? sha, bool known)
    {
        if (!File.Exists(path) || sha is null) return "Not imported";
        var v = FileUtil.Format(FileUtil.ReadVersion(path));
        var size = new FileInfo(path).Length / 1048576.0;
        var variant = known ? (ComponentStore.KnownDlssNr.TryGetValue(sha, out var n) ? n : "Unverified build") : null;
        return $"{v}  ·  {size:0.0} MB  ·  SHA-256 {sha[..12]}…" + (variant is null ? "" : $"\n{variant}");
    }

    private void Save() => Settings.Save();

    /// <summary>Writes the override lists back to settings (called when the panel closes).</summary>
    public void Commit()
    {
        Capturing = null;
        Settings.IniOverrides = Clean(Overrides);
        Settings.ReShadeOverrides = Clean(ReShadeOverrides);
        Save();

        static List<IniOverride> Clean(IEnumerable<IniOverride> list) =>
            list.Where(o => !string.IsNullOrWhiteSpace(o.Section) && !string.IsNullOrWhiteSpace(o.Key)).ToList();
    }

    // ---------- keybinds ----------

    [RelayCommand]
    private void Capture(KeybindViewModel bind)
    {
        if (Capturing is not null) Capturing.Capturing = false;
        Capturing = bind;
        bind.Capturing = true;
    }

    /// <summary>Called by the window while a bind is capturing. Returns true when the key was consumed.</summary>
    public bool HandleKey(int vk, bool ctrl, bool shift, bool alt)
    {
        if (Capturing is not { } bind) return false;
        if (vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C) return true; // wait for a real key
        if (vk == 0x1B) // Esc cancels
        {
            bind.Capturing = false;
            Capturing = null;
            return true;
        }
        bind.Set(bind.Def.ReShade ? new Hotkey(vk, ctrl, shift, alt) : new Hotkey(vk));
        Capturing = null;
        return true;
    }

    [RelayCommand]
    private void ResetBind(KeybindViewModel bind) { bind.Set(null); Capturing = null; }

    [RelayCommand]
    private void DisableBind(KeybindViewModel bind) { bind.Set(Hotkey.None); Capturing = null; }

    // ---------- overrides ----------

    [RelayCommand]
    private void AddOverride() => Overrides.Add(new IniOverride("", "", ""));

    [RelayCommand]
    private void AddReShadeOverride() => ReShadeOverrides.Add(new IniOverride("", "", ""));

    [RelayCommand]
    private void RemoveOverride(IniOverride o)
    {
        if (!Overrides.Remove(o)) ReShadeOverrides.Remove(o);
        RefreshEditors();
    }

    [RelayCommand]
    private void ResetAll()
    {
        if (!Views.Dialog.Confirm("Reset to defaults",
                "Reset all OptiScaler.ini and ReShade.ini settings, keybinds and options to the upstream defaults (what OptiScaler-NR, ReShade and MFG Unlock ship with)? " +
                "Saved presets are kept; load '" + ConfigProfile.RecommendedName + "' for the tuned setup.", "Reset", danger: true))
            return;
        ResetOverrides();
        _reloading = true;
        SelectedPreset = null;
        _reloading = false;
    }

    [RelayCommand]
    private void ResetOverrides()
    {
        Overrides.Clear();
        foreach (var o in ConfigProfile.Defaults()) Overrides.Add(o);
        ReShadeOverrides.Clear();
        foreach (var o in ConfigProfile.ReShadeDefaults()) ReShadeOverrides.Add(o);
        Commit();
        RefreshEditors();
    }

    private void RefreshEditors()
    {
        foreach (var k in OptiKeybinds.Concat(ReShadeKeybinds)) k.Refresh();
        foreach (var o in AllOptions) o.Refresh();
    }

    // ---------- components / folders ----------

    [RelayCommand]
    private Task ImportDlssNr() => Import(ComponentStore.DlssNrFile, "nvngx_dlssnr.dll|nvngx_dlssnr.dll|DLL|*.dll");

    [RelayCommand]
    private async Task ImportDlss()
    {
        var dlg = new OpenFileDialog { Title = "Import DLSS SR / RR / FG", Filter = "DLSS runtimes|nvngx_dlss.dll;nvngx_dlssd.dll;nvngx_dlssg.dll" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var release = await Task.Run(() => _main.S.Store.ImportDlss(dlg.FileName));
            Settings.DlssTag = release.Tag;
            Save();
            Reload();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { Log.Error("DLSS import failed", ex); Views.Dialog.Show("Import failed", UserErrors.Message(ex)); }
    }

    [RelayCommand]
    private Task ImportReShade() => Import(ComponentStore.ReShadeFile, "ReShade (add-on build)|ReShade64.dll;dxgi.dll;*.dll");

    private async Task Import(string fileName, string filter, string? what = null)
    {
        var dlg = new OpenFileDialog { Filter = filter, Title = $"Select {what ?? fileName}" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await Task.Run(() => _main.S.Store.Import(dlg.FileName, fileName));
            if (fileName == ComponentStore.DlssNrFile)
            {
                var id = FileUtil.Sha256(dlg.FileName);
                Settings.SelectDlssNr(id);
                Save();
            }
            Reload();
            await _main.RefreshLocalComponentsAsync();
            ApplyDfcCompat();
            if (fileName == ComponentStore.DfcFile && DfcWarning is { } warning)
                Views.Dialog.Show("Deep Fried Chicken config check", warning + Environment.NewLine + Environment.NewLine + "Settings that still exist are applied normally.");
            OnPropertyChanged(nameof(DlssNrInfo));
            OnPropertyChanged(nameof(ReShadeInfo));
            OnPropertyChanged(nameof(ReShadeImported));
            OnPropertyChanged(nameof(DfcInfo));
            OnPropertyChanged(nameof(DfcImported));
            OnPropertyChanged(nameof(SelectedNrHint));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { Log.Error($"Import of {fileName} failed", ex); Views.Dialog.Show("Import failed", UserErrors.Message(ex)); }
    }

    [RelayCommand]
    private void RemoveFolder(FolderEntry f)
    {
        Folders.Remove(f);
        Settings.ManualGames.RemoveAll(p => p.Equals(f.Path, StringComparison.OrdinalIgnoreCase));
        Settings.LibraryRoots.RemoveAll(p => p.Equals(f.Path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    [RelayCommand]
    private void ShowHidden()
    {
        Settings.HiddenGames.Clear();
        Save();
        _main.RescanCommand.Execute(null);
    }

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true });

    public void Reload()
    {
        Folders.Clear();
        foreach (var p in Settings.ManualGames) Folders.Add(new FolderEntry(p, "Game"));
        foreach (var p in Settings.LibraryRoots) Folders.Add(new FolderEntry(p, "Library"));

        ReloadPresets(SelectedPreset is { } sp && Settings.AllPresets.Any(p => p.Name == sp) ? sp : null);

        NrChoices.Clear();
        foreach (var nr in _main.S.Store.DlssNrVersions) NrChoices.Add(new DlssChoice(nr.Id, nr.Label));
        DlssChoices.Clear();
        var store = _main.S.Store;
        DlssChoices.Add(new DlssChoice(null, $"Latest{(store.Dlss is { } l ? " · " + l.Tag : "")}"));
        foreach (var r in store.DlssReleases.Concat(ComponentStore.CachedReleases(Component.Dlss)).DistinctBy(r => r.Tag)) DlssChoices.Add(new DlssChoice(r.Tag, r.Tag));
        if (Settings.DlssTag is { } t && DlssChoices.All(c => c.Tag != t)) DlssChoices.Add(new DlssChoice(t, t));

        ReloadAddons();
        RefreshEditors();
        OnPropertyChanged(string.Empty);
    }
}

public sealed record FolderEntry(string Path, string Kind);
