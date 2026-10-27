using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// One driver setting of a game profile (the choices come from <see cref="NvidiaProfileCatalog"/>). Choice values are
/// "ID=VALUE;ID=VALUE" (hex) and are written as a block. The first choice (Value null) removes every id of the option
/// from the game profile so the global / NVIDIA default applies again.
/// </summary>
public sealed partial class NvOptionViewModel : ObservableObject
{
    private readonly NvOptionDef _def;

    public NvOptionViewModel(NvOptionDef def)
    {
        _def = def;
        Choices = [new OptionChoice("Use global", null, "No per-game value."), .. def.Choices.Select(c => new OptionChoice(c.Label, Encode(c), c.Description))];
        DisplayChoices = new(Choices);
    }

    public string Label => _def.Label;
    public string Hint => _def.MayNeedAdmin ? $"{_def.Hint} Some systems need administrator rights to save this." : _def.Hint;
    public string Topic => _def.Topic;
    public uint[] Ids => _def.Ids;
    public OptionChoice[] Choices { get; }
    public NvGroup Group => _def.Group;
    public string GroupTitle => Group switch
    {
        NvGroup.Dlss => "DLSS overrides",
        NvGroup.FrameGeneration => "Multi frame generation",
        NvGroup.SmoothMotion => "Smooth Motion",
        NvGroup.RtxHdr => "RTX HDR",
        NvGroup.Sync => "Sync and latency",
        _ => "Other",
    };
    /// <summary>First row of its group in the list it is shown in.</summary>
    [ObservableProperty] private bool _showGroupHeader;
    /// <summary>Game feature this driver setting acts on; None = applies to every game.</summary>
    public GameFeatures Requires => _def.Requires;
    /// <summary>Whether the game uses the feature (false moves the option into "not used by this game").</summary>
    public bool Relevant { get; private set; } = true;
    /// <summary>Why the option is (or isn't) worth changing for this game.</summary>
    public string? Note { get; private set; }

    public void ApplyFeatures(GameFeatures features)
    {
        Relevant = Requires == GameFeatures.None || features.HasFlag(Requires);
        Note = !Relevant ? $"The game ships no DLSS {Requires switch
            {
                GameFeatures.SR => "Super Resolution",
                GameFeatures.RR => "Ray Reconstruction",
                GameFeatures.FG => "Frame Generation",
                _ => Requires.ToString(),
            }} files, so this has no effect."
            : _def.Key == "smooth-motion"
                ? features.HasFlag(GameFeatures.FG) ? "The game ships DLSS Frame Generation (nvngx_dlssg / Streamline DLSS-G). If its settings offer Frame Generation, use that instead; if not, Smooth Motion works."
                    : "Recommended here: the game has no DLSS Frame Generation."
            : null;
    }
    public OptionChoice? Loaded { get; private set; }

    /// <summary>Stable list for the dropdown (rebuilt only on load, so the ComboBox never resets the selection).</summary>
    public ObservableCollection<OptionChoice> DisplayChoices { get; }

    private OptionChoice? _selected;

    public OptionChoice? Selected
    {
        get => _selected;
        set
        {
            if (value is null || Equals(value, _selected)) return; // ComboBox pushes null while its items change
            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public bool IsDirty => Selected is not null && Loaded is not null && !Equals(Selected, Loaded);

    private static string Encode(NvChoice c) => string.Join(";", c.Writes.Select(w => $"{w.Id:X}={w.Value:X}"));

    public static IEnumerable<(uint Id, uint Value)> Pairs(string? value) =>
        (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('='))
            .Select(p => (uint.Parse(p[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                          uint.Parse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

    private OptionChoice? Match(Func<uint, uint?> value) =>
        Choices.Skip(1).FirstOrDefault(c => Pairs(c.Value).All(p => value(p.Id) == p.Value));

    /// <summary>
    /// <paramref name="user"/>: values set in this game's profile. <paramref name="fallback"/>: what applies without them
    /// (NVIDIA's shipped value for this game, else the global profile). <paramref name="gameDefault"/>: fallback comes from NVIDIA's game profile.
    /// </summary>
    public void SetLoaded(IReadOnlyDictionary<uint, uint?> user, IReadOnlyDictionary<uint, uint?> fallback, bool gameDefault)
    {
        string Describe(Func<uint, uint?> v) =>
            Match(v)?.Label
            ?? (Ids.All(id => v(id) is null) ? "not set" : string.Join(", ", Ids.Where(id => v(id) is not null).Select(id => $"0x{v(id):X}")));

        var inheritLabel = $"{(gameDefault ? "Game default" : "Use global")} ({Describe(id => fallback.GetValueOrDefault(id))})";
        var inherit = new OptionChoice(inheritLabel, null, gameDefault
            ? "No value of your own: NVIDIA's shipped setting for this game applies."
            : "No per-game value: the global driver profile applies.");

        OptionChoice current;
        if (Ids.All(id => user.GetValueOrDefault(id) is null)) current = inherit;
        else
        {
            uint? Effective(uint id) => user.GetValueOrDefault(id) ?? fallback.GetValueOrDefault(id);
            current = Match(Effective)
                      ?? new OptionChoice($"Custom ({Describe(Effective)})",
                          string.Join(";", Ids.Where(id => user.GetValueOrDefault(id) is not null).Select(id => $"{id:X}={user[id]:X}")),
                          "Set by the NVIDIA App or Profile Inspector; left as is unless you pick another value.");
        }

        DisplayChoices.Clear();
        DisplayChoices.Add(inherit);
        foreach (var c in Choices.Skip(1)) DisplayChoices.Add(c);
        if (!DisplayChoices.Contains(current)) DisplayChoices.Add(current);

        Loaded = current;
        _selected = current;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(IsDirty));
    }
}

/// <summary>
/// Editor for one game's NVIDIA driver profile. Options this driver doesn't define are hidden. What the app writes is recorded
/// per game (<see cref="NvidiaRecord"/>) so Reset puts the profile back exactly as it was. The global profile is only read, never written.
/// </summary>
public sealed partial class DriverProfileViewModel : ObservableObject
{
    private readonly NvidiaGame _game;
    private readonly string _exePath;
    private readonly string _gameName;
    private readonly NvidiaProfile _profile;
    private readonly Action _saveSettings;

    /// <param name="features">What the game ships (null = unknown: every option is shown as relevant).</param>
    /// <param name="settings">The game's settings, where the record of written values is kept (null = not kept, for tests and the demo).</param>
    /// <param name="saveSettings">Called after the record changed.</param>
    /// <param name="profile">The driver access; defaults to the real NVIDIA driver.</param>
    public DriverProfileViewModel(string exePath, string gameName, GameFeatures? features = null, Version? fgVersion = null,
        GameOverride? settings = null, Action? saveSettings = null, NvidiaProfile? profile = null)
    {
        _exePath = exePath;
        _gameName = gameName;
        _game = new NvidiaGame(exePath, gameName, settings ?? new GameOverride());
        _saveSettings = saveSettings ?? (() => { });
        _profile = profile ?? NvidiaProfile.Real;
        Features = features;
        Options = new(NvSettings.Create());
        foreach (var o in Options)
        {
            if (features is { } f) o.ApplyFeatures(f);
            o.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(NvOptionViewModel.IsDirty)) OnPropertyChanged(nameof(IsDirty));
            };
        }
        ShowOptions(Options);
        FeatureSummary = features is { } g ? Summarize(g, fgVersion) : null;
        CanReset = !NvidiaRecord.Load(_game.Settings).IsEmpty;
    }

    public GameFeatures? Features { get; }
    public ObservableCollection<NvOptionViewModel> Options { get; }
    /// <summary>Options for features the game uses (or that apply to every game).</summary>
    public ObservableCollection<NvOptionViewModel> Relevant { get; } = [];
    /// <summary>Options for DLSS features the game doesn't ship; collapsed by default.</summary>
    public ObservableCollection<NvOptionViewModel> Other { get; } = [];
    public bool HasOther => Other.Count > 0;
    public string OtherLabel => $"{(ShowOther ? "Hide" : "Show")} {Other.Count} setting{(Other.Count == 1 ? "" : "s")} this game doesn't use";
    public string? FeatureSummary { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OtherLabel))]
    private bool _showOther;

    /// <summary>One line when the driver doesn't define some options (they are not shown); null when it knows all.</summary>
    [ObservableProperty] private string? _hiddenNote;

    /// <summary>The app has written settings to this game's profile that Reset can put back.</summary>
    [ObservableProperty] private bool _canReset;

    [RelayCommand]
    private void ToggleOther() => ShowOther = !ShowOther;

    private void ShowOptions(IEnumerable<NvOptionViewModel> visible)
    {
        var shown = visible.ToList();
        Fill(Relevant, shown.Where(o => o.Relevant));
        Fill(Other, shown.Where(o => !o.Relevant));
        OnPropertyChanged(nameof(HasOther));
        OnPropertyChanged(nameof(OtherLabel));
    }

    private static void Fill(ObservableCollection<NvOptionViewModel> list, IEnumerable<NvOptionViewModel> options)
    {
        list.Clear();
        NvOptionViewModel? last = null;
        foreach (var o in options)
        {
            o.ShowGroupHeader = last is null || last.Group != o.Group;
            list.Add(o);
            last = o;
        }
    }

    internal static string Summarize(GameFeatures f, Version? fgVersion)
    {
        var parts = new List<string>();
        if (f.HasFlag(GameFeatures.SR)) parts.Add("Super Resolution");
        if (f.HasFlag(GameFeatures.RR)) parts.Add("Ray Reconstruction");
        if (f.HasFlag(GameFeatures.FG)) parts.Add("Frame Generation" + (fgVersion is { Major: >= 310 } ? " (MFG capable)" : ""));
        if (f.HasFlag(GameFeatures.Reflex)) parts.Add("Reflex");
        if (parts.Count == 0)
            return "No DLSS files found in this game: DLSS settings have no effect here. Smooth Motion, RTX HDR, the FPS limiter and VSync still apply.";
        return "Detected in the game files: DLSS " + string.Join(" · ", parts) + (f.HasFlag(GameFeatures.Streamline) ? " (via Streamline)" : "") + ".";
    }
    public bool IsDirty => Options.Any(o => o.IsDirty);

    [ObservableProperty] private string _status = "Not loaded";
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private bool _busy;

    [RelayCommand]
    public async Task Load()
    {
        if (!_profile.Available)
        {
            Status = "No NVIDIA driver found.";
            return;
        }
        Busy = true;
        try
        {
            var ids = Options.SelectMany(o => o.Ids).Distinct().ToList();
            var snap = await Task.Run(() => _profile.Read(_exePath, ids));
            var known = Options.Where(o => o.Ids.All(snap.Known.Contains)).ToList();
            foreach (var o in known) o.SetLoaded(snap.User, snap.Fallback, o.Ids.Any(snap.GameDefaults.Contains));
            ShowOptions(known);
            var hidden = Options.Count - known.Count;
            HiddenNote = hidden == 0 ? null : $"{hidden} setting{(hidden == 1 ? "" : "s")} not shown: this driver doesn't define {(hidden == 1 ? "it" : "them")}. Updating the NVIDIA driver may add them.";
            Status = snap.Status;
            CanReset = !NvidiaRecord.Load(_game.Settings).IsEmpty;
            Loaded = true;
        }
        catch (Exception ex) when (ex is NvApiException or InvalidOperationException or EntryPointNotFoundException)
        {
            Status = "Could not read the NVIDIA profile. The details are in the log.";
            Log.Error("Reading NVIDIA profile failed", ex);
        }
        finally
        {
            Busy = false;
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    /// <summary>
    /// Writes only the changed options to this game's profile in one session and saves once, so a failure
    /// leaves the driver untouched. Afterwards every written id is read back and compared.
    /// </summary>
    [RelayCommand]
    private async Task Apply()
    {
        var changed = Options.Where(o => o.IsDirty).ToList();
        if (changed.Count == 0) return;
        var expected = new Dictionary<uint, uint?>();
        foreach (var o in changed)
        {
            if (o.Selected!.Value is null) foreach (var id in o.Ids) expected[id] = null;
            else foreach (var (id, v) in NvOptionViewModel.Pairs(o.Selected.Value)) expected[id] = v;
        }

        var result = await Write(() => _profile.Apply(_game, expected));
        if (result is null) return;
        Log.Info($"NVIDIA profile {_gameName}: {string.Join(", ", changed.Select(o => $"{o.Label} = {o.Selected!.Label}"))}");
        var labels = string.Join(", ", changed.Where(o => o.Ids.Any(id => result.Mismatches.Any(w => w.Id == id))).Select(o => o.Label));
        ReportMismatches(result, labels);
        await Load();
    }

    /// <summary>Puts every setting the app wrote back to what this game's profile held before.</summary>
    [RelayCommand]
    private async Task Reset()
    {
        var count = NvidiaRecord.Load(_game.Settings).Previous.Count;
        if (count == 0) return;
        if (!Views.Dialog.Confirm("Reset NVIDIA profile", $"Put the {count} driver setting{(count == 1 ? "" : "s")} this app changed for {_gameName} back to what {(count == 1 ? "it was" : "they were")} before?", "Reset")) return;
        var result = await Write(() => _profile.Reset(_game));
        if (result is null) return;
        Log.Info($"NVIDIA profile {_gameName}: reset {count} setting(s) to their previous values");
        ReportMismatches(result, "the reset settings");
        await Load();
    }

    private void ReportMismatches(NvidiaResult result, string labels)
    {
        if (result.Verified) return;
        var detail = string.Join(", ", result.Mismatches.Select(w => $"0x{w.Id:X8} expected {(w.Wanted is { } v ? $"0x{v:X}" : "unset")}, got {(w.Got is { } g ? $"0x{g:X}" : "unset")}"));
        Log.Error($"NVIDIA profile {_gameName}: verification mismatch: {detail}");
        Views.Dialog.Show("NVIDIA profile", $"The driver didn't keep these settings: {labels}. Try again or check the profile in the NVIDIA App.");
    }

    /// <summary>Runs a write on a background thread and keeps the record; null when it failed (the user has been told).</summary>
    private async Task<NvidiaResult?> Write(Func<NvidiaResult> write)
    {
        Busy = true;
        try
        {
            var result = await Task.Run(write);
            _saveSettings();
            return result;
        }
        catch (NvApiException ex) when (ex.NeedsAdmin)
        {
            if (Views.Dialog.Confirm("Administrator rights needed", "The NVIDIA driver only saves profile changes for administrators.", "Restart as admin"))
                App.RestartElevated();
        }
        catch (Exception ex) when (ex is NvApiException or InvalidOperationException)
        {
            Log.Error("Writing NVIDIA profile failed", ex);
            Views.Dialog.Show("NVIDIA profile", (ex is NvApiException ? "The NVIDIA driver didn't accept the change." : UserErrors.Message(ex)) + "\nNothing was saved.");
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            Log.Error("Writing NVIDIA profile failed", ex);
            Views.Dialog.Show("NVIDIA profile", "This NVIDIA driver doesn't offer the profile functions this app needs. Updating the driver usually fixes it.\nNothing was saved.");
        }
        finally
        {
            Busy = false;
        }
        return null;
    }

    [RelayCommand]
    private void Revert()
    {
        foreach (var o in Options)
            if (o.Loaded is not null) o.Selected = o.Loaded;
    }
}

/// <summary>Driver settings in the per-game NVIDIA tab; the list itself is <see cref="NvidiaProfileCatalog"/>.</summary>
public static class NvSettings
{
    public static NvOptionViewModel[] Create() => [.. NvidiaProfileCatalog.Create().Select(d => new NvOptionViewModel(d))];
}
