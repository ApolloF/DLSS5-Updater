using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// The "Luma" section of a game's Install tab: which Luma mod the game gets and why, what is installed, and the state of it. Plain
/// state for the view; the long work (install, remove) runs through <see cref="MainViewModel"/> so it shares the busy gate.
/// </summary>
public sealed partial class LumaViewModel(GameViewModel game, Services services) : ObservableObject
{
    public const string DgVoodooUrl = "https://github.com/dege-diosg/dgVoodoo2/releases";

    private LumaChoice? _choice;
    private LumaChoice? _picked;
    private bool _filling;
    private LumaStatus? _status;
    private LumaPicker? _manualPicker;
    private string _autoTitle = "";
    private string? _manualCurrent;
    private AutoPick? _modPick;

    /// <summary>Raised after every refresh; the main view model re-counts updates and re-checks its commands.</summary>
    public event Action? Changed;

    /// <summary>False without a catalog that has the luma section (and in the demo, which never installs): the section is hidden.</summary>
    public bool Visible => services.Catalog?.LumaPicker is not null && services.Luma is not null && !Demo.Active;

    public LumaChoice? Choice => _choice;
    public LumaStatus? Status => _status;

    private InstalledComponent? Installed => game.Manifest?.Component(ComponentRegistry.Luma);

    public string PickedText => _choice is { IsOffered: true } c ? $"{c.Title}. {c.Reason}" : _choice?.Reason ?? "";
    public bool HasPickedText => PickedText.Length > 0;

    public string Notes => string.Join(Environment.NewLine, _choice?.Notes ?? []);
    public bool HasNotes => Notes.Length > 0;

    public string StatusLine => _status is { State: HdrState.Unavailable } or null ? "" : _status.Line;
    public bool HasStatusLine => StatusLine.Length > 0;

    public string InstalledVersion => _status?.Installed ?? "—";
    public string LatestVersion => _status?.Latest ?? "—";
    public bool NeedsUpdate => _status?.UpdateAvailable == true;

    public bool CanInstall => _choice is { IsOffered: true } && game.SelectedTarget is not null;
    public bool CanRemove => Installed is not null && game.SelectedTarget is not null;

    public string ActionText => _status?.State switch
    {
        HdrState.Update => "Update",
        HdrState.Current => "Reinstall",
        _ => "Install",
    };

    public string? Link => _choice?.Link;
    public bool HasLink => Link is not null && Link.StartsWith("https://", StringComparison.Ordinal);

    [RelayCommand]
    private void OpenLink()
    {
        if (Link is { } url) MainViewModel.OpenUrl(url);
    }

    // ---------- the clash with RenoDX HDR ----------

    /// <summary>Shown while RenoDX HDR is in the game and Luma is, or could be, as well; the install asks for a confirmation.</summary>
    public string ClashWarning => game.Manifest?.Component(ComponentRegistry.RenoDxHdr) is not null && (Installed is not null || CanInstall) ? LumaConflict.Message : "";
    public bool HasClashWarning => ClashWarning.Length > 0;

    /// <summary>The install must ask first: RenoDX HDR is in the game and the user hasn't confirmed this pair yet.</summary>
    public bool NeedsClashConfirm => LumaConflict.ForLuma(game.Manifest) is not null && !LumaConflict.Confirmed(game.Manifest);

    /// <summary>Update all skips a pair of mods whose clash the user hasn't confirmed.</summary>
    public bool ClashBlocksUpdate => LumaConflict.Clashes(game.Manifest) && !LumaConflict.Confirmed(game.Manifest);

    // ---------- the mod ----------

    public AutoPick ModPick => _modPick ??= new("Luma mod for this game", "The mod this game gets. Auto picks by the game's exact name; pick another to use it instead.", PickMod);

    [ObservableProperty] private string _modSearch = "";

    partial void OnModSearchChanged(string value) => RefillMods();

    [ObservableProperty] private string _importProblem = "";

    private static string CustomTag(string sha) => LumaManual.CustomMod + ":" + sha;

    /// <summary>The user chose in the mod list: null = auto, "custom:&lt;sha&gt;" = an imported zip, else a catalog mod id or "unreal".</summary>
    internal void PickMod(string? tag)
    {
        if (_filling) return;
        var over = services.Settings.For(game.Id);
        var wasCustom = over.LumaMod == LumaManual.CustomMod;
        if (tag is not null && tag.StartsWith(LumaManual.CustomMod + ":", StringComparison.Ordinal))
        {
            over.SetLumaMod(LumaManual.CustomMod);
            over.SetBuildPick(ComponentRegistry.Luma, tag[(LumaManual.CustomMod.Length + 1)..]);
        }
        else
        {
            over.SetLumaMod(tag);
            // The pinned build belonged to the imported zip.
            if (wasCustom) over.SetBuildPick(ComponentRegistry.Luma, null);
        }
        services.Settings.Save();
        game.RefreshHdr();
    }

    [RelayCommand]
    private async Task ImportZip()
    {
        var dlg = new OpenFileDialog { Filter = "Luma zip|*.zip|All files|*.*", Title = "Select a Luma zip (Luma-<Game>.zip)" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var build = await Task.Run(() => services.Store.ImportLumaBuild(dlg.FileName));
            PickMod(CustomTag(build.Id));
            Log.Info($"{game.Name}: using the imported Luma zip {build.FileName}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Error($"Could not import {dlg.FileName}", ex);
            ImportProblem = ex.Message;
        }
    }

    private void RefillMods()
    {
        if (_manualPicker is null)
        {
            ModPick.Fill("catalog not loaded", [], null);
            return;
        }
        var all = _manualPicker.ManualChoices().Select(c => new DlssChoice(c.Id, c.Name))
            .Concat(services.Store.LumaBuilds.Where(b => b.Imported).Select(b => new DlssChoice(CustomTag(b.Id), $"Imported: {b.FileName}")));
        var needle = ModSearch.Trim();
        var shown = all.Where(c => needle.Length == 0 || c.Label.Contains(needle, StringComparison.OrdinalIgnoreCase) || c.Tag == _manualCurrent).ToList();
        _filling = true;
        ModPick.Fill(_autoTitle, shown, _manualCurrent);
        _filling = false;
    }

    // ---------- build ----------

    public ObservableCollection<DlssChoice> BuildChoices { get; } = [];

    [ObservableProperty] private DlssChoice? _buildChoice;

    public bool ShowBuildChoices => _picked is { IsOffered: true, Kind: not LumaKind.Custom };

    partial void OnBuildChoiceChanged(DlssChoice? value)
    {
        if (_filling) return;
        services.Settings.For(game.Id).SetBuildPick(ComponentRegistry.Luma, value?.Tag);
        services.Settings.Save();
        game.RefreshHdr();
    }

    private void FillBuildChoices(string? pin, IReadOnlyList<LumaBuild> library)
    {
        _filling = true;
        BuildChoices.Clear();
        if (_picked is { IsOffered: true, Kind: not LumaKind.Custom } picked)
        {
            BuildChoices.Add(new DlssChoice(null, $"Default · {picked.Version}"));
            foreach (var b in LumaPin.Candidates(picked, library)) BuildChoices.Add(new DlssChoice(b.Id, b.Version));
            if (pin is not null && BuildChoices.All(c => c.Tag != pin)) BuildChoices.Add(new DlssChoice(pin, "Pinned build not in the library"));
            BuildChoice = BuildChoices.FirstOrDefault(c => c.Tag == pin) ?? BuildChoices[0];
        }
        else BuildChoice = null;
        _filling = false;
    }

    // ---------- HDR output ----------

    public ObservableCollection<DlssChoice> HdrChoices { get; } =
    [
        new DlssChoice(null, "Auto (Luma decides from the display)"),
        new DlssChoice("on", "On (HDR output)"),
        new DlssChoice("off", "Off (SDR)"),
    ];

    [ObservableProperty] private DlssChoice? _hdrChoice;

    public bool ShowHdrChoice => _picked is { IsOffered: true };

    public string HdrHint => "Writes EnableHDR and DisplayMode in the [Luma] section of ReShade.ini. Auto writes nothing: Luma sets it from the display on its first start.";

    partial void OnHdrChoiceChanged(DlssChoice? value)
    {
        if (_filling || value is null) return;
        services.Settings.For(game.Id).SetLumaHdr(value.Tag switch { "on" => true, "off" => false, _ => null });
        services.Settings.Save();
        game.RefreshHdr();
    }

    private bool? Hdr => services.Settings.Games.GetValueOrDefault(game.Id)?.LumaHdr;

    // ---------- Engine.ini lines, launch arguments, dgVoodoo ----------

    public string EngineIniText => string.Join(Environment.NewLine, (_choice?.EngineIni ?? []).Select(k => $"[{k.Section}] {k.Key}={k.Value}"));
    public bool HasEngineIni => _choice is { EngineIni.Count: > 0 };

    public string LaunchArgsText => string.Join(" ", _choice?.LaunchArgs ?? []);
    public bool HasLaunchArgs => _choice is { IsOffered: true, LaunchArgs.Count: > 0 };
    public string LaunchArgsHint => "Add these to the game's launch options (Steam: Properties → Launch options). The app can't set them for you.";

    [RelayCommand]
    private void CopyLaunchArgs()
    {
        if (LaunchArgsText.Length == 0) return;
        try { System.Windows.Clipboard.SetText(LaunchArgsText); }
        catch (System.Runtime.InteropServices.COMException ex) { Log.Warn($"Could not copy the launch arguments: {ex.Message}"); }
    }

    public string DgVoodooText => string.Join(Environment.NewLine, _choice?.DgVoodooLines ?? []);
    public bool HasDgVoodoo => _choice is { NeedsDgVoodoo: true };

    [RelayCommand]
    private void OpenDgVoodoo() => MainViewModel.OpenUrl(DgVoodooUrl);

    // ---------- refresh ----------

    /// <summary>Re-picks and re-compares with the install record. Call after a scan, a manifest read or a change of catalog or settings.</summary>
    public void Refresh()
    {
        if (!Visible)
        {
            _choice = _picked = null;
            _status = null;
            return;
        }
        var picker = services.Catalog!.LumaPicker!;
        var over = services.Settings.Games.GetValueOrDefault(game.Id);
        var manual = over?.LumaMod;
        var pin = over?.BuildPick(ComponentRegistry.Luma);
        var library = services.Store.LumaBuilds;
        if (manual == LumaManual.CustomMod)
        {
            _picked = _choice = game.Info.AntiCheat is { } ac
                ? new LumaChoice { Reason = $"{ac} anti-cheat: no add-on is installed in games with anti-cheat." }
                : library.FirstOrDefault(b => b.Id.Equals(pin, StringComparison.OrdinalIgnoreCase) && b.Imported) is { } mine
                    ? LumaManual.CustomChoice(mine, game.Info.Effective.Bits)
                    : LumaManual.MissingCustom();
        }
        else
        {
            _picked = picker.Pick(game.Info, manual);
            _choice = LumaPin.Apply(_picked, pin, library);
        }
        FillBuildChoices(pin, library);
        _filling = true;
        HdrChoice = HdrChoices.First(c => c.Tag == (over?.LumaHdr switch { true => "on", false => "off", _ => null }));
        _filling = false;

        ImportProblem = "";
        _manualPicker = picker;
        _autoTitle = picker.Pick(game.Info) is { IsOffered: true } auto ? auto.Title : "nothing offered";
        _manualCurrent = manual == LumaManual.CustomMod ? pin is { } sha ? CustomTag(sha) : null : manual;
        RefillMods();
        _status = LumaStatus.Of(_choice, Installed, Hdr);
        OnPropertyChanged(string.Empty);
        Changed?.Invoke();
    }

    // ---------- actions ----------

    /// <summary>Installs or updates the mod this game is picked for. Throws what the service throws (anti-cheat, Vulkan without the layer, offline).</summary>
    internal async Task InstallAsync(IProgress<TransferProgress>? progress, CancellationToken ct, bool clashConfirmed = false)
    {
        if (_choice is not { IsOffered: true } choice || game.SelectedTarget is not { } target || services.Luma is not { } luma)
            throw new InvalidOperationException(_choice?.Reason ?? "Nothing is offered for this game.");
        var settings = services.Settings;
        var over = settings.Games.GetValueOrDefault(game.Id);
        var hint = EngineIniLocator.HintFor(over, null);
        var reshade = new HdrReShade(game.ReShadeChoice?.Tag, over?.Proxy, GameConfig.EffectiveReShade(settings, game.Id), ReShadeIniTemplate.Load());
        await luma.InstallAsync(game.Info, target.Dir, choice, over?.LumaHdr, hint, reshade, clashConfirmed || LumaConflict.Confirmed(game.Manifest), progress, ct);
        Log.Info($"{game.Name}: {choice.Title} {choice.Version} installed");
    }

    internal async Task RemoveAsync(CancellationToken ct)
    {
        if (game.SelectedTarget is not { } target || services.Luma is not { } luma) return;
        await luma.RemoveAsync(game.Info, target.Dir, ct);
        Log.Info($"{game.Name}: Luma mod removed");
    }
}
