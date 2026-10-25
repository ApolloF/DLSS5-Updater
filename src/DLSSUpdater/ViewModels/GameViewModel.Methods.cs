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
    /// <summary>The row is the method the page is set to: the user clicked it, or its mode and components are all on.</summary>
    [ObservableProperty] private bool _isPicked;
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
    public string CardTip => IsAdviceOnly ? "Installed from its own section further down this page"
        : "Turns on this method's components and sets the install mode it needs. Nothing is installed until you press Install.";
    /// <summary>Methods that can't be used are shown dimmed, with the reason they can't.</summary>
    public bool IsDim => Option.Availability is Availability.NotPossible or Availability.Coming;
    public bool IsUnavailable => Option.Availability == Availability.NotPossible;
}

/// <summary>The per-game method advisor: which ways of improving this game the app knows (<see cref="MethodAdvisor"/>); clicking one selects its components.</summary>
public sealed partial class GameViewModel
{
    public ObservableCollection<MethodRow> Methods { get; } = [];

    [ObservableProperty] private string? _adviceBlocked;
    [ObservableProperty] private string? _adviceCaution;
    [ObservableProperty] private string _adviceFooter = "";

    public bool HasAdviceBlocked => AdviceBlocked is not null;
    /// <summary>
    /// The caution shows only when no banner already says it: not under a block, and not when it is just the anti-cheat
    /// warning the page's anti-cheat banner shows above the tabs (a Store-install caution still shows).
    /// </summary>
    public bool HasAdviceCaution => AdviceCaution is not null && AdviceBlocked is null
        && !(Info.AntiCheat is { } ac && AdviceCaution == MethodAdvisor.AntiCheatCaution(ac));
    public bool HasMethods => Methods.Count > 0;

    partial void OnAdviceBlockedChanged(string? value)
    {
        OnPropertyChanged(nameof(HasAdviceBlocked));
        OnPropertyChanged(nameof(HasAdviceCaution));
    }
    partial void OnAdviceCautionChanged(string? value) => OnPropertyChanged(nameof(HasAdviceCaution));

    /// <summary>Component id -> the key of its row on the game page.</summary>
    private static readonly Dictionary<string, string> RowKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        [ComponentRegistry.OptiScaler] = "opti",
        [ComponentRegistry.ReShade] = "reshade",
        [ComponentRegistry.MfgUnlock] = "mfg",
        [ComponentRegistry.DlssNr] = "dlssnr",
        [ComponentRegistry.Dlss] = "dlss",
        [ComponentRegistry.Streamline] = "streamline",
        [ComponentRegistry.RenoDx] = "renodx",
        [ComponentRegistry.Lecram] = "lecram",
        [ComponentRegistry.Dfc] = "dfc",
        [ComponentRegistry.Uplift] = "uplift",
        [ComponentRegistry.Feeder] = "feeder",
    };

    // ---------- the Feeder build: Auto, or the user's own pick ----------

    public ObservableCollection<DlssChoice> FeederChoices { get; } = [];
    [ObservableProperty] private DlssChoice? _feederChoice;

    /// <summary>True when the game pins a Feeder build instead of following Auto.</summary>
    public bool FeederIsManual => FeederChoice?.Tag is not null;

    /// <summary>"Auto (1.19.0)" first, then every build in the library; a pin the library lost stays visible.</summary>
    private void FillFeederChoices(GameOverride? over)
    {
        var pin = over?.BuildPick(ComponentRegistry.Feeder);
        FeederChoices.Clear();
        FeederChoices.Add(new DlssChoice(null, FeederLibrary.AutoLabel(_s.Settings.DefaultBuild(ComponentRegistry.Feeder))));
        foreach (var b in FeederLibrary.Builds()) FeederChoices.Add(new DlssChoice(b.Id, b.Label));
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

    /// <summary>Back to following Auto for this game.</summary>
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
        };
    }

    /// <summary>Recomputes the panel. Cheap and pure; called after a load, an install, and when the GPU or the catalog arrive.</summary>
    public void RefreshAdvice()
    {
        MethodAdvice advice;
        try { advice = MethodAdvisor.Advise(AdviceInput()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnsafeManifestException)
        {
            Log.Info($"{Name}: method advice not computed ({ex.Message})");
            advice = new MethodAdvice([], [], null);
        }
        var top = advice.Top;
        var keep = Methods.FirstOrDefault(m => m.IsPicked)?.Option.Method;
        var rows = advice.Methods.Select(m => new MethodRow(m, ReferenceEquals(m, top), advice.Caution)).ToList();
        Methods.Clear();
        foreach (var r in rows) Methods.Add(r);
        SyncPick(keep);
        AdviceBlocked = advice.Blocked;
        AdviceCaution = advice.Caution;
        AdviceFooter = advice.Methods.Any(m => m.Method is Method.NeuralAddon or Method.OptiScalerNr or Method.Feeder)
            ? $"{_s.Gpu.Label}. Neural rendering runs a DLSS 5 build NVIDIA hasn't released (nvngx_dlssnr.dll); the app downloads and installs it automatically."
            : _s.Gpu.IsKnown ? _s.Gpu.Label : "";
        OnPropertyChanged(nameof(HasMethods));
    }

    /// <summary>Applies a method: its components go on and the install mode follows (see <see cref="SelectMethod"/>). Cards without components only open their details.</summary>
    [RelayCommand]
    private void PickMethod(MethodRow? row)
    {
        if (row is null) return;
        if (row.IsAdviceOnly)
        {
            row.IsExpanded = !row.IsExpanded;
            return;
        }
        if (!row.IsSelectable) return;
        SelectMethod(row);
        foreach (var m in Methods) m.IsPicked = ReferenceEquals(m, row);
    }

    /// <summary>
    /// Marks the card the page is set to, so a picked card always means "applied": the last pick while the page still matches it,
    /// else the most specific method whose install mode and components are all on, else none. Runs after a refresh and whenever
    /// the user changes the mode or a component by hand.
    /// </summary>
    private void SyncPick(Method? keep)
    {
        var picked = Methods.FirstOrDefault(r => r.Option.Method == keep && PageMatches(r))
            ?? Methods.Where(PageMatches).MaxBy(r => r.Option.Components.Count);
        foreach (var m in Methods) m.IsPicked = ReferenceEquals(m, picked);
    }

    private void SyncPick() => SyncPick(Methods.FirstOrDefault(m => m.IsPicked)?.Option.Method);

    /// <summary>The page already has this method's mode (when it needs one) and every one of its components that the page shows on (the mode's loader always is).</summary>
    private bool PageMatches(MethodRow row)
    {
        if (!row.IsSelectable || row.Option.Mode is { } mode && mode != Mode) return false;
        var rows = row.Option.Components
            .Select(id => RowKeys.TryGetValue(id, out var key) ? Components.FirstOrDefault(c => c.Key == key) : null)
            .Where(c => c is { Visible: true })
            .ToList();
        return rows.Count > 0 && rows.All(c => c!.Enabled);
    }

    /// <summary>Turns on the rows of the method (and the install mode it needs). Nothing is installed until the user presses Install.</summary>
    private void SelectMethod(MethodRow row)
    {
        if (!row.IsSelectable) return;
        if (row.Option.Mode is { } mode && mode != Mode) Mode = mode;
        foreach (var id in row.Option.Components)
            if (RowKeys.TryGetValue(id, out var key) && Components.FirstOrDefault(c => c.Key == key) is { Visible: true, Editable: true } component)
                component.Enabled = true;
        // The NR add-ons exclude each other: the one the method picked stays, the others go (see NrAddons).
        RefreshStatus();
        OnPropertyChanged(nameof(AnyEnabled));
    }
}
