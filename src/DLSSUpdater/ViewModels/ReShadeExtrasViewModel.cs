using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// A game's "Add-ons &amp; shaders" tab: the ReShade add-ons, shader packs and preset that game gets. Every list follows
/// Settings (Auto) until the game has its own pick, and Reset to auto goes back. The work is done by
/// <see cref="ReShadeExtras"/>; this only asks and shows.
/// </summary>
public sealed partial class ReShadeExtrasViewModel : ObservableObject
{
    private readonly GameViewModel _game;
    private readonly Services _s;
    private bool _loading;

    public ReShadeExtrasViewModel(GameViewModel game, Services services)
    {
        _game = game;
        _s = services;
        BitsChoices = [new OptionChoice("Auto", null, "The bitness found in the game's exe."), new OptionChoice("32-bit", "32", "Use the 32-bit builds (.addon32)."), new OptionChoice("64-bit", "64", "Use the 64-bit builds (.addon64).")];
        if (_s.Extras is { } extras) extras.Changed += Load;
    }

    private ReShadeExtras? Extras => _s.Extras;
    private AppSettings Settings => _s.Settings;
    private Scan.GameInfo Info => _game.Info;
    private GameOverride? Over => Settings.Games.GetValueOrDefault(_game.Id);
    private string? Target => _game.SelectedTarget?.Dir;

    public ObservableCollection<ExtrasItemViewModel> Addons { get; } = [];
    public ObservableCollection<ExtrasItemViewModel> Packs { get; } = [];
    /// <summary>The presets library; the first entry (empty value) is "No preset".</summary>
    public ObservableCollection<OptionChoice> Presets { get; } = [];
    public ObservableCollection<string> Notes { get; } = [];
    public IReadOnlyList<OptionChoice> BitsChoices { get; }

    /// <summary>Why nothing can be done in this game (anti-cheat, no ReShade), or null.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanUse), nameof(IsBlocked))] private string? _blocked;
    public bool IsBlocked => Blocked is not null;
    public bool CanUse => Blocked is null;

    [ObservableProperty] private string _status = "";
    public bool HasNotes => Notes.Count > 0;

    [ObservableProperty] private string _addonsMode = "";
    [ObservableProperty] private bool _addonsOwn;
    [ObservableProperty] private string _addonsInstalled = "";
    [ObservableProperty] private bool _addonsInstalledAny;
    [ObservableProperty] private string _packsMode = "";
    [ObservableProperty] private bool _packsOwn;
    [ObservableProperty] private string _packsInstalled = "";
    [ObservableProperty] private bool _packsInstalledAny;
    [ObservableProperty] private string _presetInstalled = "";
    [ObservableProperty] private bool _presetInstalledAny;
    [ObservableProperty] private string? _presetNeeds;
    [ObservableProperty] private bool _canAddPresetPacks;
    [ObservableProperty] private string _bitsText = "";
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _catalogMissing;
    [ObservableProperty] private bool _bitsOwn;

    private OptionChoice? _bitsChoice;

    public OptionChoice? BitsChoice
    {
        get => _bitsChoice;
        set
        {
            if (value is null || Equals(value, _bitsChoice) || _loading) return;
            _bitsChoice = value;
            ExtrasPicks.SetGameBits(Settings.For(_game.Id), value.Value is null ? null : int.Parse(value.Value));
            Settings.Save();
            OnPropertyChanged();
            Load();
        }
    }

    private string? _presetPick;

    /// <summary>The preset chosen for this game; empty = none.</summary>
    public string PresetPick
    {
        get => _presetPick ?? "";
        set
        {
            if (_loading || value is null || value == PresetPick) return;
            _presetPick = value.Length == 0 ? null : value;
            ExtrasPicks.SetGamePreset(Settings.For(_game.Id), _presetPick);
            Settings.Save();
            OnPropertyChanged();
            RefreshPreset();
        }
    }

    /// <summary>Rebuilds the lists and texts from the settings, the catalog and what the game has installed.</summary>
    public void Load()
    {
        if (Extras is null) return;
        _loading = true;
        try
        {
            var target = Target;
            var manifest = _game.Manifest;
            Blocked = target is null ? "This game has no install folder yet." : ReShadeExtras.Guard(Info, target, manifest);
            CatalogMissing = Extras.Catalog.IsEmpty;
            var bits = Extras.BitsOf(Info);
            var over = Over;
            BitsOwn = ExtrasPicks.BitsOf(over) is not null;
            _bitsChoice = BitsChoices.First(c => c.Value == ExtrasPicks.BitsOf(over)?.ToString());
            OnPropertyChanged(nameof(BitsChoice));
            BitsText = BitsOwn ? $"{bits}-bit chosen for this game" : $"Auto: {bits}-bit (found in the game's exe)";

            var addonsComponent = manifest?.Component(ComponentRegistry.ReShadeAddons);
            var packsComponent = manifest?.Component(ComponentRegistry.ReShadeShaders);
            var presetComponent = manifest?.Component(ComponentRegistry.ReShadePreset);

            var wantedAddons = ExtrasPicks.AddonIds(Settings, over);
            Addons.Clear();
            foreach (var a in Extras.Catalog.Addons)
            {
                var note = ExtrasItemViewModel.AddonNote(a, bits);
                Addons.Add(new ExtrasItemViewModel(a.Id, a.Name, a.Description, ExtrasItemViewModel.AddonDetail(a), note, note is null, a.Imported, "", wantedAddons.Contains(a.Id), OnAddonToggled)
                {
                    Installed = addonsComponent?.Extras.ContainsKey("placed:" + a.Id) == true,
                });
            }
            AddonsOwn = ExtrasPicks.GameHasAddons(over);
            AddonsMode = AddonsOwn ? "This game has its own list." : $"Auto: follows Settings ({wantedAddons.Count} selected).";
            AddonsInstalledAny = addonsComponent is not null;
            AddonsInstalled = addonsComponent is null ? "Nothing installed." : $"Installed: {addonsComponent.Version}.";

            var wantedPacks = ExtrasPicks.PackIds(Settings, over, Extras.Catalog.PackList);
            Packs.Clear();
            foreach (var p in Extras.Catalog.Packs)
            {
                Packs.Add(new ExtrasItemViewModel(p.Id, p.Name, p.Description, ExtrasItemViewModel.PackDetail(p), p.IsInstallable ? null : "The catalog has no verified download for it.", p.IsInstallable,
                    p.Imported, p.Category, wantedPacks.Contains(p.Id), OnPackToggled)
                {
                    Installed = packsComponent?.Extras.ContainsKey("placed:" + p.Id) == true,
                });
            }
            PacksOwn = ExtrasPicks.GameHasPacks(over);
            PacksMode = PacksOwn ? "This game has its own selection."
                : Settings.PickOf(ComponentRegistry.ReShadeShaders)?.Options?.ContainsKey(ExtrasPicks.PacksKey) == true ? $"Auto: follows Settings ({wantedPacks.Count} selected)."
                : $"Auto: the HDR essentials ({string.Join(", ", Extras.Catalog.Packs.Where(p => p.Default).Select(p => p.Name))}).";
            PacksInstalledAny = packsComponent is not null;
            PacksInstalled = packsComponent is null ? "Nothing installed." : $"Installed: {packsComponent.Version} in {packsComponent.Extras.GetValueOrDefault("folder") ?? ReShadeLayout.ShadersFolder}.";

            Presets.Clear();
            Presets.Add(new OptionChoice("No preset", ""));
            foreach (var p in ReShadePresets.List().Where(p => p.Check.Valid)) Presets.Add(new OptionChoice(p.Name, p.Name));
            _presetPick = ExtrasPicks.PresetOf(over);
            if (_presetPick is not null && Presets.All(c => c.Value != _presetPick)) Presets.Add(new OptionChoice(_presetPick + " (missing from the library)", _presetPick));
            OnPropertyChanged(nameof(PresetPick));
            PresetInstalledAny = presetComponent is not null;
            PresetInstalled = presetComponent is null ? "No preset applied." : $"Applied: {presetComponent.Version}.";

            UpdateAvailable = target is not null && manifest is not null && Extras.ComponentsToUpdate(Info, target, manifest).Count > 0;
        }
        finally { _loading = false; }
        RefreshPreset();
        OnPropertyChanged(nameof(HasNotes));
    }

    private void RefreshPreset()
    {
        PresetNeeds = null;
        CanAddPresetPacks = false;
        if (Extras is null || Target is null || Blocked is not null || ReShadePresets.Find(_presetPick) is not { Check.Valid: true } preset) return;
        var needs = Extras.NeedsOf(Info, Target, preset);
        if (needs.Missing.Count == 0) return;
        var text = $"This preset uses {needs.Missing.Count} shader file{(needs.Missing.Count == 1 ? "" : "s")} the game doesn't have: {string.Join(", ", needs.Missing.Take(6))}{(needs.Missing.Count > 6 ? ", …" : "")}.";
        if (needs.Suggested.Count > 0) text += $" Packs that have them: {string.Join(", ", needs.Suggested.Select(p => p.Name))}.";
        if (needs.NoPack.Count > 0) text += $" No pack has {string.Join(", ", needs.NoPack.Take(4))}{(needs.NoPack.Count > 4 ? ", …" : "")}.";
        PresetNeeds = text;
        CanAddPresetPacks = needs.Suggested.Count > 0;
    }

    // ---- ticks ----

    private void OnAddonToggled(ExtrasItemViewModel _)
    {
        if (_loading) return;
        ExtrasPicks.SetGameAddons(Settings.For(_game.Id), Addons.Where(a => a.Selected).Select(a => a.Id));
        Settings.Save();
        AddonsOwn = true;
        AddonsMode = "This game has its own list.";
    }

    private void OnPackToggled(ExtrasItemViewModel _)
    {
        if (_loading) return;
        ExtrasPicks.SetGamePacks(Settings.For(_game.Id), Packs.Where(p => p.Selected).Select(p => p.Id));
        Settings.Save();
        PacksOwn = true;
        PacksMode = "This game has its own selection.";
    }

    [RelayCommand]
    private void ResetAddons()
    {
        ExtrasPicks.SetGameAddons(Settings.For(_game.Id), null);
        Settings.Save();
        Load();
    }

    [RelayCommand]
    private void ResetPacks()
    {
        ExtrasPicks.SetGamePacks(Settings.For(_game.Id), null);
        Settings.Save();
        Load();
    }

    [RelayCommand]
    private void ResetBits() => BitsChoice = BitsChoices[0];

    // ---- actions ----

    private Task Job(string label, Func<string, IProgress<TransferProgress>?, CancellationToken, Task<ExtrasResult>> work)
    {
        if (_s.RunJob is null || Extras is null || Target is not { } target) return Task.CompletedTask;
        return _s.RunJob(label, async ct =>
        {
            var result = await work(target, _s.Progress?.Invoke(), ct);
            Show(result);
            if (_s.Rescan is not null) await _s.Rescan(_game);
            Load();
        });
    }

    private void Show(ExtrasResult result)
    {
        Status = result.Message;
        Notes.Clear();
        foreach (var n in result.Notes) Notes.Add(n);
        OnPropertyChanged(nameof(HasNotes));
        if (result.Ok) Log.Info($"{_game.Name}: {result.Message}");
        else Log.Warn($"{_game.Name}: {result.Message}");
        foreach (var n in result.Notes) Log.Info($"  {n}");
    }

    [RelayCommand(CanExecute = nameof(CanUse))]
    private Task ApplyAddons() => Job("Installing ReShade add-ons", (t, p, ct) => Extras!.ApplyAddonsAsync(Info, t, p, ct));

    [RelayCommand(CanExecute = nameof(CanUse))]
    private Task RemoveAddons() => Job("Removing ReShade add-ons", (t, _, ct) => Extras!.RemoveAsync(Info, t, ComponentRegistry.ReShadeAddons, ct));

    [RelayCommand(CanExecute = nameof(CanUse))]
    private Task ApplyPacks() => Job("Installing shader packs", (t, p, ct) => Extras!.ApplyShadersAsync(Info, t, p, ct));

    [RelayCommand(CanExecute = nameof(CanUse))]
    private Task RemovePacks() => Job("Removing shader packs", (t, _, ct) => Extras!.RemoveAsync(Info, t, ComponentRegistry.ReShadeShaders, ct));

    [RelayCommand(CanExecute = nameof(CanUse))]
    private Task ApplyPreset() => Job("Applying the preset", (t, _, ct) => Extras!.ApplyPresetAsync(Info, t, _presetPick, ct));

    [RelayCommand(CanExecute = nameof(CanUse))]
    private Task RemovePreset() => Job("Removing the preset", (t, _, ct) => Extras!.RemoveAsync(Info, t, ComponentRegistry.ReShadePreset, ct));

    /// <summary>Adds the packs that hold the preset's missing shaders to this game's selection and installs them.</summary>
    [RelayCommand(CanExecute = nameof(CanUse))]
    private Task AddPresetPacks()
    {
        if (Extras is null || Target is not { } target || ReShadePresets.Find(_presetPick) is not { Check.Valid: true } preset) return Task.CompletedTask;
        var suggested = Extras.NeedsOf(Info, target, preset).Suggested;
        if (suggested.Count == 0) return Task.CompletedTask;
        ExtrasPicks.SetGamePacks(Settings.For(_game.Id), ExtrasPicks.PackIds(Settings, Over, Extras.Catalog.PackList).Concat(suggested.Select(p => p.Id)));
        Settings.Save();
        Load();
        return ApplyPacks();
    }

    [RelayCommand(CanExecute = nameof(CanUse))]
    private Task UpdateExtras() => Job("Updating ReShade add-ons and shaders", async (t, p, ct) =>
    {
        var results = await Extras!.UpdateAsync(Info, t, p, ct);
        return new ExtrasResult(results.All(r => r.Ok), results.Count == 0 ? "Everything is up to date." : string.Join(" · ", results.Select(r => r.Message)), [.. results.SelectMany(r => r.Notes)]);
    });

    partial void OnBlockedChanged(string? value)
    {
        ApplyAddonsCommand.NotifyCanExecuteChanged();
        RemoveAddonsCommand.NotifyCanExecuteChanged();
        ApplyPacksCommand.NotifyCanExecuteChanged();
        RemovePacksCommand.NotifyCanExecuteChanged();
        ApplyPresetCommand.NotifyCanExecuteChanged();
        RemovePresetCommand.NotifyCanExecuteChanged();
        AddPresetPacksCommand.NotifyCanExecuteChanged();
        UpdateExtrasCommand.NotifyCanExecuteChanged();
    }
}
