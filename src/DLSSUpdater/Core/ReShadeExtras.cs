using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>What <see cref="CatalogDownloader"/> does, so a test can hand in files without a network.</summary>
public interface ICatalogFetch
{
    Task<CatalogDownload> DownloadAsync(CatalogBinary file, string dest, IProgress<TransferProgress>? progress, CancellationToken ct);
}

/// <summary>The catalog's add-ons and shader packs together with the ones the user imported.</summary>
public sealed class ExtrasCatalog
{
    public bool HasCatalog { get; private init; }
    public IReadOnlyList<CatalogAddon> Addons { get; private init; } = [];
    public IReadOnlyList<CatalogShaderPack> Packs { get; private init; } = [];

    public static ExtrasCatalog From(CatalogDocument? doc) => new()
    {
        HasCatalog = doc?.Sections is { Addons: not null } or { ShaderPacks: not null },
        Addons = [.. doc?.Sections.Addons?.Addons ?? [], .. ExtrasLibrary.Addons()],
        Packs = [.. doc?.Sections.ShaderPacks?.Packs ?? [], .. ExtrasLibrary.Packs()],
    };

    public bool IsEmpty => Addons.Count == 0 && Packs.Count == 0;
    public CatalogAddon? Addon(string id) => Addons.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    public CatalogShaderPack? Pack(string id) => Packs.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    public CatalogShaderPacks PackList => new() { Packs = [.. Packs] };
}

/// <param name="Ok">False when nothing was changed.</param>
/// <param name="Message">One line for the status text and the log.</param>
/// <param name="Notes">What was left out or kept, one line each.</param>
public sealed record ExtrasResult(bool Ok, string Message, IReadOnlyList<string> Notes);

/// <summary>What a preset needs that a game doesn't have, and the packs that would supply it.</summary>
public sealed record PresetNeeds(IReadOnlyList<string> Missing, IReadOnlyList<CatalogShaderPack> Suggested, IReadOnlyList<string> NoPack);

/// <summary>
/// ReShade add-ons (crosire's list), shader packs and presets in a game, each recorded as a generic component
/// (<see cref="ComponentRegistry.ReShadeAddons"/>, <see cref="ComponentRegistry.ReShadeShaders"/>, <see cref="ComponentRegistry.ReShadePreset"/>)
/// whose plan is the whole wanted set: what the settings say now, from the catalog or the user's imports, replaces whatever was
/// there. Downloads are checked against the catalog's hash before anything is unpacked. Nothing happens in a game with anti-cheat
/// or without ReShade. A user's own ReShade.ini override of a key the app would set wins.
/// </summary>
public sealed class ReShadeExtras
{
    private readonly Installer _installer;
    private readonly AppSettings _settings;
    private readonly ICatalogFetch _fetch;
    private readonly Func<CancellationToken, Task<CatalogLoad>>? _load;
    private CatalogDocument? _document;

    public ReShadeExtras(Installer installer, AppSettings settings, ICatalogFetch fetch, Func<CancellationToken, Task<CatalogLoad>>? load = null)
    {
        _installer = installer;
        _settings = settings;
        _fetch = fetch;
        _load = load;
        Catalog = ExtrasCatalog.From(null);
    }

    public ExtrasCatalog Catalog { get; private set; }
    public string? CatalogNote { get; private set; }
    public event Action? Changed;

    /// <summary>Loads the catalog (online, else the last good copy); the lists keep what they had when it can't.</summary>
    public async Task LoadCatalogAsync(CancellationToken ct)
    {
        if (_load is not null)
        {
            var load = await _load(ct);
            if (load.Document is not null) _document = load.Document;
            CatalogNote = load.Note;
        }
        Reload();
    }

    /// <summary>Tells the lists that a selection changed (a game page and the settings show the same picks).</summary>
    public void NotifyPicksChanged() => Changed?.Invoke();

    /// <summary>Picks up the user's imports (and a catalog set from outside).</summary>
    public void Reload(CatalogDocument? document = null)
    {
        if (document is not null) _document = document;
        Catalog = ExtrasCatalog.From(_document);
        Changed?.Invoke();
    }

    // ---- guards and choices ----

    /// <summary>Why nothing can be placed in this game, or null.</summary>
    public static string? Guard(GameInfo game, string target, InstallManifest? manifest)
    {
        if (game.AntiCheat is { } ac)
            return $"{ac} detected. ReShade add-ons and shaders can get you banned in online modes, so none are installed here.";
        if (manifest?.ReShade != true && !File.Exists(ReShadeLayout.IniPath(FileUtil.Normalize(target))))
            return "ReShade isn't installed in this game yet. Install it from the Install tab first.";
        return null;
    }

    /// <summary>The bitness add-ons are chosen for: the game's own, unless the game has a manual pick.</summary>
    public int BitsOf(GameInfo game) => ExtrasPicks.BitsOf(_settings.Games.GetValueOrDefault(game.Id)) ?? (game.Effective.Bits == 32 ? 32 : 64);

    private bool UserSets(string gameId, string key) =>
        GameConfig.EffectiveReShade(_settings, gameId).Any(o => o.Section.Equals("GENERAL", StringComparison.OrdinalIgnoreCase)
                                                                 && o.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(o.Value));

    private static Func<string, string?> OtherOwner(InstallManifest? m, string root, string target, string self) => dest =>
    {
        var rel = InstallManifest.Entry(dest, target, root);
        var owner = m?.InstalledComponents().FirstOrDefault(c => !c.Id.Equals(self, StringComparison.OrdinalIgnoreCase) && c.FileAt(rel) is not null);
        return owner is null ? null : ComponentRegistry.Find(owner.Id)?.Name ?? owner.Id;
    };

    private static ExtrasResult Fail(string message, IReadOnlyList<string>? notes = null) => new(false, message, notes ?? []);

    // ---- what is wanted, and whether the game has it ----

    private Dictionary<string, string> WantedAddons(GameInfo game)
    {
        var bits = BitsOf(game);
        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["bits"] = bits.ToString() };
        foreach (var id in ExtrasPicks.AddonIds(_settings, _settings.Games.GetValueOrDefault(game.Id)))
            wanted["want:" + id] = Catalog.Addon(id) is { } a && ReShadeAddonPlanner.WhyNot(a, bits) is null ? ReShadeAddonPlanner.Token(a, bits) : "n/a";
        return wanted;
    }

    private Dictionary<string, string> WantedPacks(GameInfo game)
    {
        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ExtrasPicks.PackIds(_settings, _settings.Games.GetValueOrDefault(game.Id), Catalog.PackList))
            wanted["want:" + id] = Catalog.Pack(id) is { IsInstallable: true } p ? ShaderPackPlanner.Token(p) : "n/a";
        return wanted;
    }

    private Dictionary<string, string>? WantedPreset(GameInfo game)
    {
        if (ExtrasPicks.PresetOf(_settings.Games.GetValueOrDefault(game.Id)) is not { } name) return new();
        return ReShadePresets.Find(name) is { Check.Valid: true } p
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["preset"] = p.Name, ["sha"] = FileUtil.Sha256(p.Path) }
            : null;
    }

    private static bool Same(IReadOnlyDictionary<string, string> recorded, IReadOnlyDictionary<string, string> wanted, params string[] prefixes)
    {
        var have = recorded.Where(e => prefixes.Any(p => e.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase) || e.Key.Equals(p, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
        return have.Count == wanted.Count && have.All(e => wanted.TryGetValue(e.Key, out var v) && v == e.Value);
    }

    /// <summary>
    /// The extras components installed in the game whose wanted set or builds differ from the settings and catalog now. A game
    /// that doesn't have a component is never listed, and nothing is listed while the catalog is missing.
    /// </summary>
    public IReadOnlyList<string> ComponentsToUpdate(GameInfo game, string target, InstallManifest? m)
    {
        var list = new List<string>();
        if (m is null || Guard(game, target, m) is not null) return list;
        if (m.Component(ComponentRegistry.ReShadeAddons) is { } a && !Catalog.IsEmpty && !Same(a.Extras, WantedAddons(game), "want:", "bits"))
            list.Add(ComponentRegistry.ReShadeAddons);
        if (m.Component(ComponentRegistry.ReShadeShaders) is { } s && !Catalog.IsEmpty && !Same(s.Extras, WantedPacks(game), "want:"))
            list.Add(ComponentRegistry.ReShadeShaders);
        if (m.Component(ComponentRegistry.ReShadePreset) is { } p && WantedPreset(game) is { Count: > 0 } wp && !Same(p.Extras, wp, "preset", "sha"))
            list.Add(ComponentRegistry.ReShadePreset);
        return list;
    }

    /// <summary>Brings every installed extras component of the game to what the settings say now.</summary>
    public async Task<IReadOnlyList<ExtrasResult>> UpdateAsync(GameInfo game, string target, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var results = new List<ExtrasResult>();
        foreach (var id in ComponentsToUpdate(game, target, InstallManifest.Load(FileUtil.Normalize(target))))
            results.Add(id switch
            {
                ComponentRegistry.ReShadeAddons => await ApplyAddonsAsync(game, target, progress, ct),
                ComponentRegistry.ReShadeShaders => await ApplyShadersAsync(game, target, progress, ct),
                _ => await ApplyPresetAsync(game, target, ExtrasPicks.PresetOf(_settings.Games.GetValueOrDefault(game.Id)), ct),
            });
        return results;
    }

    // ---- download and unpack ----

    private static string CacheDir(CatalogBinary file) => Path.Combine(ExtrasLibrary.CacheRoot, file.Sha256!.ToLowerInvariant());

    /// <summary>The verified download unpacked in the cache (once; a second call finds it there), or the reason it was refused.</summary>
    private async Task<(string? Dir, string? Refusal)> PrepareAsync(CatalogBinary file, Func<string, bool>? include, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var dir = CacheDir(file);
        if (Directory.Exists(dir)) return (dir, null);
        Directory.CreateDirectory(ExtrasLibrary.CacheRoot);
        var part = dir + ".download";
        var stage = dir + ".new";
        try
        {
            var dl = await _fetch.DownloadAsync(file, part, progress, ct);
            if (!dl.Ok) return (null, dl.Refusal ?? $"{file.Name}: could not be downloaded");
            FileUtil.DeleteDirectory(stage);
            if (file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ZipStage.Extract(part, stage, include);
            else
            {
                Directory.CreateDirectory(stage);
                File.Copy(part, Path.Combine(stage, file.FileName), true);
            }
            Directory.Move(stage, dir);
            return (dir, null);
        }
        catch (InvalidDataException ex) { return (null, $"{file.Name}: refused, {ex.Message}"); }
        finally
        {
            FileUtil.TryDeleteFile(part);
            FileUtil.DeleteDirectory(stage);
        }
    }

    private static string ImportedDir(string id) => ExtrasLibrary.DirOf(id);

    // ---- add-ons ----

    public async Task<ExtrasResult> ApplyAddonsAsync(GameInfo game, string target, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        target = FileUtil.Normalize(target);
        var m = InstallManifest.Load(target);
        if (Guard(game, target, m) is { } why) return Fail(why);
        var over = _settings.Games.GetValueOrDefault(game.Id);
        var wanted = ExtrasPicks.AddonIds(_settings, over);
        var installed = m?.Component(ComponentRegistry.ReShadeAddons) is not null;
        if (wanted.Count == 0) return installed ? await RemoveAsync(game, target, ComponentRegistry.ReShadeAddons, ct) : new ExtrasResult(true, "No add-ons are selected.", []);
        if (Catalog.IsEmpty) return Fail("The add-on catalog isn't available (offline, and no earlier copy), so add-ons can't be changed now.");
        var (addonDir, refusal) = ReShadeLayout.AddonDir(target, game.Root);
        if (addonDir is null) return Fail(refusal!);

        var bits = BitsOf(game);
        var notes = new List<string>();
        var sources = new List<AddonSource>();
        foreach (var id in wanted.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (Catalog.Addon(id) is not { } addon) { notes.Add($"{id}: not in the catalog any more, so it is left out"); continue; }
            if (ReShadeAddonPlanner.WhyNot(addon, bits) is { } no) { notes.Add($"{addon.Name}: {no}"); continue; }
            foreach (var file in ReShadeAddonPlanner.FilesFor(addon, bits))
            {
                var (dir, bad) = addon.Imported ? (ImportedDir(addon.Id), null) : await PrepareAsync(file, null, progress, ct);
                if (dir is null) return Fail(bad!, notes);
                sources.Add(new AddonSource(addon, file, dir));
            }
        }

        var folder = ReShadeLayout.ResolveShaderFolder(target, game.Root, m);
        var plan = ReShadeAddonPlanner.Plan(sources, addonDir, folder.ShadersDir, bits, OtherOwner(m, game.Root, target, ComponentRegistry.ReShadeAddons));
        notes.AddRange(plan.Skips.Select(s => s.Reason));
        if (plan.Files.Count == 0)
            return installed ? await RemoveAsync(game, target, ComponentRegistry.ReShadeAddons, ct) : Fail("None of the selected add-ons can be installed in this game.", notes);

        var edits = new List<(string File, string Section, string Key, string Value)>();
        if (plan.HasEffects)
        {
            if (UserSets(game.Id, "EffectSearchPaths")) notes.Add("Your ReShade.ini override of EffectSearchPaths is kept; add the add-on's effects folder there yourself if it needs one.");
            else edits.AddRange(ReShadeLayout.SearchPathEdits(target, folder, effects: true, textures: false));
        }
        var extras = WantedAddons(game);
        if (plan.HasEffects && !UserSets(game.Id, "EffectSearchPaths")) extras[ReShadeLayout.OwnedEntryKey("EffectSearchPaths")] = ReShadeLayout.SearchEntry(folder, "Shaders");
        foreach (var id in plan.Placed.Keys) extras["placed:" + id] = plan.Placed[id];
        var undo = await _installer.InstallComponentAsync(game, target,
            new ComponentPlan(ComponentRegistry.ReShadeAddons) { Version = Count(plan.Placed.Count, "add-on"), Files = plan.Files, ConfigEdits = edits, Extras = extras }, ct);
        notes.AddRange(UndoNotes(undo));
        return new ExtrasResult(true, $"{Count(plan.Placed.Count, "add-on")} installed in {game.Name} ({bits}-bit)" + (addonDir == target ? "" : $", in {Path.GetRelativePath(game.Root, addonDir)}"), notes);
    }

    // ---- shader packs ----

    public async Task<ExtrasResult> ApplyShadersAsync(GameInfo game, string target, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        target = FileUtil.Normalize(target);
        var m = InstallManifest.Load(target);
        if (Guard(game, target, m) is { } why) return Fail(why);
        var over = _settings.Games.GetValueOrDefault(game.Id);
        var wanted = ExtrasPicks.PackIds(_settings, over, Catalog.PackList);
        var installed = m?.Component(ComponentRegistry.ReShadeShaders) is not null;
        if (wanted.Count == 0) return installed ? await RemoveAsync(game, target, ComponentRegistry.ReShadeShaders, ct) : new ExtrasResult(true, "No shader packs are selected.", []);
        if (Catalog.IsEmpty) return Fail("The shader pack catalog isn't available (offline, and no earlier copy), so shaders can't be changed now.");

        var notes = new List<string>();
        var sources = new List<PackSource>();
        foreach (var id in wanted.Where(id => Catalog.Pack(id) is null).Order(StringComparer.OrdinalIgnoreCase)) notes.Add($"{id}: not in the catalog any more, so it is left out");
        foreach (var pack in Catalog.Packs.Where(p => wanted.Contains(p.Id)))
        {
            if (!pack.IsInstallable) { notes.Add($"{pack.Name}: the catalog has no verified download for it"); continue; }
            var layout = pack.Layout!;
            bool Wanted(string name) => name.StartsWith(layout.ShadersDir, StringComparison.OrdinalIgnoreCase)
                                        || (layout.TexturesDir is { Length: > 0 } t && name.StartsWith(t, StringComparison.OrdinalIgnoreCase));
            var (dir, bad) = pack.Imported ? (ImportedDir(pack.Id), null) : await PrepareAsync(pack.File!, Wanted, progress, ct);
            if (dir is null) return Fail(bad!, notes);
            sources.Add(new PackSource(pack, dir));
        }

        var folder = ReShadeLayout.ResolveShaderFolder(target, game.Root, m);
        var plan = ShaderPackPlanner.Plan(sources, folder, OtherOwner(m, game.Root, target, ComponentRegistry.ReShadeShaders));
        notes.AddRange(plan.Skips.Select(s => s.Reason));
        if (plan.Files.Count == 0)
            return installed ? await RemoveAsync(game, target, ComponentRegistry.ReShadeShaders, ct) : Fail("None of the selected shader packs can be installed.", notes);
        if (folder.UserFolderKept) notes.Add($"Your own {ReShadeLayout.ShadersFolder} folder is left exactly as it is. The packs went to {ReShadeLayout.ManagedFolder} instead, and both are on the search paths.");

        var marker = Path.Combine(AppPaths.Cache, "reshade-extras-marker-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            FileUtil.AtomicWriteText(marker, ShaderPackPlanner.MarkerText(sources.Where(s => plan.Placed.ContainsKey(s.Pack.Id)).Select(s => s.Pack)));
            var files = new List<(string Source, string Dest)>(plan.Files) { (marker, Path.Combine(folder.Dir, ReShadeLayout.Marker)) };
            var edits = new List<(string File, string Section, string Key, string Value)>();
            var hasTextures = sources.Any(s => s.Pack.Layout?.TexturesDir is { Length: > 0 });
            foreach (var (key, effects) in new[] { ("EffectSearchPaths", true), ("TextureSearchPaths", false) })
            {
                if (!effects && !hasTextures) continue;
                if (UserSets(game.Id, key)) { notes.Add($"Your ReShade.ini override of {key} is kept; the packs are in {folder.Name} if you want to point it there."); continue; }
                edits.AddRange(ReShadeLayout.SearchPathEdits(target, folder, effects, !effects).Where(e => e.Key == key));
            }
            var extras = WantedPacks(game);
            extras["folder"] = folder.Name;
            if (!UserSets(game.Id, "EffectSearchPaths")) extras[ReShadeLayout.OwnedEntryKey("EffectSearchPaths")] = ReShadeLayout.SearchEntry(folder, "Shaders");
            if (hasTextures && !UserSets(game.Id, "TextureSearchPaths")) extras[ReShadeLayout.OwnedEntryKey("TextureSearchPaths")] = ReShadeLayout.SearchEntry(folder, "Textures");
            foreach (var id in plan.Placed.Keys) extras["placed:" + id] = plan.Placed[id];
            var undo = await _installer.InstallComponentAsync(game, target,
                new ComponentPlan(ComponentRegistry.ReShadeShaders) { Version = Count(plan.Placed.Count, "pack"), Files = files, ConfigEdits = edits, Extras = extras }, ct);
            notes.AddRange(UndoNotes(undo));
            return new ExtrasResult(true, $"{Count(plan.Placed.Count, "shader pack")} installed in {game.Name} ({plan.Files.Count} files in {folder.Name})", notes);
        }
        finally { FileUtil.TryDeleteFile(marker); }
    }

    // ---- presets ----

    /// <summary>What <paramref name="preset"/> needs that the game doesn't have, and the packs (catalog and imported) that hold it.</summary>
    public PresetNeeds NeedsOf(GameInfo game, string target, PresetInfo preset)
    {
        var missing = ReShadePresets.Missing(preset.Check, ReShadePresets.InstalledEffects(target, game.Root));
        var suggested = ReShadePresets.SuggestPacks(missing, Catalog.Packs);
        var covered = new HashSet<string>(suggested.SelectMany(p => p.EffectNames), StringComparer.OrdinalIgnoreCase);
        return new PresetNeeds(missing, suggested, missing.Where(e => !covered.Contains(e)).ToList());
    }

    /// <summary>Copies the preset into the game and points ReShade at it. Presets that need shaders the game lacks are still applied.</summary>
    public async Task<ExtrasResult> ApplyPresetAsync(GameInfo game, string target, string? name, CancellationToken ct)
    {
        target = FileUtil.Normalize(target);
        var m = InstallManifest.Load(target);
        if (Guard(game, target, m) is { } why) return Fail(why);
        if (name is null) return m?.Component(ComponentRegistry.ReShadePreset) is not null ? await RemoveAsync(game, target, ComponentRegistry.ReShadePreset, ct) : new ExtrasResult(true, "No preset is chosen.", []);
        var preset = ReShadePresets.Find(name);
        if (preset is null) return Fail($"The preset \"{name}\" is not in the presets library. Import it again.");
        if (!preset.Check.Valid) return Fail($"\"{name}\" is not a ReShade preset: {preset.Check.Problem}.");

        var notes = new List<string>();
        var rel = $@".\{ReShadeLayout.PresetsFolder}\{preset.Name}.ini";
        var edits = new List<(string File, string Section, string Key, string Value)>();
        if (UserSets(game.Id, "PresetPath")) notes.Add("Your ReShade.ini override of PresetPath is kept, so ReShade will not load this preset by itself.");
        else edits.Add((ReShadeLayout.IniPath(target), "GENERAL", "PresetPath", rel));
        var needs = NeedsOf(game, target, preset);
        if (needs.Missing.Count > 0)
            notes.Add($"The preset needs {Count(needs.Missing.Count, "effect file")} this game doesn't have: {string.Join(", ", needs.Missing.Take(8))}{(needs.Missing.Count > 8 ? ", …" : "")}.");
        var undo = await _installer.InstallComponentAsync(game, target, new ComponentPlan(ComponentRegistry.ReShadePreset)
        {
            Version = preset.Name,
            Files = [(preset.Path, Path.Combine(target, ReShadeLayout.PresetsFolder, preset.Name + ".ini"))],
            ConfigEdits = edits,
            Extras = new Dictionary<string, string> { ["preset"] = preset.Name, ["sha"] = FileUtil.Sha256(preset.Path) },
        }, ct);
        notes.AddRange(UndoNotes(undo));
        return new ExtrasResult(true, $"Preset {preset.Name} applied to {game.Name}", notes);
    }

    // ---- removal ----

    public async Task<ExtrasResult> RemoveAsync(GameInfo game, string target, string componentId, CancellationToken ct)
    {
        target = FileUtil.Normalize(target);
        var name = ComponentRegistry.Find(componentId)?.Name ?? componentId;
        if (InstallManifest.Load(target)?.Component(componentId) is null) return new ExtrasResult(true, $"{name} is not installed in {game.Name}.", []);
        var undo = await _installer.RemoveComponentAsync(game, target, componentId, ct);
        return new ExtrasResult(true, $"{name} removed from {game.Name}", UndoNotes(undo));
    }

    private static List<string> UndoNotes(ComponentUndo undo) =>
        undo.ChangedFiles.Select(f => $"{f} changed since it was installed and was left in place")
            .Concat(undo.ChangedKeys.Select(k => $"{k} changed since it was set and was left as it is"))
            .Concat(undo.ChangedAttributes.Select(a => $"{a}: attributes changed since they were set and were left as they are")).ToList();

    private static string Count(int n, string what) => $"{n} {what}{(n == 1 ? "" : "s")}";
}
