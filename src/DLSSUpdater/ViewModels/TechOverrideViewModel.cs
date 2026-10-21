using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// The "Detection" section of a game's Install tab: what the scan found for graphics API, bitness, engine, Unreal version, main exe and
/// mod folder, each overridable by hand. The values live in <see cref="GameOverride.Tech"/>; every consumer reads
/// <see cref="GameInfo.Effective"/>, so a change here reaches the installer, the advisor, HDR matching, ReShade naming and the limiters.
/// </summary>
public sealed partial class TechOverrideViewModel : ObservableObject
{
    private readonly GameViewModel _game;
    private readonly Services _services;

    public TechOverrideViewModel(GameViewModel game, Services services)
    {
        _game = game;
        _services = services;
        Api = new AutoPick("Graphics API", "Which API the game renders with. Decides the file name ReShade loads as and which mods fit.",
            v => Set(t => t with { Apis = v is null ? null : (GraphicsApis)int.Parse(v) }));
        Bits = new AutoPick("Bitness", "32 or 64-bit. Decides which build of ReShade, the add-ons and the limiters is used.",
            v => Set(t => t with { Bits = v is null ? null : int.Parse(v) }));
        Engine = new AutoPick("Engine", "The game engine. Decides which RenoDX add-on fits and whether the Unreal frame cap is offered.",
            v => Set(t => t with { Engine = v is null ? null : Enum.Parse<GameEngine>(v), EngineVersion = null }));
        UeVersion = new AutoPick("Unreal version", "Unreal 5 games get HDR switched on in Engine.ini; Unreal 4 games take the upgrade path.",
            v => Set(t => t with { EngineVersion = v }));
        MainExe = new AutoPick("Main executable", "The exe the app treats as the game: the next scan reads API and bitness from it, and the driver profile matches on it.",
            v => Set(t => t with { MainExe = v }, rescan: true));
        ModFolder = new AutoPick("Mod folder", "Where mods and ReShade go by default.",
            v => Set(t => t with { ModFolder = v }));
    }

    public AutoPick Api { get; }
    public AutoPick Bits { get; }
    public AutoPick Engine { get; }
    public AutoPick UeVersion { get; }
    public AutoPick MainExe { get; }
    public AutoPick ModFolder { get; }

    public IReadOnlyList<AutoPick> Rows => [Api, Bits, Engine, UeVersion, MainExe, ModFolder];

    public bool AnySet => _game.Info.Manual is { IsEmpty: false };

    public bool Visible => !Demo.Active;

    /// <summary>One line with what the app uses now, for the section header.</summary>
    public string Summary => _game.Info.Effective.Label is { Length: > 0 } l ? l : "nothing detected";

    /// <summary>Rebuilds every picker from the scan and the saved overrides.</summary>
    public void Refresh()
    {
        var info = _game.Info;
        var detected = info.Tech;
        var manual = info.Manual;

        Api.Fill(TechOverrides.ApiText(detected.Apis) + (detected.ApisGuessed ? ", guessed" : ""),
            TechOverrides.ApiChoices.Select(a => new DlssChoice(((int)a).ToString(), GraphicsApiInfo.List(a).Replace(", ", " + "))),
            manual?.Apis is { } apis ? ((int)apis).ToString() : null);
        Bits.Fill(detected.Bits == 0 ? "not read" : detected.Bits + "-bit",
            [new DlssChoice("64", "64-bit"), new DlssChoice("32", "32-bit")], manual?.Bits?.ToString());
        Engine.Fill(TechOverrides.EngineText(detected.Engine),
            Enum.GetValues<GameEngine>().Where(e => e != GameEngine.Unknown).Select(e => new DlssChoice(e.ToString(), TechOverrides.EngineText(e))),
            manual?.Engine?.ToString());
        UeVersion.Fill(detected.EngineVersion ?? "unknown", TechOverrides.UnrealVersions.Select(v => new DlssChoice(v, v)), manual?.EngineVersion);
        UeVersion.Visible = info.Effective.Engine == GameEngine.Unreal;

        MainExe.Fill(info.Exes.FirstOrDefault() is { } first ? Path.GetFileName(first) : "none found",
            info.Exes.Take(12).Select(e => new DlssChoice(e, Path.GetRelativePath(info.Root, e))), manual?.MainExe);
        ModFolder.Fill(Rel(detected.ModFolder) ?? "the exe's folder",
            ModFolderCandidates(info).Select(d => new DlssChoice(d, Rel(d) ?? d)), manual?.ModFolder);

        OnPropertyChanged(nameof(AnySet));
        OnPropertyChanged(nameof(Summary));
    }

    private string? Rel(string? path) =>
        path is null ? null : FileUtil.IsUnder(path, _game.Info.Root) ? Path.GetRelativePath(_game.Info.Root, path) : path;

    private static IEnumerable<string> ModFolderCandidates(GameInfo info) =>
        info.Exes.Select(Path.GetDirectoryName).OfType<string>().Append(info.Root).Distinct(StringComparer.OrdinalIgnoreCase).Take(12);

    private void Set(Func<TechOverride, TechOverride> change, bool rescan = false)
    {
        TechOverrides.Update(_services.Settings, _game.Info, change);
        _services.Settings.Save();
        Apply(rescan);
    }

    // Deferred: this runs inside a ComboBox selection change, and the reload refills the lists of the same boxes.
    private void Apply(bool rescan)
    {
        void Run()
        {
            if (rescan && _services.Rescan is { } scan) _ = scan(_game);
            else _game.Load(_game.Info);
            Refresh();
        }
        if (System.Windows.Application.Current?.Dispatcher is { } d) d.BeginInvoke(Run);
        else Run();
    }

    [RelayCommand]
    private void ResetAll()
    {
        var hadExe = _game.Info.Manual?.MainExe is not null;
        TechOverrides.ResetAll(_services.Settings, _game.Info);
        _services.Settings.Save();
        Apply(hadExe);
    }

    [RelayCommand]
    private void BrowseExe()
    {
        var dlg = new OpenFileDialog { Filter = "Game executable|*.exe", Title = "Select the game's main executable", InitialDirectory = _game.Info.Root };
        if (dlg.ShowDialog() != true) return;
        if (!FileUtil.IsUnder(dlg.FileName, _game.Info.Root))
        {
            Log.Warn($"{_game.Name}: {dlg.FileName} is outside the game folder, so it isn't used.");
            return;
        }
        Set(t => t with { MainExe = FileUtil.Normalize(dlg.FileName) }, rescan: true);
    }

    [RelayCommand]
    private void BrowseModFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Folder the mods go into", InitialDirectory = _game.Info.Root };
        if (dlg.ShowDialog() == true) Set(t => t with { ModFolder = FileUtil.Normalize(dlg.FolderName) });
    }
}
