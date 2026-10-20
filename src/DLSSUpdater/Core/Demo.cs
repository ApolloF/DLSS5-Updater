using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>
/// <c>--demo</c>: made-up games and releases for screenshots, store videos and trying the interface.
/// Runs in a throwaway data folder under %TEMP%; never scans launchers, downloads, touches game folders or the driver
/// profile. Installs, updates, uninstalls and restores only change the in-memory games and install records.
/// <c>--demo --screenshots &lt;folder&gt;</c> additionally renders the main views to PNG files and exits.
/// </summary>
public static class Demo
{
    public static bool Active { get; internal set; }

    /// <summary>Folder for the automatic screenshot export (<c>--screenshots</c>); null = no export.</summary>
    public static string? ScreenshotDir { get; private set; }

    public const string LatestDlss = "v310.10.0";
    public const string LatestStreamline = "v2.14.1";
    // DLSSU_DEMO_OPTI_NR=1 pretends OptiScaler-NR (ShyVortex) is selected instead of the default F5 build, to check its labels.
    private static bool OptiNr => Environment.GetEnvironmentVariable("DLSSU_DEMO_OPTI_NR") == "1";
    // DLSSU_DEMO_ONBOARDING=1 opens the first-run setup at start, which the demo otherwise leaves out.
    public static bool ShowOnboarding => Environment.GetEnvironmentVariable("DLSSU_DEMO_ONBOARDING") == "1";
    public static string LatestOpti => OptiNr ? "v0.9.51" : "v0.1.32" + OptiSources.F5.TagSuffix;
    private static string PreviousOpti => OptiNr ? "v0.9.47" : "v0.1.31" + OptiSources.F5.TagSuffix;
    public const string LatestMfg = "v1.4.0";
    public const string LatestUplift = "v1.4.2";
    public const string LatestReShade = "6.6.2";

    private const string NrCompatId = "e67dee209320cdafe0e93e45675d7aa34323a53acc57a72b2e40a181581c989a";
    private const string NrOriginalId = "2bc19bc09a7b89dd44f73e2ac0108fcc586e2154bdd0830f242f4f040e28789f";
    private static readonly Version NrCompatVersion = new(310, 9, 16, 0);
    private static readonly Version NrOriginalVersion = new(310, 8, 0, 0);
    private const string DfcInstalled = "5.0.0";
    private const string DfcSeed = "dfc-5.0.0";

    private static readonly Dictionary<string, InstallManifest> Manifests = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<DlssDll, Version?> Originals = [];
    // Placeholder files in the demo data folder that pose as real components: version and hash come from here.
    private static readonly Dictionary<string, (Version Version, string Sha)> Fakes = new(StringComparer.OrdinalIgnoreCase);
    private static List<GameInfo> _games = [];

    /// <summary>
    /// Switches to a fresh data folder under %TEMP% and fills its component library with placeholder files;
    /// call before anything reads settings. <paramref name="root"/> is for tests.
    /// </summary>
    public static void Enable(string? root = null, string? screenshotDir = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "DLSS5Updater-demo");
        if (!FileUtil.IsUnder(root, Path.GetTempPath())) throw new ArgumentException("The demo data folder must be under the temp folder.", nameof(root));
        try { FileUtil.DeleteDirectory(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"The demo data folder {root} is still in use, probably by another demo or a program that has a file in it open. Close it and start the demo again.", ex);
        }
        AppPaths.Root = root;
        AppPaths.Ensure();
        Active = true;
        ScreenshotDir = screenshotDir;
        SeedLibrary();
        Reset();
    }

    /// <summary>The value after <c>--screenshots</c>, or null when absent.</summary>
    public static string? ScreenshotArg(IReadOnlyList<string> args)
    {
        var i = args.ToList().FindIndex(a => a.Equals("--screenshots", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Count && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
    }

    // ---------- seam for the file readers (FileUtil.ReadVersion, HashCache.Get) ----------

    /// <summary>The version a demo placeholder or a made-up game file reports; null for any other path.</summary>
    internal static Version? VersionOf(string path)
    {
        if (Fakes.TryGetValue(Path.GetFullPath(path), out var fake)) return fake.Version;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        var name = Path.GetFileName(path);
        if (dir is null) return null;
        if (dir.EndsWith(ComponentStore.UpliftNrDir, StringComparison.OrdinalIgnoreCase)) dir = Path.GetDirectoryName(dir)!;
        if (!Manifests.TryGetValue(dir, out var m)) return null;
        if (name.Equals(ComponentStore.DlssNrFile, StringComparison.OrdinalIgnoreCase) && m.DlssNr) return NrCompatVersion;
        var reShadeName = m.Mode == InstallMode.ReShadeOnly && m.Proxy is { } p ? p : ComponentStore.ReShadeFile;
        return name.Equals(reShadeName, StringComparison.OrdinalIgnoreCase) && m.ReShade ? FileUtil.ParseTag(LatestReShade) : null;
    }

    internal static string? HashOf(string path) =>
        Fakes.TryGetValue(Path.GetFullPath(path), out var fake) ? fake.Sha : null;

    // ---------- component library and releases ----------

    private static void SeedLibrary()
    {
        Fakes.Clear();
        PlaceNr(NrCompatId, NrCompatVersion);
        PlaceNr(NrOriginalId, NrOriginalVersion);
        PlaceAddon("renodx-dlss", "renodx-dlss", ComponentStore.RenoDxFile, "0.2026.928.0", "0.2026.928", null);
        PlaceAddon("lecram-dlss5", "lecram-dlss5-v11", ComponentStore.Lecram11File, "", "v11.0.1", LecramGeneration.V11);
        PlaceAddon("lecram-dlss5", "lecram-dlss5-beta48", ComponentStore.Lecram11File, "", "v11.0.0-beta.48", LecramGeneration.V11);
        PlaceAddon("lecram-dlss5", "lecram-dlss5-beta45", ComponentStore.Lecram11File, "", "v11.0.0-beta.45", LecramGeneration.V11);
        PlaceAddon("lecram-dlss5", "lecram-dlss5-v10", ComponentStore.Lecram10File, "8.5.0.0", "v8.5.0-rc10-model-sdr1", LecramGeneration.V10);
        // Downloaded builds in the OptiScaler and GITC Uplift libraries; one more than the cleanup keeps.
        PlaceRelease(Component.OptiScaler, LatestOpti, new DateTime(2026, 10, 18));
        PlaceRelease(Component.OptiScaler, "v0.9.49", new DateTime(2026, 9, 30));
        PlaceRelease(Component.OptiScaler, "v0.9.47", new DateTime(2026, 9, 12));
        PlaceRelease(Component.OptiScaler, "v0.9.45", new DateTime(2026, 8, 20));
        PlaceRelease(Component.OptiScaler, "v0.1.32" + OptiSources.F5.TagSuffix, new DateTime(2026, 10, 16));
        PlaceRelease(Component.OptiScaler, "v0.1.30" + OptiSources.F5.TagSuffix, new DateTime(2026, 10, 2));
        PlaceRelease(Component.Uplift, LatestUplift, new DateTime(2026, 10, 8));
        PlaceRelease(Component.Uplift, "v1.3.0", new DateTime(2026, 9, 1));
        PlaceRelease(Component.Uplift, "v1.2.0", new DateTime(2026, 7, 14));

        // Deep Fried Chicken: 5.0.0 (the newest, so the default) and the 4.0.6 preview, each a whole set.
        PlaceDfc(DfcSeed, DfcInstalled);
        PlaceDfc("dfc-4.0.6-preview", "4.0.0");

        void PlaceNr(string id, Version version)
        {
            var path = Path.Combine(AppPaths.Components, "dlssnr", id, ComponentStore.DlssNrFile);
            var record = new NrVersion(id, FileUtil.Format(version), id);
            FileUtil.AtomicWriteText(path, Placeholder(id));
            FileUtil.AtomicWriteText(Path.Combine(Path.GetDirectoryName(path)!, "version.json"), JsonSerializer.Serialize(record, JsonCtx.Default.NrVersion));
            Fakes[path] = (version, id);
        }

        void PlaceRelease(Component c, string tag, DateTime published)
        {
            var file = c == Component.OptiScaler ? "OptiScaler.dll" : ComponentStore.UpliftFile;
            FileUtil.AtomicWriteText(Path.Combine(ComponentStore.TagDir(c, tag), "pkg", file), Placeholder($"{c} {tag}"));
            ComponentStore.MarkComplete(c, Release(tag, DateTime.SpecifyKind(published, DateTimeKind.Utc)));
        }

        void PlaceAddon(string dir, string seed, string file, string fileVersion, string label, LecramGeneration? generation)
        {
            var id = Sha(seed);
            var folder = Path.Combine(AppPaths.Components, dir, id);
            FileUtil.AtomicWriteText(Path.Combine(folder, file), Placeholder(seed));
            var build = new AddonBuild(id, file, fileVersion, generation, label, null, new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc));
            FileUtil.AtomicWriteText(Path.Combine(folder, "version.json"), JsonSerializer.Serialize(build, JsonCtx.Default.AddonBuild));
        }

        void PlaceDfc(string seed, string version)
        {
            PlaceAddon("dfc", seed, ComponentStore.DfcFile, version, version, null);
            var folder = Path.Combine(ComponentStore.DfcDir, Sha(seed));
            FileUtil.AtomicWriteText(Path.Combine(folder, ComponentStore.DfcBridgeFile), Placeholder(seed + " bridge"));
            FileUtil.AtomicWriteText(Path.Combine(folder, ComponentStore.DfcConfigFile), $"config_schema={DfcConfig.TestedSchema}\n");
        }
    }

    private static string Placeholder(string what) => $"DLSS5 Updater demo placeholder ({what}); not a real file.\n";
    private static string Sha(string what) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(what))).ToLowerInvariant();

    private static string RenoDxId => Sha("renodx-dlss");
    private static string LecramId => Sha("lecram-dlss5-v11");
    private static string RenoDxTag => $"0.2026.928-{RenoDxId[..12]}";
    private static string LecramTag => $"v11.0.1-{LecramId[..12]}";

    /// <summary>The releases the demo pretends are the newest, with release notes, and which of them count as new.</summary>
    public static void Seed(ComponentStore store, AppSettings settings)
    {
        var today = DateTime.UtcNow.Date;
        store.Dlss = Release(LatestDlss, today.AddDays(-4), "## Neural texture compression\n- DLSS Super Resolution 310.10 improves stability of the preset A transformer model.\n- Frame Generation fixes for dynamic multi frame generation.\n- Smaller memory footprint on RTX 40 and 50 GPUs.");
        store.DlssReleases = [store.Dlss, Release("v310.9.1", today.AddDays(-45)), Release("v310.4.0", today.AddDays(-150)), Release("v310.2.1", today.AddDays(-230)), Release("v3.7.10", today.AddDays(-400))];
        store.Streamline = Release(LatestStreamline, today.AddDays(-20));
        if (OptiNr) settings.OptiSource = ComponentStore.OptiRepo;
        store.Opti = Release(LatestOpti, today.AddDays(-2), "## Faster NR pass\n- DLSSNR runs about 15% faster with the small model.\n- New tonemap and HDR output options.\n- Fixes the flicker in games that resize their swap chain.");
        store.Mfg = Release(LatestMfg, today.AddDays(-30), "## MFG Unlock\n- Works with Streamline 2.14.\n- Fixes a crash on startup in some Unreal Engine 5 games.");
        store.Uplift = Release(LatestUplift, today.AddDays(-12), "## GITC Uplift\n- New mask editor.\n- Lower VRAM use with the performance preset.");
        store.ReShade = Release(LatestReShade, today.AddDays(-60));
        // The first two have a release the user has not looked at yet; they show the NEW dot.
        settings.SeenReleases[nameof(Component.OptiScaler)] = PreviousOpti;
        settings.SeenReleases[nameof(Component.Dlss)] = "v310.9.1";
        settings.LecramLastVersion = "v11.0.1";
        settings.LecramLastResult = "Up to date: v11.0.1.";
    }

    private static ReleaseInfo Release(string tag, DateTime published, string? notes = null) => new() { Tag = tag, Published = published, Notes = notes };

    // ---------- the made-up library ----------

    public static List<GameInfo> Games => _games;

    public static GameInfo? Find(string root) => _games.FirstOrDefault(g => g.Root.Equals(root, StringComparison.OrdinalIgnoreCase));

    public static InstallManifest? ManifestAt(string dir) => Manifests.GetValueOrDefault(dir);

    private sealed record Spec(
        string Name, string Source, string Bin, string Exe,
        string? Sr = null, string? Rr = null, string? Fg = null, string? Streamline = null, string? AntiCheat = null,
        Action<InstallManifest>? Installed = null);

    private static readonly Spec[] Catalog =
    [
        // OptiScaler mode with MFG and Streamline, everything one release behind.
        new("Ember Crown", "Steam", @"EmberCrown\Binaries\Win64", "EmberCrown-Win64-Shipping.exe", "310.2.1", "310.2.1", "310.2.1", "2.7.30", Installed: m =>
        {
            m.Opti = m.ReShade = m.Mfg = m.DlssNr = m.Dlss = m.Streamline = true;
            m.OptiTag = PreviousOpti; m.MfgTag = "v1.3.0"; m.DlssTag = "v310.2.1"; m.StreamlineTag = "v2.7.30";
            m.DlssNrId = NrCompatId; m.Proxy = "dxgi.dll";
        }),
        // Not installed yet.
        new("Frostline", "Steam", "bin", "Frostline.exe", "3.7.10", null, "3.7.10"),
        // Plain DLSS swap that is up to date.
        new("Grimwald", "GOG", "", "Grimwald.exe", "310.10.0", Installed: m => { m.Dlss = true; m.DlssTag = LatestDlss; }),
        // ReShade with Deep Fried Chicken.
        new("Hollow Tide", "Epic", @"HollowTide\Binaries\Win64", "HollowTide-Win64-Shipping.exe", "3.5.10", Installed: m =>
        {
            m.Mode = InstallMode.ReShadeOnly;
            m.ReShade = m.DlssNr = m.Dfc = true;
            m.DfcId = DfcId; m.DfcTag = DfcTag; m.DlssNrId = NrCompatId; m.Proxy = "dxgi.dll";
        }),
        // Anti-cheat game with an old DLSS-only swap.
        new("Iron Veil", "Steam", "x64", "IronVeil.exe", "310.9.1", "310.9.1", "310.9.1", "2.7.30", AntiCheat: "EasyAntiCheat", Installed: m => { m.Dlss = true; m.DlssTag = "v310.9.1"; }),
        // No DLSS of its own: OptiScaler brings one.
        new("Lumen Drift", "Steam", "", "LumenDrift.exe", Installed: m =>
        {
            m.Opti = m.ReShade = m.DlssNr = true;
            m.OptiTag = LatestOpti; m.DlssNrId = NrCompatId; m.Proxy = "dxgi.dll";
            m.Files.Add("nvngx_dlss.dll");
        }),
        // ReShade with the ShortFuse and Lecram neural rendering add-ons.
        new("Neon Meridian", "Epic", @"bin\x64", "NeonMeridian.exe", "310.10.0", "310.10.0", "310.10.0", "2.14.1", Installed: m =>
        {
            m.Mode = InstallMode.ReShadeOnly;
            m.ReShade = m.DlssNr = m.RenoDx = m.Lecram = m.Dlss = true;
            m.RenoDxId = RenoDxId; m.RenoDxTag = RenoDxTag; m.LecramId = LecramId; m.LecramTag = LecramTag;
            m.LecramFile = ComponentStore.Lecram11File; m.DlssTag = LatestDlss; m.DlssNrId = NrCompatId; m.Proxy = "dxgi.dll";
        }),
        new("Quiet Harbor", "GOG", "", "QuietHarbor.exe"),
        // ReShade alone.
        new("Sable Run", "Steam", @"SableRun\Binaries\Win64", "SableRun-Win64-Shipping.exe", "2.5.1", Installed: m =>
        {
            m.Mode = InstallMode.ReShadeOnly;
            m.ReShade = true; m.Proxy = "dxgi.dll";
        }),
        // OptiScaler mode, fully up to date.
        new("Starfall Protocol", "Steam", @"Starfall\Binaries\Win64", "Starfall-Win64-Shipping.exe", "310.10.0", "310.10.0", "310.10.0", "2.14.1", Installed: m =>
        {
            m.Opti = m.ReShade = m.Mfg = m.DlssNr = m.Dlss = m.Streamline = true;
            m.OptiTag = LatestOpti; m.MfgTag = LatestMfg; m.DlssTag = LatestDlss; m.StreamlineTag = LatestStreamline;
            m.DlssNrId = NrCompatId; m.Proxy = "dxgi.dll";
        }),
        // GITC Uplift, one release behind.
        new("Tidebreaker", "Manual", "", "Tidebreaker.exe", "3.1.30", Installed: m =>
        {
            m.Mode = InstallMode.ReShadeOnly;
            m.ReShade = m.DlssNr = m.Uplift = true;
            m.UpliftTag = "v1.3.0"; m.DlssNrId = NrCompatId; m.Proxy = "dxgi.dll";
        }),
        // DLSS swap one release behind.
        new("Wicker & Ash", "Steam", @"bin\win64", "WickerAndAsh.exe", "310.9.1", null, "310.9.1", Installed: m => { m.Dlss = true; m.DlssTag = "v310.9.1"; }),
    ];

    private static string DfcId => Sha(DfcSeed);
    private static string DfcTag => $"{DfcInstalled}-{DfcId[..12]}";

    /// <summary>Rebuilds the made-up library; games live on a drive the demo never writes to.</summary>
    public static void Reset()
    {
        Manifests.Clear();
        Originals.Clear();
        _games = Catalog.Select(Build).ToList();
    }

    private static GameInfo Build(Spec c)
    {
        var root = $@"D:\Games\{c.Name}";
        var bin = c.Bin.Length == 0 ? root : Path.Combine(root, c.Bin);
        var dlss = new List<DlssDll>();
        if (c.Sr is not null) dlss.Add(Dll(bin, "nvngx_dlss.dll", c.Sr));
        if (c.Rr is not null) dlss.Add(Dll(bin, "nvngx_dlssd.dll", c.Rr));
        if (c.Fg is not null) dlss.Add(Dll(bin, "nvngx_dlssg.dll", c.Fg));
        var sl = c.Streamline is { } v
            ? new List<DlssDll> { Dll(bin, "sl.interposer.dll", v), Dll(bin, "sl.dlss.dll", v), Dll(bin, "sl.reflex.dll", v), Dll(bin, "sl.dlss_g.dll", v) }
            : [];
        var game = new GameInfo
        {
            Id = GameInfo.MakeId(root), Name = c.Name, Source = c.Source, Root = root,
            Exes = [Path.Combine(bin, c.Exe)], Dlss = dlss, Streamline = sl, AntiCheat = c.AntiCheat,
            Scanned = DateTime.UtcNow,
        };
        foreach (var d in dlss.Concat(sl)) Originals[d] = d.Version;
        if (c.Installed is { } install)
        {
            var m = new InstallManifest { RootRel = Path.GetRelativePath(bin, root), Updated = DateTime.UtcNow };
            install(m);
            game.Installs.Add(bin);
            Manifests[bin] = m;
        }
        return game;
    }

    private static DlssDll Dll(string dir, string name, string version) =>
        new() { Path = Path.Combine(dir, name), Name = name, Version = Version.Parse(version + ".0") };

    // ---------- simulated changes ----------

    /// <summary>Pretends to download and install: only the in-memory game and install record change.</summary>
    public static async Task InstallAsync(ComponentStore store, GameInfo game, string targetDir, InstallOptions o, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var dlss = store.DlssFor(o.DlssTag) ?? throw new InvalidOperationException("No DLSS release");
        // Paced by the clock, not by steps, so a busy UI thread doesn't stretch it.
        var download = System.Diagnostics.Stopwatch.StartNew();
        for (double f; (f = download.ElapsedMilliseconds / 1000.0) < 1;)
        {
            progress?.Report(new TransferProgress($"Downloading DLSS {dlss.Tag}", f));
            await Task.Delay(40, ct);
        }
        progress?.Report(new TransferProgress($"Downloading DLSS {dlss.Tag}", 1));
        progress?.Report(new TransferProgress($"Installing to {game.Name}", null));
        await Task.Delay(500, ct);

        var m = ManifestAt(targetDir) ?? new InstallManifest { RootRel = Path.GetRelativePath(targetDir, game.Root) };
        // A DLSS-only update keeps what else is installed; any other install sets the whole component list.
        var full = o.Opti || o.ReShade || o.Mfg || o.DlssNr || o.RenoDx || o.Lecram || o.Uplift || o.Dfc;
        if (full)
        {
            m.Mode = o.Mode;
            m.Proxy = o.Proxy;
            m.Opti = o.Opti;
            m.ReShade = o.ReShade || o.Opti;
            m.Mfg = o.Mfg;
            m.DlssNr = o.DlssNr;
            m.DlssNrId = o.DlssNr ? o.DlssNrId ?? store.DlssNrVersions.FirstOrDefault()?.Id : null;
            m.RenoDx = o.RenoDx;
            m.Lecram = o.Lecram;
            m.Uplift = o.Uplift;
            m.Dfc = o.Dfc;
            m.AntiCheatConfirmed = o.AntiCheatConfirmed;
            m.OptiTag = o.Opti ? o.OptiTag ?? store.Opti?.Tag : null;
            m.MfgTag = o.Mfg ? o.MfgTag ?? store.Mfg?.Tag : null;
            m.UpliftTag = o.Uplift ? o.UpliftTag ?? store.Uplift?.Tag : null;
            (m.DfcId, m.DfcTag) = o.Dfc && store.DfcBuildFor(o.DfcId) is { } dfc ? (dfc.Id, dfc.Tag) : (null, null);
            (m.RenoDxId, m.RenoDxTag) = o.RenoDx && store.RenoDxBuildFor(o.RenoDxId) is { } sf ? (sf.Id, sf.Tag) : (null, null);
            (m.LecramId, m.LecramTag, m.LecramFile) = o.Lecram && store.LecramBuildFor(o.LecramId) is { } lc ? (lc.Id, lc.Tag, lc.FileName) : (null, null, null);
        }
        if (o.Dlss)
        {
            var v = new Version(dlss.Version!.Major, dlss.Version.Minor, Math.Max(dlss.Version.Build, 0), 0);
            foreach (var d in game.Dlss.Where(d => o.DlssTag is not null || d.Version < v))
            {
                Originals.TryAdd(d, d.Version);
                d.Version = v;
            }
            m.Dlss = true;
            m.DlssTag = dlss.Tag;
            m.DlssPinned = o.DlssTag is not null;
        }
        else if (full) m.Dlss = false;
        if (o.AddMissingDlss && game.Dlss.Count == 0 && !m.Files.Contains("nvngx_dlss.dll")) m.Files.Add("nvngx_dlss.dll");
        if (o.Streamline && store.Streamline is { } sl)
        {
            var v = new Version(sl.Version!.Major, sl.Version.Minor, Math.Max(sl.Version.Build, 0), 0);
            foreach (var d in game.Streamline)
            {
                Originals.TryAdd(d, d.Version);
                d.Version = v;
            }
            m.Streamline = true;
            m.StreamlineTag = sl.Tag;
        }
        else if (full) m.Streamline = false;
        m.Updated = DateTime.UtcNow;
        Manifests[targetDir] = m;
        if (!game.Installs.Contains(targetDir, StringComparer.OrdinalIgnoreCase)) game.Installs.Add(targetDir);
        Log.Info($"{game.Name}: DLSS {dlss.Tag} installed");
    }

    public static async Task SwitchNrAsync(string targetDir, string? id, CancellationToken ct)
    {
        await Task.Delay(300, ct);
        var m = ManifestAt(targetDir) ?? throw new InvalidOperationException("Install NR in this game first.");
        m.PreviousDlssNrId = m.DlssNrId;
        m.DlssNrId = id;
        Log.Info("NR runtime switched");
    }

    public static async Task UninstallAsync(GameInfo game, string targetDir, CancellationToken ct)
    {
        await Task.Delay(400, ct);
        Manifests.Remove(targetDir);
        game.Installs.RemoveAll(d => d.Equals(targetDir, StringComparison.OrdinalIgnoreCase));
        RestoreOriginals(game);
        Log.Info($"{game.Name}: uninstalled, originals restored");
    }

    /// <summary>Puts the original DLSS and Streamline files back and keeps whatever else is installed.</summary>
    public static async Task RestoreDlssAsync(GameInfo game, string targetDir, CancellationToken ct)
    {
        await Task.Delay(400, ct);
        var m = ManifestAt(targetDir) ?? throw new InvalidOperationException("No DLSS5 Updater install found here.");
        RestoreOriginals(game);
        m.Dlss = m.Streamline = m.DlssPinned = false;
        m.DlssTag = m.StreamlineTag = null;
        if (!(m.Opti || m.ReShade || m.Mfg || m.DlssNr || m.RenoDx || m.Lecram || m.Uplift || m.Dfc))
        {
            Manifests.Remove(targetDir);
            game.Installs.RemoveAll(d => d.Equals(targetDir, StringComparison.OrdinalIgnoreCase));
        }
        Log.Info($"{game.Name}: original DLSS and Streamline files restored");
    }

    private static void RestoreOriginals(GameInfo game)
    {
        foreach (var d in game.Dlss.Concat(game.Streamline)) d.Version = Originals.GetValueOrDefault(d, d.Version);
    }
}
