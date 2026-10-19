using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.ViewModels;

public enum RowState { None, Current, Update, Missing }

public sealed partial class ComponentRow(string key, string name) : ObservableObject
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    [ObservableProperty] private bool _enabled = true;
    /// <summary>Hidden when it doesn't apply to the install mode (OptiScaler / DLSSNR in ReShade-only).</summary>
    [ObservableProperty] private bool _visible = true;
    /// <summary>False when the mode requires this component (OptiScaler in OptiScaler mode, ReShade in ReShade-only).</summary>
    [ObservableProperty] private bool _editable = true;
    [ObservableProperty] private string _installed = "—";
    [ObservableProperty] private string _latest = "—";
    [ObservableProperty] private RowState _state;
}

public sealed record DlssRow(string RelPath, string Kind, string Current, string Latest, RowState State);

/// <summary>A config base choice; Name null = the current settings. GameOnly presets belong to one game.</summary>
public sealed record PresetChoice(string? Name, bool GameOnly, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A DLSS version choice; Tag null = follow the default.</summary>
public sealed record DlssChoice(string? Tag, string Label)
{
    public override string ToString() => Label;
}

public sealed record ModeChoice(InstallMode Mode, string Label, string Description)
{
    public static readonly ModeChoice[] All =
    [
        new(InstallMode.OptiScaler, "OptiScaler-NR", "OptiScaler-NR as the proxy; it loads ReShade, add-ons and DLSSNR."),
        new(InstallMode.ReShadeOnly, "ReShade + add-ons", "ReShade as the proxy with MFG Unlock and DLSS updates, without OptiScaler."),
    ];

    public override string ToString() => Label;
}

public sealed record TargetOption(string Dir, string Label, string? Exe)
{
    public override string ToString() => Label;
}

/// <summary>Shared app state the per-game view models read from.</summary>
public sealed class Services
{
    public required AppSettings Settings { get; init; }
    public required ComponentStore Store { get; init; }
    public required Installer Installer { get; init; }
    public string? DlssNrSha { get; set; }
    public string? ReShadeSha { get; set; }
}

public sealed partial class GameViewModel : ObservableObject
{
    private readonly Services _s;
    private bool _loading;

    public GameViewModel(GameInfo info, Services services)
    {
        _s = services;
        Components =
        [
            new ComponentRow("opti", "OptiScaler-NR"),
            new ComponentRow("reshade", "ReShade"),
            new ComponentRow("mfg", "MFG Unlock"),
            new ComponentRow("dlssnr", "DLSSNR runtime"),
            new ComponentRow("renodx", "ShortFuse RenoDX DLSS add-on (own NR pass; replaces OptiScaler NR)"),
            new ComponentRow("lecram", "Lecram DLSS5 add-on (own NR pass; with ShortFuse both passes run)"),
            new ComponentRow("uplift", "GITC Uplift (exclusive DLSS-NR route)"),
            new ComponentRow("dfc", "Deep Fried Chicken (replaces OptiScaler NR, RenoDX and Lecram)"),
            new ComponentRow("dlss", "DLSS SR · RR · FG"),
            new ComponentRow("streamline", "Streamline (Dynamic MFG)"),
        ];
        foreach (var c in Components) c.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ComponentRow.Enabled)) return;
            if (c.Key is "renodx" or "lecram" or "dfc" or "uplift" && c.Enabled)
            {
                Components.First(r => r.Key == "dlssnr").Enabled = true;
                Components.First(r => r.Key == "reshade").Enabled = true;
                // One NR provider per game; ShortFuse's and Lecram's add-ons are the pair that can run together.
                foreach (var other in Components.Where(r => r.Key is "renodx" or "lecram" or "dfc" or "uplift" && r.Key != c.Key && (c.Key is "dfc" or "uplift" || r.Key is "dfc" or "uplift")))
                    other.Enabled = false;
            }
            OnPropertyChanged(nameof(AnyEnabled));
            OnPropertyChanged(nameof(RenoDxEnabled));
            OnPropertyChanged(nameof(LecramEnabled));
            OnPropertyChanged(nameof(BothAddonsEnabled));
            RefreshAddonPairStatus();
            NotifyDfcHint();
            RefreshModPick();
        };
        Info = info;
        Load(info);
    }

    public GameInfo Info { get; private set; }
    public string Id => Info.Id;
    public string Name => Info.Name;
    public string Source => Info.Source;
    public string Root => Info.Root;
    public bool IsManual => Info.Source is "Manual" or "Library";

    public ObservableCollection<TargetOption> Targets { get; } = [];
    public ObservableCollection<ComponentRow> Components { get; }
    public ObservableCollection<DlssRow> DlssRows { get; } = [];
    public IReadOnlyList<string> ProxyNames => IsReShadeOnly ? AppSettings.ReShadeProxyNames : AppSettings.ProxyNames;
    public IReadOnlyList<ModeChoice> Modes => ModeChoice.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReShadeOnly), nameof(ProxyNames), nameof(ModeChoice), nameof(ProxyHint))]
    private InstallMode _mode;

    public bool IsReShadeOnly => Mode == InstallMode.ReShadeOnly;
    public string ProxyHint => IsReShadeOnly
        ? "Files go next to the game executable. Proxy is the name ReShade is loaded as (dxgi.dll for DX10–12, d3d9.dll, opengl32.dll …)."
        : "Files go next to the game executable. Proxy is the name OptiScaler is loaded as.";

    public ModeChoice ModeChoice
    {
        get => ModeChoice.All.First(m => m.Mode == Mode);
        set
        {
            if (value is null) return;
            Mode = value.Mode;
        }
    }

    partial void OnModeChanged(InstallMode value)
    {
        ApplyModeToRows();
        if (IsReShadeOnly && !On("renodx") && !On("lecram") && !On("dfc") && !On("uplift")) Components.First(c => c.Key == "dlssnr").Enabled = false;
        if (!ProxyNames.Contains(Proxy, StringComparer.OrdinalIgnoreCase)) Proxy = "dxgi.dll";
        RefreshModPick();
        if (_loading) return;
        _s.Settings.For(Id).Mode = value == _s.Settings.DefaultMode ? null : value;
        _s.Settings.Save();
        RefreshStatus();
    }

    private void ApplyModeToRows()
    {
        foreach (var c in Components)
        {
            c.Visible = !(IsReShadeOnly && c.Key == "opti");
            // The loader of the mode is always installed: without it nothing else in the game loads.
            c.Editable = c.Key != (IsReShadeOnly ? "reshade" : "opti");
            if (!c.Editable) c.Enabled = true;
            // The add-on replaces OptiScaler's NR, so it exists in ReShade-only mode alone.
            if (c.Key is "renodx" or "lecram" or "dfc" or "uplift" && !IsReShadeOnly) { c.Visible = false; c.Enabled = false; }
        }
        OnPropertyChanged(nameof(AnyEnabled));
    }

    [ObservableProperty] private TargetOption? _selectedTarget;
    [ObservableProperty] private string _proxy = "dxgi.dll";
    [ObservableProperty] private InstallManifest? _manifest;
    [ObservableProperty] private DlssChoice? _dlssChoice;
    [ObservableProperty] private DlssChoice? _nrChoice;
    public ObservableCollection<DlssChoice> NrChoices { get; } = [];
    public string? EffectiveNrId => NrChoice?.Tag ?? _s.Settings.DlssNrId;
    public bool CanSwitchNr => Manifest?.DlssNr == true;
    public bool CanUndoNr => Manifest?.PreviousDlssNrId is not null;

    [ObservableProperty] private DlssChoice? _renoDxChoice;
    [ObservableProperty] private DlssChoice? _lecramChoice;
    public ObservableCollection<DlssChoice> RenoDxChoices { get; } = [];
    public ObservableCollection<DlssChoice> LecramChoices { get; } = [];
    /// <summary>The ShortFuse build this game installs: its own pick, else the default of the settings.</summary>
    public string? EffectiveRenoDxId => RenoDxChoice?.Tag ?? _s.Settings.RenoDxId;
    public string? EffectiveLecramId => LecramChoice?.Tag ?? _s.Settings.LecramId;
    /// <summary>Generation of the Lecram build this game uses; it decides which Lecram options apply.</summary>
    public LecramGeneration? EffectiveLecramGeneration => _s.Store.LecramBuildFor(EffectiveLecramId)?.Generation;
    public bool RenoDxEnabled => On("renodx");
    public bool LecramEnabled => On("lecram");
    public bool BothAddonsEnabled => RenoDxEnabled && LecramEnabled;

    public string? SfLecramMessage => _s.Store.SfLecramMessage(RenoDxEnabled, LecramEnabled, EffectiveRenoDxId, EffectiveLecramId);
    public bool HasSfLecramMessage => SfLecramMessage is not null;
    public AddonPairStatus PairStatus => _s.Store.PairTestFor(EffectiveRenoDxId, EffectiveLecramId)?.Status ?? AddonPairStatus.Untested;
    public bool IsPairCrashed => PairStatus == AddonPairStatus.Crashed;
    public bool CanMarkAddonPair => BothAddonsEnabled && _s.Store.RenoDxBuildFor(EffectiveRenoDxId) is not null && _s.Store.LecramBuildFor(EffectiveLecramId) is not null;

    public void RefreshAddonPairStatus()
    {
        OnPropertyChanged(nameof(SfLecramMessage));
        OnPropertyChanged(nameof(HasSfLecramMessage));
        OnPropertyChanged(nameof(PairStatus));
        OnPropertyChanged(nameof(IsPairCrashed));
        OnPropertyChanged(nameof(CanMarkAddonPair));
        MarkPairWorkingCommand.NotifyCanExecuteChanged();
        MarkPairCrashedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMarkAddonPair))]
    private void MarkPairWorking() => MarkAddonPair(AddonPairStatus.Works);

    [RelayCommand(CanExecute = nameof(CanMarkAddonPair))]
    private void MarkPairCrashed() => MarkAddonPair(AddonPairStatus.Crashed);

    private void MarkAddonPair(AddonPairStatus status)
    {
        try
        {
            _s.Store.MarkAddonPair(_s.Settings, EffectiveRenoDxId, EffectiveLecramId, status);
            RefreshAddonPairStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log.Error("Could not mark the ShortFuse + Lecram pair", ex); Views.Dialog.Show("Could not mark pair", ex.Message); }
    }

    /// <summary>Deep Fried Chicken is ticked for a game that ships no DLSS of its own (copies an install of ours added don't count).</summary>
    public bool DfcWithoutNativeDlss => IsReShadeOnly && On("dfc") && !Installer.HasNativeDlss(Info, Manifest, SelectedTarget?.Dir ?? Root);
    /// <summary>The imported DFC is older than 4.x and the installer will refuse this game; null otherwise.</summary>
    public string? DfcNoDlssWarning => DfcWithoutNativeDlss && _s.Store.DfcImported && !_s.Store.DfcSupportsNonDlssGames
        ? Installer.DfcNeedsNativeDlss(_s.Store.DfcVersion) : null;
    public bool HasDfcNoDlssWarning => DfcNoDlssWarning is not null;
    public bool ShowDfcNoDlssHint => DfcWithoutNativeDlss && !HasDfcNoDlssWarning;
    public const string DfcNoDlssHint =
        "No DLSS in this game: Deep Fried Chicken 4.x runs NR on its own motion vectors (its motion model is in the deep-fried-chicken-neural folder) or an external ReShade motion provider's, on top of the game's anti-aliasing. " +
        "There is no DLSS upscaling (4.x removed DFC's own SR). nvngx_dlss.dll from NVIDIA's DLSS release is added beside the exe. " +
        "Frame generation (DFC overlay, Rendering card) needs borderless or windowed mode; DLSS-G needs an RTX 40/50 GPU.";

    /// <summary>Per game: place NVIDIA's nvngx_dlssg.dll for DFC's DLSS-G frame generation when the game has no DLSS of its own.</summary>
    public bool DfcFrameGen
    {
        get => _s.Settings.Games.GetValueOrDefault(Id)?.DfcFrameGen == true;
        set
        {
            _s.Settings.For(Id).DfcFrameGen = value ? true : null;
            _s.Settings.Save();
            OnPropertyChanged();
        }
    }

    private void NotifyDfcHint()
    {
        OnPropertyChanged(nameof(DfcWithoutNativeDlss));
        OnPropertyChanged(nameof(DfcNoDlssWarning));
        OnPropertyChanged(nameof(HasDfcNoDlssWarning));
        OnPropertyChanged(nameof(ShowDfcNoDlssHint));
    }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalPresetSelected))]
    private PresetChoice? _presetChoice;
    [ObservableProperty] private string _detailTab = "Install";
    [ObservableProperty] private DriverProfileViewModel? _driver;

    /// <summary>The exe the driver profile is matched on.</summary>
    public string? DriverExe => SelectedTarget?.Exe ?? Info.Exes.FirstOrDefault();

    partial void OnDetailTabChanged(string value)
    {
        if (value == "Config") EnsureConfigRows();
        if (value != "Nvidia") return;
        if (Driver is null && DriverExe is { } exe) Driver = new DriverProfileViewModel(exe, Name, Features, Info.FgVersion);
        if (Driver is { Loaded: false, Busy: false } d) d.LoadCommand.Execute(null);
    }

    public const string GlobalPreset = "Current settings";
    public ObservableCollection<PresetChoice> PresetChoices { get; } = [];
    public bool IsLocalPresetSelected => PresetChoice is { GameOnly: true };

    public void RebuildPresetChoices()
    {
        var wasLoading = _loading;
        _loading = true;
        PresetChoices.Clear();
        PresetChoices.Add(new PresetChoice(null, false, GlobalPreset));
        foreach (var p in _s.Settings.AllPresets) PresetChoices.Add(new PresetChoice(p.Name, false, p.Name));
        foreach (var p in GameConfig.LocalPresets(_s.Settings, Id)) PresetChoices.Add(new PresetChoice(p.Name, true, p.Name + " · this game"));
        var current = _s.Settings.PresetFor(Id);
        var local = current is not null && GameConfig.LocalPresets(_s.Settings, Id).Contains(current);
        PresetChoice = current is null ? PresetChoices[0] : PresetChoices.First(c => c.Name == current.Name && c.GameOnly == local);
        _loading = wasLoading;
    }

    partial void OnPresetChoiceChanged(PresetChoice? value)
    {
        if (_loading || value is null) return;
        _s.Settings.For(Id).Preset = value.Name;
        _s.Settings.Save();
        RefreshConfigRows();
    }

    public IReadOnlyList<OptionChoice> IniModeChoices { get; private set; } = [];

    /// <summary>How this game treats its existing config files; the first choice follows Settings.</summary>
    public OptionChoice IniModeChoice
    {
        get => IniModeChoices.FirstOrDefault(c => c.Value == _s.Settings.Games.GetValueOrDefault(Id)?.IniMode) ?? IniModeChoices[0];
        set
        {
            if (value is null) return;
            _s.Settings.For(Id).IniMode = value.Value;
            _s.Settings.Save();
            OnPropertyChanged();
        }
    }

    private void RebuildIniModeChoices()
    {
        var global = SettingsOptions.IniModes.FirstOrDefault(m => m.Value == _s.Settings.IniMode) ?? SettingsOptions.IniModes[0];
        IniModeChoices = [new OptionChoice($"Default ({global.Label})", null, "Follows Settings: " + global.Description), .. SettingsOptions.IniModes];
        OnPropertyChanged(nameof(IniModeChoices));
        OnPropertyChanged(nameof(IniModeChoice));
    }

    private ModChoice? _pickedMod;

    private IReadOnlyList<ModChoice> _modChoices = ModPicker.Available(false);
    public IReadOnlyList<ModChoice> ModChoices => _modChoices;

    /// <summary>The mod whose options the Game config tab shows. Follows the NR route in use until the user picks one; not saved.</summary>
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

    private Mod DefaultMod => ModPicker.DefaultFor(IsReShadeOnly, On("uplift"), On("dfc"), On("lecram"), On("renodx"));

    public bool ShowOptiNr => ModPicker.Shows(ModSelection.Mod, Mod.OptiScalerNr);
    public bool ShowReShade => ModPicker.Shows(ModSelection.Mod, Mod.ReShade);
    public bool ShowMfg => ModPicker.Shows(ModSelection.Mod, Mod.Mfg);
    public bool ShowRenoDx => IsReShadeOnly && ModPicker.Shows(ModSelection.Mod, Mod.RenoDx);
    public bool ShowLecram => IsReShadeOnly && ModPicker.Shows(ModSelection.Mod, Mod.Lecram);
    public bool ShowDfc => IsReShadeOnly && ModPicker.Shows(ModSelection.Mod, Mod.Dfc);
    public bool ShowUplift => IsReShadeOnly && ModPicker.Shows(ModSelection.Mod, Mod.Uplift);

    /// <summary>Mode or ticks changed: keep the user's pick while that mod is still listed; otherwise follow the route.</summary>
    private void RefreshModPick()
    {
        if (_modChoices.Count != ModPicker.Available(IsReShadeOnly).Count)
        {
            _modChoices = ModPicker.Available(IsReShadeOnly);
            OnPropertyChanged(nameof(ModChoices));
        }
        if (_pickedMod is not null && !ModChoices.Contains(_pickedMod)) _pickedMod = null;
        NotifyModShown();
    }

    private void NotifyModShown()
    {
        OnPropertyChanged(nameof(ModSelection));
        OnPropertyChanged(nameof(ShowOptiNr));
        OnPropertyChanged(nameof(ShowReShade));
        OnPropertyChanged(nameof(ShowMfg));
        OnPropertyChanged(nameof(ShowRenoDx));
        OnPropertyChanged(nameof(ShowLecram));
        OnPropertyChanged(nameof(ShowDfc));
        OnPropertyChanged(nameof(ShowUplift));
    }

    public ObservableCollection<GameOptionViewModel> ReShadeRows { get; } = [];
    public ObservableCollection<GameOptionViewModel> MfgRows { get; } = [];
    public ObservableCollection<GameOptionViewModel> RenoDxRows { get; } = [];
    public ObservableCollection<GameOptionViewModel> LecramRows { get; } = [];
    public ObservableCollection<GameOptionViewModel> UpliftRows { get; } = [];
    public ObservableCollection<GameOptionViewModel> DfcRows { get; } = [];
    public ObservableCollection<GameOptionViewModel> NrRows { get; } = [];
    public ObservableCollection<GameOptionViewModel> TonemapRows { get; } = [];
    public ObservableCollection<GameOptionViewModel> HdrRows { get; } = [];
    private IEnumerable<GameOptionViewModel> ConfigRows => ReShadeRows.Concat(MfgRows).Concat(RenoDxRows).Concat(LecramRows).Concat(UpliftRows).Concat(DfcRows).Concat(NrRows).Concat(TonemapRows).Concat(HdrRows);
    private bool _rowsBuilt;

    /// <summary>Built when the Game config tab first opens: most games never need them.</summary>
    private void EnsureConfigRows()
    {
        if (_rowsBuilt) return;
        _rowsBuilt = true;
        Fill(ReShadeRows, GameOptionViewModel.Definitions.ReShade, true);
        Fill(MfgRows, GameOptionViewModel.Definitions.Mfg, true);
        Fill(RenoDxRows, GameOptionViewModel.Definitions.RenoDx, true);
        Fill(LecramRows, GameOptionViewModel.Definitions.Lecram, true, () => EffectiveLecramGeneration);
        Fill(UpliftRows, GameOptionViewModel.Definitions.Uplift, true);
        Fill(DfcRows, GameOptionViewModel.Definitions.Dfc, true);
        Fill(NrRows, GameOptionViewModel.Definitions.Nr, false);
        Fill(TonemapRows, GameOptionViewModel.Definitions.Tonemap, false);
        Fill(HdrRows, GameOptionViewModel.Definitions.HdrOutput, false);

        void Fill(ObservableCollection<GameOptionViewModel> rows, IEnumerable<IniOptionViewModel> defs, bool reShade, Func<LecramGeneration?>? generation = null)
        {
            foreach (var d in defs) rows.Add(new GameOptionViewModel(d, reShade, _s.Settings, Id, generation));
        }
    }

    private void RefreshConfigRows()
    {
        foreach (var r in ConfigRows) r.Refresh();
    }

    /// <summary>Presets or global settings changed: rebuild what the Game config tab shows.</summary>
    public void RefreshConfig()
    {
        RebuildPresetChoices();
        RebuildIniModeChoices();
        RefreshConfigRows();
    }

    public ObservableCollection<DlssChoice> DlssChoices { get; } = [];
    public bool HasStreamline => Info.Streamline.Count > 0;

    /// <summary>Tag to install: this game's pin, else the global pin, else null (latest).</summary>
    public string? EffectiveDlssTag => DlssChoice?.Tag ?? _s.Settings.DlssTag;

    public bool HasDlss => Info.Dlss.Count > 0;

    /// <summary>What the game ships, plus DLSS SR when OptiScaler (which can run DLSS in any game) is installed.</summary>
    public GameFeatures Features => Info.Features | (IsOptiInstalled && HasDlssForOpti ? GameFeatures.SR : GameFeatures.None);
    private bool HasDlssForOpti => Manifest?.Files.Any(f => Path.GetFileName(f).Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>Short list for the game list / header, e.g. "RR · FG · Reflex" (SR is implied by the DLSS badge).</summary>
    public string FeatureBadge => string.Join(" · ", new[]
    {
        Info.Features.HasFlag(GameFeatures.RR) ? "RR" : null,
        Info.Features.HasFlag(GameFeatures.FG) ? Info.FgVersion is { Major: >= 310 } ? "FG/MFG" : "FG" : null,
        Info.Features.HasFlag(GameFeatures.Reflex) ? "Reflex" : null,
    }.OfType<string>());
    public bool HasFeatureBadge => FeatureBadge.Length > 0;
    public bool HasAntiCheat => Info.AntiCheat is not null;
    public string? AntiCheat => Info.AntiCheat;
    public bool IsInstalled => Manifest is not null || Info.Installs.Count > 0;
    public bool IsOptiInstalled => Manifest?.Opti == true;
    public string? ModBadge => Manifest?.ModLabel();
    public bool AnyEnabled => Components.Any(c => c.Enabled);

    public string DlssBadge
    {
        get
        {
            var v = Info.Dlss.Where(d => d.Kind == "SR").Select(d => d.Version).Max()
                    ?? Info.Dlss.Select(d => d.Version).Max();
            return v is null ? "DLSS" : $"DLSS {v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
        }
    }

    public bool DlssCurrent => LatestDlss is { } l && Info.Dlss.All(d => d.Version is { } v && (DlssPinned ? Trim(v) == l : Trim(v) >= l));

    [ObservableProperty] private bool _needsUpdate;

    public string ActionText => Manifest is null ? "Install" : NeedsUpdate ? "Update" : "Reinstall";
    public string Subtitle => Info.Exes.Count == 0 ? "No executable found" : Path.GetFileName(Info.Exes[0]);

    private Version? LatestDlss => (_s.Store.DlssFor(EffectiveDlssTag)?.Version) is { } v ? Trim(v) : null;
    private bool DlssPinned => EffectiveDlssTag is not null;
    private static Version Trim(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public void Load(GameInfo info)
    {
        if (Driver is not null && Driver.Features != Features) Driver = null; // rebuilt with the new scan when the tab opens
        Info = info;
        _pickedMod = null;
        _loading = true;
        var over = _s.Settings.Games.GetValueOrDefault(Id);

        Targets.Clear();
        foreach (var t in BuildTargets(info, over?.TargetDir)) Targets.Add(t);
        var preferred = info.Installs.FirstOrDefault() ?? over?.TargetDir;
        SelectedTarget = Targets.FirstOrDefault(t => preferred is not null && t.Dir.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                         ?? Targets.FirstOrDefault();

        RebuildDlssChoices();
        RebuildPresetChoices();
        RebuildIniModeChoices();
        _loading = false;
        LoadManifest();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>"Default" plus every DLSS SDK release; keeps the game's pin selected.</summary>
    public void RebuildDlssChoices()
    {
        var wasLoading = _loading;
        _loading = true;
        var pinned = _s.Settings.Games.GetValueOrDefault(Id)?.DlssTag;
        DlssChoices.Clear();
        var global = _s.Settings.DlssTag ?? $"latest{(_s.Store.Dlss is { } l ? " · " + l.Tag : "")}";
        DlssChoices.Add(new DlssChoice(null, $"Default ({global})"));
        foreach (var r in _s.Store.DlssReleases.Concat(ComponentStore.CachedReleases(Component.Dlss)).DistinctBy(r => r.Tag)) DlssChoices.Add(new DlssChoice(r.Tag, r.Tag));
        if (pinned is not null && DlssChoices.All(c => c.Tag != pinned)) DlssChoices.Add(new DlssChoice(pinned, pinned));
        DlssChoice = DlssChoices.FirstOrDefault(c => c.Tag == pinned) ?? DlssChoices[0];
        RebuildNrChoices();
        RebuildAddonChoices();
        _loading = wasLoading;
    }

    public void RebuildNrChoices()
    {
        var loading = _loading;
        _loading = true;
        var pin = _s.Settings.Games.GetValueOrDefault(Id)?.DlssNrId;
        NrChoices.Clear();
        var versions = _s.Store.DlssNrVersions;
        var global = _s.Settings.DlssNrId;
        var follows = versions.FirstOrDefault(v => v.Id == global) ?? (global is null ? versions.FirstOrDefault() : null);
        NrChoices.Add(new DlssChoice(null, $"Default ({follows?.Version ?? "none imported"})"));
        foreach (var nr in versions) NrChoices.Add(new DlssChoice(nr.Id, nr.Label));
        if (pin is not null && NrChoices.All(c => c.Tag != pin)) NrChoices.Add(new DlssChoice(pin, "Missing NR file · import again"));
        NrChoice = NrChoices.FirstOrDefault(c => c.Tag == pin) ?? NrChoices[0];
        _loading = loading;
    }

    /// <summary>The add-on dropdowns: "Use default" plus every library build.</summary>
    public void RebuildAddonChoices()
    {
        var loading = _loading;
        _loading = true;
        var over = _s.Settings.Games.GetValueOrDefault(Id);
        RenoDxChoice = FillAddonChoices(RenoDxChoices, AddonKind.RenoDx, over?.RenoDxId);
        LecramChoice = FillAddonChoices(LecramChoices, AddonKind.Lecram, over?.LecramId);
        _loading = loading;
        OnPropertyChanged(nameof(EffectiveLecramGeneration));
        RefreshAddonPairStatus();
        if (_rowsBuilt) RefreshConfigRows();
    }

    private DlssChoice FillAddonChoices(ObservableCollection<DlssChoice> list, AddonKind kind, string? pin)
    {
        var builds = _s.Store.AddonBuilds(kind);
        var global = _s.Settings.AddonId(kind);
        var follows = builds.FirstOrDefault(b => b.Id == global) ?? builds.FirstOrDefault();
        list.Clear();
        list.Add(new DlssChoice(null, $"Use default ({follows?.Label ?? "none imported"})"));
        foreach (var b in builds) list.Add(new DlssChoice(b.Id, b.Label));
        if (pin is not null && list.All(c => c.Tag != pin)) list.Add(new DlssChoice(pin, "Missing add-on file · import again"));
        return list.FirstOrDefault(c => c.Tag == pin) ?? list[0];
    }

    private void AddonPicksChanged()
    {
        _s.Settings.Save();
        OnPropertyChanged(nameof(EffectiveLecramGeneration));
        RefreshAddonPairStatus();
        if (_rowsBuilt) RefreshConfigRows();
        RefreshStatus();
    }

    partial void OnRenoDxChoiceChanged(DlssChoice? value)
    {
        if (_loading) return;
        _s.Settings.For(Id).RenoDxId = value?.Tag;
        AddonPicksChanged();
    }

    partial void OnLecramChoiceChanged(DlssChoice? value)
    {
        if (_loading) return;
        _s.Settings.For(Id).LecramId = value?.Tag;
        AddonPicksChanged();
    }

    partial void OnNrChoiceChanged(DlssChoice? value)
    {
        if (_loading) return;
        _s.Settings.For(Id).DlssNrId = value?.Tag;
        _s.Settings.Save();
        RefreshStatus();
    }

    partial void OnDlssChoiceChanged(DlssChoice? value)
    {
        if (_loading) return;
        _s.Settings.For(Id).DlssTag = value?.Tag;
        _s.Settings.Save();
        RefreshStatus();
    }

    private static IEnumerable<TargetOption> BuildTargets(GameInfo info, string? custom)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in info.Installs)
            if (seen.Add(dir)) yield return Option(info.Root, dir, info.Exes.FirstOrDefault(e => Path.GetDirectoryName(e)!.Equals(dir, StringComparison.OrdinalIgnoreCase)));
        foreach (var exe in info.Exes.Take(8))
        {
            var dir = Path.GetDirectoryName(exe)!;
            if (seen.Add(dir)) yield return Option(info.Root, dir, exe);
        }
        if (custom is not null && Directory.Exists(custom) && seen.Add(custom)) yield return Option(info.Root, custom, null);
        if (seen.Add(info.Root)) yield return Option(info.Root, info.Root, null);
    }

    private static TargetOption Option(string root, string dir, string? exe)
    {
        var rel = FileUtil.IsUnder(dir, root) ? Path.GetRelativePath(root, dir) : dir;
        var label = rel == "." ? "(game folder)" : rel;
        if (exe is not null) label += "   ·   " + Path.GetFileName(exe);
        return new TargetOption(dir, label, exe);
    }

    public void AddCustomTarget(string dir)
    {
        var opt = Option(Root, dir, null);
        Targets.Add(opt);
        SelectedTarget = opt;
    }

    partial void OnSelectedTargetChanged(TargetOption? value)
    {
        if (_loading) return;
        if (value is not null) _s.Settings.For(Id).TargetDir = value.Dir;
        _s.Settings.Save();
        LoadManifest();
    }

    partial void OnProxyChanged(string value)
    {
        if (_loading) return;
        _s.Settings.For(Id).Proxy = value;
        _s.Settings.Save();
    }

    private void LoadManifest()
    {
        _loading = true;
        Manifest = SelectedTarget is null ? null : InstallManifest.Load(SelectedTarget.Dir);
        var over = _s.Settings.Games.GetValueOrDefault(Id);
        Mode = Manifest is { } im && (im.Opti || im.ReShade) ? im.Mode : over?.Mode ?? _s.Settings.DefaultMode;
        Proxy = Manifest?.Proxy ?? _s.Settings.Games.GetValueOrDefault(Id)?.Proxy ?? ExistingOptiProxy() ?? _s.Settings.DefaultProxy;

        var m = Manifest;
        Set("opti", m?.Opti ?? true);
        Set("reshade", m?.ReShade ?? _s.Settings.InstallReShade);
        Set("mfg", m?.Mfg ?? _s.Settings.InstallMfgUnlock);
        Set("dlssnr", m?.DlssNr ?? (_s.Settings.InstallDlssNr && (!IsReShadeOnly || _s.Settings.InstallRenoDx || _s.Settings.InstallLecram || _s.Settings.InstallUplift)));
        Set("renodx", m?.RenoDx ?? _s.Settings.InstallRenoDx);
        Set("lecram", m?.Lecram ?? _s.Settings.InstallLecram);
        Set("dfc", m?.Dfc ?? false);
        Set("uplift", m?.Uplift ?? _s.Settings.InstallUplift);
        Set("dlss", m?.Dlss ?? HasDlss); // nothing to replace in games that don't ship DLSS
        Set("streamline", m?.Streamline ?? (_s.Settings.InstallStreamline && HasStreamline));
        ApplyModeToRows();
        if (!ProxyNames.Contains(Proxy, StringComparer.OrdinalIgnoreCase)) Proxy = "dxgi.dll";
        _loading = false;
        RefreshStatus();

        void Set(string key, bool on) => Components.First(c => c.Key == key).Enabled = on;
    }

    /// <summary>A manual OptiScaler install keeps its proxy name so the update replaces the file the game loads.</summary>
    private string? ExistingOptiProxy() => SelectedTarget is null ? null
        : AppSettings.ProxyNames.FirstOrDefault(n => File.Exists(Path.Combine(SelectedTarget.Dir, n)) && Installer.IsOptiScaler(Path.Combine(SelectedTarget.Dir, n)));

    /// <summary>Recomputes installed-vs-latest for every component and DLSS file.</summary>
    public void RefreshStatus()
    {
        var m = Manifest;
        var store = _s.Store;

        Row("opti", m?.Opti == true ? m.OptiTag : null, store.Opti?.Tag, true);
        Row("reshade", m?.ReShade == true ? InstalledVersion(ReShadeInstalledName(m)) : null, ReShadeLatest(), ReShadeCurrent(m));
        Row("mfg", m?.Mfg == true ? m.MfgTag : null, store.Mfg?.Tag, true);
        if (!Info.Streamline.Any(d => d.Name.Equals("sl.dlss_g.dll", StringComparison.OrdinalIgnoreCase)) && m?.Mfg != true)
        {
            // MFG Unlock only unlocks frame generation the game ships through Streamline; the installer skips it here.
            var mfgRow = Components.First(c => c.Key == "mfg");
            mfgRow.Latest = "needs Streamline FG";
            mfgRow.State = RowState.Missing;
        }
        var nrPath = store.DlssNrFor(EffectiveNrId);
        var nrFile = m?.Uplift == true ? Path.Combine(ComponentStore.UpliftNrDir, ComponentStore.DlssNrFile) : ComponentStore.DlssNrFile;
        var gameNr = SelectedTarget is null ? null : Path.Combine(SelectedTarget.Dir, nrFile);
        Row("dlssnr", gameNr is not null && File.Exists(gameNr) ? InstalledVersion(nrFile) : null,
            File.Exists(nrPath) ? FileUtil.Format(FileUtil.ReadVersion(nrPath)) : null,
            gameNr is null || !File.Exists(gameNr) || (File.Exists(nrPath) && FileUtil.SameFile(gameNr, nrPath)));
        var sf = store.RenoDxBuildFor(EffectiveRenoDxId);
        var lecram = store.LecramBuildFor(EffectiveLecramId);
        Row("renodx", m?.RenoDx == true ? InstalledAddon(AddonKind.RenoDx, m.RenoDxId, m.RenoDxTag) : null, sf?.Name, sf is not null && sf.Matches(m?.RenoDxId, m?.RenoDxTag));
        Row("lecram", m?.Lecram == true ? InstalledAddon(AddonKind.Lecram, m.LecramId, m.LecramTag) : null, lecram?.Name, lecram is not null && lecram.Matches(m?.LecramId, m?.LecramTag));
        Row("uplift", m?.Uplift == true ? m.UpliftTag : null, store.Uplift?.Tag, true);
        Row("dfc", m?.Dfc == true ? RenoDxVersionOf(m.DfcTag) : null, store.DfcVersion, m?.DfcTag == store.DfcTag);
        OnPropertyChanged(nameof(CanSwitchNr));
        OnPropertyChanged(nameof(CanUndoNr));
        RefreshAddonPairStatus();
        NotifyDfcHint();

        var latest = LatestDlss;
        DlssRows.Clear();
        foreach (var d in Info.Dlss)
        {
            var rel = Path.GetRelativePath(Root, d.Path);
            var ok = d.Version is { } v && latest is not null && (DlssPinned ? Trim(v) == latest : Trim(v) >= latest);
            var state = d.Version is null || latest is null ? RowState.None : ok ? RowState.Current : RowState.Update;
            DlssRows.Add(new DlssRow(rel, d.Kind, FileUtil.Format(d.Version), latest is null ? "—" : FileUtil.Format(latest), state));
        }
        var slLatest = store.Streamline?.Version;
        var interposer = Info.Streamline.FirstOrDefault(d => d.Kind == "SL");
        if (interposer is not null)
        {
            var extra = Info.Streamline.Count - 1;
            var rel = Path.GetRelativePath(Root, interposer.Path) + (extra > 0 ? $"  +{extra} sl.*.dll" : "");
            var cur = interposer.Version is { } v ? Trim(v) : null;
            var state = cur is null || slLatest is null ? RowState.None : cur == Trim(slLatest) ? RowState.Current : RowState.Update;
            DlssRows.Add(new DlssRow(rel, "SL", FileUtil.Format(cur), slLatest is null ? "—" : FileUtil.Format(slLatest), state));
        }

        var dlssRow = Components.First(c => c.Key == "dlss");
        dlssRow.Installed = m?.Dlss == true ? m.DlssTag ?? "—" : HasDlss ? "game" : "—";
        dlssRow.Latest = _s.Store.DlssFor(EffectiveDlssTag)?.Tag ?? "—";
        dlssRow.State = !HasDlss ? RowState.Missing : DlssCurrent ? RowState.Current : RowState.Update;

        var slRow = Components.First(c => c.Key == "streamline");
        slRow.Installed = interposer?.Version is { } iv ? FileUtil.Format(Trim(iv)) : HasStreamline ? "game" : "not used";
        slRow.Latest = store.Streamline?.Tag ?? "—";
        slRow.State = !HasStreamline ? RowState.None
            : interposer?.Version is { } sv && slLatest is not null && Trim(sv) == Trim(slLatest) ? RowState.Current
            : m?.Streamline == true ? RowState.Update : RowState.None;

        NeedsUpdate = m is not null && Components.Where(c => c.Key != "dlss" || m.Dlss).Any(c => c.State == RowState.Update && IsTracked(m, c.Key));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(IsOptiInstalled));
        OnPropertyChanged(nameof(ModBadge));
        OnPropertyChanged(nameof(DlssCurrent));
    }

    private static bool IsTracked(InstallManifest m, string key) => key switch
    {
        "opti" => m.Opti,
        "reshade" => m.ReShade,
        "mfg" => m.Mfg,
        "dlssnr" => m.DlssNr,
        "renodx" => m.RenoDx,
        "lecram" => m.Lecram,
        "uplift" => m.Uplift,
        "dfc" => m.Dfc,
        "streamline" => m.Streamline,
        _ => m.Dlss,
    };

    private void Row(string key, string? installed, string? latest, bool hashMatches)
    {
        var row = Components.First(c => c.Key == key);
        row.Installed = installed ?? "—";
        row.Latest = latest ?? "—";
        row.State = latest is null ? RowState.Missing
            : installed is null ? RowState.None
            : key is "reshade" or "dlssnr" or "renodx" or "lecram" or "dfc" ? (hashMatches ? RowState.Current : RowState.Update)
            : installed == latest ? RowState.Current : RowState.Update;
    }

    /// <summary>Version installs would use: the imported dll, the downloaded one, or the reshade.me release not downloaded yet.</summary>
    private string? ReShadeLatest()
    {
        var store = _s.Store;
        if (store.CurrentReShadePath is { } path) return FileUtil.Format(Trim3(FileUtil.ReadVersion(path)));
        return _s.Settings.AutoDownloadReShade && store.ReShade is { } r ? r.Tag : null;
    }

    private bool ReShadeCurrent(InstallManifest? m)
    {
        if (m?.ReShade != true) return true;
        if (_s.Store.ReShadeImported || _s.Store.CurrentReShadePath is not null) return m.ReShadeSha == _s.ReShadeSha;
        // Auto download not fetched yet: compare the installed file with the release version.
        var installed = SelectedTarget is null ? null : FileUtil.ReadVersion(Path.Combine(SelectedTarget.Dir, ReShadeInstalledName(m)));
        return installed is null || _s.Store.ReShade?.Version is not { } latest || Trim(installed) >= Trim(latest);
    }

    private static Version? Trim3(Version? v) => v is null ? null : Trim(v);

    /// <summary>ReShade64.dll beside OptiScaler, or the proxy name when ReShade is the proxy.</summary>
    private static string ReShadeInstalledName(InstallManifest m) =>
        m.Mode == InstallMode.ReShadeOnly && m.Proxy is { } p ? p : ComponentStore.ReShadeFile;

    /// <summary>The version part of a "{version}-{sha12}" tag.</summary>
    private static string? RenoDxVersionOf(string? tag) => tag is null ? null : tag.LastIndexOf('-') is > 0 and var i ? tag[..i] : tag;

    /// <summary>What the game has, by the library's name for that build, else the version part of its recorded tag.</summary>
    private string? InstalledAddon(AddonKind kind, string? id, string? tag) =>
        _s.Store.AddonBuilds(kind).FirstOrDefault(b => b.Matches(id, tag))?.Name ?? RenoDxVersionOf(tag);

    private string? InstalledVersion(string file) =>
        SelectedTarget is null ? null : FileUtil.Format(FileUtil.ReadVersion(Path.Combine(SelectedTarget.Dir, file)));

    public InstallOptions BuildOptions(bool dlssOnly, bool acConfirmed) => new()
    {
        Mode = Mode,
        Opti = !dlssOnly && !IsReShadeOnly && On("opti"),
        ReShade = !dlssOnly && (IsReShadeOnly || On("reshade")),
        Mfg = !dlssOnly && On("mfg"),
        DlssNr = !dlssOnly && On("dlssnr"),
        DlssNrId = EffectiveNrId,
        RenoDx = !dlssOnly && IsReShadeOnly && On("renodx") && !On("dfc"),
        RenoDxId = EffectiveRenoDxId,
        Lecram = !dlssOnly && IsReShadeOnly && On("lecram") && !On("dfc"),
        LecramId = EffectiveLecramId,
        Uplift = !dlssOnly && IsReShadeOnly && On("uplift"),
        Dfc = !dlssOnly && IsReShadeOnly && On("dfc"),
        DfcFrameGen = DfcFrameGen,
        Dlss = dlssOnly || On("dlss"),
        AddMissingDlss = _s.Settings.AddMissingDlss && !dlssOnly && !IsReShadeOnly && On("opti"),
        DlssTag = EffectiveDlssTag,
        Streamline = On("streamline") && HasStreamline,
        ReShadeOverrides = GameConfig.EffectiveReShade(_s.Settings, Id),
        CarryOverIni = IniMode != "fresh",
        OverwriteIni = IniMode == "apply",
        Proxy = Proxy,
        Overrides = GameConfig.EffectiveOpti(_s.Settings, Id),
        AntiCheatConfirmed = acConfirmed,
    };

    /// <summary>Options that reproduce what is installed, for "Update all".</summary>
    public InstallOptions UpdateOptions(InstallManifest m) => new()
    {
        Mode = m.Mode,
        Opti = m.Opti,
        ReShade = m.ReShade,
        Mfg = m.Mfg,
        DlssNr = m.DlssNr,
        DlssNrId = EffectiveNrId,
        RenoDx = m.RenoDx,
        RenoDxId = EffectiveRenoDxId,
        Lecram = m.Lecram,
        LecramId = EffectiveLecramId,
        Uplift = m.Uplift,
        Dfc = m.Dfc,
        DfcFrameGen = DfcFrameGen,
        Dlss = m.Dlss,
        AddMissingDlss = m.Opti && m.Files.Any(f => Path.GetFileName(f).Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase)),
        DlssTag = EffectiveDlssTag,
        Streamline = m.Streamline && HasStreamline,
        ReShadeOverrides = GameConfig.EffectiveReShade(_s.Settings, Id),
        CarryOverIni = IniMode != "fresh",
        OverwriteIni = IniMode == "apply",
        Proxy = m.Proxy ?? Proxy,
        Overrides = GameConfig.EffectiveOpti(_s.Settings, Id),
        AntiCheatConfirmed = m.AntiCheatConfirmed,
    };

    private string IniMode => GameConfig.EffectiveIniMode(_s.Settings, Id);

    private bool On(string key) => Components.First(c => c.Key == key).Enabled;

    public bool NeedsInjection(InstallOptions o) => o.Opti || o.ReShade || o.Mfg || o.DlssNr || o.RenoDx || o.Lecram || o.Dfc || o.Uplift;
}
