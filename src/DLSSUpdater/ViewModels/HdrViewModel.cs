using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// The "HDR (RenoDX)" section of a game's Install tab: which catalog mod the game gets and why, what is installed, and the
/// state of it. Plain state for the view; the long work (install, remove) runs through <see cref="MainViewModel"/> so it shares the busy gate.
/// </summary>
public sealed partial class HdrViewModel(GameViewModel game, Services services) : ObservableObject
{
    public const string UeHint = "Unreal Extended is the default for Unreal games without their own mod. Switch it off for the plain generic Unreal add-on.";

    private HdrChoice? _choice;
    private HdrChoice? _picked;
    private bool _filling;
    private HdrStatus? _status;
    private (HdrPicker? Picker, GameInfo Info, bool? Ue)? _pickKey;

    /// <summary>Raised after every refresh; the main view model re-counts updates and re-checks its commands.</summary>
    public event Action? Changed;

    /// <summary>False without the catalog services (and in the demo, which never installs): the section is hidden.</summary>
    public bool Visible => services.Catalog is not null && services.Hdr is not null && !Demo.Active;

    public HdrChoice? Choice => _choice;
    public HdrStatus? Status => _status;

    private InstalledComponent? Installed => game.Manifest?.Component(ComponentRegistry.RenoDxHdr);

    public string PickedText => !CatalogReady ? ""
        : _choice is { IsOffered: true } c ? $"{c.Title}. {c.Reason}"
        : _choice?.Reason ?? "";
    public bool HasPickedText => PickedText.Length > 0;

    public string Notes => string.Join(Environment.NewLine, _choice?.Notes ?? []);
    public bool HasNotes => Notes.Length > 0;

    /// <summary>Installed against the pick, or why the catalog can't be used. Empty when the picked text already says it all.</summary>
    public string StatusLine => !CatalogReady ? Problem
        : _status is { State: HdrState.Unavailable } or null ? ""
        : _status.Line;
    public bool HasStatusLine => StatusLine.Length > 0;

    public string InstalledVersion => _status?.Installed ?? "—";
    public string LatestVersion => _status?.Latest ?? "—";

    public bool NeedsUpdate => _status?.UpdateAvailable == true;

    /// <summary>The pick is offered and a target folder is selected; the busy state is checked by the command.</summary>
    public bool CanInstall => CatalogReady && _choice is { IsOffered: true } && game.SelectedTarget is not null;
    public bool CanRemove => Installed is not null && game.SelectedTarget is not null;

    public string ActionText => _status?.State switch
    {
        HdrState.Update => "Update",
        HdrState.Current => "Reinstall",
        _ => "Install",
    };

    public string? Link => CatalogReady ? _choice?.Link : null;
    public bool HasLink => Link is not null && Link.StartsWith("https://", StringComparison.Ordinal);

    [RelayCommand]
    private void OpenLink()
    {
        if (Link is { } url) MainViewModel.OpenUrl(url);
    }

    public bool ShowUeToggle => CatalogReady && _choice is { CanToggleUeExtended: true };

    /// <summary>Unreal Extended or the plain generic add-on; unset shows what the default picks.</summary>
    public bool UeExtended
    {
        get => _choice?.Kind == HdrKind.UeExtended;
        set
        {
            if (value == UeExtended) return;
            services.Settings.For(game.Id).SetHdrUeExtended(value);
            services.Settings.Save();
            game.RefreshHdr();
        }
    }

    public string UeHintText => UeHint;

    /// <summary>"Default (...)" naming the catalog's current build, then the library's builds of the picked mod, newest first.</summary>
    public ObservableCollection<DlssChoice> BuildChoices { get; } = [];

    [ObservableProperty] private DlssChoice? _buildChoice;

    public bool ShowBuildChoices => CatalogReady && _picked is { IsOffered: true };

    partial void OnBuildChoiceChanged(DlssChoice? value)
    {
        if (_filling) return;
        services.Settings.For(game.Id).SetBuildPick(ComponentRegistry.RenoDxHdr, value?.Tag);
        services.Settings.Save();
        game.RefreshHdr();
    }

    private bool CatalogReady => services.Catalog?.Picker is not null;
    private string Problem => services.Catalog?.Problem ?? "";

    /// <summary>Re-picks and re-compares with the install record. Call after a scan, a manifest read or a change of catalog or settings.</summary>
    public void Refresh()
    {
        if (!Visible)
        {
            _choice = _picked = null;
            _status = null;
            return;
        }
        var picker = services.Catalog!.Picker;
        if (picker is null)
        {
            _choice = _picked = null;
            _status = null;
            FillBuildChoices(null, null);
        }
        else
        {
            var ue = services.Settings.Games.GetValueOrDefault(game.Id)?.HdrUeExtended;
            if (_pickKey is not { } key || !ReferenceEquals(key.Picker, picker) || !ReferenceEquals(key.Info, game.Info) || key.Ue != ue)
            {
                _picked = picker.Pick(game.Info, ue);
                _pickKey = (picker, game.Info, ue);
            }
            var pin = services.Settings.Games.GetValueOrDefault(game.Id)?.BuildPick(ComponentRegistry.RenoDxHdr);
            var library = services.Store.HdrBuilds;
            _choice = HdrPin.Apply(_picked!, pin, library);
            FillBuildChoices(pin, library);
            _status = HdrStatus.Of(_choice!, Installed, services.Settings.HdrPeakNits);
        }
        OnPropertyChanged(string.Empty);
        Changed?.Invoke();
    }

    private void FillBuildChoices(string? pin, IReadOnlyList<HdrBuild>? library)
    {
        _filling = true;
        BuildChoices.Clear();
        if (_picked is { IsOffered: true } picked && library is not null)
        {
            BuildChoices.Add(new DlssChoice(null, $"Default ({picked.File!.Version})"));
            foreach (var b in HdrPin.Candidates(picked, library)) BuildChoices.Add(new DlssChoice(b.Id, b.Version));
            if (pin is not null && BuildChoices.All(c => c.Tag != pin)) BuildChoices.Add(new DlssChoice(pin, "Pinned build not in the library"));
            BuildChoice = BuildChoices.FirstOrDefault(c => c.Tag == pin) ?? BuildChoices[0];
        }
        else BuildChoice = null;
        _filling = false;
    }

    /// <summary>Installs or updates the mod this game is picked for. Throws what the service throws (anti-cheat, Vulkan without the layer, offline).</summary>
    internal async Task InstallAsync(IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (_choice is not { IsOffered: true } choice || game.SelectedTarget is not { } target || services.Hdr is not { } hdr)
            throw new InvalidOperationException(_choice?.Reason ?? "Nothing is offered for this game.");
        var hint = services.Catalog?.Document?.Sections.Renodx?.Overrides.EngineIniPath(game.Info.SteamAppId, game.Info.Name);
        var settings = services.Settings;
        var reshade = new HdrReShade(game.ReShadeChoice?.Tag, settings.Games.GetValueOrDefault(game.Id)?.Proxy,
            GameConfig.EffectiveReShade(settings, game.Id), ReShadeIniTemplate.Load());
        await hdr.InstallAsync(game.Info, target.Dir, choice, settings.HdrPeakNits, hint, reshade, progress, ct);
        Log.Info($"{game.Name}: {choice.Title} {choice.File!.Version} installed");
    }

    internal async Task RemoveAsync(CancellationToken ct)
    {
        if (game.SelectedTarget is not { } target || services.Hdr is not { } hdr) return;
        await hdr.RemoveAsync(game.Info, target.Dir, ct);
        Log.Info($"{game.Name}: RenoDX HDR mod removed");
    }
}
