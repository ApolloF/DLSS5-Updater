using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.Views;
using Microsoft.Win32;
using Component = DLSSUpdater.Core.Component;

namespace DLSSUpdater.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private readonly BusyGate _busy = new();
    /// <summary>Work asked for while another run was going; it starts when that run ends.</summary>
    private bool _refreshPending, _rescanPending;
    /// <summary>Held from construction until <see cref="InitializeAsync"/> hands it to the startup run.</summary>
    private BusyGate.Lease? _startup;

    public MainViewModel()
    {
        AppPaths.Ensure();
        Log.TrimFile();
        var settings = AppSettings.Load();
        var gh = new GitHubClient { Token = settings.GitHubToken };
        var store = new ComponentStore(gh, () => settings.IncludePrereleases) { AutoReShade = () => settings.AutoDownloadReShade, SelectedDlssNr = () => settings.DlssNrId, SelectedRenoDx = () => settings.RenoDxId, SelectedLecram = () => settings.LecramId, PairTests = () => settings.AddonPairTests, OptiSourceRepo = () => settings.OptiSource };
        S = new Services { Settings = settings, Store = store, Installer = new Installer(store) };
        Gh = gh;
        _busy.Changed += () => IsBusy = _busy.IsBusy;
        // Busy from the start: no command may take the gate before the startup scan and update check have run.
        _startup = _busy.TryEnter();
        SettingsVm = new SettingsViewModel(this);
        store.PairStatusChanged += () =>
        {
            foreach (var game in Games) game.RefreshAddonPairStatus();
            SettingsVm.RefreshAddonPairStatus();
        };
        OptiNews = new ReleaseNewsViewModel(Component.OptiScaler, OptiSources.For(settings.OptiSource).Name, settings);
        DlssNews = new ReleaseNewsViewModel(Component.Dlss, "DLSS", settings);
        UpliftNews = new ReleaseNewsViewModel(Component.Uplift, "GITC Uplift", settings);
        MfgNews = new ReleaseNewsViewModel(Component.MfgUnlock, "MFG Unlock", settings);
        StreamlineNews = new ReleaseNewsViewModel(Component.Streamline, "Streamline", settings);
        ReShadeNews = new ReleaseNewsViewModel(Component.ReShade, "ReShade", settings);

        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = o => o is GameViewModel g && Matches(g);
        GamesView.SortDescriptions.Add(new SortDescription(nameof(GameViewModel.HasDlss), ListSortDirection.Descending));
        GamesView.SortDescriptions.Add(new SortDescription(nameof(GameViewModel.Name), ListSortDirection.Ascending));

        Log.Line += line => _ui.BeginInvoke(() =>
        {
            AppendLogLine(LogLines, line);
            LastLog = line;
        });
    }

    public Services S { get; }
    public GitHubClient Gh { get; }
    public SettingsViewModel SettingsVm { get; }
    public ObservableCollection<GameViewModel> Games { get; } = [];
    public ICollectionView GamesView { get; }
    public ObservableCollection<string> LogLines { get; } = [];
    public ReleaseNewsViewModel OptiNews { get; }

    /// <summary>The selected OptiScaler build's name, shared by every label that names it.</summary>
    public string OptiName => OptiSources.For(S.Settings.OptiSource).Name;
    public string OptiChipTitle => OptiName.ToUpperInvariant();
    public ReleaseNewsViewModel DlssNews { get; }
    public ReleaseNewsViewModel UpliftNews { get; }
    public ReleaseNewsViewModel MfgNews { get; }
    public ReleaseNewsViewModel StreamlineNews { get; }
    public ReleaseNewsViewModel ReShadeNews { get; }

    internal const int MaxLogLines = 400;

    /// <summary>Adds a log line and drops the oldest ones beyond <see cref="MaxLogLines"/>.</summary>
    internal static void AppendLogLine(IList<string> lines, string line)
    {
        lines.Add(line);
        while (lines.Count > MaxLogLines) lines.RemoveAt(0);
    }

    [ObservableProperty] private GameViewModel? _selectedGame;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _filter = "All";
    [ObservableProperty] private string _lastLog = "";
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _progressIndeterminate;
    [ObservableProperty] private bool _progressVisible;
    [ObservableProperty] private bool _settingsOpen;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogButtonName))]
    private bool _logOpen;

    public string LogButtonName => LogOpen ? "Hide log" : "Show log";
    [ObservableProperty] private bool _aboutOpen;
    [ObservableProperty] private string _streamlineVersion = "—";
    /// <summary>Guide entry to scroll to when the About page opens from an ⓘ button.</summary>
    [ObservableProperty] private string? _helpTarget;
    /// <summary>The guide was opened from Settings; Back returns to the same tab and scroll position.</summary>
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private CompatReport? _compat;
    [ObservableProperty] private bool _compatWarning;

    public event Action? LeavingSettings;
    public event Action? ReturnedToSettings;

    /// <summary>Guide on the About page: every help topic plus the choices of the option it belongs to.</summary>
    public IReadOnlyList<GuideEntry> Guide => _guide ??= HelpTopics.All
        .Select(t => new GuideEntry(t, SettingsVm.AllOptions.Count(o => o.Topic == t.Id) == 1
                                       && SettingsVm.AllOptions.First(o => o.Topic == t.Id) is { IsToggle: false } opt
            ? opt.Choices.Where(c => c.Description is not null && c.Value is not null).ToList()
            : t.Id == "ini-mode" ? SettingsOptions.IniModes.ToList()
            : NvSettings.Create().FirstOrDefault(o => o.Topic == t.Id) is { } nv ? nv.Choices.Where(c => c.Value is not null).ToList()
            : []))
        .ToList();
    private List<GuideEntry>? _guide;

    public string AppVersion => "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdates), nameof(UpdateAllName))]
    private int _updateCount;

    public bool HasUpdates => UpdateCount > 0;

    /// <summary>What a screen reader says for Update all, which otherwise shows the count in a coloured badge only.</summary>
    public string UpdateAllName => UpdateCount switch
    {
        0 => "Update all",
        1 => "Update all, 1 update",
        _ => $"Update all, {UpdateCount} updates",
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(DlssOnlyCommand), nameof(UninstallCommand),
        nameof(RestoreDlssCommand), nameof(SwitchNrCommand), nameof(UndoNrCommand), nameof(UpdateAllCommand), nameof(CheckUpdatesCommand), nameof(RescanCommand),
        nameof(AddGameFolderCommand), nameof(AddLibraryRootCommand), nameof(RemoveGameCommand))]
    private bool _isBusy;

    // component chips
    [ObservableProperty] private string _optiVersion = "—";
    [ObservableProperty] private string _dlssVersion = "—";
    [ObservableProperty] private string _upliftVersion = "\u2014";
    [ObservableProperty] private string _mfgVersion = "—";
    [ObservableProperty] private string _dlssNrText = "not imported";
    [ObservableProperty] private string _reShadeText = "not imported";
    [ObservableProperty] private bool _dlssNrOk;
    [ObservableProperty] private bool _reShadeOk;
    [ObservableProperty] private bool _online = true;

    partial void OnSearchChanged(string value) => RefreshGames();

    partial void OnSettingsOpenChanged(bool value)
    {
        if (value)
        {
            AboutOpen = false;
            SettingsVm.Reload();
            return;
        }
        SettingsVm.Commit();
        foreach (var g in Games)
        {
            g.RebuildDlssChoices();
            g.RefreshConfig();
            g.RefreshStatus();
        }
        CountUpdates();
    }

    partial void OnAboutOpenChanged(bool value)
    {
        if (!value)
        {
            HelpTarget = null;
            CanGoBack = false;
        }
        if (value && SettingsOpen) SettingsOpen = false;
    }
    partial void OnFilterChanged(string value) => RefreshGames();

    /// <summary>Why the game list is empty, or null while it shows games.</summary>
    public string? GameListEmptyText => EmptyListText(Games.Count, GamesView.Cast<object>().Count(), IsScanning);

    internal static string? EmptyListText(int total, int shown, bool scanning) =>
        shown > 0 ? null
        : total > 0 ? "No games match the search or filter."
        : scanning ? "Looking for games…"
        : "No games found yet. Add a game or a library folder below.";

    private void RefreshGames()
    {
        GamesView.Refresh();
        OnPropertyChanged(nameof(GameListEmptyText));
    }

    /// <summary>A game scan is running; "Looking for games…" must not show for other busy work.</summary>
    [ObservableProperty] private bool _isScanning;

    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(GameListEmptyText));

    private bool Matches(GameViewModel g)
    {
        if (Search.Length > 0 && !g.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)) return false;
        return Filter switch
        {
            "DLSS" => g.HasDlss,
            "Installed" => g.IsInstalled,
            "Updates" => g.NeedsUpdate,
            _ => true,
        };
    }

    // ---------- startup ----------

    public async Task InitializeAsync()
    {
        var lease = _startup;
        _startup = null;
        await Run("Starting", async ct =>
        {
            if (Demo.Active)
            {
                Demo.Seed(S.Store, S.Settings);
                RefreshBuildNames();
            }
            else
                await Task.Run(() =>
                {
                    HashCache.Load();
                    S.Store.AutoImport();
                });
            await RefreshLocalComponentsAsync();

            var cached = GameScanner.LoadCache();
            if (cached.Count > 0) ApplyScan(cached);

            await Task.WhenAll(CheckUpdatesCore(ct), ScanCore(ct));
        }, lease);
        if (UpdateCount > 0) Log.Info($"{UpdateCount} game(s) have updates available");
        if (!Demo.Active) await Task.Run(HashCache.Save);
    }

    public async Task RefreshLocalComponentsAsync()
    {
        var store = S.Store;
        var seen = S.Settings.NewestDlssNrSeen;
        if (store.AdoptNewerDlssNr(S.Settings) is { } newer) Log.Info($"DLSSNR default is now the newer {newer.Label}");
        var defaultsChanged = store.EnsureAddonDefaults(S.Settings);
        if (store.SeedTestedAddonPair(S.Settings) | defaultsChanged | (S.Settings.NewestDlssNrSeen != seen)) S.Settings.Save();
        var rsPath = store.CurrentReShadePath;
        var (nr, rs) = await Task.Run(() => (
            File.Exists(store.DlssNrPath) ? HashCache.Get(store.DlssNrPath) : null,
            rsPath is not null ? HashCache.Get(rsPath) : null));
        S.DlssNrSha = nr;
        S.ReShadeSha = rs;

        DlssNrOk = nr is not null;
        DlssNrText = nr is null ? "not imported"
            : FileUtil.Format(FileUtil.ReadVersion(store.DlssNrPath)) +
              (ComponentStore.KnownDlssNr.TryGetValue(nr, out var variant) ? $" · {variant.Split(" · ")[0]}" : " · unverified");
        var auto = S.Settings.AutoDownloadReShade && !store.ReShadeImported;
        ReShadeOk = rs is not null || (auto && store.ReShade is not null);
        ReShadeText = rsPath is not null ? FileUtil.Format(FileUtil.ReadVersion(rsPath)) + (auto ? " · auto-download" : "")
            : auto && store.ReShade is { } r ? $"{r.Tag} · auto-download"
            : auto ? "auto-download" : "not imported";
        // An imported ReShade doesn't follow releases, so there is nothing new to point at.
        ReShadeNews.Load(auto ? store.ReShade : null);
        foreach (var g in Games) { g.RebuildNrChoices(); g.RebuildAddonChoices(); g.RefreshStatus(); }
        CountUpdates();
        OnPropertyChanged(nameof(LecramVersion));
    }

    public string LecramVersion => S.Settings.LecramLastVersion ?? S.Store.LecramBuildFor(null)?.DetectedLabel ?? "not in library";

    /// <summary>Lecram check on its own (Add-on library "Check now", even with automatic checks off), as a run like any other.</summary>
    internal Task CheckLecramNowAsync() => Run("Checking Lecram", ct => CheckLecramAsync(ct, force: true));

    private async Task CheckLecramAsync(CancellationToken ct, bool force = false)
    {
        if (Demo.Active) return;
        await Task.Run(() => S.Store.CheckLecramAsync(S.Settings, ct, force));
        S.Settings.Save();
        await RefreshLocalComponentsAsync();
        SettingsVm.Reload();
    }

    // ---------- commands: global ----------

    private bool CanRun() => !IsBusy;

    /// <summary>The demo only knows its made-up games; a real folder would have no demo record to install into.</summary>
    private bool CanAddGames() => !IsBusy && !Demo.Active;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task CheckUpdates() => Run("Checking for updates", CheckUpdatesCore);

    private async Task CheckUpdatesCore(CancellationToken ct)
    {
        if (!Demo.Active)
        {
            Gh.Token = S.Settings.GitHubToken;
            await S.Store.RefreshAsync(ct);
            await CheckLecramAsync(ct);
        }
        var s = S.Store;
        Online = new[] { s.Opti, s.Dlss, s.Mfg, s.Uplift }.Any(r => r is { FromCache: false, Stale: false });
        OptiVersion = s.Opti is null ? "—" : s.Opti.Tag + (s.Opti.Prerelease ? " pre" : "");
        DlssVersion = s.Dlss?.Tag.TrimStart('v') ?? "—";
        UpliftVersion = s.Uplift?.Tag ?? "\u2014";
        MfgVersion = s.Mfg?.Tag ?? (File.Exists(Path.Combine(AppPaths.Components, ComponentStore.MfgFile)) ? "local" : "—");
        StreamlineVersion = s.Streamline?.Tag.TrimStart('v') ?? "—";
        OptiNews.Load(s.Opti);
        DlssNews.Load(s.Dlss);
        MfgNews.Load(s.Mfg);
        UpliftNews.Load(s.Uplift);
        StreamlineNews.Load(s.Streamline);
        foreach (var g in Games)
        {
            g.RebuildDlssChoices();
            g.RefreshStatus();
        }
        CountUpdates();
        await RefreshLocalComponentsAsync();
        if (!Demo.Active) await CheckCompatAsync(ct);
        Log.Info($"Latest: {OptiName} {OptiVersion} · DLSS {DlssVersion} · MFG Unlock {MfgVersion}{(Online ? "" : " (offline)")}");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Rescan() => Run("Scanning games", ScanCore);

    private async Task ScanCore(CancellationToken ct)
    {
        var progress = new Progress<int>(p => Status = $"Scanning games  {p}%");
        IsScanning = true;
        List<GameInfo> games;
        try { games = Demo.Active ? Demo.Games : await GameScanner.ScanAsync(S.Settings, progress, ct); }
        finally { IsScanning = false; }
        ApplyScan(games);
        Log.Info($"Found {games.Count} games, {games.Count(g => g.Dlss.Count > 0)} with DLSS");
    }

    private void ApplyScan(List<GameInfo> games)
    {
        var selectedId = SelectedGame?.Id;
        MergeById(Games, games, g => g.Id, i => i.Id, (vm, info) => vm.Load(info), info => new GameViewModel(info, S));
        RefreshGames();
        CountUpdates();
        SelectedGame = Games.FirstOrDefault(g => g.Id == selectedId) ?? GamesView.Cast<GameViewModel>().FirstOrDefault();
    }

    /// <summary>
    /// Makes <paramref name="list"/> hold one item per id in <paramref name="incoming"/>: existing items are updated, new
    /// ones created, and items whose id is gone or that duplicate an earlier one are removed.
    /// </summary>
    internal static void MergeById<TItem, TInfo>(IList<TItem> list, IEnumerable<TInfo> incoming, Func<TItem, string> itemId,
        Func<TInfo, string> infoId, Action<TItem, TInfo> update, Func<TInfo, TItem> create)
    {
        var byId = new Dictionary<string, TItem>();
        for (var i = 0; i < list.Count;)
        {
            if (byId.TryAdd(itemId(list[i]), list[i])) i++;
            else list.RemoveAt(i);
        }
        var seen = new HashSet<string>();
        foreach (var info in incoming)
        {
            var id = infoId(info);
            if (!seen.Add(id)) continue;
            if (byId.TryGetValue(id, out var item)) update(item, info);
            else list.Add(create(info));
        }
        for (var i = list.Count - 1; i >= 0; i--)
            if (!seen.Contains(itemId(list[i]))) list.RemoveAt(i);
    }

    private void CountUpdates() => UpdateCount = Games.Count(g => g.NeedsUpdate);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task UpdateAll() => Run("Updating", async ct =>
    {
        var targets = Games.Where(g => g.NeedsUpdate && g.Manifest is not null).ToList();
        if (targets.Count == 0)
        {
            Log.Info("Everything is up to date");
            return;
        }
        if (targets.Any(g => g.Manifest!.Opti) && !ConfirmCompat()) return;
        var failed = 0;
        foreach (var g in targets)
        {
            var m = g.Manifest!;
            var o = g.UpdateOptions(m);
            if (g.HasAntiCheat && g.NeedsInjection(o) && !m.AntiCheatConfirmed)
            {
                Log.Info($"{g.Name}: skipped because it has anti-cheat. Install it once from its own page to confirm.");
                continue;
            }
            try
            {
                Log.Info($"Updating {g.Name}");
                await S.Installer.InstallAsync(g.Info, g.SelectedTarget!.Dir, o, TransferProgress(), ct);
                await RescanOne(g);
            }
            catch (NeedsAdminException ex) { failed++; Log.Error($"{g.Name}: {ex.Message}"); }
            catch (Exception ex) when (ex is not OperationCanceledException) { failed++; Log.Error(g.Name, ex); }
        }
        Log.Info(failed == 0 ? $"Updated {targets.Count} game(s)" : $"{failed} game(s) couldn't be updated. Details are in the log.");
    });

    [RelayCommand(CanExecute = nameof(CanAddGames))]
    private async Task AddGameFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Select a game folder" };
        if (dlg.ShowDialog() != true) return;
        var path = FileUtil.Normalize(dlg.FolderName);
        if (!S.Settings.ManualGames.Contains(path, StringComparer.OrdinalIgnoreCase)) S.Settings.ManualGames.Add(path);
        S.Settings.HiddenGames.RemoveAll(h => h.Equals(GameInfo.MakeId(path), StringComparison.OrdinalIgnoreCase));
        S.Settings.Save();

        await Run("Adding game", async _ =>
        {
            var existing = Games.FirstOrDefault(g => g.Id == GameInfo.MakeId(path));
            if (existing is not null)
            {
                SelectedGame = existing;
                return;
            }
            var info = await Task.Run(() => GameScanner.Inspect(new GameEntry(Path.GetFileName(path), path, "Manual")));
            var vm = new GameViewModel(info, S);
            Games.Add(vm);
            OnPropertyChanged(nameof(GameListEmptyText));
            SaveGameCache();
            Filter = "All";
            SelectedGame = vm;
            Log.Info($"Added {info.Name}");
        });
    }

    [RelayCommand(CanExecute = nameof(CanAddGames))]
    private async Task AddLibraryRoot()
    {
        var dlg = new OpenFolderDialog { Title = "Select a folder that contains game folders" };
        if (dlg.ShowDialog() != true) return;
        var path = FileUtil.Normalize(dlg.FolderName);
        if (!S.Settings.LibraryRoots.Contains(path, StringComparer.OrdinalIgnoreCase)) S.Settings.LibraryRoots.Add(path);
        S.Settings.Save();
        await Run("Scanning games", ScanCore);
    }

    [RelayCommand]
    private void ToggleSettings() => SettingsOpen = !SettingsOpen;

    [RelayCommand]
    private void ToggleLog() => LogOpen = !LogOpen;

    [RelayCommand]
    private void ToggleAbout() => AboutOpen = !AboutOpen;

    /// <summary>A click on a game, even the one already selected, leaves Settings / About for that game.</summary>
    public void ShowGameView()
    {
        SettingsOpen = false;
        AboutOpen = false;
    }

    /// <summary>Leaves the About / guide page: back to the settings tab it was opened from, otherwise to the game view.</summary>
    [RelayCommand]
    private void Back()
    {
        if (!CanGoBack)
        {
            AboutOpen = false;
            return;
        }
        SettingsOpen = true;
        ReturnedToSettings?.Invoke();
    }

    /// <summary>A preset was added, replaced or removed: every game's base list and the Settings preset list follow.</summary>
    public void RefreshPresets()
    {
        foreach (var g in Games) g.RefreshConfig();
        SettingsVm.RefreshPresetNames();
    }

    /// <summary>
    /// The OptiScaler build changed: hide options it lacks, then re-resolve releases and the config check. When a run is
    /// going, the refresh waits for it, so no install pairs the old build's binary with the new build's options.
    /// </summary>
    public void OptiSourceChanged()
    {
        SettingsVm.ApplyOptiSource();
        RefreshBuildNames();
        _refreshPending = true;
        StartPendingWork();
    }

    /// <summary>Renames every label that names the OptiScaler build: the chip, the Settings tab, and each game's mode and rows.</summary>
    private void RefreshBuildNames()
    {
        OptiNews.Name = OptiName;
        OnPropertyChanged(nameof(OptiName));
        OnPropertyChanged(nameof(OptiChipTitle));
        SettingsVm.RefreshBuildName();
        foreach (var g in Games) g.RefreshBuildName();
    }

    /// <summary>Rescans now, or right after the current run.</summary>
    public void RequestRescan()
    {
        _rescanPending = true;
        StartPendingWork();
    }

    private void StartPendingWork()
    {
        if (_busy.IsBusy) return;
        if (_refreshPending)
        {
            _refreshPending = false;
            _ = CheckUpdates();
        }
        else if (_rescanPending)
        {
            _rescanPending = false;
            _ = Rescan();
        }
    }

    /// <summary>Every OptiScaler.ini key this app can write: profile, keybinds and friendly options.</summary>
    private IEnumerable<(string Section, string Key)> ManagedOptiKeys() =>
        S.Settings.IniOverrides.Select(o => (o.Section, o.Key))
            .Concat(S.Settings.Presets.SelectMany(p => p.Opti).Select(o => (o.Section, o.Key)))
            .Concat(S.Settings.Games.Values.SelectMany(g => (g.Opti ?? []).Concat(g.Presets?.SelectMany(p => p.Opti) ?? [])).Select(o => (o.Section, o.Key)))
            .Concat(ConfigProfile.Recommended().Select(o => (o.Section, o.Key)))
            .Concat(ConfigProfile.Required(new InstallOptions { Opti = true, ReShade = true, DlssNr = true }).Select(o => (o.Section, o.Key)))
            .Concat(KeybindDef.Opti.Select(k => (k.Section, k.Key)))
            .Concat(SettingsVm.NrOptions.Concat(SettingsVm.TonemapOptions).Concat(SettingsVm.HdrOptions).Where(o => o.IsAvailable).Select(o => (o.Section, o.Key)));

    private async Task CheckCompatAsync(CancellationToken ct)
    {
        if (S.Store.Opti is not { } opti) return;
        try
        {
            Compat = await OptiCompat.CheckAsync(Gh, opti.Tag, ManagedOptiKeys(), ct);
            CompatWarning = Compat.HasIssues || Compat.ChangedKeys.Count > 0;
            if (CompatWarning) Log.Info("Config check: " + Compat.Summary.Replace("\n", " "));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Info($"Config check skipped ({ex.Message})");
        }
    }

    /// <summary>Asks before installing an OptiScaler release whose config doesn't match what this app writes.</summary>
    private bool ConfirmCompat()
    {
        if (Compat is not { HasIssues: true } c) return true;
        return Dialog.Confirm("OptiScaler config changed", c.Summary + "\n\nInstall anyway? Settings that still exist are applied normally.", "Install anyway");
    }

    [RelayCommand]
    private void ShowHelp(string topic)
    {
        var fromSettings = SettingsOpen;
        if (fromSettings) LeavingSettings?.Invoke();
        AboutOpen = true;
        CanGoBack = fromSettings;
        HelpTarget = null;
        HelpTarget = topic;
    }

    [RelayCommand]
    internal static void OpenUrl(string url)
    {
        if (!url.StartsWith("https://", StringComparison.Ordinal)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Win32Exception ex) { Log.Error($"Could not open {url}", ex); }
    }

    [RelayCommand]
    private void Cancel() => _busy.Cancel();

    // ---------- commands: selected game ----------

    private bool CanRunOnGame() => !IsBusy && SelectedGame is not null;
    private bool CanRunOnInstalled() => !IsBusy && SelectedGame?.Manifest is not null;

    partial void OnSelectedGameChanged(GameViewModel? value)
    {
        InstallCommand.NotifyCanExecuteChanged();
        DlssOnlyCommand.NotifyCanExecuteChanged();
        UninstallCommand.NotifyCanExecuteChanged();
        RestoreDlssCommand.NotifyCanExecuteChanged();
        SwitchNrCommand.NotifyCanExecuteChanged();
        UndoNrCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunOnGame))]
    private Task Install() => InstallSelected(dlssOnly: false);

    [RelayCommand(CanExecute = nameof(CanRunOnGame))]
    private Task DlssOnly() => InstallSelected(dlssOnly: true);

    private async Task InstallSelected(bool dlssOnly)
    {
        var g = SelectedGame;
        if (g?.SelectedTarget is null) return;
        var o = g.BuildOptions(dlssOnly, acConfirmed: g.Manifest?.AntiCheatConfirmed == true);

        if (g.HasAntiCheat && g.NeedsInjection(o) && !o.AntiCheatConfirmed)
        {
            var ok = Dialog.Confirm($"{g.AntiCheat} detected",
                $"{g.Name} ships {g.AntiCheat}. Injecting OptiScaler, ReShade or add-ons into a game protected by anti-cheat can get your account banned, " +
                "and online modes will usually refuse to start.\n\nOnly continue for offline / single-player use.",
                "Install anyway", danger: true);
            if (!ok) return;
            o = g.BuildOptions(dlssOnly, acConfirmed: true);
        }

        if (o.Opti && !ConfirmCompat()) return;

        if (!dlssOnly && o.Opti && g.SelectedTarget.Exe is null && !g.Info.Exes.Any())
            Log.Info($"{g.Name}: no game .exe found in the install location. OptiScaler only works next to the game's .exe; pick that folder under Install location.");

        await Run(dlssOnly ? $"Updating DLSS for {g.Name}" : $"Installing to {g.Name}", async ct =>
        {
            await S.Installer.InstallAsync(g.Info, g.SelectedTarget.Dir, o, TransferProgress(), ct);
            await RescanOne(g);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunOnInstalled))]
    private Task SwitchNr() => ChangeNr(undo: false);

    [RelayCommand(CanExecute = nameof(CanRunOnInstalled))]
    private Task UndoNr() => ChangeNr(undo: true);

    private Task ChangeNr(bool undo)
    {
        var g = SelectedGame;
        if (g?.SelectedTarget is null || !g.CanSwitchNr) return Task.CompletedTask;
        var id = undo ? g.Manifest?.PreviousDlssNrId : g.EffectiveNrId;
        if (undo && id is null) return Task.CompletedTask;
        return Run($"Switching NR for {g.Name}", async ct =>
        {
            await S.Installer.SwitchDlssNrAsync(g.Info, g.SelectedTarget.Dir, id, ct);
            if (undo)
            {
                S.Settings.For(g.Id).DlssNrId = id;
                S.Settings.Save();
            }
            await RescanOne(g);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunOnInstalled))]
    private async Task Uninstall()
    {
        var g = SelectedGame;
        if (g?.SelectedTarget is null) return;
        if (!Dialog.Confirm("Uninstall", $"Remove everything DLSS5 Updater installed in {g.Name} and restore the original files?", "Uninstall", danger: true))
            return;
        await Run($"Uninstalling from {g.Name}", async ct =>
        {
            await S.Installer.UninstallAsync(g.Info, g.SelectedTarget.Dir, ct);
            await RescanOne(g);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunOnInstalled))]
    private Task RestoreDlss()
    {
        var g = SelectedGame;
        if (g?.SelectedTarget is null) return Task.CompletedTask;
        return Run($"Restoring DLSS for {g.Name}", async ct =>
        {
            await S.Installer.RestoreDlssAsync(g.Info, g.SelectedTarget.Dir, ct);
            await RescanOne(g);
        });
    }

    // ---------- commands: game config ----------

    /// <summary>Saves the selected game's effective config as a preset, for this game only or for all games.</summary>
    [RelayCommand]
    private void SaveGamePreset()
    {
        if (SelectedGame is not { } g) return;
        if (Dialog.Prompt("Save as preset",
                $"Saves {g.Name}'s settings, with the changes made for this game, under a name. The preset becomes the game's base and its own changes are cleared.",
                g.PresetChoice?.Name ?? "My preset", "Only for this game (untick to let every game use it)", true) is not { } answer) return;
        var (name, gameOnly) = answer;
        if (GameConfig.SavePreset(S.Settings, g.Id, name, gameOnly) is { } error)
        {
            Dialog.Show("Save as preset", error);
            return;
        }
        S.Settings.Save();
        RefreshPresets();
        Log.Info($"Saved preset '{name}' ({(gameOnly ? "for " + g.Name : "all games")})");
    }

    [RelayCommand]
    private void DeleteGamePreset()
    {
        if (SelectedGame is not { PresetChoice: { GameOnly: true, Name: { } name } } g) return;
        if (!Dialog.Confirm("Delete preset", $"Delete '{name}'? {g.Name} falls back to the current settings.", "Delete", danger: true)) return;
        GameConfig.DeleteLocalPreset(S.Settings, g.Id, name);
        S.Settings.Save();
        RefreshPresets();
    }

    [RelayCommand]
    private void MakeGamePresetGlobal()
    {
        if (SelectedGame is not { PresetChoice: { GameOnly: true, Name: { } name } } g) return;
        if (GameConfig.MakeGlobal(S.Settings, g.Id, name) is { } error)
        {
            Dialog.Show("Make global", error);
            return;
        }
        S.Settings.Save();
        RefreshPresets();
        Log.Info($"Preset '{name}' is now available for all games");
    }

    [RelayCommand]
    private void ResetGameConfig()
    {
        if (SelectedGame is not { } g) return;
        if (!Dialog.Confirm("Reset game config", $"Remove every change made for {g.Name}? It follows its base again.", "Reset", danger: true)) return;
        GameConfig.ResetLayer(S.Settings.For(g.Id));
        S.Settings.Save();
        g.RefreshConfig();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var dir = SelectedGame?.SelectedTarget?.Dir ?? SelectedGame?.Root;
        if (dir is not null && Directory.Exists(dir)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void BrowseTarget()
    {
        var g = SelectedGame;
        if (g is null) return;
        var dlg = new OpenFolderDialog { Title = "Folder containing the game executable", InitialDirectory = g.Root };
        if (dlg.ShowDialog() == true) g.AddCustomTarget(FileUtil.Normalize(dlg.FolderName));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void RemoveGame()
    {
        var g = SelectedGame;
        if (g is null) return;
        if (g.IsInstalled && !Dialog.Confirm("Remove from list", $"{g.Name} still has DLSS5 Updater files installed. Remove it from the list anyway?", "Remove"))
            return;
        if (S.Settings.ManualGames.RemoveAll(p => GameInfo.MakeId(p) == g.Id) == 0)
            S.Settings.HiddenGames.Add(g.Id);
        S.Settings.Save();
        Games.Remove(g);
        OnPropertyChanged(nameof(GameListEmptyText));
        SaveGameCache();
        SelectedGame = GamesView.Cast<GameViewModel>().FirstOrDefault();
    }

    // ---------- helpers ----------

    private async Task RescanOne(GameViewModel g)
    {
        var info = Demo.Active ? Demo.Find(g.Root)! : await Task.Run(() => GameScanner.Inspect(new GameEntry(g.Name, g.Root, g.Source)));
        g.Load(info);
        RefreshGames();
        CountUpdates();
        SaveGameCache();
    }

    private void SaveGameCache() => GameScanner.SaveCache(Games.Select(g => g.Info).ToList());

    private IProgress<TransferProgress> TransferProgress() => new Progress<TransferProgress>(p =>
    {
        Status = p.Text;
        ProgressIndeterminate = p.Fraction is null;
        Progress = p.Fraction ?? 0;
    });

    /// <summary>Runs one operation with the busy state, progress and error handling. Skipped when another run is going.</summary>
    /// <param name="held">A lease the caller already holds (startup); otherwise one is taken here.</param>
    private async Task Run(string label, Func<CancellationToken, Task> work, BusyGate.Lease? held = null)
    {
        var lease = held ?? _busy.TryEnter();
        if (lease is null)
        {
            Log.Info($"{label}: skipped, another task is still running");
            return;
        }
        Status = label;
        ProgressVisible = true;
        ProgressIndeterminate = true;
        try
        {
            await work(lease.Token);
            // An install may have downloaded ReShade for the first time.
            if (S.Store.ReShade is not null && !S.Store.ReShadeImported) await RefreshLocalComponentsAsync();
            Status = "Ready";
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled";
            Log.Info("Cancelled");
        }
        catch (NeedsAdminException ex)
        {
            Status = "Needs administrator rights";
            Log.Error(ex.Message);
            if (Dialog.Confirm("Administrator rights needed", ex.Message, "Restart as admin")) App.RestartElevated();
        }
        catch (Exception ex)
        {
            Status = "Failed. See the log.";
            Log.Error(label, ex);
            Dialog.Show($"{label} didn't finish", UserErrors.Message(ex));
        }
        finally
        {
            ProgressVisible = false;
            ProgressIndeterminate = false;
            Progress = 0;
            lease.Dispose();
        }
        StartPendingWork();
    }
}
