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
        string? engineIniHint, InstalledComponent? existing, IReadOnlyList<HdrIniEdit>? userEdits = null)
    {
        if (!choice.IsOffered) throw new InvalidOperationException(choice.Reason);
        targetDir = FileUtil.Normalize(targetDir);
        var warnings = new List<string>();
        var peak = HdrIni.ClampPeak(peakNits);
        var mine = HdrUserEdits.Clean(userEdits);

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
        // Values typed on the game page win over ours and over what the file holds.
        foreach (var e in mine.Where(e => e.IsReShade))
            ReplaceOrAdd(edits, reshadeIni, e);

        var readOnly = new List<string>();
        EngineIniTarget? engine = null;
        var engineKeys = HdrIni.EngineKeys(choice);
        var engineMine = mine.Where(e => e.IsEngine).ToList();
        if (engineKeys.Count > 0 || engineMine.Count > 0)
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
                foreach (var e in engineMine) ReplaceOrAdd(edits, engine.Path, e);
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
                [SignatureKey] = choice.Signature(peak, HdrUserEdits.Signature(mine)),
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

    private static void ReplaceOrAdd(List<(string File, string Section, string Key, string Value)> edits, string file, HdrIniEdit e)
    {
        var i = edits.FindIndex(x => x.File.Equals(file, StringComparison.OrdinalIgnoreCase) && x.Section.Equals(e.Section, StringComparison.OrdinalIgnoreCase) && x.Key.Equals(e.Key, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) edits[i] = (file, edits[i].Section, edits[i].Key, e.Value);
        else edits.Add((file, e.Section, e.Key, e.Value));
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
    public static HdrStatus Of(HdrChoice choice, InstalledComponent? installed, int? peakNits, IEnumerable<HdrIniEdit>? userEdits = null)
    {
        if (installed is null)
            return choice.IsOffered
                ? new HdrStatus(HdrState.NotInstalled, null, choice.File!.Version, $"Not installed · {choice.File.Version} available")
                : new HdrStatus(HdrState.Unavailable, null, null, choice.Reason);
        var have = installed.Version ?? installed.BuildId?[..Math.Min(12, installed.BuildId.Length)];
        if (!choice.IsOffered)
            return new HdrStatus(HdrState.Orphaned, have, null, $"Installed {have}, but nothing is offered for this game now: {choice.Reason}");
        var latest = choice.File!.Version;
        var signature = choice.Signature(HdrIni.ClampPeak(peakNits), HdrUserEdits.Signature(userEdits));
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

/// <summary>
/// What the game's ReShade install uses when the HDR mod has to bring ReShade along: the game's build pick, its proxy name, and the
/// ReShade.ini template and overrides (the same ones the game page's own install passes).
/// </summary>
public sealed record HdrReShade(string? BuildPick = null, string? Proxy = null, IReadOnlyList<IniOverride>? Overrides = null, IReadOnlyList<IniOverride>? Template = null);

/// <summary>Installs, updates and removes the RenoDX HDR component in a game: library download, ReShade, then one component plan.</summary>
public sealed class HdrService(ComponentStore store, Installer installer, CatalogDownloader downloader)
{
    private readonly ReShadeAddonHost host = new(store, installer);

    /// <summary>
    /// Brings the game to the pick. ReShade with add-on support must be in the game: it is installed here through the managed ReShade
    /// install (the game's build, API-based name, ReShade32 for 32-bit games, the Vulkan layer) when the game has none, but not beside
    /// OptiScaler, where the user decides about ReShade.
    /// </summary>
    public async Task<HdrInstallResult> InstallAsync(GameInfo game, string targetDir, HdrChoice choice, int? peakNits, string? engineIniHint,
        HdrReShade? reshade, IProgress<TransferProgress>? progress, CancellationToken ct, IReadOnlyList<HdrIniEdit>? userEdits = null)
    {
        if (game.AntiCheat is { } ac) throw new InvalidOperationException($"{game.Name} has {ac} anti-cheat: no add-on is installed in games with anti-cheat.");
        if (!choice.IsOffered) throw new InvalidOperationException(choice.Reason);

        var build = choice.Pinned is { } pinned
            ? store.VerifiedHdrBuild(pinned)
            : await store.EnsureHdrBuildAsync(choice.File!, choice.ModId ?? choice.Title, choice.Title, downloader, progress, ct);
        await EnsureReShadeAsync(game, targetDir, reshade ?? new HdrReShade(), progress, ct);

        var existing = InstallManifest.Read(targetDir)?.Component(ComponentRegistry.RenoDxHdr);
        var plan = HdrPlanner.Build(game, targetDir, choice, build, ComponentStore.HdrPathOf(build), peakNits, engineIniHint, existing, userEdits);
        foreach (var w in plan.Warnings) Log.Warn($"{game.Name}: {w}");
        progress?.Report(new TransferProgress($"Installing {choice.Title} to {game.Name}", null));
        var undo = await installer.InstallComponentAsync(game, targetDir, plan.Plan, ct);
        return new HdrInstallResult(build, plan, undo);
    }

    public Task<ComponentUndo> RemoveAsync(GameInfo game, string targetDir, CancellationToken ct) =>
        installer.RemoveComponentAsync(game, targetDir, ComponentRegistry.RenoDxHdr, ct);

    private Task EnsureReShadeAsync(GameInfo game, string targetDir, HdrReShade setup, IProgress<TransferProgress>? progress, CancellationToken ct) =>
        host.EnsureAsync(game, targetDir, setup, new("the HDR mod", "The RenoDX HDR mod needs ReShade 6 or newer with add-on support."), progress, ct);
}
