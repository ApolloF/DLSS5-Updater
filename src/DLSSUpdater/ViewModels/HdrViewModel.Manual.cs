using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>A row of the HDR mod's ini editor: one value written to reshade.ini ([renodx] sections) or Engine.ini, winning over ours.</summary>
public sealed partial class HdrIniRow(HdrIniEdit edit, Action changed) : ObservableObject
{
    public static readonly IReadOnlyList<string> Files = [HdrIniEdit.ReShadeFile, HdrIniEdit.EngineFile];

    public string File { get => edit.File; set { edit.File = value; Touched(); } }
    public string Section { get => edit.Section; set { edit.Section = value; Touched(); } }
    public string Key { get => edit.Key; set { edit.Key = value; Touched(); } }
    public string Value { get => edit.Value; set { edit.Value = value; Touched(); } }

    public HdrIniEdit Edit => edit;

    /// <summary>Why the row is ignored, or empty when it will be written.</summary>
    public string Problem => edit.IsValid ? ""
        : edit.IsReShade && edit.Section.Trim().Length > 0 && !edit.Section.Trim().StartsWith("renodx", StringComparison.OrdinalIgnoreCase)
            ? "Only [renodx...] sections of reshade.ini can be set here."
            : "Needs a section and a key without [ ] = ; and a one-line value.";

    public bool HasProblem => Problem.Length > 0;

    private void Touched()
    {
        OnPropertyChanged(string.Empty);
        changed();
    }
}

public sealed partial class HdrViewModel
{
    private bool _fillingManual;
    private string _autoTitle = "";
    private HdrPicker? _manualPicker;
    private string? _manualCurrent;

    /// <summary>"Auto (X)" first, then every catalog mod (filtered by <see cref="ModSearch"/>) and the add-ons you imported.</summary>
    public AutoPick ModPick => _modPick ??= new("Mod for this game", "The mod this game gets. Auto picks by Steam AppID, then name, then engine; pick another to use it instead.", PickMod);
    private AutoPick? _modPick;

    [ObservableProperty] private string _modSearch = "";

    partial void OnModSearchChanged(string value) => RefillMods();

    public ObservableCollection<HdrIniRow> IniRows { get; } = [];

    public bool ShowManual => CatalogReady;
    public bool IsUnreal => game.Info.Effective.IsUnreal;

    [ObservableProperty] private string _engineIniProblem = "";

    /// <summary>Where Engine.ini is used now, for Unreal games.</summary>
    public string EngineIniNow
    {
        get
        {
            if (!IsUnreal) return "";
            var over = services.Settings.Games.GetValueOrDefault(game.Id);
            var hint = EngineIniLocator.HintFor(over, services.Catalog?.Document?.Sections.Renodx?.Overrides.EngineIniPath(game.Info.SteamAppId, game.Info.Name));
            return FrameLimitService.ResolveEngineIni(game.Info, hint) is { } t
                ? $"Used now: {t.Path} ({t.Source}{(t.Exists ? "" : "; not created yet")})"
                : "The Engine.ini couldn't be found: the Unreal project wasn't detected. Type its path to use one.";
        }
    }

    /// <summary>Engine.ini (or its folder) typed by the user; empty = automatic.</summary>
    public string EngineIniText
    {
        get => services.Settings.Games.GetValueOrDefault(game.Id)?.EngineIniPath ?? "";
        set
        {
            var text = value.Trim();
            var local = InstallManifest.KnownFolder("|localappdata|") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var docs = InstallManifest.KnownFolder("|documents|") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            EngineIniProblem = EngineIniLocator.Problem(game.Info, text, local, docs) ?? "";
            if (EngineIniProblem.Length > 0) return;
            services.Settings.For(game.Id).SetEngineIniPath(text);
            services.Settings.Save();
            game.RefreshHdr();
        }
    }

    public bool HasEngineIniOverride => EngineIniText.Length > 0;

    [RelayCommand]
    private void BrowseEngineIni()
    {
        var dlg = new OpenFileDialog { Filter = "Engine.ini|*.ini|All files|*.*", Title = "Select the game's Engine.ini", CheckFileExists = false };
        if (dlg.ShowDialog() == true) EngineIniText = dlg.FileName;
        OnPropertyChanged(nameof(EngineIniText));
    }

    [RelayCommand]
    private void ResetEngineIni()
    {
        EngineIniProblem = "";
        services.Settings.For(game.Id).SetEngineIniPath(null);
        services.Settings.Save();
        game.RefreshHdr();
    }

    [RelayCommand]
    private async Task ImportAddon()
    {
        var dlg = new OpenFileDialog { Filter = "ReShade add-on|*.addon64;*.addon32|All files|*.*", Title = "Select a RenoDX (or other) HDR add-on" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var build = await Task.Run(() => services.Store.ImportHdrBuild(dlg.FileName));
            PickMod(CustomTag(build.Id));
            Log.Info($"{game.Name}: using the imported add-on {build.FileName}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Error($"Could not import {dlg.FileName}", ex);
            ImportProblem = ex.Message;
        }
    }

    [ObservableProperty] private string _importProblem = "";

    [RelayCommand]
    private void AddIniRow()
    {
        IniRows.Add(new HdrIniRow(new HdrIniEdit(HdrIniEdit.ReShadeFile, "renodx", "", ""), SaveIniRows));
        SaveIniRows();
    }

    [RelayCommand]
    private void RemoveIniRow(HdrIniRow? row)
    {
        if (row is null) return;
        IniRows.Remove(row);
        SaveIniRows();
    }

    private void SaveIniRows()
    {
        if (_fillingManual) return;
        services.Settings.For(game.Id).HdrIni = IniRows.Count == 0 ? null : IniRows.Select(r => r.Edit).ToList();
        services.Settings.Save();
        game.RefreshHdr();
    }

    private static string CustomTag(string sha) => HdrManual.CustomMod + ":" + sha;

    /// <summary>The user chose in the mod list: null = auto, "custom:&lt;sha&gt;" = an imported add-on, else a catalog or generic id.</summary>
    internal void PickMod(string? tag)
    {
        if (_fillingManual) return;
        var over = services.Settings.For(game.Id);
        var wasCustom = over.HdrMod == HdrManual.CustomMod;
        if (tag is not null && tag.StartsWith(HdrManual.CustomMod + ":", StringComparison.Ordinal))
        {
            over.SetHdrMod(HdrManual.CustomMod);
            over.SetBuildPick(ComponentRegistry.RenoDxHdr, tag[(HdrManual.CustomMod.Length + 1)..]);
        }
        else
        {
            over.SetHdrMod(tag);
            // The pinned build belonged to the imported add-on.
            if (wasCustom) over.SetBuildPick(ComponentRegistry.RenoDxHdr, null);
        }
        services.Settings.Save();
        game.RefreshHdr();
    }

    private void FillManual(HdrPicker? picker, bool? ue, GameOverride? over)
    {
        ImportProblem = "";
        _manualPicker = picker;
        _autoTitle = picker?.Pick(game.Info, ue) is { IsOffered: true } auto ? auto.Title : "nothing offered";
        _manualCurrent = over?.HdrMod == HdrManual.CustomMod
            ? over.BuildPick(ComponentRegistry.RenoDxHdr) is { } sha ? CustomTag(sha) : null
            : over?.HdrMod;

        _fillingManual = true;
        IniRows.Clear();
        foreach (var e in over?.HdrIni ?? []) IniRows.Add(new HdrIniRow(e, SaveIniRows));
        _fillingManual = false;

        RefillMods();
        OnPropertyChanged(nameof(EngineIniText));
        OnPropertyChanged(nameof(EngineIniNow));
        OnPropertyChanged(nameof(HasEngineIniOverride));
        OnPropertyChanged(nameof(IsUnreal));
        OnPropertyChanged(nameof(ShowManual));
    }

    private void RefillMods()
    {
        if (_manualPicker is null)
        {
            ModPick.Fill("catalog not loaded", [], null);
            return;
        }
        var all = _manualPicker.ManualChoices().Select(c => new DlssChoice(c.Id, c.Name))
            .Concat(services.Store.HdrBuilds.Where(b => b.ModId == HdrManual.ImportedModId)
                .Select(b => new DlssChoice(CustomTag(b.Id), $"Imported: {b.FileName}")));
        var needle = ModSearch.Trim();
        var shown = all.Where(c => needle.Length == 0 || c.Label.Contains(needle, StringComparison.OrdinalIgnoreCase) || c.Tag == _manualCurrent).ToList();
        _fillingManual = true;
        ModPick.Fill(_autoTitle, shown, _manualCurrent);
        _fillingManual = false;
    }
}
