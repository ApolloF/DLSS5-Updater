using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// The "DXVK" section of a game's Install tab: which build (the default, a channel's newest, or one library build), for the Lilium build the HDR
/// preset and the NVIDIA present method, and the user's own dxvk.conf lines. Plain state for the view; the long work (apply, remove) runs through
/// <see cref="MainViewModel"/> so it shares the busy gate. The choices are kept per game and "Reset to default" removes them.
/// </summary>
public sealed partial class DxvkViewModel(GameViewModel game, Services services) : ObservableObject
{
    private bool _filling;
    private bool _needsUpdate;
    private DxvkCheck _check = new([], [], false);
    private LibraryBuild? _resolved;

    public event Action? Changed;

    public bool Visible => services.Dxvk is not null && !Demo.Active;

    private GameOverride? Over => services.Settings.Games.GetValueOrDefault(game.Id);
    private InstalledComponent? Record => game.Manifest?.Component(ComponentRegistry.Dxvk);
    public bool IsInstalled => Record is not null;
    public bool NeedsUpdate => _needsUpdate;

    /// <summary>Why DXVK has nothing to do in this game (DirectX 12, Vulkan, OpenGL, unknown API); empty when it applies.</summary>
    public string NotApplicable => DxvkRules.NotApplicable(game.Info) ?? "";
    public bool Applies => NotApplicable.Length == 0;

    // ---------- build ----------

    public ObservableCollection<DlssChoice> BuildChoices { get; } = [];

    [ObservableProperty] private DlssChoice? _buildChoice;

    /// <summary>The game has its own build or channel; Reset to default removes it.</summary>
    public bool IsManual => Over?.BuildPick(ComponentRegistry.Dxvk) is not null;

    partial void OnBuildChoiceChanged(DlssChoice? value)
    {
        if (_filling || value is null) return;
        services.Settings.For(game.Id).SetBuildPick(ComponentRegistry.Dxvk, value.Tag);
        Persist();
    }

    [RelayCommand]
    private void ResetBuild()
    {
        services.Settings.For(game.Id).SetBuildPick(ComponentRegistry.Dxvk, null);
        Persist();
    }

    private string? DefaultPick => services.Settings.DefaultBuild(ComponentRegistry.Dxvk);
    private string? Pick => Over?.BuildPick(ComponentRegistry.Dxvk);

    // ---------- Lilium: preset, present method ----------

    /// <summary>The build this game will use is the Lilium fork (or follows its channel): the HDR options apply.</summary>
    public bool ShowLilium => Visible && Applies && (Pick ?? DefaultPick) is { } p && (DxvkChannels.Parse(p) == DxvkChannel.Lilium || (DxvkLibrary.Find(p) is { } b && DxvkLibrary.HasLiliumOptions(b)));

    public ObservableCollection<DlssChoice> TierChoices { get; } = [];

    [ObservableProperty] private DlssChoice? _tierChoice;

    partial void OnTierChoiceChanged(DlssChoice? value)
    {
        if (_filling || value is null) return;
        DxvkSettings.SetTier(services.Settings.For(game.Id), value.Tag);
        Persist();
    }

    public string TierDescription =>
        TierChoice?.Tag == DxvkConf.TierOff ? "No HDR keys are written: the build runs as plain DXVK."
        : DxvkConf.Tier(DxvkConf.UsesD3d9Keys(game.Info.Effective.PrimaryApi), TierChoice?.Tag ?? DxvkConf.TierAuto)?.Description ?? "";

    public bool PresentMethod
    {
        get => DxvkSettings.PresentMethod(Over?.PickOf(ComponentRegistry.Dxvk));
        set
        {
            if (value == PresentMethod) return;
            DxvkSettings.SetPresentMethod(services.Settings.For(game.Id), value);
            Persist();
        }
    }

    public bool ShowPresentMethod => ShowLilium && services.Dxvk?.NvidiaAvailable == true;

    // ---------- the user's dxvk.conf lines ----------

    public string ConfText
    {
        get => DxvkSettings.UserLines(Over?.PickOf(ComponentRegistry.Dxvk));
        set
        {
            if (value.Trim() == ConfText) return;
            DxvkSettings.SetUserLines(services.Settings.For(game.Id), value);
            Persist();
        }
    }

    public string ConfHint => "Lines for dxvk.conf, one key = value per line (# starts a comment). They win over what this app writes and go back with DXVK.";

    // ---------- text ----------

    public string Warnings => string.Join(Environment.NewLine, _check.Warnings);
    public bool HasWarnings => _check.Warnings.Count > 0 && !IsInstalled;
    public string Blockers => string.Join(" ", _check.Blockers);
    public bool HasBlockers => _check.Blockers.Count > 0;

    public string StatusLine
    {
        get
        {
            if (Record is not { } r) return "DXVK is not installed in this game.";
            var dlls = string.Join(", ", r.Files.Select(f => Path.GetFileName(f.Path)).Where(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)));
            return $"Installed: {r.Version} ({dlls}, {(r.Extras.GetValueOrDefault(DxvkPlanner.ArchKey) == "x32" ? "32-bit" : "64-bit")})"
                   + (_needsUpdate && _resolved is not null ? $" · update to {_resolved.Label}" : "") + ".";
        }
    }

    public string ResolvedText => _resolved is null
        ? "No build in the library yet: the newest one is downloaded from GitHub when you install."
        : $"Installs {_resolved.Label}.";

    public string DllText => Applies
        ? $"Places {string.Join(" + ", DxvkDlls.For(game.Info.Effective.PrimaryApi))} ({DxvkDlls.Arch(game.Info.Effective.Is32Bit)}) beside the game, with a dxvk.conf. Anything already there is backed up and put back on remove."
        : NotApplicable;

    public bool CanApply => Visible && Applies && _check.CanInstall && game.SelectedTarget is not null;
    public bool CanRemove => Visible && IsInstalled && game.SelectedTarget is not null;
    public string ApplyText => IsInstalled ? (_needsUpdate ? "Update" : "Apply") : "Install";

    /// <summary>The install needs the user's OK first; the text is <see cref="Warnings"/>.</summary>
    public bool NeedsConfirm => _check.NeedsConfirm && !IsInstalled;

    // ---------- refresh ----------

    /// <summary>Re-reads the picks, the library and the install record. Call after a scan, a manifest read or a change of settings.</summary>
    public void Refresh()
    {
        if (!Visible) return;
        _check = game.SelectedTarget is { } t && Applies ? services.Dxvk!.Check(game.Info, t.Dir) : new DxvkCheck([], [], false);
        _resolved = DxvkLibrary.Choose(Pick, DefaultPick);
        var follows = DxvkLibrary.FollowedChannel(Pick, DefaultPick);
        _needsUpdate = Record is { BuildId: { } have } && _resolved is not null && !have.Equals(_resolved.Id, StringComparison.OrdinalIgnoreCase)
                       && (follows is not null || Pick is not null);
        FillBuilds();
        FillTiers();
        OnPropertyChanged(string.Empty);
        Changed?.Invoke();
    }

    private void FillBuilds()
    {
        _filling = true;
        BuildChoices.Clear();
        BuildChoices.Add(new DlssChoice(null, $"Default ({DxvkLibrary.AutoLabel(DefaultPick)[6..^1]})"));
        foreach (var c in Enum.GetValues<DxvkChannel>())
            BuildChoices.Add(new DlssChoice(DxvkChannels.Id(c), $"Newest {DxvkChannels.Label(c)}" + (DxvkLibrary.Choose(DxvkChannels.Id(c)) is { } b ? $" ({b.Label})" : "")));
        foreach (var b in DxvkLibrary.Builds()) BuildChoices.Add(new DlssChoice(b.Id, b.Label));
        var pin = Pick;
        if (pin is not null && BuildChoices.All(c => c.Tag != pin)) BuildChoices.Add(new DlssChoice(pin, "Pinned build not in the library"));
        BuildChoice = BuildChoices.FirstOrDefault(c => c.Tag == pin) ?? BuildChoices[0];
        _filling = false;
    }

    private void FillTiers()
    {
        _filling = true;
        TierChoices.Clear();
        var d3d9 = DxvkConf.UsesD3d9Keys(game.Info.Effective.PrimaryApi);
        foreach (var t in DxvkConf.Tiers(d3d9)) TierChoices.Add(new DlssChoice(t.Id, t.Id == DxvkConf.TierAuto ? $"{t.Label} (default)" : t.Label));
        TierChoices.Add(new DlssChoice(DxvkConf.TierOff, "Off (no HDR keys)"));
        var own = DxvkSettings.Tier(Over?.PickOf(ComponentRegistry.Dxvk));
        TierChoice = TierChoices.FirstOrDefault(c => c.Tag == (own ?? DxvkConf.TierAuto)) ?? TierChoices[0];
        _filling = false;
    }

    private void Persist()
    {
        services.Settings.Save();
        game.RefreshHdr();
    }

    // ---------- actions ----------

    /// <summary>Installs or updates DXVK with the game's choices. Throws what the service throws (a clash that needs confirming, no build, offline with an empty library).</summary>
    internal async Task ApplyAsync(IProgress<TransferProgress>? progress, CancellationToken ct, bool update = false, bool confirmed = false)
    {
        if (services.Dxvk is not { } service || game.SelectedTarget is not { } target)
            throw new InvalidOperationException("Nothing to apply for this game.");
        var over = services.Settings.For(game.Id);
        var exe = game.DriverExe ?? throw new InvalidOperationException("No game executable was found, so there is no driver profile to write.");
        var request = new DxvkRequest(game.Info, target.Dir, new NvidiaGame(exe, game.Name, over), over.BuildPick(ComponentRegistry.Dxvk),
            DxvkSettings.Tier(over.PickOf(ComponentRegistry.Dxvk)), DxvkSettings.PresentMethod(over.PickOf(ComponentRegistry.Dxvk)),
            DxvkSettings.UserLines(over.PickOf(ComponentRegistry.Dxvk)),
            game.Manifest is { ReShade: true } m ? game.UpdateOptions(m) : null, Force: !update, Confirmed: confirmed || update);
        var result = await service.ApplyAsync(request, DefaultPick, services.Settings.Save, progress, ct);
        foreach (var n in result.Notes) Log.Info($"{game.Name}: {n}");
        Log.Info($"{game.Name}: {result.Build.Label} installed");
    }

    internal async Task RemoveAsync(CancellationToken ct)
    {
        if (services.Dxvk is not { } service || game.SelectedTarget is not { } target || game.DriverExe is not { } exe) return;
        var undo = await service.RemoveAsync(game.Info, target.Dir, new NvidiaGame(exe, game.Name, services.Settings.For(game.Id)),
            game.Manifest is { ReShade: true, ReShadeVulkan: true } m ? game.UpdateOptions(m) : null, services.Settings.Save, ct);
        if (!undo.Clean) Log.Warn($"{game.Name}: some DXVK files or keys changed since they were installed and were left as they are.");
        Log.Info($"{game.Name}: DXVK removed");
    }
}
