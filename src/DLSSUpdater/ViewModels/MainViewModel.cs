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
        var store = new ComponentStore(gh, () => settings.IncludePrereleases) { AutoReShade = () => settings.AutoDownloadReShade, SelectedDlssNr = () => settings.DlssNrId, SelectedRenoDx = () => settings.RenoDxId, SelectedLecram = () => settings.LecramId, SelectedDfc = () => settings.DfcId, SelectedReShade = () => settings.ReShadeId, PairTests = () => settings.AddonPairTests, OptiSourceRepo = () => settings.OptiSource };
        var installer = new Installer(store);
        S = new Services
        {
            Settings = settings, Store = store, Installer = installer,
            Catalog = new CatalogState(new CatalogClient(gh)), Hdr = new HdrService(store, installer, new CatalogDownloader(gh)),
            FrameLimit = new FrameLimitService(store, installer, new CatalogDownloader(gh), NvidiaProfile.Real),
        };
        Gh = gh;
        S.Extras = new ReShadeExtras(S.Installer, settings, new CatalogDownloader(gh), ct => new CatalogClient(gh).LoadAsync(ct));
        S.RunJob = RunJob;
        S.Progress = TransferProgress;
        S.Rescan = RescanOne;
        S.Extras.Changed += () =>
        {
            foreach (var game in Games) game.RefreshExtrasUpdate();
            CountUpdates();
        };
        S.Catalog!.Changed += () =>
        {
            if (_ui.CheckAccess()) CatalogChanged();
            else _ui.BeginInvoke(CatalogChanged);
        };
        _busy.Changed += () => IsBusy = _busy.IsBusy;
        // Busy from the start: no command may take the gate before the startup scan and update check have run.
        _startup = _busy.TryEnter();
        SettingsVm = new SettingsViewModel(this);
        SelfUpdate = new AppUpdateViewModel(this);
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
    /// <summary>Updates of this app itself (title bar notice, Settings section).</summary>
    public AppUpdateViewModel SelfUpdate { get; }
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
    public bool IsPrivateBuild => BuildFlags.LecramChannel;
    public string PrivateBuildText => "Private build (Lecram channel on)";
    public string LecramStripTip => BuildFlags.LecramChannel
        ? "Lecram v11 update channel; builds are added to the library. Games keep their installed build."
        : "Lecram's DLSS5 add-on, imported by you. Games keep their installed build.";
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
        nameof(AddGameFolderCommand), nameof(AddLibraryRootCommand), nameof(RemoveGameCommand), nameof(InstallHdrCommand), nameof(RemoveHdrCommand), nameof(ApplyFrameLimitCommand), nameof(RemoveFrameLimitCommand))]
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
            "Updates" => HasUpdate(g),
            _ => true,
        };
    }

    // ---------- startup ----------

    /// <summary>The method advice of every game, after the GPU or the catalog changed.</summary>
    private void RefreshAdvice()
    {
        foreach (var g in Games) g.RefreshAdvice();
    }

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
            {
                S.Catalog?.LoadCached();
                await Task.Run(() =>
                {
                    HashCache.Load();
                    S.Store.AutoImport();
                    // A ReShade64.dll imported before the library keeps being what installs use: its build becomes the pinned default once.
                    if (S.Store.TakeLegacyReShadePin() is { } pin && S.Settings.ReShadeId is null)
                    {
                        S.Settings.ReShadeId = pin;
                        S.Settings.Save();
                    }
                });
            }
            await RefreshLocalComponentsAsync();
            if (!Demo.Active)
            {
                S.Gpu = await Task.Run(GpuDetector.Detect);
                RefreshAdvice();
            }

            var cached = GameScanner.LoadCache();
            if (cached.Count > 0) ApplyScan(cached);

            await Task.WhenAll(CheckUpdatesCore(ct), ScanCore(ct));
            // Only now are all install records known, which the cleanup must keep builds for.
            await AutoCleanUpAsync();
        }, lease);
        if (UpdateCount > 0) Log.Info($"{UpdateCount} game(s) have updates available");
        if (!Demo.Active) await Task.Run(HashCache.Save);
        await SelfUpdate.StartupAsync();
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
        SettingsVm.ReloadReleaseChoices();
        CountUpdates();
        OnPropertyChanged(nameof(LecramVersion));
    }

    public string LecramVersion => S.Settings.LecramLastVersion ?? S.Store.LecramBuildFor(null)?.DetectedLabel ?? "not in library";

    /// <summary>Lecram check on its own (Add-on library "Check now", even with automatic checks off), as a run like any other.</summary>
    internal Task CheckLecramNowAsync() => Run("Checking Lecram", async ct =>
    {
        await CheckLecramAsync(ct, force: true);
        await AutoCleanUpAsync();
    });

    /// <summary>
    /// Refreshes the mod catalog, then downloads ShortFuse's DLSS add-on from it when it has a newer build. Offline or refused
    /// downloads are logged and change nothing.
    /// </summary>
    private async Task CheckCatalogAsync(CancellationToken ct)
    {
        if (Demo.Active || S.Catalog is not { } catalog) return;
        try
        {
            await catalog.RefreshAsync(ct);
            var build = await S.Store.CheckRenoDxCatalogAsync(S.Settings, catalog.Document?.Sections.RenodxDlss, new CatalogDownloader(Gh), TransferProgress(), ct);
            if (build is null) return;
            S.Settings.Save();
            await RefreshLocalComponentsAsync();
            SettingsVm.Reload();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.Warn($"Catalog check skipped ({ex.Message})");
        }
    }

    /// <summary>The catalog has a new document: every game picks again and the update count follows.</summary>
    private void CatalogChanged()
    {
        foreach (var g in Games) g.RefreshHdr();
        RefreshAdvice();
        CountUpdates();
    }

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
            await CheckCatalogAsync(ct);
            await S.Extras!.LoadCatalogAsync(ct);
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
        MergeById(Games, games, g => g.Id, i => i.Id, (vm, info) => vm.Load(info), info => Track(new GameViewModel(info, S)));
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

    /// <summary>A game has something to update: its main install, its RenoDX HDR mod or its frame limiter add-on.</summary>
    internal static bool HasUpdate(GameViewModel g) => g.NeedsUpdate || g.NeedsHdrUpdate || g.NeedsFrameLimitUpdate;

    /// <summary>What Update all works through: games with a main update and an install record, and games whose HDR mod has an update.</summary>
    internal static List<GameViewModel> UpdateTargets(IEnumerable<GameViewModel> games) =>
        games.Where(g => (g.NeedsUpdate && g.Manifest is not null) || g.NeedsHdrUpdate || g.NeedsFrameLimitUpdate).ToList();

    private void CountUpdates() => UpdateCount = Games.Count(HasUpdate);

    /// <summary>An HDR section changed (new pick, install, catalog): the update count and the HDR buttons follow.</summary>
    private GameViewModel Track(GameViewModel g)
    {
        g.Hdr.Changed += () =>
        {
            CountUpdates();
            if (ReferenceEquals(g, SelectedGame)) NotifyHdrCommands();
        };
        g.FrameLimit.Changed += () =>
        {
            CountUpdates();
            if (ReferenceEquals(g, SelectedGame)) NotifyHdrCommands();
        };
        return g;
    }

    private void NotifyHdrCommands()
    {
        InstallHdrCommand.NotifyCanExecuteChanged();
        RemoveHdrCommand.NotifyCanExecuteChanged();
        ApplyFrameLimitCommand.NotifyCanExecuteChanged();
        RemoveFrameLimitCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task UpdateAll() => Run("Updating", async ct =>
    {
        var targets = UpdateTargets(Games);
        if (targets.Count == 0)
        {
            Log.Info("Everything is up to date");
            return;
        }
        if (targets.Any(g => g.CoreNeedsUpdate && g.Manifest is { Opti: true }) && !ConfirmCompat()) return;
        var failed = 0;
        foreach (var g in targets)
        {
            try
            {
                if (g.CoreNeedsUpdate && g.Manifest is { } m)
                {
                    var o = g.UpdateOptions(m);
                    if (g.HasAntiCheat && g.NeedsInjection(o) && !m.AntiCheatConfirmed)
                        Log.Info($"{g.Name}: skipped because it has anti-cheat. Install it once from its own page to confirm.");
                    else if (Installer.Conflicts(g.SelectedTarget!.Dir, o) is { Count: > 0 } clash && o.ConfirmedConflict != Installer.ConflictKey(clash))
                        Log.Info($"{g.Name}: skipped because its mods clash and the warning isn't confirmed. Install it once from its own page to confirm.");
                    else
                    {
                        Log.Info($"Updating {g.Name}");
                        await S.Installer.InstallAsync(g.Info, g.SelectedTarget!.Dir, o, TransferProgress(), ct);
                        await RescanOne(g);
                    }
                }
                // The ReShade add-ons, shader packs and preset this game already has follow the catalog and Settings.
                if (g.ExtrasToUpdate.Count > 0)
                {
                    foreach (var r in await S.Extras!.UpdateAsync(g.Info, g.SelectedTarget!.Dir, TransferProgress(), ct))
                    {
                        if (r.Ok) Log.Info($"{g.Name}: {r.Message}");
                        else Log.Warn($"{g.Name}: {r.Message}");
                        foreach (var n in r.Notes) Log.Info($"  {n}");
                    }
                    await RescanOne(g);
                }
                // Read again after the main update: it can change what the HDR section sees.
                if (g.NeedsHdrUpdate)
                {
                    if (g.HasAntiCheat)
                        Log.Info($"{g.Name}: the HDR mod was skipped because it has anti-cheat.");
                    else
                    {
                        Log.Info($"Updating the HDR mod in {g.Name}");
                        await g.Hdr.InstallAsync(TransferProgress(), ct);
                        await RescanOne(g);
                    }
                }
                if (g.NeedsFrameLimitUpdate)
                {
                    if (g.HasAntiCheat)
                        Log.Info($"{g.Name}: the frame limiter add-on was skipped because it has anti-cheat.");
                    else
                    {
                        Log.Info($"Updating the frame limiter add-on in {g.Name}");
                        await g.FrameLimit.ApplyAsync(TransferProgress(), ct, update: true);
                        await RescanOne(g);
                    }
                }
            }
            catch (NeedsAdminException ex) { failed++; Log.Error($"{g.Name}: {ex.Message}"); }
            catch (Exception ex) when (ex is not OperationCanceledException) { failed++; Log.Error(g.Name, ex); }
        }
        Log.Info(failed == 0 ? $"Updated {targets.Count} game(s)" : $"{failed} game(s) couldn't be updated. Details are in the log.");
        await AutoCleanUpAsync();
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
            var vm = Track(new GameViewModel(info, S));
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
        NotifyHdrCommands();
    }

    private bool CanRunHdr() => !IsBusy && SelectedGame?.Hdr is { CanInstall: true };
    private bool CanRemoveHdr() => !IsBusy && SelectedGame?.Hdr is { CanRemove: true };

    /// <summary>Installs or updates the RenoDX HDR mod picked for the selected game. Anti-cheat games are never offered one, so there is nothing to confirm.</summary>
    [RelayCommand(CanExecute = nameof(CanRunHdr))]
    private async Task InstallHdr()
    {
        var g = SelectedGame;
        if (g?.SelectedTarget is null || !g.Hdr.CanInstall) return;
        await Run($"Installing the HDR mod to {g.Name}", async ct =>
        {
            await g.Hdr.InstallAsync(TransferProgress(), ct);
            // The install may have put ReShade into a game that had none.
            await RescanOne(g);
            await AutoCleanUpAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanRemoveHdr))]
    private async Task RemoveHdr()
    {
        var g = SelectedGame;
        if (g?.SelectedTarget is null || !g.Hdr.CanRemove) return;
        await Run($"Removing the HDR mod from {g.Name}", async ct =>
        {
            await g.Hdr.RemoveAsync(ct);
            await RescanOne(g);
        });
    }

    private bool CanApplyFrameLimit() => !IsBusy && SelectedGame?.FrameLimit is { CanApply: true };
    private bool CanRemoveFrameLimit() => !IsBusy && SelectedGame?.FrameLimit is { CanRemove: true };

    /// <summary>Sets the frame limit chosen for the selected game (the method switch removes the previous limiter). Anti-cheat games only get the driver limiter, which places nothing.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyFrameLimit))]
    private async Task ApplyFrameLimit()
    {
        var g = SelectedGame;
        if (g?.SelectedTarget is null || !g.FrameLimit.CanApply) return;
        var confirmed = false;
        if (g.FrameLimit.NeedsAntiCheatConfirm)
        {
            if (!Dialog.Confirm($"{g.AntiCheat} detected", $"{g.FrameLimit.Note}\n\nOnly continue for offline / single-player use.", "Install anyway", danger: true)) return;
            confirmed = true;
        }
        await Run($"Setting the frame limit for {g.Name}", async ct =>
        {
            await g.FrameLimit.ApplyAsync(TransferProgress(), ct, antiCheatConfirmed: confirmed);
            // An add-on may have put ReShade into a game that had none.
            await RescanOne(g);
            await AutoCleanUpAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanRemoveFrameLimit))]
    private async Task RemoveFrameLimit()
    {
        var g = SelectedGame;
        if (g?.SelectedTarget is null || !g.FrameLimit.CanRemove) return;
        await Run($"Removing the frame limiter from {g.Name}", async ct =>
        {
            await g.FrameLimit.RemoveAsync(ct);
            await RescanOne(g);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunOnGame))]
    private Task Install() => InstallSelected(dlssOnly: false);

    [RelayCommand(CanExecute = nameof(CanRunOnGame))]
    private Task DlssOnly() => InstallSelected(dlssOnly: true);

    private async Task InstallSelected(bool dlssOnly)
    {
        var g = SelectedGame;
        if (g?.SelectedTarget is null) return;
        var o = g.BuildOptions(dlssOnly, acConfirmed: g.Manifest?.AntiCheatConfirmed == true, g.Manifest?.ConfirmedConflict);

        if (g.HasAntiCheat && g.NeedsInjection(o) && !o.AntiCheatConfirmed)
        {
            var ok = Dialog.Confirm($"{g.AntiCheat} detected",
                $"{g.Name} ships {g.AntiCheat}. Injecting OptiScaler, ReShade or add-ons into a game protected by anti-cheat can get your account banned, " +
                "and online modes will usually refuse to start.\n\nOnly continue for offline / single-player use.",
                "Install anyway", danger: true);
            if (!ok) return;
            o = g.BuildOptions(dlssOnly, acConfirmed: true);
        }

        if (!dlssOnly && Installer.Conflicts(g.SelectedTarget.Dir, o) is { Count: > 0 } conflicts)
        {
            var key = Installer.ConflictKey(conflicts);
            if (o.ConfirmedConflict != key)
            {
                var ok = Dialog.Confirm("Check before installing",
                    string.Join(Environment.NewLine + Environment.NewLine, conflicts) + Environment.NewLine + Environment.NewLine + "You can install them anyway, for example to compare. The warning stays on the game.",
                    "Install anyway", danger: true);
                if (!ok) return;
                o = g.BuildOptions(dlssOnly, acConfirmed: o.AntiCheatConfirmed, key);
            }
        }

        if (o.Opti && !ConfirmCompat()) return;

        // ReShade replacing a dll that belongs to another mod: say so in one line first; the original is backed up either way.
        if (!dlssOnly && o.ReShade && g.ReShadeForeignWarning is { } foreign
            && !Dialog.Confirm("Another mod's dll is in the way", foreign + "\n\nContinue?", "Install ReShade", danger: true)) return;

        if (!dlssOnly && o.Opti && g.SelectedTarget.Exe is null && !g.Info.Exes.Any())
            Log.Info($"{g.Name}: no game .exe found in the install location. OptiScaler only works next to the game's .exe; pick that folder under Install location.");

        await Run(dlssOnly ? $"Updating DLSS for {g.Name}" : $"Installing to {g.Name}", async ct =>
        {
            await S.Installer.InstallAsync(g.Info, g.SelectedTarget.Dir, o, TransferProgress(), ct);
            await RescanOne(g);
            await AutoCleanUpAsync();
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
            var dir = g.SelectedTarget.Dir;
            await Task.Run(() => KnownInstalls.Forget([dir]));
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

    // ---------- build cleanup ----------

    /// <summary>Every folder a listed game has an install record in.</summary>
    internal List<string> InstallDirs() =>
        Games.SelectMany(g => g.Info.Installs).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Game ids and install folders to game names, for the reasons a build is kept. Read on the UI thread.</summary>
    internal Dictionary<string, string> GameNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in Games)
        {
            names[g.Info.Id] = g.Name;
            foreach (var dir in g.Info.Installs) names[FileUtil.Normalize(dir)] = g.Name;
        }
        return names;
    }

    /// <summary>
    /// The install records the cleanup protects: those of the listed games and the remembered ones of games that are
    /// hidden or can't be reached now (see <see cref="KnownInstalls"/>). With <paramref name="settings"/>, the first run
    /// also reads the hidden games' records. Reads disk, so call it off the UI thread.
    /// </summary>
    internal static CleanupRecords ReadRecords(IEnumerable<string> dirs, AppSettings? settings = null) =>
        Demo.Active
            ? KnownInstalls.Gather(dirs, Demo.ManifestAt, (_, _) => FolderState.Here, volumeSerial: _ => null)
            : KnownInstalls.Gather(dirs, InstallManifest.Read, KnownInstalls.Probe,
                settings is null ? null : () => GameScanner.HiddenInstallDirs(settings));

    /// <summary>The manual cleanup's plan; null when an install record can't be read and nothing about it is remembered.</summary>
    private static List<CleanupItem>? PlanManual(ComponentStore store, AppSettings settings, IEnumerable<string> dirs) =>
        ReadRecords(dirs, settings) is { Unknown: false } r ? store.PlanCleanup(settings, r.Records) : null;

    /// <summary>The kept builds of every library with their reasons for a dialog, a few lines at most; null when none is kept.</summary>
    private static string? KeptDetails(Dictionary<LibraryKind, LibraryKeepNote> notes)
    {
        const int MaxLines = 12;
        var lines = notes.SelectMany(n => n.Value.Kept.Select(k => $"• {ComponentStore.LibraryTitle(n.Key)} {k.Build.Label}: {string.Join(", ", k.Reasons)}")).ToList();
        return lines.Count == 0 ? null
            : string.Join(Environment.NewLine, lines.Take(MaxLines)) + (lines.Count > MaxLines ? $"{Environment.NewLine}… and {lines.Count - MaxLines} more" : "");
    }

    /// <summary>Settings' Clean up: says what goes and how much it frees, deletes after a yes.</summary>
    internal async Task CleanUpBuildsAsync()
    {
        if (IsBusy) return;
        var dirs = InstallDirs();
        var names = GameNames();
        var (plan, kept) = await Task.Run(() =>
            ReadRecords(dirs, S.Settings) is { Unknown: false } r
                ? (S.Store.PlanCleanup(S.Settings, r.Records), KeptDetails(S.Store.KeepNotes(S.Settings, r.Records, names)))
                : (null, null));
        if (plan is null)
        {
            Dialog.Show("Cleanup skipped", "An install record can't be read, so nothing is deleted. The details are in the log.");
            return;
        }
        if (plan.Count == 0)
        {
            Dialog.Show("Nothing to clean up", "Every kept build is one of the newest, a default, chosen for a game or installed in one." +
                (kept is null ? "" : $"{Environment.NewLine}{Environment.NewLine}Kept beyond the newest:{Environment.NewLine}{kept}"));
            return;
        }
        var list = string.Join(Environment.NewLine, plan.Take(12).Select(p => $"• {p.What} ({BuildCleanup.FormatBytes(p.Bytes)})"))
                   + (plan.Count > 12 ? $"{Environment.NewLine}… and {plan.Count - 12} more" : "");
        if (!Dialog.Confirm("Clean up old builds",
                $"Delete these for good and free {BuildCleanup.FormatBytes(plan.Sum(p => p.Bytes))}?{Environment.NewLine}{Environment.NewLine}{list}{Environment.NewLine}{Environment.NewLine}" +
                "Defaults, builds chosen for a game and builds installed in one always stay." +
                (kept is null ? "" : $"{Environment.NewLine}{Environment.NewLine}Kept beyond the newest:{Environment.NewLine}{kept}"), "Delete", danger: true)) return;
        await Run("Cleaning up old builds", async _ =>
        {
            // Something may have changed while the question was open: only what both plans name goes.
            var confirmed = plan.Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dirsNow = InstallDirs();
            await Task.Run(() =>
            {
                if (PlanManual(S.Store, S.Settings, dirsNow) is { } now) S.Store.RunCleanup(now.Where(p => confirmed.Contains(p.Path)));
                else Log.Warn("Cleanup skipped: an install record can't be read");
            });
            await AfterCleanupAsync();
        });
    }

    /// <summary>After an import outside a run: the automatic cleanup, when it is on, as a run of its own.</summary>
    internal Task AutoCleanUpAfterImportAsync() =>
        S.Settings.AutoCleanBuilds && !IsBusy ? Run("Deleting old builds", _ => AutoCleanUpAsync()) : Task.CompletedTask;

    /// <summary>
    /// Inside a run, after downloads or imports: deletes old downloads when automatic cleanup is on. Imported builds and
    /// the builds just downloaded always stay, and a library that a game's unreadable or unreachable install points into is left alone.
    /// </summary>
    private async Task AutoCleanUpAsync()
    {
        var added = S.Store.TakeAdded();
        if (!S.Settings.AutoCleanBuilds) return;
        var dirs = InstallDirs();
        var store = S.Store;
        var settings = S.Settings;
        var freed = await Task.Run(() =>
        {
            var r = ReadRecords(dirs, settings);
            if (r.Unknown) return false;
            var plan = store.PlanCleanup(settings, r.Records, added, r.Unsure, downloadsOnly: true);
            if (plan.Count == 0) return false;
            Log.Info("Deleting old downloads (automatic cleanup is on)");
            store.RunCleanup(plan);
            return true;
        });
        if (freed) await AfterCleanupAsync();
    }

    private async Task AfterCleanupAsync()
    {
        await RefreshLocalComponentsAsync();
        SettingsVm.Reload();
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
        // A hidden game can come back, so the cleanup keeps what it has installed; a removed one is gone from the app.
        if (S.Settings.ManualGames.RemoveAll(p => GameInfo.MakeId(p) == g.Id) == 0)
            S.Settings.HiddenGames.Add(g.Id);
        else KnownInstalls.Forget(g.Info.Installs);
        S.Settings.Save();
        Games.Remove(g);
        OnPropertyChanged(nameof(GameListEmptyText));
        SaveGameCache();
        SelectedGame = GamesView.Cast<GameViewModel>().FirstOrDefault();
    }

    // ---------- helpers ----------

    private async Task RescanOne(GameViewModel g)
    {
        var info = Demo.Active ? Demo.Find(g.Root)! : await Task.Run(() => GameScanner.Inspect(new GameEntry(g.Name, g.Root, g.Source, g.Info.SteamAppId)));
        g.Load(info);
        RefreshGames();
        CountUpdates();
        SaveGameCache();
    }

    private void SaveGameCache() => GameScanner.SaveCache(Games.Select(g => g.Info).ToList());

    internal IProgress<TransferProgress> TransferProgress() => new Progress<TransferProgress>(p =>
    {
        Status = p.Text;
        ProgressIndeterminate = p.Fraction is null;
        Progress = p.Fraction ?? 0;
    });

    /// <summary>A run like any other for work outside this view model (the self-update download).</summary>
    internal Task RunJob(string label, Func<CancellationToken, Task> work) => Run(label, work);

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
