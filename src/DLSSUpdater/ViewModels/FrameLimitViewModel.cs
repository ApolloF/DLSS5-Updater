using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// The "Frame limit" section of a game's Install tab: one method (NVIDIA driver, ReLimiter, Display Commander, Unreal t.MaxFPS, none) and
/// one cap. The method is picked automatically ("Auto (...)") unless the user chooses; a manual choice is kept per game and Reset to auto
/// removes it. Plain state for the view; the long work (apply, remove) runs through <see cref="MainViewModel"/> so it shares the busy gate.
/// </summary>
public sealed partial class FrameLimitViewModel(GameViewModel game, Services services) : ObservableObject
{
    private bool _filling;
    private FrameLimitSetting? _effective;
    private LimitEnvironment? _env;
    private LimiterResolution? _resolution;
    private LimitMethod _active = LimitMethod.None;
    private bool _needsUpdate;

    public event Action? Changed;

    public bool Visible => services.FrameLimit is not null && !Demo.Active;

    private GameOverride? Over => services.Settings.Games.GetValueOrDefault(game.Id);
    private InstalledComponent? ActiveRecord => LimitMethods.ComponentOf(_active) is { } id ? game.Manifest?.Component(id) : null;

    public LimitMethod Method => _effective?.Method ?? LimitMethod.None;
    public bool NeedsUpdate => _needsUpdate;

    // ---------- method ----------

    public ObservableCollection<DlssChoice> MethodChoices { get; } = [];

    [ObservableProperty] private DlssChoice? _methodChoice;

    /// <summary>The game has its own method; Reset to auto removes it.</summary>
    public bool IsManual => FrameLimitSettings.Method(Over?.PickOf(ComponentRegistry.FrameLimit)) is not null;

    partial void OnMethodChoiceChanged(DlssChoice? value)
    {
        if (_filling || value is null) return;
        FrameLimitSettings.SetMethod(services.Settings.For(game.Id), LimitMethods.Parse(value.Tag));
        Persist();
    }

    [RelayCommand]
    private void ResetMethod()
    {
        var over = services.Settings.For(game.Id);
        FrameLimitSettings.SetMethod(over, null);
        FrameLimitSettings.SetFps(over, null);
        over.SetBuildPick(ComponentRegistry.ReLimiter, null);
        over.SetBuildPick(ComponentRegistry.DisplayCommander, null);
        Persist();
    }

    // ---------- cap ----------

    public string FpsText
    {
        get => (_effective?.Fps ?? FrameLimitRules.DefaultFps).ToString();
        set
        {
            if (!int.TryParse(value.Trim(), out var fps) || !FrameLimitRules.ValidFps(fps))
            {
                OnPropertyChanged();
                return;
            }
            if (fps == _effective?.Fps) return;
            FrameLimitSettings.SetFps(services.Settings.For(game.Id), fps);
            Persist();
        }
    }

    public string FpsHint => $"{FrameLimitRules.MinFps} to {FrameLimitRules.MaxFps}";

    // ---------- ReLimiter options and the user's own ini lines ----------

    public bool ShowOsd => Method == LimitMethod.ReLimiter;

    /// <summary>ReLimiter's overlay: on, off, or (unchecked-and-dim) left to the add-on.</summary>
    public bool? Osd
    {
        get => FrameLimitSettings.Osd(Over?.PickOf(ComponentRegistry.FrameLimit));
        set
        {
            FrameLimitSettings.SetOsd(services.Settings.For(game.Id), value);
            Persist();
        }
    }

    public bool ShowIniLines => Method is LimitMethod.ReLimiter or LimitMethod.DisplayCommander or LimitMethod.Unreal;

    public string IniText
    {
        get => FrameLimitSettings.IniText(Over?.PickOf(ComponentRegistry.FrameLimit));
        set
        {
            if (value.Trim() == IniText) return;
            FrameLimitSettings.SetIniText(services.Settings.For(game.Id), value);
            Persist();
        }
    }

    public string IniHint => Method switch
    {
        LimitMethod.ReLimiter => "Lines for relimiter.ini, one key=value per line (the [FrameLimiter] section is the default; add a [Section] line above for another). They win over what this app writes and go back with the add-on.",
        LimitMethod.DisplayCommander => "Lines for DisplayCommander.ini, one key=value per line (the [DisplayCommander] section is the default). They win over what this app writes and go back with the add-on.",
        _ => "Lines for Engine.ini, one key=value per line (the [SystemSettings] section is the default). They win over what this app writes and go back with the cap.",
    };

    // ---------- build (add-on methods) ----------

    public ObservableCollection<DlssChoice> BuildChoices { get; } = [];

    [ObservableProperty] private DlssChoice? _buildChoice;

    public bool ShowBuildChoices => LimitMethods.IsAddon(Method) && _resolution is not null;

    partial void OnBuildChoiceChanged(DlssChoice? value)
    {
        if (_filling || LimitMethods.ComponentOf(Method) is not { } id) return;
        services.Settings.For(game.Id).SetBuildPick(id, value?.Tag);
        Persist();
    }

    // ---------- text ----------

    /// <summary>Why the chosen method can't be used (and what is used instead), or why the picked one is offered; empty when there is nothing to say.</summary>
    public string Note => _effective?.Note ?? "";
    public bool HasNote => Note.Length > 0;

    public string HintText => FrameLimitHint.Text(game.Info, services.RefreshHz()) ?? "";
    public bool HasHint => HintText.Length > 0;

    /// <summary>What is in the game now, and whether the add-on has an update.</summary>
    public string StatusLine
    {
        get
        {
            var record = ActiveRecord;
            var cap = record?.Extras.GetValueOrDefault(FrameLimitPlanner.FpsKey) ?? FrameLimitSettings.DriverCap(Over?.PickOf(ComponentRegistry.FrameLimit))?.ToString();
            return _active switch
            {
                LimitMethod.None => "This app has not set a frame limit for this game.",
                LimitMethod.Driver => $"Active: NVIDIA driver limiter at {cap} FPS.",
                LimitMethod.Unreal => $"Active: t.MaxFPS = {cap} in Engine.ini.",
                _ => $"Active: {LimitMethods.Label(_active)} {record?.Version} at {cap} FPS" + (_needsUpdate && _resolution is { Found: true } r ? $" · update to {r.Version}" : "") + ".",
            };
        }
    }

    public string MethodDescription => Method switch
    {
        LimitMethod.Driver => "Set in the game's NVIDIA driver profile. Nothing is placed in the game, it works for every graphics API and in games with anti-cheat.",
        LimitMethod.ReLimiter => "A ReShade add-on that paces frames (made for G-Sync / VRR displays). ReShade is installed first when the game has none.",
        LimitMethod.DisplayCommander => "A ReShade add-on with an FPS limiter, window and display controls. ReShade is installed first when the game has none.",
        LimitMethod.Unreal => "Sets t.MaxFPS in the game's Engine.ini. The game may ignore it or override it in its own settings.",
        _ => "No limiter is set; an existing one is removed.",
    };

    public bool CanApply => Visible && _effective is { Method: not LimitMethod.None } e && game.SelectedTarget is not null && Available(e.Method);
    public bool CanRemove => Visible && _active != LimitMethod.None && game.SelectedTarget is not null;

    public string ApplyText => _active == Method && _needsUpdate ? "Update" : "Apply";

    /// <summary>The chosen add-on needs the user's OK first (anti-cheat); the warning stays in <see cref="Note"/>.</summary>
    public bool NeedsAntiCheatConfirm => _effective is { } e && _env is not null && FrameLimitRules.Availability(e.Method, game.Info, _env).Warning.Length > 0 && _active != e.Method;

    private bool Available(LimitMethod m) => _env is not null && FrameLimitRules.Availability(m, game.Info, _env).Available;

    // ---------- refresh ----------

    /// <summary>Re-reads the choice, the install record and the catalog. Call after a scan, a manifest read or a change of catalog or settings.</summary>
    public void Refresh()
    {
        if (!Visible)
        {
            _effective = null;
            return;
        }
        _env = BuildEnvironment();
        var over = Over;
        _effective = FrameLimitRules.Effective(game.Info, _env, over, services.Settings);
        _active = FrameLimitService.Active(game.Manifest, over);

        _resolution = null;
        _needsUpdate = false;
        if (LimitMethods.ComponentOf(_effective.Method) is { } id && LimitMethods.IsAddon(_effective.Method))
        {
            _resolution = LimiterResolution.Resolve(services.Store, id, LimiterFiles.ArchOf(game.Info.Effective.Is32Bit), CatalogLimiter(id), PinFor(id));
            _needsUpdate = _active == _effective.Method && _resolution.Found && ActiveRecord?.BuildId is { } have
                           && !have.Equals(_resolution.BuildId, StringComparison.OrdinalIgnoreCase);
        }

        FillMethods();
        FillBuilds();
        OnPropertyChanged(string.Empty);
        Changed?.Invoke();
    }

    private string? PinFor(string limiterId) => Over?.BuildPick(limiterId) ?? services.Settings.DefaultBuild(limiterId);

    private CatalogLimiter? CatalogLimiter(string id) => services.Catalog?.Document?.Sections.FrameLimiters?.For(id);

    private LimitEnvironment BuildEnvironment()
    {
        (bool, bool) Has(string id)
        {
            var catalog = CatalogLimiter(id);
            bool Arch(string arch) => catalog?.File(arch) is { IsInstallable: true } || services.Store.LimiterBuildsOf(id, arch).Count > 0;
            return (Arch("x64"), Arch("x86"));
        }
        var hint = services.Catalog?.Document?.Sections.Renodx?.Overrides.EngineIniPath(game.Info.SteamAppId, game.Info.Name);
        return new LimitEnvironment(services.FrameLimit?.NvidiaAvailable == true,
            new Dictionary<LimitMethod, (bool, bool)>
            {
                [LimitMethod.ReLimiter] = Has(ComponentRegistry.ReLimiter),
                [LimitMethod.DisplayCommander] = Has(ComponentRegistry.DisplayCommander),
            },
            game.Info.Effective.IsUnreal && FrameLimitService.ResolveEngineIni(game.Info, hint) is not null);
    }

    private void FillMethods()
    {
        _filling = true;
        MethodChoices.Clear();
        MethodChoices.Add(new DlssChoice(null, $"Auto ({LimitMethods.Label(_effective!.AutoMethod)})"));
        foreach (var m in LimitMethods.All)
        {
            var a = FrameLimitRules.Availability(m, game.Info, _env!);
            MethodChoices.Add(new DlssChoice(LimitMethods.Id(m), a.Available ? LimitMethods.Label(m) : $"{LimitMethods.Label(m)} (not available)"));
        }
        var own = FrameLimitSettings.Method(Over?.PickOf(ComponentRegistry.FrameLimit));
        MethodChoice = MethodChoices.FirstOrDefault(c => own is not null && c.Tag == LimitMethods.Id(own.Value)) ?? MethodChoices[0];
        _filling = false;
    }

    private void FillBuilds()
    {
        _filling = true;
        BuildChoices.Clear();
        if (LimitMethods.ComponentOf(Method) is { } id && LimitMethods.IsAddon(Method) && _resolution is not null)
        {
            var arch = LimiterFiles.ArchOf(game.Info.Effective.Is32Bit);
            var auto = LimiterResolution.Resolve(services.Store, id, arch, CatalogLimiter(id), null);
            BuildChoices.Add(new DlssChoice(null, auto.Found ? $"Auto ({auto.Version})" : "Auto (nothing available yet)"));
            foreach (var b in LimiterResolution.Candidates(services.Store, id, arch))
                BuildChoices.Add(new DlssChoice(b.Id, b.Imported ? $"{b.Version} (imported)" : b.Version));
            var pin = Over?.BuildPick(id);
            if (pin is not null && BuildChoices.All(c => c.Tag != pin)) BuildChoices.Add(new DlssChoice(pin, "Pinned build not in the library"));
            BuildChoice = BuildChoices.FirstOrDefault(c => c.Tag == pin) ?? BuildChoices[0];
        }
        else BuildChoice = null;
        _filling = false;
    }

    private void Persist()
    {
        services.Settings.Save();
        game.RefreshHdr();
    }

    // ---------- actions ----------

    /// <summary>Brings the game to the chosen method and cap. Throws what the service throws (anti-cheat, no 32-bit build, offline with an empty library).</summary>
    internal async Task ApplyAsync(IProgress<TransferProgress>? progress, CancellationToken ct, bool update = false, bool antiCheatConfirmed = false)
    {
        if (_effective is not { } e || services.FrameLimit is not { } service || game.SelectedTarget is not { } target)
            throw new InvalidOperationException("Nothing to apply for this game.");
        var method = update ? _active : e.Method;
        var fps = update && ActiveRecord?.Extras.GetValueOrDefault(FrameLimitPlanner.FpsKey) is { } kept && int.TryParse(kept, out var k) ? k : e.Fps;
        var request = Request(target, method, fps, force: !update, antiCheatConfirmed || update);
        var result = await service.ApplyAsync(request, services.Settings.Save, progress, ct);
        foreach (var n in result.Notes) Log.Info($"{game.Name}: {n}");
        Log.Info($"{game.Name}: frame limit {LimitMethods.Label(result.Method)}{(result.Method == LimitMethod.None ? "" : $" at {fps} FPS")}");
    }

    internal async Task RemoveAsync(CancellationToken ct)
    {
        if (services.FrameLimit is not { } service || game.SelectedTarget is not { } target) return;
        await service.RemoveAsync(Request(target, LimitMethod.None, FrameLimitRules.DefaultFps, force: true), services.Settings.Save, ct);
        Log.Info($"{game.Name}: frame limiter removed");
    }

    private FrameLimitRequest Request(TargetOption target, LimitMethod method, int fps, bool force, bool antiCheatConfirmed = false)
    {
        var settings = services.Settings;
        var over = settings.For(game.Id);
        var exe = game.DriverExe ?? throw new InvalidOperationException("No game executable was found, so there is no driver profile to write.");
        var id = LimitMethods.ComponentOf(method);
        var pick = FrameLimitSettings.Osd(over.PickOf(ComponentRegistry.FrameLimit));
        var hint = services.Catalog?.Document?.Sections.Renodx?.Overrides.EngineIniPath(game.Info.SteamAppId, game.Info.Name);
        var reshade = new HdrReShade(game.ReShadeChoice?.Tag, over.Proxy, GameConfig.EffectiveReShade(settings, game.Id), ReShadeIniTemplate.Load());
        return new FrameLimitRequest(game.Info, target.Dir, new NvidiaGame(exe, game.Name, over), method, fps, pick,
            FrameLimitSettings.IniText(over.PickOf(ComponentRegistry.FrameLimit)), id is null ? null : PinFor(id),
            id is null ? null : CatalogLimiter(id), hint, reshade, force, antiCheatConfirmed);
    }
}
