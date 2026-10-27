using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

/// <summary>One line of the "Works in this game" panel. A warning the panel already shows above the list (<paramref name="PanelCaution"/>) isn't repeated on the line.</summary>
public sealed partial class MethodRow(MethodOption option, bool isTop, string? panelCaution = null) : ObservableObject
{
    public MethodOption Option { get; } = option;
    public bool IsTop { get; } = isTop;
    public string? PanelCaution { get; } = panelCaution;
    public string Title => Option.Title;
    public string Summary => Option.Summary;
    public string Reason => Option.Reason;
    public bool IsAddOn => Option.IsAddOn;
    /// <summary>The row is the method the component table installs (<see cref="MethodMatch.Picked"/>). An add-on row is picked while its components are on.</summary>
    [ObservableProperty] private bool _isPicked;
    /// <summary>"Turns on: ..." with the names of the component table's rows, and the install mode the card sets.</summary>
    [ObservableProperty] private string _turnsOn = "";
    /// <summary>On the picked card: the rows on in the table that this card and the add-ons don't bring; null when there are none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlsoOn))]
    private string? _alsoOn;
    public bool HasAlsoOn => AlsoOn is not null;
    /// <summary>The long reason is open below the row.</summary>
    [ObservableProperty] private bool _isExpanded;
    public string DetailsLabel => IsExpanded ? "Hide details" : "Details";
    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(DetailsLabel));

    [RelayCommand]
    private void ToggleDetails() => IsExpanded = !IsExpanded;

    public bool HasWarning => Warning is not null;
    public string? Warning => Option.Warning == PanelCaution ? null : Option.Warning;
    public string StateText => IsAdviceOnly ? "Own section below" : Option.Availability switch
    {
        Availability.Available => "Available",
        Availability.NeedsSetup => "Needs setup",
        Availability.Coming => "Not in the app yet",
        _ => "Not possible here",
    };
    public bool HasNeeds => Option.Needs.Count > 0;
    public string NeedsText => "Needs: " + string.Join("; ", Option.Needs);
    public bool IsSelectable => Option.Selectable;
    /// <summary>A usable method with no components (RenoDX HDR, Luma, DXVK): it is installed from its own section further down the page, so the card only opens its details.</summary>
    public bool IsAdviceOnly => Option.Availability is Availability.Available or Availability.NeedsSetup && Option.Components.Count == 0;
    /// <summary>Dim and disabled cards are the ones that can neither be picked nor opened.</summary>
    public bool IsClickable => IsSelectable || IsAdviceOnly;
    /// <summary>Methods that can't be used are shown dimmed, with the reason they can't.</summary>
    public bool IsDim => Option.Availability is Availability.NotPossible or Availability.Coming;
    public bool IsUnavailable => Option.Availability == Availability.NotPossible;
}

/// <summary>The per-game method advisor: which ways of improving this game the app knows (<see cref="MethodAdvisor"/>); clicking one selects its components.</summary>
public sealed partial class GameViewModel
{
    public ObservableCollection<MethodRow> Methods { get; } = [];
    /// <summary>Add-ons shown under the methods (MFG Unlock): each goes on or off by itself, whichever method is picked.</summary>
    public ObservableCollection<MethodRow> AddOns { get; } = [];

    [ObservableProperty] private string? _adviceBlocked;
    [ObservableProperty] private string? _adviceCaution;
    [ObservableProperty] private string _adviceFooter = "";
    /// <summary>Set when the table matches no card: the rows that are on, so the panel never shows a card the table doesn't install.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomSelection))]
    private string? _customSelection;
    public bool HasCustomSelection => CustomSelection is not null;
    /// <summary>The input of the last advice, so a change of NR add-on in the table can re-advise without reading the disk again.</summary>
    private AdvisorInput? _adviceInput;

    public bool HasAdviceBlocked => AdviceBlocked is not null;
    /// <summary>
    /// The caution shows only when no banner already says it: not under a block, and not when it is just the anti-cheat
    /// warning the page's anti-cheat banner shows above the tabs (a Store-install caution still shows).
    /// </summary>
    public bool HasAdviceCaution => AdviceCaution is not null && AdviceBlocked is null
        && !(Info.AntiCheat is { } ac && AdviceCaution == MethodAdvisor.AntiCheatCaution(ac));
    public bool HasMethods => Methods.Count > 0;
    public bool HasAddOns => AddOns.Count > 0;

    partial void OnAdviceBlockedChanged(string? value)
    {
        OnPropertyChanged(nameof(HasAdviceBlocked));
        OnPropertyChanged(nameof(HasAdviceCaution));
    }
    partial void OnAdviceCautionChanged(string? value) => OnPropertyChanged(nameof(HasAdviceCaution));

    // ---------- the Feeder build: Auto, or the user's own pick ----------

    public ObservableCollection<DlssChoice> FeederChoices { get; } = [];
    [ObservableProperty] private DlssChoice? _feederChoice;

    /// <summary>True when the game pins a Feeder build instead of following Auto.</summary>
    public bool FeederIsManual => FeederChoice?.Tag is not null;

    /// <summary>"Newest · 1.19.0" (or "Default · ..." when Settings chose a build) first, then the other library builds; a pin the library lost stays visible.</summary>
    private void FillFeederChoices(GameOverride? over)
    {
        var pin = over?.BuildPick(ComponentRegistry.Feeder);
        var defaultPick = _s.Settings.DefaultBuild(ComponentRegistry.Feeder);
        var builds = FeederLibrary.Builds();
        var pinned = builds.FirstOrDefault(b => b.Id.Equals(defaultPick, StringComparison.OrdinalIgnoreCase));
        var follows = pinned ?? builds.FirstOrDefault();
        // An empty library still installs: the newest release downloads with the first install.
        var def = new PickerDefault(follows?.Id, follows?.Label ?? "", pinned is null, Download: follows is null);
        var installed = Manifest?.Component(ComponentRegistry.Feeder)?.BuildId;
        FeederChoices.Clear();
        foreach (var c in GamePickerLabels.Compose(def, builds.Select(b => new PickerChoice(b.Id, b.Label)).ToList(), installed, pin))
            FeederChoices.Add(new DlssChoice(c.Tag, c.Label));
        if (pin is not null && FeederChoices.All(c => c.Tag != pin)) FeederChoices.Add(new DlssChoice(pin, $"{pin} · not in the library"));
        FeederChoice = FeederChoices.FirstOrDefault(c => c.Tag == pin) ?? FeederChoices[0];
    }

    partial void OnFeederChoiceChanged(DlssChoice? value)
    {
        OnPropertyChanged(nameof(FeederIsManual));
        if (_loading || value is null) return;
        _s.Settings.For(Id).SetBuildPick(ComponentRegistry.Feeder, value.Tag);
        _s.Settings.Save();
        RefreshStatus();
    }

    /// <summary>Back to following the default for this game.</summary>
    [RelayCommand]
    private void ResetFeederToAuto() => FeederChoice = FeederChoices.FirstOrDefault();

    /// <summary>What the advisor needs to know about this game right now.</summary>
    public AdvisorInput AdviceInput()
    {
        var target = SelectedTarget?.Dir ?? Root;
        var store = _s.Store;
        var imported = new HashSet<string>();
        if (File.Exists(store.DlssNrFor(EffectiveNrId))) imported.Add(ComponentRegistry.DlssNr);
        if (store.AddonBuilds(AddonKind.RenoDx).Count > 0) imported.Add(ComponentRegistry.RenoDx);
        if (store.AddonBuilds(AddonKind.Lecram).Count > 0) imported.Add(ComponentRegistry.Lecram);
        var dfc = store.DfcBuildFor(EffectiveDfcId);
        if (dfc is not null) imported.Add(ComponentRegistry.Dfc);
        var shaders = Path.Combine(target, FeederLibrary.ShaderDir);
        var catalog = AdvisorCatalog.For(_s.Catalog?.Document);
        var protectedInstall = MethodAdvisor.IsProtectedStoreInstall(Info);
        return new AdvisorInput(Info, _s.Gpu)
        {
            NativeDlss = Installer.HasNativeDlss(Info, Manifest, target),
            ProtectedInstall = protectedInstall,
            StoreInstall = !protectedInstall && MethodAdvisor.IsStoreInstall(Info),
            OptiCompat = catalog.OptiCompat.Find(Info.Name),
            HudfixIncompatible = catalog.OptiCompat.IsHudfixIncompatible(Info.Name),
            RenoDxMod = catalog.RenoDx?.Match(Info).Mod,
            Luma = _s.Catalog?.LumaPicker?.Pick(Info),
            Imported = imported,
            DfcRunsWithoutDlss = dfc is null || ComponentStore.DfcSupportsNonDlssGames(dfc),
            InstalledRoutes = NrAddons.PresentIn(target, Manifest),
            MotionProviderPresent = Directory.Exists(shaders) && Directory.EnumerateFiles(shaders, "lumenite_*.fx").Any(),
            AddonsOn = AddonsOn(),
        };
    }

    /// <summary>Recomputes the panel. Cheap and pure; called after a load, an install, and when the GPU or the catalog arrive.</summary>
    public void RefreshAdvice()
    {
        MethodAdvice advice;
        try
        {
            _adviceInput = AdviceInput();
            advice = MethodAdvisor.Advise(_adviceInput);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnsafeManifestException)
        {
            Log.Info($"{Name}: method advice not computed ({ex.Message})");
            _adviceInput = null;
            advice = new MethodAdvice([], [], null);
        }
        ShowAdvice(advice);
    }

    private void ShowAdvice(MethodAdvice advice)
    {
        var top = advice.Top;
        var keep = Methods.FirstOrDefault(m => m.IsPicked)?.Option.Method;
        var rows = advice.Choices.Select(m => new MethodRow(m, ReferenceEquals(m, top), advice.Caution)).ToList();
        var addOns = advice.AddOns.Select(m => new MethodRow(m, false, advice.Caution)).ToList();
        Methods.Clear();
        foreach (var r in rows) Methods.Add(r);
        AddOns.Clear();
        foreach (var r in addOns) AddOns.Add(r);
        SyncPick(keep);
        AdviceBlocked = advice.Blocked;
        AdviceCaution = advice.Caution;
        AdviceFooter = advice.Methods.Any(m => m.Method is Method.NeuralAddon or Method.OptiScalerNr or Method.Feeder)
            ? $"{_s.Gpu.Label}. Neural rendering runs a DLSS 5 build NVIDIA hasn't released (nvngx_dlssnr.dll); the app downloads and installs it automatically."
            : _s.Gpu.IsKnown ? _s.Gpu.Label : "";
        OnPropertyChanged(nameof(HasMethods));
        OnPropertyChanged(nameof(HasAddOns));
    }

    /// <summary>
    /// Applies a method: its components go on, the install mode follows and the previous card's extras go off (see
    /// <see cref="SelectMethod"/>). Cards without components only open their details; an add-on card switches its components
    /// on or off and leaves the method alone.
    /// </summary>
    [RelayCommand]
    private void PickMethod(MethodRow? row)
    {
        if (row is null) return;
        if (row.IsAddOn)
        {
            ToggleAddOn(row);
            return;
        }
        if (row.IsAdviceOnly)
        {
            row.IsExpanded = !row.IsExpanded;
            return;
        }
        if (!row.IsSelectable) return;
        SelectMethod(row, Methods.FirstOrDefault(m => m.IsPicked));
        SyncPick(row.Option.Method);
    }

    /// <summary>
    /// Marks the card the component table installs (<see cref="MethodMatch.Picked"/>; the card clicked last only breaks a tie), fills
    /// each card's "Turns on" line and the picked card's "Also on" line, or the custom-selection line when no card matches. Runs
    /// after a refresh and whenever the user changes the mode or a component by hand.
    /// </summary>
    private void SyncPick(Method? clicked)
    {
        if (NeuralCardIsStale()) return;
        var table = Table();
        var picked = MethodMatch.Picked(Methods.Select(m => m.Option), table, Mode, clicked);
        var outside = MethodMatch.Outside(picked, AddOns.Where(AddOnOn).Select(a => a.Option), table);
        var also = outside.Count > 0 ? MethodMatch.Names(outside, RowName) : null;
        foreach (var m in Methods)
        {
            m.IsPicked = ReferenceEquals(m.Option, picked);
            m.AlsoOn = m.IsPicked ? also : null;
            m.TurnsOn = TurnsOnText(m.Option);
        }
        foreach (var a in AddOns)
        {
            a.IsPicked = AddOnOn(a);
            a.TurnsOn = TurnsOnText(a.Option);
        }
        CustomSelection = picked is null ? also : null;
    }

    private void SyncPick() => SyncPick(Methods.FirstOrDefault(m => m.IsPicked)?.Option.Method);

    /// <summary>
    /// The neural rendering card names the add-on it installs. When the table has another NR add-on on, the card is advised again
    /// with that one (reusing the last input, so no disk reads); the refresh marks the pick.
    /// </summary>
    private bool NeuralCardIsStale()
    {
        if (_adviceInput is null || Methods.FirstOrDefault(m => m.Option.Method == Method.NeuralAddon) is not { IsSelectable: true } neural) return false;
        var on = AddonsOn();
        if (on.Count == 0 || on.SequenceEqual(_adviceInput.AddonsOn) || on.Any(id => neural.Option.Components.Contains(id, StringComparer.OrdinalIgnoreCase))) return false;
        _adviceInput = _adviceInput with { AddonsOn = on };
        ShowAdvice(MethodAdvisor.Advise(_adviceInput));
        return true;
    }

    /// <summary>The NR add-ons switched on in the table.</summary>
    private IReadOnlyList<string> AddonsOn() =>
        [.. new[] { ComponentRegistry.RenoDx, ComponentRegistry.Lecram, ComponentRegistry.Uplift, ComponentRegistry.Dfc }
            .Where(id => TableRowOf(id) is { Visible: true, Enabled: true })];

    private Dictionary<string, TableRow> Table()
    {
        var table = new Dictionary<string, TableRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in ComponentTable.Rows)
            if (TableRowOf(r.Id) is { } c) table[r.Id] = new TableRow(c.Visible, c.Enabled);
        return table;
    }

    private ComponentRow? TableRowOf(string id) => ComponentTable.KeyOf(id) is { } key ? Components.FirstOrDefault(c => c.Key == key) : null;

    /// <summary>The name the table shows for a component (the OptiScaler row is named after its build).</summary>
    private string RowName(string id) => TableRowOf(id)?.Name ?? ComponentTable.NameOf(id);

    private string TurnsOnText(MethodOption m)
    {
        if (!m.Selectable) return "";
        var text = "Turns on: " + MethodMatch.Names(m.Components, RowName);
        return m.Mode == InstallMode.ReShadeOnly ? $"{text}. Turns off: {RowName(ComponentRegistry.OptiScaler)}" : text;
    }

    /// <summary>
    /// Turns on the rows of the method (and the install mode it needs, which ticks or unticks the OptiScaler row), and turns off the rows only the <paramref name="previous"/>
    /// card used, so switching from OptiScaler + MFG to OptiScaler leaves MFG off. Nothing is installed until the user presses Install.
    /// </summary>
    private void SelectMethod(MethodRow row, MethodRow? previous)
    {
        if (!row.IsSelectable) return;
        if (row.Option.Mode is { } mode && mode != Mode) Mode = mode;
        foreach (var id in row.Option.Components)
            if (EditableRow(id) is { } component) component.Enabled = true;
        if (previous is not null && !ReferenceEquals(previous, row))
            foreach (var id in previous.Option.Components.Except(row.Option.Components, StringComparer.OrdinalIgnoreCase))
                if (EditableRow(id) is { } component) component.Enabled = false;
        // The NR add-ons exclude each other: the one the method picked stays, the others go (see NrAddons).
        RefreshStatus();
        OnPropertyChanged(nameof(AnyEnabled));
    }

    /// <summary>An add-on is on while all of its components the page can change are on; it needs no install mode of its own.</summary>
    private bool AddOnOn(MethodRow row)
    {
        var rows = row.Option.Components.Select(EditableRow).Where(c => c is not null).ToList();
        return row.IsSelectable && rows.Count > 0 && rows.All(c => c!.Enabled);
    }

    /// <summary>Switches an add-on's components on or off. The picked method, its mode and its components stay as they are.</summary>
    private void ToggleAddOn(MethodRow row)
    {
        if (!row.IsSelectable) return;
        var on = !AddOnOn(row);
        foreach (var id in row.Option.Components)
            if (EditableRow(id) is { } component) component.Enabled = on;
        SyncPick();
        RefreshStatus();
        OnPropertyChanged(nameof(AnyEnabled));
    }

    private ComponentRow? EditableRow(string id) => TableRowOf(id) is { Visible: true, Editable: true } component ? component : null;
}
