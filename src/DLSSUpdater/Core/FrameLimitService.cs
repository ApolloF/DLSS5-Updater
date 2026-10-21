using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>
/// Which file of a limiter a game gets: its own pick, else the Settings default, else automatically the catalog's current build
/// (downloaded when the library lacks it) or, offline, the newest build in the library. <see cref="Auto"/> = nothing pinned.
/// </summary>
public sealed record LimiterResolution(LimiterBuild? Library, CatalogLimiter? Limiter, CatalogBinary? File, bool Auto, string? Note)
{
    public bool Found => Library is not null || File is not null;
    public string? BuildId => Library?.Id ?? File?.Sha256;
    public string Version => Library?.Version ?? (Limiter is not null && File is not null ? Limiter.VersionOf(File) : "?");

    /// <param name="pin">The game's pick (a library build id), else the Settings default; null = automatic.</param>
    public static LimiterResolution Resolve(ComponentStore store, string limiterId, string arch, CatalogLimiter? catalog, string? pin)
    {
        string? note = null;
        if (pin is not null)
        {
            if (store.LimiterBuildFor(pin) is { } pinned && pinned.LimiterId == limiterId && pinned.Arch.Equals(arch, StringComparison.OrdinalIgnoreCase))
                return new LimiterResolution(pinned, null, null, Auto: false, null);
            note = "The chosen build is no longer in the library (or is for the other bitness), so the automatic one is used.";
        }
        if (catalog?.File(arch) is { IsInstallable: true } file) return new LimiterResolution(null, catalog, file, Auto: true, note);
        // Offline or the catalog has no file for this bitness: whatever the library holds, newest first.
        if (store.LimiterBuildsOf(limiterId, arch).FirstOrDefault() is { } newest) return new LimiterResolution(newest, null, null, Auto: true, note);
        return new LimiterResolution(null, catalog, null, Auto: true, note);
    }

    /// <summary>The picker entries: Auto first, then every library build of this limiter and bitness.</summary>
    public static IReadOnlyList<LimiterBuild> Candidates(ComponentStore store, string limiterId, string arch) => store.LimiterBuildsOf(limiterId, arch);
}

/// <summary>Everything the service needs to bring one game to a frame-limit choice.</summary>
public sealed record FrameLimitRequest(
    GameInfo Game,
    string TargetDir,
    NvidiaGame Driver,
    LimitMethod Method,
    int Fps,
    bool? Osd,
    string? UserIni,
    string? BuildPick,
    CatalogLimiter? Catalog,
    string? EngineIniHint,
    HdrReShade? ReShade,
    /// <summary>An explicit Apply: our keys replace what the files hold. False for an update of the add-on.</summary>
    bool Force = true,
    /// <summary>The user confirmed the anti-cheat warning for an add-on (or the add-on is already installed and only updates).</summary>
    bool AntiCheatConfirmed = false);

/// <param name="Notes">Warnings for the log and the game page (a second limiter in the folder, a build that fell back).</param>
public sealed record FrameLimitResult(LimitMethod Method, IReadOnlyList<ComponentUndo> Undone, IReadOnlyList<string> Notes);

/// <summary>
/// Brings a game to one frame limiter and takes the one it had away: the NVIDIA driver limiter, ReLimiter or Display Commander (ReShade
/// add-ons) or Unreal's t.MaxFPS in Engine.ini. Exactly one is active. An add-on limiter conflicts with the driver's, so the driver's is
/// set to 0 while an add-on is installed and put back when it goes. What can fail (a download, a missing Engine.ini, anti-cheat) is checked
/// before the previous limiter is removed.
/// </summary>
public sealed class FrameLimitService(ComponentStore store, Installer installer, CatalogDownloader downloader, NvidiaProfile nvidia)
{
    private readonly ReShadeAddonHost _host = new(store, installer);

    public bool NvidiaAvailable => nvidia.Available;

    /// <summary>The limiter this game has now. The install record says for the add-ons and the Engine.ini cap; the driver one is recorded in the game's settings.</summary>
    public static LimitMethod Active(InstallManifest? manifest, GameOverride? over)
    {
        if (manifest?.Component(ComponentRegistry.ReLimiter) is not null) return LimitMethod.ReLimiter;
        if (manifest?.Component(ComponentRegistry.DisplayCommander) is not null) return LimitMethod.DisplayCommander;
        if (manifest?.Component(ComponentRegistry.UnrealFrameCap) is not null) return LimitMethod.Unreal;
        return FrameLimitSettings.DriverCap(over?.PickOf(ComponentRegistry.FrameLimit)) is not null ? LimitMethod.Driver : LimitMethod.None;
    }

    public async Task<FrameLimitResult> ApplyAsync(FrameLimitRequest r, Action saveSettings, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (r.Method != LimitMethod.None && !FrameLimitRules.ValidFps(r.Fps))
            throw new ArgumentOutOfRangeException(nameof(r), r.Fps, $"The cap must be {FrameLimitRules.MinFps}-{FrameLimitRules.MaxFps} FPS.");
        var notes = new List<string>();
        var undone = new List<ComponentUndo>();
        var targetDir = FileUtil.Normalize(r.TargetDir);
        var manifest = InstallManifest.Read(targetDir);
        var current = Active(manifest, r.Driver.Settings);

        // Prepare: everything that can fail comes before anything is removed.
        LimiterBuild? build = null;
        EngineIniTarget? engine = null;
        switch (r.Method)
        {
            case LimitMethod.Driver:
                if (!nvidia.Available) throw new InvalidOperationException("The driver limiter needs an NVIDIA graphics card and driver.");
                break;
            case LimitMethod.ReLimiter or LimitMethod.DisplayCommander:
                build = await PrepareAddonAsync(r, notes, progress, ct);
                break;
            case LimitMethod.Unreal:
                if (!r.Game.Tech.IsUnreal) throw new InvalidOperationException($"{r.Game.Name} isn't an Unreal Engine game: t.MaxFPS is for Unreal games only.");
                engine = EngineIniFor(r) ?? throw new InvalidOperationException("The game's Engine.ini location couldn't be worked out (Unreal project not found), so t.MaxFPS can't be set.");
                break;
        }

        // Switch: the previous limiter goes first.
        if (current != r.Method && current != LimitMethod.None)
        {
            if (await RemoveAsync(r, current, ct) is { } undo) undone.Add(undo);
            if (current == LimitMethod.Driver) FrameLimitSettings.SetDriverCap(r.Driver.Settings, null);
            saveSettings();
        }

        switch (r.Method)
        {
            case LimitMethod.Driver:
            {
                var result = nvidia.SetFrameRateLimit(r.Driver, r.Fps);
                if (!result.Verified)
                    throw new InvalidOperationException("The NVIDIA driver didn't keep the frame limit (it may need a newer driver). Nothing else was changed.");
                FrameLimitSettings.SetDriverCap(r.Driver.Settings, r.Fps);
                saveSettings();
                break;
            }
            case LimitMethod.ReLimiter or LimitMethod.DisplayCommander:
            {
                await _host.EnsureAsync(r.Game, targetDir, r.ReShade ?? new HdrReShade(), Purpose, progress, ct);
                var existing = InstallManifest.Read(targetDir)?.Component(LimitMethods.ComponentOf(r.Method)!);
                var plan = FrameLimitPlanner.Addon(r.Game, targetDir, r.Method, build!, LimiterPathOf(build!), r.Fps, r.Osd, r.UserIni, r.Force, existing);
                notes.AddRange(plan.Warnings);
                foreach (var w in plan.Warnings) Log.Warn($"{r.Game.Name}: {w}");
                progress?.Report(new TransferProgress($"Installing {build!.Label} to {r.Game.Name}", null));
                undone.Add(await installer.InstallComponentAsync(r.Game, targetDir, plan.Plan, ct));
                notes.AddRange(DriverOff(r));
                saveSettings();
                break;
            }
            case LimitMethod.Unreal:
            {
                var existing = InstallManifest.Read(targetDir)?.Component(ComponentRegistry.UnrealFrameCap);
                var plan = FrameLimitPlanner.Unreal(r.Game, targetDir, engine!, r.Fps, r.UserIni, r.Force, existing);
                undone.Add(await installer.InstallComponentAsync(r.Game, targetDir, plan.Plan, ct));
                break;
            }
        }
        return new FrameLimitResult(r.Method, undone, notes);
    }

    private static readonly AddonPurpose Purpose = new("the frame limiter", "The frame limiter add-ons need ReShade 6 or newer with add-on support.");

    private static string LimiterPathOf(LimiterBuild build) => ComponentStore.LimiterPathOf(build);

    private async Task<LimiterBuild> PrepareAddonAsync(FrameLimitRequest r, List<string> notes, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (r.Game.AntiCheat is { } ac)
        {
            if (!r.AntiCheatConfirmed) throw new InvalidOperationException($"{r.Game.Name} has {ac} anti-cheat: the add-on is only installed after you confirm the warning.");
            notes.Add($"{r.Game.Name} has {ac} anti-cheat; the add-on was installed after confirmation.");
        }
        var limiterId = LimitMethods.ComponentOf(r.Method)!;
        var name = LimitMethods.Label(r.Method);
        var arch = LimiterFiles.ArchOf(r.Game.Tech.Is32Bit);
        var resolved = LimiterResolution.Resolve(store, limiterId, arch, r.Catalog, r.BuildPick);
        if (resolved.Note is { } note) notes.Add(note);
        if (!resolved.Found)
            throw new InvalidOperationException(arch == "x86" && r.Catalog is not null
                ? $"{name} has no 32-bit build, and {r.Game.Name} is 32-bit."
                : $"{name} isn't in the library and the catalog can't be reached. Import {LimiterFiles.Names(limiterId)} in Settings, or try again online.");
        return resolved.Library is { } have
            ? store.VerifiedLimiterBuild(have)
            : await store.EnsureLimiterBuildAsync(resolved.Limiter!, resolved.File!, downloader, progress, ct);
    }

    private static EngineIniTarget? EngineIniFor(FrameLimitRequest r) => ResolveEngineIni(r.Game, r.EngineIniHint);

    /// <summary>The game's Engine.ini (found or to be created), by the same rules the HDR component uses; null when the Unreal project isn't known.</summary>
    public static EngineIniTarget? ResolveEngineIni(GameInfo game, string? hint)
    {
        var local = InstallManifest.KnownFolder("|localappdata|") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var docs = InstallManifest.KnownFolder("|documents|") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return EngineIniLocator.Resolve(game, hint, local, docs);
    }

    /// <summary>Takes one limiter away and puts back what it replaced; null when there was nothing recorded to remove.</summary>
    private async Task<ComponentUndo?> RemoveAsync(FrameLimitRequest r, LimitMethod method, CancellationToken ct)
    {
        if (method == LimitMethod.Driver)
        {
            if (nvidia.Available) nvidia.RestoreFrameRateLimit(r.Driver);
            return null;
        }
        var undo = await installer.RemoveComponentAsync(r.Game, FileUtil.Normalize(r.TargetDir), LimitMethods.ComponentOf(method)!, ct);
        // The driver limiter was off only while this add-on was there.
        if (LimitMethods.IsAddon(method) && nvidia.Available) nvidia.RestoreFrameRateLimit(r.Driver);
        return undo;
    }

    /// <summary>Removes whatever limiter the game has; the driver limiter goes back to what it was.</summary>
    public async Task<FrameLimitResult> RemoveAsync(FrameLimitRequest r, Action saveSettings, CancellationToken ct) =>
        await ApplyAsync(r with { Method = LimitMethod.None }, saveSettings, null, ct);

    /// <summary>Sets the driver limiter to 0 while an add-on limiter runs, when the profile would otherwise cap the game. Nothing is written when it wouldn't.</summary>
    private IEnumerable<string> DriverOff(FrameLimitRequest r)
    {
        if (!nvidia.Available) return [];
        var id = NvidiaProfileCatalog.FrameRateLimit;
        var snap = nvidia.Read(r.Driver.Exe, [id]);
        var own = snap.User.GetValueOrDefault(id);
        var effective = own ?? snap.Fallback.GetValueOrDefault(id);
        if (effective is null or 0) return [];
        var result = nvidia.SetFrameRateLimit(r.Driver, 0);
        if (!result.Verified) return ["The NVIDIA driver kept its frame limit; it will fight with the add-on's. Turn the driver limiter off in the NVIDIA app."];
        return [$"The driver's frame limit ({effective} FPS) was switched off for this game: the add-on limits the frame rate now."];
    }
}
