using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

/// <summary>One line of the "Works in this game" panel.</summary>
public sealed record MethodRow(MethodOption Option, bool IsTop)
{
    public string Title => Option.Title;
    public string Reason => Option.Reason;
    public string StateText => Option.Availability switch
    {
        Availability.Available => "Available",
        Availability.NeedsSetup => "Needs setup",
        Availability.Coming => "Not in the app yet",
        _ => "Not possible here",
    };
    public bool HasNeeds => Option.Needs.Count > 0;
    public string NeedsText => "Needs: " + string.Join("; ", Option.Needs);
    public bool IsSelectable => Option.Selectable;
    /// <summary>Methods that can't be used are shown dimmed, with the reason they can't.</summary>
    public bool IsDim => Option.Availability is Availability.NotPossible or Availability.Coming;
    public bool IsUnavailable => Option.Availability == Availability.NotPossible;
}

/// <summary>The per-game method advisor: which ways of improving this game the app knows (<see cref="MethodAdvisor"/>), and a button that selects the components of one.</summary>
public sealed partial class GameViewModel
{
    public ObservableCollection<MethodRow> Methods { get; } = [];

    [ObservableProperty] private string? _adviceBlocked;
    [ObservableProperty] private string _adviceFooter = "";
    [ObservableProperty] private string _selectTopLabel = "";

    public bool HasAdviceBlocked => AdviceBlocked is not null;
    public bool HasMethods => Methods.Count > 0;
    public bool CanSelectTop => Methods.Any(m => m.IsTop);

    partial void OnAdviceBlockedChanged(string? value) => OnPropertyChanged(nameof(HasAdviceBlocked));

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
        return new AdvisorInput(Info, _s.Gpu)
        {
            NativeDlss = Installer.HasNativeDlss(Info, Manifest, target),
            ProtectedInstall = MethodAdvisor.IsProtectedStoreInstall(Info),
            OptiCompat = catalog.OptiCompat.Find(Info.Name),
            HudfixIncompatible = catalog.OptiCompat.IsHudfixIncompatible(Info.Name),
            RenoDxMod = catalog.RenoDx?.Match(Info).Mod,
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
        Methods.Clear();
        foreach (var m in advice.Methods) Methods.Add(new MethodRow(m, ReferenceEquals(m, top)));
        AdviceBlocked = advice.Blocked;
        SelectTopLabel = top is null ? "" : $"Select: {top.Title}";
        AdviceFooter = advice.Methods.Any(m => m.Method is Method.NeuralAddon or Method.OptiScalerNr or Method.Feeder)
            ? $"{_s.Gpu.Label}. Neural rendering is the leaked DLSS 5 build and needs community DLLs; the app never downloads nvngx_dlssnr.dll."
            : _s.Gpu.IsKnown ? _s.Gpu.Label : "";
        OnPropertyChanged(nameof(HasMethods));
        OnPropertyChanged(nameof(CanSelectTop));
    }

    [RelayCommand]
    private void SelectTopMethod()
    {
        if (Methods.FirstOrDefault(m => m.IsTop) is { } row) SelectMethod(row);
    }

    /// <summary>Turns on the rows of the method (and the install mode it needs). Nothing is installed until the user presses Install.</summary>
    [RelayCommand]
    private void SelectMethod(MethodRow? row)
    {
        if (row is null || !row.IsSelectable) return;
        if (row.Option.Mode is { } mode && mode != Mode) Mode = mode;
        foreach (var id in row.Option.Components)
            if (RowKeys.TryGetValue(id, out var key) && Components.FirstOrDefault(c => c.Key == key) is { Visible: true, Editable: true } component)
                component.Enabled = true;
        // The NR add-ons exclude each other: the one the method picked stays, the others go (see NrAddons).
        RefreshStatus();
        OnPropertyChanged(nameof(AnyEnabled));
    }
}
