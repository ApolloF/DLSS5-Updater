using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

public enum HdrKind { None, Named, UeExtended, UnrealGeneric, UnityGeneric }

/// <summary>How Unreal Extended gets HDR into a game; read from the wiki notes in the catalog (<see cref="CatalogEngineGame.Method"/>).</summary>
public enum UeMethod { Native, Ini, Upgrade }

/// <summary>
/// What the RenoDX HDR component would install in one game, and why. <see cref="Kind"/> None means nothing is offered and
/// <see cref="Reason"/> says why in one line.
/// </summary>
public sealed record HdrChoice
{
    public HdrKind Kind { get; init; }
    /// <summary>The catalog entry that names the library group: the mod id for a named mod, else "unreal", "unity" or "ue-extended".</summary>
    public string? ModId { get; init; }
    public string Title { get; init; } = "";
    /// <summary>Why this was picked, or why nothing was.</summary>
    public string Reason { get; init; } = "";
    public CatalogBinary? File { get; init; }
    /// <summary>The library build the game is pinned to (<see cref="HdrPin"/>); null follows the catalog's file above.</summary>
    public HdrBuild? Pinned { get; init; }
    public CatalogMod? Mod { get; init; }
    public CatalogEngineGame? EngineGame { get; init; }
    /// <summary>Unreal Extended settings for this game (method, upgrades, Engine.ini); null for every other kind.</summary>
    public UeSettings? Ue { get; init; }
    /// <summary>The author's page for the mod, if the catalog or the generic add-on has one.</summary>
    public string? Link { get; init; }
    /// <summary>Caveats worth a line: in progress, opted out of Unreal Extended, no 32-bit build.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
    /// <summary>Unreal game without its own mod: the Unreal Extended switch applies.</summary>
    public bool CanToggleUeExtended { get; init; }

    public bool IsOffered => Kind != HdrKind.None && File is not null;

    /// <summary>Everything the files and ini edits depend on; an install is current while its stored signature matches.</summary>
    public string Signature(int? peakNits) =>
        string.Join('|', Kind, ModId, File?.Sha256, Ue?.Signature, peakNits?.ToString() ?? "");
}

/// <summary>Unreal Extended per game: which path it takes and which keys that needs.</summary>
public sealed record UeSettings(UeMethod Method, bool Known, IReadOnlyList<(string Format, int Mode)> Upgrades, bool? CopyDestinations,
    IReadOnlyList<(string Section, string Key, string Value)> EngineIni)
{
    /// <summary>[renodx] Set_Path: 1 = the SDR upgrade path, 0 = the game's native HDR path.</summary>
    public int SetPath => Method == UeMethod.Upgrade ? 1 : 0;

    /// <summary>Only the "ini" method (and an unknown UE5 game) needs HDR switched on in Engine.ini.</summary>
    public bool WritesEngineIni => Method == UeMethod.Ini;

    public string Signature => $"{Method}:{Known}:{string.Join(',', Upgrades.Select(u => u.Format + "=" + u.Mode))}:{CopyDestinations}:" +
                               string.Join(',', EngineIni.Select(k => $"{k.Section}/{k.Key}={k.Value}"));

    public static int ModeValue(string? mode) => mode switch { "output-size" => 1, "output-ratio" => 2, "any-size" => 3, _ => 0 };

    /// <summary>
    /// The catalog's entry for the game when it has one. Without one: UE4 and games whose Unreal version is unknown take the upgrade
    /// path (no Engine.ini edit); UE5 takes the native path with HDR switched on in Engine.ini.
    /// </summary>
    public static UeSettings For(GameTech tech, CatalogEngineGame? entry)
    {
        if (entry?.Method is "native" or "ini" or "upgrade")
        {
            var method = entry.Method switch { "native" => UeMethod.Native, "ini" => UeMethod.Ini, _ => UeMethod.Upgrade };
            return new UeSettings(method, true,
                entry.Upgrades.Select(u => (u.Format, ModeValue(u.Mode))).Where(u => u.Item2 > 0).ToList(),
                entry.CopyDestinations,
                entry.EngineIni.Select(k => (k.Section, k.Key, k.Value)).ToList());
        }
        return tech.UeMajor >= 5
            ? new UeSettings(UeMethod.Ini, false, [], null, [])
            : new UeSettings(UeMethod.Upgrade, false, [], null, []);
    }
}

/// <summary>
/// Which RenoDX HDR mod a game gets: the named mod (Steam AppID first), else for Unreal Unreal Extended or the plain generic add-on
/// (the user's switch), for Unity the generic add-on in the game's bitness, else nothing. Pure: it reads the catalog and what the
/// scan found, and changes nothing.
/// </summary>
public sealed class HdrPicker
{
    public const string UeExtendedId = "ue-extended", UnrealId = "unreal", UnityId = "unity";

    private readonly CatalogRenodx _catalog;
    private readonly GameMatcher _matcher;
    private readonly List<(string Key, CatalogEngineGame Game)> _engineGames;

    public HdrPicker(CatalogRenodx catalog)
    {
        _catalog = catalog;
        _matcher = new GameMatcher(catalog);
        _engineGames = catalog.EngineGames.Select(g => (GameMatcher.Normalize(g.Name), g)).Where(g => g.Item1.Length > 0).ToList();
    }

    /// <param name="ueExtended">The user's switch for Unreal games without a named mod; null = the default (Unreal Extended unless the catalog opts the game out).</param>
    public HdrChoice Pick(GameInfo game, bool? ueExtended)
    {
        if (game.AntiCheat is { } ac)
            return None($"{ac} anti-cheat: no add-on is installed in games with anti-cheat.");

        var tech = game.Tech;
        var bits = tech.Bits;
        var match = _matcher.Match(game);
        var mod = match.Mod;

        if (IsBlocked(game, mod) is { } blocked) return None(blocked, mod);

        string? engine = null;
        var why = "";
        if (mod is not null)
        {
            var how = match.Kind switch
            {
                MatchKind.AppId => $"Steam AppID {game.SteamAppId}",
                MatchKind.Override => "the catalog's name list",
                _ => "its name",
            };
            if (mod.Files.Any(f => f.IsInstallable))
                return Named(mod, how, bits);
            if (mod.Generic is UnrealId or UnityId)
            {
                engine = mod.Generic;
                why = $"{mod.Name} uses the generic {(engine == UnrealId ? "Unreal" : "Unity")} add-on (matched by {how}). ";
            }
            else
                return None($"{mod.Name} has a RenoDX mod, but the catalog has no download for it" +
                            (mod.Links?.Nexus is not null || mod.Links?.Discord is not null || mod.Links?.Discussion is not null ? "; see the mod's own page." : "."), mod);
        }
        else
        {
            engine = tech.Engine switch
            {
                GameEngine.Unreal => UnrealId,
                GameEngine.Unity => UnityId,
                _ => null,
            };
            if (tech.Engine == GameEngine.UnrealLegacy)
                return None("Unreal Engine 3 and older aren't supported by RenoDX.");
            if (engine is null)
                return None(tech.Engine == GameEngine.Unknown
                    ? "No RenoDX mod is known for this game, and its engine isn't Unreal or Unity."
                    : $"No RenoDX mod is known for this game, and the generic add-ons don't cover {tech.EngineLabel}.");
        }

        // The generic add-ons only come in 32 or 64 bit; a game whose exe couldn't be read is taken as 64-bit.
        return engine == UnrealId ? Unreal(game, bits == 32 ? 32 : 64, ueExtended, why) : Unity(game, bits == 32 ? 32 : 64, why);
    }

    private HdrChoice Named(CatalogMod mod, string how, int bits)
    {
        var file = FileFor(mod.Files, bits);
        var notes = new List<string>();
        if (mod.Compat == "in-progress") notes.Add("The mod is marked in progress by its author.");
        if (mod.Notes is { Length: > 0 } n) notes.Add(n.Replace('\n', ' '));
        if (file is null)
            return None($"{mod.Name}: the catalog has no {(bits == 32 ? "32-bit" : bits == 64 ? "64-bit" : "usable")} build for this game.", mod);
        return new HdrChoice
        {
            Kind = HdrKind.Named, ModId = mod.Id, Title = mod.Name, Mod = mod, File = file,
            Reason = $"{mod.Name} has its own RenoDX mod (matched by {how}).",
            Link = mod.Links?.Discussion ?? mod.Links?.Nexus ?? mod.Links?.Discord,
            Notes = notes,
        };
    }

    private HdrChoice Unreal(GameInfo game, int bits, bool? ueExtended, string why)
    {
        var overrides = _catalog.Overrides;
        var optedOut = overrides.OptsOutOfUeExtended(game.SteamAppId, game.Name);
        var wanted = ueExtended ?? !optedOut;
        var notes = new List<string>();
        if (optedOut)
            notes.Add(ueExtended == true
                ? "The catalog says this game shouldn't get Unreal Extended by default."
                : "The catalog opts this game out of Unreal Extended, so it gets the plain generic add-on.");

        if (bits == 32)
            return None(why + "The Unreal add-ons are 64-bit only; this game is 32-bit.");

        var entry = FindEngineGame("ue-extended", game.Name);
        if (wanted)
        {
            var g = _catalog.GenericFor(UeExtendedId);
            var file = g is null ? null : FileFor(g.Files, 64);
            if (file is null) return None(why + "The catalog has no Unreal Extended build right now.");
            if (entry?.Compat == "in-progress") notes.Add("Listed as in progress for Unreal Extended.");
            var ue = UeSettings.For(game.Tech, entry);
            return new HdrChoice
            {
                Kind = HdrKind.UeExtended, ModId = UeExtendedId, Title = g!.Name, File = file, EngineGame = entry, Ue = ue,
                Reason = why + (ueExtended == true ? "Unreal Extended, as you chose." : "Unreal game without its own mod: Unreal Extended is the default.") +
                         (entry is null ? "" : " The wiki lists this game."),
                Link = ModPage(UeExtendedId), Notes = notes, CanToggleUeExtended = true,
            };
        }
        var plain = _catalog.GenericFor(UnrealId);
        var plainFile = plain is null ? null : FileFor(plain.Files, 64);
        if (plainFile is null) return None(why + "The catalog has no generic Unreal build right now.");
        var listed = FindEngineGame("unreal", game.Name);
        if (listed?.Compat == "in-progress") notes.Add("Listed as in progress for the generic Unreal add-on.");
        return new HdrChoice
        {
            Kind = HdrKind.UnrealGeneric, ModId = UnrealId, Title = plain!.Name, File = plainFile, EngineGame = listed,
            Reason = why + (ueExtended == false ? "Plain generic Unreal add-on, as you chose." : "Plain generic Unreal add-on."),
            Link = ModPage(UnrealId), Notes = notes, CanToggleUeExtended = true,
        };
    }

    private HdrChoice Unity(GameInfo game, int bits, string why)
    {
        var g = _catalog.GenericFor(UnityId);
        var file = g is null ? null : FileFor(g.Files, bits);
        if (file is null)
            return None(why + (g is null ? "The catalog has no generic Unity add-on right now." : $"The catalog has no {bits}-bit generic Unity add-on."));
        var entry = FindEngineGame("unity", game.Name);
        var notes = new List<string>();
        if (entry?.Compat == "in-progress") notes.Add("Listed as in progress for the generic Unity add-on.");
        if (entry?.Notes is { Length: > 0 } n) notes.Add(n.Replace('\n', ' '));
        return new HdrChoice
        {
            Kind = HdrKind.UnityGeneric, ModId = UnityId, Title = g!.Name + $" ({bits}-bit)", File = file, EngineGame = entry,
            Reason = why + $"Unity game without its own mod: the generic {bits}-bit Unity add-on.",
            Link = ModPage(UnityId), Notes = notes,
        };
    }

    /// <summary>The catalog's reason when the game is blocked: the matched entry's flag, or a blocklist item naming the game or the entry.</summary>
    private string? IsBlocked(GameInfo game, CatalogMod? mod)
    {
        if (mod is { Blocked: true }) return Blocked(mod.Name, mod.BlockReason);
        var key = GameMatcher.Normalize(game.Name);
        foreach (var b in _catalog.Overrides.Blocklist)
        {
            var byId = mod is not null && b.Id is not null && b.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase);
            var byName = b.Name is not null && GameMatcher.Normalize(b.Name) is { Length: > 0 } n && (n == key || (mod is not null && n == GameMatcher.Normalize(mod.Name)));
            if (byId || byName) return Blocked(game.Name, b.Reason);
        }
        return null;
    }

    private static string Blocked(string name, string? reason) =>
        $"{name} is blocked in the catalog" + (string.IsNullOrWhiteSpace(reason) ? "." : $": {reason.Trim().TrimEnd('.')}.");

    private CatalogEngineGame? FindEngineGame(string engine, string gameName)
    {
        var key = GameMatcher.Normalize(gameName);
        if (key.Length == 0) return null;
        var rows = _engineGames.Where(g => g.Game.Engine == engine).ToList();
        if (rows.FirstOrDefault(g => g.Key == key).Game is { } exact) return exact;
        // "Hades II" is not "Hades": an entry that is the name plus only a sequel number doesn't match.
        var contained = rows.Where(g => g.Key.Length >= 6 && key.Contains(g.Key, StringComparison.Ordinal)
                                        && !(key.StartsWith(g.Key, StringComparison.Ordinal) && GameMatcher.IsSequelSuffix(key[g.Key.Length..])))
            .OrderByDescending(g => g.Key.Length).FirstOrDefault();
        return contained.Game;
    }

    /// <summary>
    /// The x64 file for a 64-bit game; the x86 file for a 32-bit one. With unknown bitness (0) x64 first, else x86. Only files
    /// with a hash count.
    /// </summary>
    private static CatalogBinary? FileFor(IEnumerable<CatalogBinary> files, int bits)
    {
        var usable = files.Where(f => f.IsInstallable).ToList();
        CatalogBinary? Of(string arch) => usable.FirstOrDefault(f => f.Arch.Equals(arch, StringComparison.OrdinalIgnoreCase));
        return bits switch { 32 => Of("x86"), 64 => Of("x64"), _ => Of("x64") ?? Of("x86") };
    }

    private static HdrChoice None(string reason, CatalogMod? mod = null) =>
        new() { Kind = HdrKind.None, Reason = reason, Mod = mod, Title = mod?.Name ?? "", Link = mod?.Links?.Discussion ?? mod?.Links?.Nexus ?? mod?.Links?.Discord };

    /// <summary>The author's repository for each generic add-on (the catalog README lists the same sources).</summary>
    private static string? ModPage(string id) => id switch
    {
        UnrealId => "https://github.com/clshortfuse/renodx",
        UnityId => "https://github.com/NotVoosh/renodx-unity",
        UeExtendedId => "https://github.com/marat569/renodx",
        _ => null,
    };
}
