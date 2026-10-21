using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>The reshade.ini keys Luma documents (Source/Core/core.hpp in Filoppi/Luma-Framework): section [Luma], EnableHDR and DisplayMode.</summary>
public static class LumaIni
{
    public const string Section = "Luma", EnableHdr = "EnableHDR", DisplayMode = "DisplayMode";

    /// <summary>Nothing for Auto (Luma sets HDR from the display on its first start); On or Off write both keys, because the user chose them here.</summary>
    public static IReadOnlyList<(string Section, string Key, string Value)> ReShadeKeys(bool? hdr) => hdr is { } on
        ? [(Section, EnableHdr, on ? "1" : "0"), (Section, DisplayMode, on ? "1" : "0")]
        : [];
}

/// <summary>The RenoDX HDR component and Luma in one game: both rework the HDR output, so installing the second is a warning the user confirms.</summary>
public static class LumaConflict
{
    public const string ClashKey = "clash", ClashValue = "renodx-luma";

    public const string Message =
        "RenoDX HDR is installed in this game, and Luma also reworks the game's HDR output. The two don't stack: expect a broken picture or a crash. " +
        "Remove one of them, or install anyway if you know what you're doing.";

    public const string MessageForRenoDx =
        "Luma is installed in this game, and RenoDX HDR also reworks the game's HDR output. The two don't stack: expect a broken picture or a crash. " +
        "Remove one of them, or install anyway if you know what you're doing.";

    /// <summary>Both are in the game now (record-wise).</summary>
    public static bool Clashes(InstallManifest? m) =>
        m?.Component(ComponentRegistry.Luma) is not null && m.Component(ComponentRegistry.RenoDxHdr) is not null;

    /// <summary>The user already said yes to this pair: either component's record carries the mark.</summary>
    public static bool Confirmed(InstallManifest? m) =>
        m?.Component(ComponentRegistry.Luma)?.Extras.GetValueOrDefault(ClashKey) == ClashValue
        || m?.Component(ComponentRegistry.RenoDxHdr)?.Extras.GetValueOrDefault(ClashKey) == ClashValue;

    /// <summary>The warning for installing Luma now (RenoDX HDR is there), else null.</summary>
    public static string? ForLuma(InstallManifest? m) => m?.Component(ComponentRegistry.RenoDxHdr) is not null ? Message : null;

    /// <summary>The warning for installing RenoDX HDR now (Luma is there), else null.</summary>
    public static string? ForRenoDx(InstallManifest? m) => m?.Component(ComponentRegistry.Luma) is not null ? MessageForRenoDx : null;

    public static IReadOnlyDictionary<string, string> WithMark(IReadOnlyDictionary<string, string> extras, bool confirmed)
    {
        if (!confirmed) return extras;
        var d = new Dictionary<string, string>(extras) { [ClashKey] = ClashValue };
        return d;
    }
}

/// <summary>A plan for the installer plus where its pieces went, for the log and the tests.</summary>
public sealed record LumaPlan(ComponentPlan Plan, IReadOnlyList<string> AddonFiles, EngineIniTarget? EngineIni, IReadOnlyList<string> Warnings);

/// <summary>Builds the component plan for a pick: the zip's files (add-on to the add-on folder, the Luma\ tree beside the exe), reshade.ini and Engine.ini keys. No writes.</summary>
public static class LumaPlanner
{
    public const string SignatureKey = "signature", ModKey = "mod", PackageKey = "package", EngineIniKey = "engineIni";

    /// <param name="names">The zip entries that were unpacked into <paramref name="packageDir"/> (<see cref="LumaFiles.Include"/> applied).</param>
    public static LumaPlan Build(GameInfo game, string targetDir, LumaChoice choice, LumaBuild build, string packageDir, IReadOnlyList<string> names,
        bool? hdr, string? engineIniHint, InstalledComponent? existing)
    {
        if (!choice.IsOffered) throw new InvalidOperationException(choice.Reason);
        targetDir = FileUtil.Normalize(targetDir);
        var warnings = new List<string>();
        var addonDir = ReShadeAddonPath.Resolve(targetDir, game.Root);

        var files = names.Select(n => (Source: Path.Combine(packageDir, n.Replace('/', Path.DirectorySeparatorChar)), Dest: LumaFiles.Destination(n, targetDir, addonDir))).ToList();
        var addons = files.Where(f => LumaFiles.IsAddon(f.Dest)).Select(f => Path.GetFileName(f.Dest)).ToList();
        if (addons.Count == 0) throw new InvalidDataException($"{build.FileName} has no ReShade add-on in it.");
        foreach (var other in OtherLumaAddons(addonDir, addons))
            warnings.Add($"{other} is also in the add-on folder; two Luma mods in one game fight over the same hooks.");

        var edits = new List<(string File, string Section, string Key, string Value)>();
        var reshadeIni = Path.Combine(targetDir, ReShadeAddonPath.IniName);
        foreach (var (s, k, v) in LumaIni.ReShadeKeys(hdr)) edits.Add((reshadeIni, s, k, v));

        var readOnly = new List<string>();
        EngineIniTarget? engine = null;
        if (choice.EngineIni.Count > 0)
        {
            var local = InstallManifest.KnownFolder("|localappdata|") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var docs = InstallManifest.KnownFolder("|documents|") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            engine = EngineIniLocator.Resolve(game, engineIniHint, local, docs);
            if (engine is null)
                warnings.Add("Engine.ini couldn't be placed (the game's Unreal project wasn't found), so the wiki's Engine.ini lines were not written. They are listed on the page.");
            else
            {
                var entry = InstallManifest.ConfigEntry(FileUtil.Normalize(engine.Path), targetDir, FileUtil.Normalize(game.Root));
                var wanted = choice.EngineIni.Select(k => (k.Section, k.Key, k.Value));
                foreach (var (s, k, v) in HdrIni.MissingOnly(Read(engine.Path), existing, entry, wanted)) edits.Add((engine.Path, s, k, v));
                readOnly.Add(engine.Path);
            }
        }

        var plan = new ComponentPlan(ComponentRegistry.Luma)
        {
            BuildId = build.Id,
            Version = build.Version,
            Files = files.Select(f => (f.Source, f.Dest)).ToList(),
            ConfigEdits = edits,
            ReadOnly = readOnly,
            Extras = new Dictionary<string, string>
            {
                [SignatureKey] = choice.Signature(hdr),
                [ModKey] = choice.ModId ?? "",
                [PackageKey] = choice.PackageId ?? "",
                [EngineIniKey] = engine?.Path ?? "",
            },
        };
        return new LumaPlan(plan, addons, engine, warnings);
    }

    private static IniFile? Read(string path)
    {
        try { return File.Exists(path) ? IniFile.Load(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static IEnumerable<string> OtherLumaAddons(string dir, IReadOnlyList<string> ours)
    {
        if (!Directory.Exists(dir)) return [];
        try
        {
            return Directory.EnumerateFiles(dir, "Luma-*.addon*").Select(Path.GetFileName).OfType<string>()
                .Where(n => !ours.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}

/// <summary>Installed against catalog for one game, as the game page and Update all use it.</summary>
public sealed record LumaStatus(HdrState State, string? Installed, string? Latest, string Line)
{
    public bool UpdateAvailable => State == HdrState.Update;

    public static LumaStatus Of(LumaChoice choice, InstalledComponent? installed, bool? hdr)
    {
        if (installed is null)
            return choice.IsOffered
                ? new LumaStatus(HdrState.NotInstalled, null, choice.Version, $"Not installed · {choice.Version} available")
                : new LumaStatus(HdrState.Unavailable, null, null, choice.Reason);
        var have = installed.Version ?? installed.BuildId?[..Math.Min(12, installed.BuildId.Length)];
        if (!choice.IsOffered)
            return new LumaStatus(HdrState.Orphaned, have, null, $"Installed {have}, but nothing is offered for this game now: {choice.Reason}");
        var latest = choice.Version;
        if (installed.Extras.GetValueOrDefault(LumaPlanner.SignatureKey) == choice.Signature(hdr) && installed.BuildId == choice.File!.Sha256)
            return new LumaStatus(HdrState.Current, have, latest, $"Installed {have} · up to date");
        if (installed.BuildId != choice.File!.Sha256)
            return new LumaStatus(HdrState.Update, have, latest, installed.Extras.GetValueOrDefault(LumaPlanner.ModKey) == (choice.ModId ?? "")
                ? $"Installed {have} · update to {latest}"
                : $"Installed {have} (another mod) · switch to {choice.Title} {latest}");
        return new LumaStatus(HdrState.Update, have, latest, $"Installed {have} · settings changed, update to apply them");
    }
}

public sealed record LumaInstallResult(LumaBuild Build, LumaPlan Plan, ComponentUndo Undo);

/// <summary>Installs, updates and removes the Luma component in a game: library download, unpack, ReShade, then one component plan.</summary>
public sealed class LumaService(ComponentStore store, Installer installer, CatalogDownloader downloader)
{
    private readonly ReShadeAddonHost host = new(store, installer);

    /// <summary>
    /// Brings the game to the pick. ReShade with add-on support must be in the game: it is installed here through the managed ReShade
    /// install when the game has none, but not beside OptiScaler, where the user decides about ReShade. The zip's own ReShade dll and
    /// nvngx_dlss.dll are never copied.
    /// </summary>
    public async Task<LumaInstallResult> InstallAsync(GameInfo game, string targetDir, LumaChoice choice, bool? hdr, string? engineIniHint,
        HdrReShade? reshade, bool clashConfirmed, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (game.AntiCheat is { } ac) throw new InvalidOperationException($"{game.Name} has {ac} anti-cheat: no add-on is installed in games with anti-cheat.");
        if (!choice.IsOffered) throw new InvalidOperationException(choice.Reason);

        var build = choice.Pinned is { } pinned
            ? store.VerifiedLumaBuild(pinned)
            : await store.EnsureLumaBuildAsync(choice.File!, choice.PackageId ?? choice.Title, choice.Version, downloader, progress, ct);

        // Unpacked fresh from the verified zip every time, so the files placed are exactly the zip's.
        var pkg = ComponentStore.LumaPackageDirOf(build);
        var names = await Task.Run(() =>
        {
            if (Directory.Exists(pkg)) FileUtil.DeleteDirectory(pkg);
            return ZipStage.Extract(ComponentStore.LumaPathOf(build), pkg, LumaFiles.Include);
        }, ct);

        await host.EnsureAsync(game, targetDir, reshade ?? new HdrReShade(), new("Luma", "Luma needs ReShade 6.6 or newer (its ReadMe says so). Update ReShade for this game first.", new Version(6, 6)), progress, ct);

        var existing = InstallManifest.Read(targetDir)?.Component(ComponentRegistry.Luma);
        var plan = LumaPlanner.Build(game, targetDir, choice, build, pkg, names, hdr, engineIniHint, existing);
        foreach (var w in plan.Warnings) Log.Warn($"{game.Name}: {w}");
        progress?.Report(new TransferProgress($"Installing {choice.Title} to {game.Name}", null));
        var component = plan.Plan with { Extras = LumaConflict.WithMark(plan.Plan.Extras, clashConfirmed) };
        var undo = await installer.InstallComponentAsync(game, targetDir, component, ct);
        return new LumaInstallResult(build, plan, undo);
    }

    public Task<ComponentUndo> RemoveAsync(GameInfo game, string targetDir, CancellationToken ct) =>
        installer.RemoveComponentAsync(game, targetDir, ComponentRegistry.Luma, ct);
}
