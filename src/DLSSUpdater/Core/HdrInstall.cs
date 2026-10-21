using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>A plan for the installer plus where its pieces went, for the log and the tests.</summary>
public sealed record HdrPlan(ComponentPlan Plan, string AddonFile, EngineIniTarget? EngineIni, IReadOnlyList<string> Warnings);

/// <summary>Builds the component plan for a pick: the add-on file, the reshade.ini keys, Engine.ini and the peak brightness. No writes.</summary>
public static class HdrPlanner
{
    public const string SignatureKey = "signature", ModKey = "mod", PeakKey = "peakNits", EngineIniKey = "engineIni";

    /// <param name="engineIniHint">The catalog's Engine.ini path for this game (<see cref="CatalogOverrides.EngineIniPath"/>).</param>
    public static HdrPlan Build(GameInfo game, string targetDir, HdrChoice choice, HdrBuild build, string buildFile, int? peakNits,
        string? engineIniHint, InstalledComponent? existing)
    {
        if (!choice.IsOffered) throw new InvalidOperationException(choice.Reason);
        targetDir = FileUtil.Normalize(targetDir);
        var warnings = new List<string>();
        var peak = HdrIni.ClampPeak(peakNits);

        var addonDir = ReShadeAddonPath.Resolve(targetDir, game.Root);
        var addonFile = Path.Combine(addonDir, build.FileName);
        foreach (var other in OtherRenoDxAddons(addonDir, build.FileName))
            warnings.Add($"{other} is also in the add-on folder; two RenoDX add-ons in one game usually fight over the same hooks.");

        var edits = new List<(string File, string Section, string Key, string Value)>();
        var reshadeIni = Path.Combine(targetDir, ReShadeAddonPath.IniName);
        var reshadeEntry = EntryOf(reshadeIni, targetDir, game.Root);
        var reshadeDoc = Read(reshadeIni);
        foreach (var (s, k, v) in HdrIni.MissingOnly(reshadeDoc, existing, reshadeEntry, HdrIni.ReShadeKeys(choice))) edits.Add((reshadeIni, s, k, v));
        // The peak brightness is the user's own setting in this app, so it replaces whatever the preset holds.
        foreach (var (s, k, v) in HdrIni.PeakKeys(peak)) edits.Add((reshadeIni, s, k, v));

        var readOnly = new List<string>();
        EngineIniTarget? engine = null;
        if (HdrIni.EngineKeys(choice) is { Count: > 0 } engineKeys)
        {
            var local = InstallManifest.KnownFolder("|localappdata|") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var docs = InstallManifest.KnownFolder("|documents|") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            engine = EngineIniLocator.Resolve(game, engineIniHint, local, docs);
            if (engine is null)
                warnings.Add("Engine.ini couldn't be placed (the game's Unreal project wasn't found), so HDR is not switched on there.");
            else
            {
                var entry = EntryOf(engine.Path, targetDir, game.Root);
                foreach (var (s, k, v) in HdrIni.MissingOnly(Read(engine.Path), existing, entry, engineKeys)) edits.Add((engine.Path, s, k, v));
                readOnly.Add(engine.Path);
            }
        }

        var plan = new ComponentPlan(ComponentRegistry.RenoDxHdr)
        {
            BuildId = build.Id,
            Version = build.Version,
            Files = [(buildFile, addonFile)],
            ConfigEdits = edits,
            ReadOnly = readOnly,
            Extras = new Dictionary<string, string>
            {
                [SignatureKey] = choice.Signature(peak),
                [ModKey] = choice.ModId ?? "",
                ["kind"] = choice.Kind.ToString(),
                ["file"] = build.FileName,
                [PeakKey] = peak?.ToString() ?? "",
                [EngineIniKey] = engine?.Path ?? "",
                ["method"] = choice.Ue?.Method.ToString() ?? "",
            },
        };
        return new HdrPlan(plan, addonFile, engine, warnings);
    }

    /// <summary>The record's name for a config file: game-relative, install-folder-relative or behind a known-folder tag.</summary>
    private static string EntryOf(string full, string targetDir, string gameRoot) => InstallManifest.ConfigEntry(FileUtil.Normalize(full), targetDir, FileUtil.Normalize(gameRoot));

    private static IniFile? Read(string path)
    {
        try { return File.Exists(path) ? IniFile.Load(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>RenoDX add-ons already in the folder besides ours; ShortFuse's DLSS add-on is a different component and fine to keep.</summary>
    private static IEnumerable<string> OtherRenoDxAddons(string dir, string ours)
    {
        if (!Directory.Exists(dir)) return [];
        try
        {
            return Directory.EnumerateFiles(dir, "renodx-*.addon*").Select(Path.GetFileName).OfType<string>()
                .Where(n => !n.Equals(ours, StringComparison.OrdinalIgnoreCase) && !n.StartsWith("renodx-dlss", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}

public enum HdrState { Unavailable, NotInstalled, Current, Update, Orphaned }

/// <summary>Installed against catalog for one game, as the game page and Update all use it.</summary>
public sealed record HdrStatus(HdrState State, string? Installed, string? Latest, string Line)
{
    public bool UpdateAvailable => State == HdrState.Update;

    /// <summary>
    /// Compares what the record says is installed with what the pick would install now. A newer catalog build (another SHA-256), a
    /// different mod, another method and a changed peak brightness all count: the signature covers them.
    /// </summary>
    public static HdrStatus Of(HdrChoice choice, InstalledComponent? installed, int? peakNits)
    {
        if (installed is null)
            return choice.IsOffered
                ? new HdrStatus(HdrState.NotInstalled, null, choice.File!.Version, $"Not installed · {choice.File.Version} available")
                : new HdrStatus(HdrState.Unavailable, null, null, choice.Reason);
        var have = installed.Version ?? installed.BuildId?[..Math.Min(12, installed.BuildId.Length)];
        if (!choice.IsOffered)
            return new HdrStatus(HdrState.Orphaned, have, null, $"Installed {have}, but nothing is offered for this game now: {choice.Reason}");
        var latest = choice.File!.Version;
        var signature = choice.Signature(HdrIni.ClampPeak(peakNits));
        if (installed.Extras.GetValueOrDefault(HdrPlanner.SignatureKey) == signature && installed.BuildId == choice.File.Sha256)
            return new HdrStatus(HdrState.Current, have, latest, $"Installed {have} · up to date");
        if (installed.BuildId != choice.File.Sha256)
            return new HdrStatus(HdrState.Update, have, latest, installed.Extras.GetValueOrDefault(HdrPlanner.ModKey) == (choice.ModId ?? "")
                ? $"Installed {have} · update to {latest}"
                : $"Installed {have} (another mod) · switch to {choice.Title} {latest}");
        return new HdrStatus(HdrState.Update, have, latest, $"Installed {have} · settings changed, update to apply them");
    }
}

public sealed record HdrInstallResult(HdrBuild Build, HdrPlan Plan, ComponentUndo Undo);

/// <summary>Installs, updates and removes the RenoDX HDR component in a game: library download, ReShade, then one component plan.</summary>
public sealed class HdrService(ComponentStore store, Installer installer, CatalogDownloader downloader)
{
    /// <summary>
    /// Brings the game to the pick. ReShade with add-on support must be in the game: it is installed here (ReShade + add-ons mode,
    /// through the installer's own code) when the game has none, but not beside OptiScaler, where the user decides about ReShade.
    /// </summary>
    public async Task<HdrInstallResult> InstallAsync(GameInfo game, string targetDir, HdrChoice choice, int? peakNits, string? engineIniHint,
        string? preferredProxy, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (game.AntiCheat is { } ac) throw new InvalidOperationException($"{game.Name} has {ac} anti-cheat: no add-on is installed in games with anti-cheat.");
        if (!choice.IsOffered) throw new InvalidOperationException(choice.Reason);

        var build = await store.EnsureHdrBuildAsync(choice.File!, choice.ModId ?? choice.Title, choice.Title, downloader, progress, ct);
        await EnsureReShadeAsync(game, targetDir, preferredProxy, progress, ct);

        var existing = InstallManifest.Read(targetDir)?.Component(ComponentRegistry.RenoDxHdr);
        var plan = HdrPlanner.Build(game, targetDir, choice, build, ComponentStore.HdrPathOf(build), peakNits, engineIniHint, existing);
        foreach (var w in plan.Warnings) Log.Warn($"{game.Name}: {w}");
        progress?.Report(new TransferProgress($"Installing {choice.Title} to {game.Name}", null));
        var undo = await installer.InstallComponentAsync(game, targetDir, plan.Plan, ct);
        return new HdrInstallResult(build, plan, undo);
    }

    public Task<ComponentUndo> RemoveAsync(GameInfo game, string targetDir, CancellationToken ct) =>
        installer.RemoveComponentAsync(game, targetDir, ComponentRegistry.RenoDxHdr, ct);

    private async Task EnsureReShadeAsync(GameInfo game, string targetDir, string? preferredProxy, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var m = InstallManifest.Read(targetDir);
        if (m is { ReShade: true })
        {
            CheckReShadeVersion(targetDir, m);
            return;
        }
        if (m is { Opti: true })
            throw new InvalidOperationException("OptiScaler is installed here without ReShade. Turn ReShade on in the install above and install again, then add the HDR mod.");
        var proxy = ProxyFor(game, preferredProxy)
                    ?? throw new InvalidOperationException("This game renders with Vulkan only, which needs ReShade's Vulkan layer. DLSS5 Updater doesn't install that yet.");
        Log.Info($"{game.Name}: no ReShade yet, installing it as {proxy} (ReShade + add-ons) for the HDR mod");
        var o = new InstallOptions
        {
            Mode = InstallMode.ReShadeOnly, ReShade = true, Proxy = proxy,
            Mfg = m?.Mfg == true, KeepMfg = m?.Mfg == true,
            DlssNr = m?.DlssNr == true, DlssNrId = m?.DlssNrId,
            Streamline = m?.Streamline == true && game.Streamline.Count > 0,
        };
        await installer.InstallAsync(game, targetDir, o, progress, ct);
        CheckReShadeVersion(targetDir, InstallManifest.Read(targetDir));
    }

    /// <summary>The proxy name ReShade loads under for the game's API; null for a Vulkan-only game.</summary>
    public static string? ProxyFor(GameInfo game, string? preferred)
    {
        if (preferred is not null && AppSettings.ReShadeProxyNames.Contains(preferred, StringComparer.OrdinalIgnoreCase)) return preferred;
        return GraphicsApiInfo.Primary(game.Tech.Apis) switch
        {
            GraphicsApis.Dx9 => "d3d9.dll",
            GraphicsApis.OpenGL => "opengl32.dll",
            GraphicsApis.Vulkan => null,
            _ => "dxgi.dll",
        };
    }

    private static void CheckReShadeVersion(string targetDir, InstallManifest? m)
    {
        var name = m is { Mode: InstallMode.ReShadeOnly, Proxy: { } p } ? p : ComponentStore.ReShadeFile;
        var dll = Path.Combine(targetDir, name);
        if (File.Exists(dll) && FileUtil.ReadVersion(dll) is { Major: < 6 })
            throw new InvalidOperationException("The RenoDX HDR mod needs ReShade 6 or newer with add-on support.");
    }
}
