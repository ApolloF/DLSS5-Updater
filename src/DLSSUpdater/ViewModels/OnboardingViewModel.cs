using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

public enum OnboardingStep { Welcome, Mode, Updates, Done }

/// <summary>One choice card of the setup: a title and the one sentence under it.</summary>
public sealed record OnboardingChoice<T>(T Value, string Label, string Description)
{
    public override string ToString() => Label;
}

/// <summary>
/// What finishing the setup changed that other parts of the app have to follow. <paramref name="Build"/> is a newly
/// picked OptiScaler build: it is switched through Settings like any other build change, so it isn't written here.
/// </summary>
public sealed record OnboardingResult(bool Applied, OptiSource? Build);

/// <summary>
/// The short first-run setup. Its choices start at the current settings and are written only by Finish; Skip (or Esc)
/// writes nothing but <see cref="AppSettings.OnboardingDone"/>, so the defaults stay as they were.
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly NrAddon? _initialAddon;
    private readonly int _initialKeep;

    public OnboardingViewModel(AppSettings settings)
    {
        _settings = settings;
        _mode = Modes.First(m => m.Value == settings.DefaultMode);
        _optiBuild = Builds.First(b => b.Value == OptiSources.For(settings.OptiSource));
        var addons = NrAddons.Defaults(settings).ToList();
        _initialAddon = NrAddons.Priority.Where(addons.Contains).Select(a => (NrAddon?)a).FirstOrDefault();
        _addon = Addons.First(a => a.Value == _initialAddon);
        _checkAppUpdates = settings.CheckAppUpdates;
        _autoInstallAppUpdates = settings.AutoInstallAppUpdates;
        _autoDownloadReShade = settings.AutoDownloadReShade;
        _autoDownloadLecram = settings.AutoDownloadLecram;
        _autoCleanBuilds = settings.AutoCleanBuilds;
        _keepBuilds = _initialKeep = settings.KeepBuildsFor(LibraryKind.OptiScaler);
    }

    /// <summary>Shown by itself on a fresh install only; the demo shows it only with DLSSU_DEMO_ONBOARDING=1.</summary>
    public static bool ShouldShow(AppSettings s, bool demo, bool demoSwitch) => s.OnboardingDone == false && (!demo || demoSwitch);

    /// <summary>Raised once when the setup is finished or skipped.</summary>
    public event Action<OnboardingResult>? Closed;

    public static IReadOnlyList<OnboardingStep> Steps { get; } = Enum.GetValues<OnboardingStep>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepNumber), nameof(StepText), nameof(Title), nameof(CanGoBack), nameof(IsLast), nameof(NextText),
        nameof(IsWelcome), nameof(IsMode), nameof(IsUpdates), nameof(IsDone))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private OnboardingStep _step;

    public int StepNumber => Steps.ToList().IndexOf(Step) + 1;
    public string StepText => $"Step {StepNumber} of {Steps.Count}";
    public bool CanGoBack => Step != OnboardingStep.Welcome;
    public bool IsLast => Step == OnboardingStep.Done;
    public string NextText => IsLast ? "Start" : "Next";
    public bool IsWelcome => Step == OnboardingStep.Welcome;
    public bool IsMode => Step == OnboardingStep.Mode;
    public bool IsUpdates => Step == OnboardingStep.Updates;
    public bool IsDone => Step == OnboardingStep.Done;

    public string Title => Step switch
    {
        OnboardingStep.Welcome => "Welcome to DLSS5 Updater",
        OnboardingStep.Mode => "How should games be set up?",
        OnboardingStep.Updates => "Updates and disk space",
        _ => "All set",
    };

    // ---------- step 2: install mode ----------

    public static IReadOnlyList<OnboardingChoice<InstallMode>> Modes { get; } =
    [
        new(InstallMode.OptiScaler, "OptiScaler", "OptiScaler loads into the game and brings ReShade, MFG Unlock and NR with it."),
        new(InstallMode.ReShadeOnly, "ReShade + add-ons (DLSS5 mod)", ModeChoice.For(OptiSources.F5).First(m => m.Mode == InstallMode.ReShadeOnly).Description),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReShadeMode), nameof(IsOptiScalerMode))]
    private OnboardingChoice<InstallMode> _mode;

    public bool IsReShadeMode => Mode.Value == InstallMode.ReShadeOnly;
    public bool IsOptiScalerMode => !IsReShadeMode;

    public static IReadOnlyList<OnboardingChoice<OptiSource>> Builds { get; } =
    [
        new(OptiSources.F5, OptiSources.F5.Label, "The default. Experimental and released often; it installs its own open-source NGX forwarder next to OptiScaler."),
        new(OptiSources.Default, OptiSources.Default.Label, "The fork this app was first built around. Pick it if F5 gives you trouble in a game."),
    ];

    [ObservableProperty] private OnboardingChoice<OptiSource> _optiBuild;

    public static IReadOnlyList<OnboardingChoice<NrAddon?>> Addons { get; } =
    [
        new(NrAddon.RenoDx, "ShortFuse RenoDX", "ShortFuse's RenoDX DLSS add-on. Downloaded and installed automatically; needs a game with native DLSS."),
        new(NrAddon.Lecram, "Lecram DLSS5", LecramHint(BuildFlags.LecramChannel)),
        new(NrAddon.Uplift, "GITC Uplift", "Open-source add-on, downloaded from GitHub. Single-player games only: injection can trigger anti-cheat."),
        new(null, "None", "ReShade, MFG Unlock and the DLSS updates only. Each game can still pick an add-on later."),
    ];

    internal static string LecramHint(bool channelEnabled) => channelEnabled
        ? "Lecram's DLSS5 add-on. New v11 builds are fetched automatically."
        : "Lecram's DLSS5 add-on. Downloaded and installed automatically.";

    /// <summary>The automatic Lecram check only exists in the private build; the stored choice is left alone elsewhere.</summary>
    public bool ShowLecramChannel => BuildFlags.LecramChannel;

    [ObservableProperty] private OnboardingChoice<NrAddon?> _addon;

    // ---------- step 3: updates and space ----------

    [ObservableProperty] private bool _checkAppUpdates;
    [ObservableProperty] private bool _autoInstallAppUpdates;
    [ObservableProperty] private bool _autoDownloadReShade;
    [ObservableProperty] private bool _autoDownloadLecram;
    [ObservableProperty] private bool _autoCleanBuilds;

    /// <summary>How many newest builds each library keeps. Starts at the OptiScaler library's; written to every library only when changed.</summary>
    [ObservableProperty] private int _keepBuilds;

    public static IReadOnlyList<int> KeepChoices => LibraryRowViewModel.KeepChoices;

    // ---------- navigation ----------

    [RelayCommand]
    private void Next()
    {
        if (IsLast) Finish();
        else Step = Steps[StepNumber];
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step = Steps[StepNumber - 2];

    [RelayCommand]
    private void Skip() => Close(new OnboardingResult(false, null));

    /// <summary>Writes the choices to the settings and saves them with the done flag.</summary>
    public void Finish() => Close(Apply());

    /// <summary>The choices as settings. Only the mode's own sub-choice counts: the build for OptiScaler, the add-on for ReShade + add-ons.</summary>
    private OnboardingResult Apply()
    {
        var s = _settings;
        s.DefaultMode = Mode.Value;
        if (IsReShadeMode) ApplyAddon(s);
        var build = !IsReShadeMode && OptiBuild.Value != OptiSources.For(s.OptiSource) ? OptiBuild.Value : null;
        s.CheckAppUpdates = CheckAppUpdates;
        s.AutoInstallAppUpdates = AutoInstallAppUpdates;
        s.AutoDownloadReShade = AutoDownloadReShade;
        s.AutoDownloadLecram = AutoDownloadLecram;
        s.AutoCleanBuilds = AutoCleanBuilds;
        if (KeepBuilds != _initialKeep)
            foreach (var kind in Enum.GetValues<LibraryKind>()) s.KeepBuilds[kind.ToString()] = KeepBuilds;
        return new OnboardingResult(true, build);
    }

    /// <summary>An unchanged pick leaves the add-ons alone (a kept ShortFuse + Lecram pair stays); a new one replaces them (the page offers a single pick).</summary>
    private void ApplyAddon(AppSettings s)
    {
        if (Addon.Value == _initialAddon) return;
        foreach (var a in NrAddons.Defaults(s).ToList()) NrAddons.SetDefault(s, a, false);
        if (Addon.Value is { } addon) NrAddons.SetDefault(s, addon, true);
    }

    private bool _closed;

    private void Close(OnboardingResult result)
    {
        if (_closed) return;
        _closed = true;
        _settings.OnboardingDone = true;
        _settings.Save();
        Closed?.Invoke(result);
    }
}
