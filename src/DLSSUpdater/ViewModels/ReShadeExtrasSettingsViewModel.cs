using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>A preset in the library, as the settings list shows it.</summary>
public sealed record PresetRow(string Name, string Detail);

/// <summary>
/// Settings → Add-ons &amp; shaders: the catalog's ReShade add-ons and shader packs with search, category and licence, the app-wide
/// selection (a game follows it until it has its own), imports of the user's own add-ons, packs and presets. Logic lives in
/// <see cref="ReShadeExtras"/>, <see cref="ExtrasPicks"/>, <see cref="ExtrasLibrary"/> and <see cref="ReShadePresets"/>.
/// </summary>
public sealed partial class ReShadeExtrasSettingsViewModel : ObservableObject
{
    private readonly Services _s;
    private bool _loading, _notifying;

    public ReShadeExtrasSettingsViewModel(Services services)
    {
        _s = services;
        PackCategories =
        [
            new OptionChoice("All categories", null), new OptionChoice("HDR", "hdr"), new OptionChoice("General", "general"), new OptionChoice("Color", "color"),
            new OptionChoice("Depth & 3D", "depth-3d"), new OptionChoice("Cinematic", "cinematic"), new OptionChoice("Imported", "imported"),
        ];
        _packCategory = PackCategories[0];
        if (Extras is { } extras) extras.Changed += () =>
        {
            if (!_notifying) Reload();
        };
        Reload();
    }

    private ReShadeExtras? Extras => _s.Extras;
    private AppSettings Settings => _s.Settings;

    public ObservableCollection<ExtrasItemViewModel> Addons { get; } = [];
    public ObservableCollection<ExtrasItemViewModel> Packs { get; } = [];
    public ObservableCollection<PresetRow> Presets { get; } = [];
    /// <summary>What the user imported (add-ons and packs), with a way to remove each.</summary>
    public ObservableCollection<ExtrasItemViewModel> Imports { get; } = [];
    public IReadOnlyList<OptionChoice> PackCategories { get; }

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private OptionChoice _packCategory;
    [ObservableProperty] private PresetRow? _selectedPreset;
    [ObservableProperty] private string _catalogText = "";
    [ObservableProperty] private string _addonsSummary = "";
    [ObservableProperty] private string _packsSummary = "";
    [ObservableProperty] private string _packsMode = "";
    [ObservableProperty] private bool _packsOwn;
    [ObservableProperty] private bool _addonsAny;
    [ObservableProperty] private bool _packsAny;

    partial void OnFilterChanged(string value) => Reload();
    partial void OnPackCategoryChanged(OptionChoice value) => Reload();

    public void Reload()
    {
        if (Extras is not { } extras) return;
        _loading = true;
        try
        {
            var catalog = extras.Catalog;
            var text = Filter.Trim();
            bool Matches(string name, string? description, string? extra) =>
                text.Length == 0 || new[] { name, description, extra }.Any(s => s?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);

            var wantedAddons = ExtrasPicks.AddonIds(Settings, null);
            Addons.Clear();
            foreach (var a in catalog.Addons.Where(a => Matches(a.Name, a.Description, a.Author ?? a.License)))
            {
                var note = ExtrasItemViewModel.AddonNote(a, null);
                Addons.Add(new ExtrasItemViewModel(a.Id, a.Name, a.Description, ExtrasItemViewModel.AddonDetail(a), note, note is null, a.Imported, "", wantedAddons.Contains(a.Id), OnAddonToggled));
            }

            var wantedPacks = ExtrasPicks.PackIds(Settings, null, catalog.PackList);
            Packs.Clear();
            foreach (var p in catalog.Packs.Where(p => Matches(p.Name, p.Description, p.License ?? p.Repo)
                                                       && (PackCategory.Value is null || (PackCategory.Value == "imported" ? p.Imported : p.Category == PackCategory.Value))))
                Packs.Add(new ExtrasItemViewModel(p.Id, p.Name, p.Description, ExtrasItemViewModel.PackDetail(p), p.IsInstallable ? null : "The catalog has no verified download for it.", p.IsInstallable,
                    p.Imported, p.Category, wantedPacks.Contains(p.Id), OnPackToggled));

            var selected = SelectedPreset?.Name;
            Presets.Clear();
            foreach (var p in ReShadePresets.List())
                Presets.Add(new PresetRow(p.Name, p.Check.Valid ? $"{p.Check.Techniques.Count} effects switched on, {p.Check.Effects.Count} effect files" : $"not valid: {p.Check.Problem}"));
            SelectedPreset = Presets.FirstOrDefault(p => p.Name == selected);

            Imports.Clear();
            foreach (var a in catalog.Addons.Where(a => a.Imported)) Imports.Add(new ExtrasItemViewModel(a.Id, a.Name, null, "add-on", null, true, true, "", false, null));
            foreach (var p in catalog.Packs.Where(p => p.Imported)) Imports.Add(new ExtrasItemViewModel(p.Id, p.Name, null, "shader pack", null, true, true, "", false, null));

            RefreshTexts();
            CatalogText = catalog.IsEmpty ? "The catalog isn't available (offline, and no earlier copy). Your imports still work."
                : extras.CatalogNote is { } why ? $"Catalog: last good copy ({why})." : "Catalog: up to date.";
        }
        finally { _loading = false; }
    }

    /// <summary>The summary lines under the lists; they change on every tick, the lists don't.</summary>
    private void RefreshTexts()
    {
        if (Extras is not { } extras) return;
        var catalog = extras.Catalog;
        var wantedAddons = ExtrasPicks.AddonIds(Settings, null);
        var wantedPacks = ExtrasPicks.PackIds(Settings, null, catalog.PackList);
        AddonsAny = catalog.Addons.Count > 0;
        AddonsSummary = catalog.Addons.Count == 0 ? "No add-ons yet." : $"{wantedAddons.Count} of {catalog.Addons.Count} selected for all games. A game can pick its own list on its Add-ons & shaders tab.";
        PacksAny = catalog.Packs.Count > 0;
        PacksOwn = Settings.PickOf(ComponentRegistry.ReShadeShaders)?.Options?.ContainsKey(ExtrasPicks.PacksKey) == true;
        PacksMode = PacksOwn ? $"Your selection: {wantedPacks.Count} of {catalog.Packs.Count} packs."
            : $"Auto: the HDR essentials ({string.Join(", ", catalog.Packs.Where(p => p.Default).Select(p => p.Name))}).";
        PacksSummary = catalog.Packs.Count == 0 ? "No shader packs yet." : $"{catalog.Packs.Count} packs";
    }

    private void OnAddonToggled(ExtrasItemViewModel item)
    {
        if (_loading) return;
        var set = ExtrasPicks.AddonIds(Settings, null);
        if (item.Selected) set.Add(item.Id); else set.Remove(item.Id);
        ExtrasPicks.SetAddons(Settings, set);
        Saved();
    }

    private void OnPackToggled(ExtrasItemViewModel item)
    {
        if (_loading || Extras is null) return;
        var set = ExtrasPicks.PackIds(Settings, null, Extras.Catalog.PackList);
        if (item.Selected) set.Add(item.Id); else set.Remove(item.Id);
        ExtrasPicks.SetPacks(Settings, set);
        Saved();
    }

    private void Saved()
    {
        Settings.Save();
        // The lists are already right; only the games (and the texts) need to hear about it.
        _notifying = true;
        Extras?.NotifyPicksChanged();
        _notifying = false;
        RefreshTexts();
    }

    [RelayCommand]
    private void ResetPacks()
    {
        Settings.Components = ComponentPicks.SetOptions(Settings.Components, ComponentRegistry.ReShadeShaders, null);
        Settings.Save();
        Extras?.NotifyPicksChanged();
    }

    [RelayCommand]
    private async Task RefreshCatalog()
    {
        if (Extras is null) return;
        await Extras.LoadCatalogAsync(default);
    }

    // ---- imports ----

    [RelayCommand]
    private async Task ImportAddon()
    {
        var dlg = new OpenFileDialog { Filter = "ReShade add-on|*.addon;*.addon32;*.addon64;*.zip|All files|*.*", Title = "Select an add-on file (.addon, .addon32, .addon64) or a .zip that holds some" };
        if (dlg.ShowDialog() == true) await Import(() => ExtrasLibrary.ImportAddon(dlg.FileName), selectAddon: true);
    }

    [RelayCommand]
    private async Task ImportPackFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Select a folder with shaders (a Shaders folder with .fx files, or the .fx files themselves)" };
        if (dlg.ShowDialog() == true) await Import(() => ExtrasLibrary.ImportPack(dlg.FolderName), selectAddon: false);
    }

    [RelayCommand]
    private async Task ImportPackZip()
    {
        var dlg = new OpenFileDialog { Filter = "Shader pack|*.zip|All files|*.*", Title = "Select a .zip with shaders" };
        if (dlg.ShowDialog() == true) await Import(() => ExtrasLibrary.ImportPack(dlg.FileName), selectAddon: false);
    }

    private async Task Import(Func<string> import, bool selectAddon)
    {
        try
        {
            var id = await Task.Run(import);
            Extras?.Reload();
            Reload();
            Log.Info($"Imported {id}");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Import failed", ex);
            Views.Dialog.Show("Import failed", UserErrors.Message(ex));
        }
    }

    [RelayCommand]
    private void RemoveImport(ExtrasItemViewModel? item)
    {
        if (item is not { Imported: true }) return;
        if (!Views.Dialog.Confirm("Remove import", $"Remove {item.Name} from the library? Games that already have it keep their copy until you update them.", "Remove", danger: true)) return;
        ExtrasLibrary.Remove(item.Id);
        var addons = ExtrasPicks.AddonIds(Settings, null);
        if (addons.Remove(item.Id)) ExtrasPicks.SetAddons(Settings, addons);
        if (Settings.PickOf(ComponentRegistry.ReShadeShaders)?.Options?.ContainsKey(ExtrasPicks.PacksKey) == true)
        {
            var packs = ExtrasPicks.PackIds(Settings, null, null);
            if (packs.Remove(item.Id)) ExtrasPicks.SetPacks(Settings, packs);
        }
        Settings.Save();
        Extras?.Reload();
    }

    [RelayCommand]
    private async Task ImportPreset()
    {
        var dlg = new OpenFileDialog { Filter = "ReShade preset|*.ini|All files|*.*", Title = "Select a ReShade preset (.ini)", Multiselect = true };
        if (dlg.ShowDialog() != true) return;
        foreach (var file in dlg.FileNames)
            try { await Task.Run(() => ReShadePresets.Import(file)); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                Log.Error("Import of a preset failed", ex);
                Views.Dialog.Show("Import failed", UserErrors.Message(ex));
            }
        Extras?.NotifyPicksChanged();
        Reload();
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset is not { } preset) return;
        if (!Views.Dialog.Confirm("Delete preset", $"Delete {preset.Name} from the presets library? Games that already have it keep their copy.", "Delete", danger: true)) return;
        ReShadePresets.Delete(preset.Name);
        Extras?.NotifyPicksChanged();
        Reload();
    }
}
