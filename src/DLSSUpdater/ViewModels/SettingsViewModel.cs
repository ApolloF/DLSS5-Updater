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
        Dfc = new AddonLibraryViewModel(main, AddonKind.Dfc, AddonsChanged);
        ReShade = new ReShadeSettingsViewModel(main, AddonsChanged);
        Extras = new ReShadeExtrasSettingsViewModel(main.S);
        FrameLimit = new FrameLimitSettingsViewModel(main, AddonsChanged);
        Feeder = new FeederSettingsViewModel(main, AddonsChanged);
        Dxvk = new DxvkSettingsViewModel(main, AddonsChanged);
        Cleanup = new BuildCleanupViewModel(main);
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
    public ReShadeSettingsViewModel ReShade { get; }
    public ReShadeExtrasSettingsViewModel Extras { get; }
    public FrameLimitSettingsViewModel FrameLimit { get; }
    public FeederSettingsViewModel Feeder { get; }
    public DxvkSettingsViewModel Dxvk { get; }
    public AddonLibraryViewModel Lecram { get; }
    public AddonLibraryViewModel Dfc { get; }
    public BuildCleanupViewModel Cleanup { get; }
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

    public IReadOnlyList<ModeChoice> Modes => ModeChoice.For(OptiBuild);

    /// <summary>Name of the selected OptiScaler build; the Settings tab with its options carries it.</summary>
    public string OptiName => OptiBuild.Name;

    /// <summary>The selected build changed: rename the tab and the default-mode entry.</summary>
    public void RefreshBuildName()
    {
        OnPropertyChanged(nameof(OptiName));
        OnPropertyChanged(nameof(Modes));
        OnPropertyChanged(nameof(DefaultMode));
    }

    public ModeChoice DefaultMode
    {
        get => Modes.First(m => m.Mode == Settings.DefaultMode);
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
            // A default build of the other source would install that source; the new one follows its newest release.
            if (Settings.OptiTag is { } tag && OptiSources.OfTag(tag) != value) Settings.OptiTag = null;
            Save();
            ReloadReleaseChoices();
            OnPropertyChanged();
            _main.OptiSourceChanged();
        }
    }

    // ---------- default OptiScaler and GITC Uplift builds ----------

    public ObservableCollection<DlssChoice> OptiVersionChoices { get; } = [];
    public ObservableCollection<DlssChoice> UpliftVersionChoices { get; } = [];
    private bool _reloadingReleases;

    /// <summary>The default OptiScaler build: the newest release of the selected source, or a kept build of either source.</summary>
    public DlssChoice? SelectedOptiVersion
    {
        get => OptiVersionChoices.FirstOrDefault(c => c.Tag == Settings.OptiTag) ?? OptiVersionChoices.FirstOrDefault();
        set
        {
            if (_reloadingReleases || value is null || value.Tag == Settings.OptiTag) return;
            Settings.OptiTag = value.Tag;
            // A build of the other source makes that source the selected one, so names, options and checks follow it.
            if (value.Tag is { } tag && OptiSources.OfTag(tag) is var source && source != OptiBuild)
            {
                Settings.OptiSource = source.Repo;
                OnPropertyChanged(nameof(OptiBuild));
                _main.OptiSourceChanged();
            }
            Save();
            _ = _main.RefreshLocalComponentsAsync();
        }
    }

    public DlssChoice? SelectedUpliftVersion
    {
        get => UpliftVersionChoices.FirstOrDefault(c => c.Tag == Settings.UpliftTag) ?? UpliftVersionChoices.FirstOrDefault();
        set
        {
            if (_reloadingReleases || value is null || value.Tag == Settings.UpliftTag) return;
            Settings.UpliftTag = value.Tag;
            Save();
            _ = _main.RefreshLocalComponentsAsync();
        }
    }

    /// <summary>"Newest (…)" first, then the kept builds newest first; a default whose build is gone stays listed so it can be changed.</summary>
    public void ReloadReleaseChoices()
    {
        _reloadingReleases = true;
        var store = _main.S.Store;
        Fill(OptiVersionChoices, store.Library(LibraryKind.OptiScaler), GameViewModel.NewestOptiLabel(store), Settings.OptiTag);
        Fill(UpliftVersionChoices, store.Library(LibraryKind.Uplift), GameViewModel.NewestUpliftLabel(store), Settings.UpliftTag);
        _reloadingReleases = false;
        OnPropertyChanged(nameof(SelectedOptiVersion));
        OnPropertyChanged(nameof(SelectedUpliftVersion));

        static void Fill(ObservableCollection<DlssChoice> list, IReadOnlyList<LibraryBuild> builds, string newest, string? chosen)
        {
            list.Clear();
            list.Add(new DlssChoice(null, char.ToUpperInvariant(newest[0]) + newest[1..]));
            foreach (var b in builds) list.Add(new DlssChoice(b.Id, b.Label));
            if (chosen is not null && list.All(c => c.Tag != chosen)) list.Add(new DlssChoice(chosen, $"{chosen} · not downloaded"));
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

    /// <summary>Per update check: fetch ShortFuse's DLSS add-on from the catalog when it has a newer build.</summary>
    public bool AutoDownloadRenoDx
    {
        get => Settings.AutoDownloadRenoDx;
        set { Settings.AutoDownloadRenoDx = value; Save(); }
    }

    /// <summary>The HDR peak brightness as typed: empty = off, else a number of nits kept between the limits the mod takes.</summary>
    public string HdrPeakText
    {
        get => Settings.HdrPeakNits?.ToString() ?? "";
        set
        {
            if (TryParsePeak(value, out var nits))
            {
                Settings.HdrPeakNits = nits;
                Save();
            }
            OnPropertyChanged();
        }
    }

    /// <summary>"" = off (null). A number is clamped to 80..10000; anything else is rejected and the old value stays.</summary>
    internal static bool TryParsePeak(string? text, out int? nits)
    {
        nits = null;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return true;
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)) return false;
        nits = HdrIni.ClampPeak(n);
        return true;
    }

    public bool InstallRenoDx
    {
        get => Settings.InstallRenoDx;
        set => SetNrAddon(NrAddon.RenoDx, value);
    }

    public bool InstallUplift
    {
        get => Settings.InstallUplift;
        set => SetNrAddon(NrAddon.Uplift, value);
    }

    public bool InstallLecram
    {
        get => Settings.InstallLecram;
        set => SetNrAddon(NrAddon.Lecram, value);
    }

    private void SetNrAddon(NrAddon addon, bool on)
    {
        if (on)
        {
            NrAddons.SetDefault(Settings, addon, true);
            if (NrAddons.DefaultConflicts(Settings) is { Count: > 0 } clash
                && !Views.Dialog.Confirm("Check before installing",
                    string.Join(Environment.NewLine + Environment.NewLine, clash) + Environment.NewLine + Environment.NewLine + "You can keep them all on anyway, for example to compare. The warning stays here and on the games.",
                    "Keep both on", danger: true))
                NrAddons.SetDefault(Settings, addon, false);
        }
        else NrAddons.SetDefault(Settings, addon, false);
        Save();
        NotifyNrAddons();
    }

    private void NotifyNrAddons()
    {
        OnPropertyChanged(nameof(InstallRenoDx));
        OnPropertyChanged(nameof(InstallUplift));
        OnPropertyChanged(nameof(InstallLecram));
        OnPropertyChanged(nameof(NrAddonDefaultsWarning));
        OnPropertyChanged(nameof(HasNrAddonDefaultsWarning));
    }

    public string? NrAddonDefaultsWarning => NrAddons.DefaultConflicts(Settings) is { Count: > 0 } c ? string.Join(Environment.NewLine, c) : null;
    public bool HasNrAddonDefaultsWarning => NrAddonDefaultsWarning is not null;

    public string DlssNrInfo => _main.S.DlssNrSha is null && !File.Exists(_main.S.Store.DlssNrPath)
        ? "Not in the library yet; it is downloaded at the next update check."
        : Describe(_main.S.Store.DlssNrPath, _main.S.DlssNrSha, true);

    /// <summary>Per update check: download nvngx_dlssnr.dll from the catalog when it has a build the library lacks.</summary>
    public bool AutoDownloadDlssNr
    {
        get => Settings.AutoDownloadDlssNr;
        set { Settings.AutoDownloadDlssNr = value; Save(); }
    }
    public string? LecramMessage => _main.S.Store.SfLecramMessage(_main.S.Store.RenoDxImported, _main.S.Store.LecramImported, Settings.RenoDxId, Settings.LecramId);
    public bool HasLecramMessage => LecramMessage is not null;

    public void RefreshAddonPairStatus()
    {
        OnPropertyChanged(nameof(LecramMessage));
        OnPropertyChanged(nameof(HasLecramMessage));
    }

    /// <summary>Hides the options the default Lecram build doesn't have and switches the rest to its generation's values.</summary>
    public void ApplyLecramGeneration()
    {
        var build = _main.S.Store.LecramBuildFor(null);
        foreach (var o in LecramOptions)
            o.ApplyGeneration(build?.Generation, build?.DetectedLabel);
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
        Dfc.Reload();
        ReShade.Reload();
        FrameLimit.Reload();
        Feeder.Reload();
        Dxvk.Reload();
        ApplyLecramGeneration();
        ApplyDfcCompat();
        RefreshAddonPairStatus();
    }

    /// <summary>Hides the DFC options the default build's cfg doesn't have (a different config schema); a game uses its own build.</summary>
    public void ApplyDfcCompat()
    {
        var missing = _main.S.Store.DfcCompat(DfcOptions.Select(o => o.Key))?.MissingKeys ?? [];
        foreach (var o in DfcOptions)
            o.IsAvailable = !missing.Contains(o.Key, StringComparer.OrdinalIgnoreCase);
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

    // ---------- first-run setup ----------

    /// <summary>The open first-run setup; null while it is closed.</summary>
    [ObservableProperty] private OnboardingViewModel? _onboarding;

    /// <summary>Opens the setup on a fresh install (once the window is up).</summary>
    public void ShowOnboardingIfDue()
    {
        if (OnboardingViewModel.ShouldShow(Settings, Demo.Active, Demo.ShowOnboarding)) RunSetup();
    }

    /// <summary>"Run setup again": the same steps, starting at the current settings.</summary>
    [RelayCommand]
    private void RunSetup()
    {
        if (Onboarding is not null) return;
        var setup = new OnboardingViewModel(Settings);
        setup.Closed += OnboardingClosed;
        Onboarding = setup;
    }

    private void OnboardingClosed(OnboardingResult result)
    {
        Onboarding = null;
        if (!result.Applied) return;
        if (result.Build is { } build) OptiBuild = build;
        OnPropertyChanged(nameof(DefaultMode));
        NotifyNrAddons();
        NotifyModShown();
        Lecram.Reload();
        ReShade.Reload();
        _main.SelfUpdate.SettingsChanged();
        Cleanup.SettingsChanged();
    }

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

    private async Task Import(string fileName, string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter, Title = $"Select {fileName}" };
        if (dlg.ShowDialog() == true) await ImportFrom(dlg.FileName, fileName);
    }

    private async Task ImportFrom(string source, string fileName)
    {
        try
        {
            await Task.Run(() => _main.S.Store.Import(source, fileName));
            if (fileName == ComponentStore.DlssNrFile)
            {
                var id = FileUtil.Sha256(source);
                Settings.SelectDlssNr(id);
                Save();
            }
            Reload();
            await _main.RefreshLocalComponentsAsync();
            OnPropertyChanged(nameof(DlssNrInfo));
            OnPropertyChanged(nameof(SelectedNrHint));
            if (fileName == ComponentStore.DlssNrFile) await _main.AutoCleanUpAfterImportAsync();
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
        _main.RequestRescan();
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
        ReloadReleaseChoices();
        _ = Cleanup.RefreshAsync();
        RefreshEditors();
        OnPropertyChanged(string.Empty);
    }
}

public sealed record FolderEntry(string Path, string Kind);
