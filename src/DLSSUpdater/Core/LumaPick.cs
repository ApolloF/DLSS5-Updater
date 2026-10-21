using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

public enum LumaKind { None, Named, Unreal, Custom }

/// <summary>
/// What the Luma component would install in one game, and why. <see cref="Kind"/> None means nothing is offered and
/// <see cref="Reason"/> says why in one line.
/// </summary>
public sealed record LumaChoice
{
    public LumaKind Kind { get; init; }
    /// <summary>The catalog mod id for a named mod, "unreal" for the generic Unreal mod, "custom" for an imported zip.</summary>
    public string? ModId { get; init; }
    public string Title { get; init; } = "";
    public string Reason { get; init; } = "";
    public CatalogBinary? File { get; init; }
    /// <summary>The package id in the catalog ("Unreal_Engine", "Prey"), the library group.</summary>
    public string? PackageId { get; init; }
    /// <summary>The release the zip belongs to ("latest-755"): the version shown and recorded.</summary>
    public string Version { get; init; } = "";
    /// <summary>The library build the game is pinned to (<see cref="LumaPin"/>); null follows the catalog's file above.</summary>
    public LumaBuild? Pinned { get; init; }
    public CatalogLumaMod? Mod { get; init; }
    public CatalogLumaUnrealGame? UnrealGame { get; init; }
    public string? Link { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
    /// <summary>Engine.ini keys the wiki lists for this Unreal game; written on install.</summary>
    public IReadOnlyList<CatalogEngineIniKey> EngineIni { get; init; } = [];
    /// <summary>Launch arguments the wiki lists (-dx11); the app can't set Steam launch options, so they are shown with a copy button.</summary>
    public IReadOnlyList<string> LaunchArgs { get; init; } = [];
    public bool NeedsDgVoodoo { get; init; }
    public IReadOnlyList<string> DgVoodooLines { get; init; } = [];

    public bool IsOffered => Kind != LumaKind.None && File is not null;

    /// <summary>Everything the files and ini edits depend on; an install is current while its stored signature matches.</summary>
    public string Signature(bool? hdr) =>
        string.Join('|', Kind, ModId, File?.Sha256, hdr is null ? "" : hdr.Value ? "hdr-on" : "hdr-off",
            string.Join(',', EngineIni.Select(k => $"{k.Section}/{k.Key}={k.Value}")));
}

public static class LumaManual
{
    /// <summary>The "mod" pick of a game that uses a Luma zip the user imported; the zip is the game's Luma build pick.</summary>
    public const string CustomMod = "custom";
    public const string ImportedPackageId = "custom";

    /// <summary>An imported zip, installed as it is: no ini keys of ours, pinned so the install uses the library copy and checks its hash.</summary>
    public static LumaChoice CustomChoice(LumaBuild build, int gameBits = 0) => new()
    {
        Notes = (gameBits == 64 && build.Arch == "x86") || (gameBits == 32 && build.Arch == "x64")
            ? [$"This is a {(build.Arch == "x86" ? "32" : "64")}-bit Luma add-on and the game is {gameBits}-bit; ReShade will not load it."]
            : [],
        Kind = LumaKind.Custom, ModId = CustomMod, Title = Path.GetFileNameWithoutExtension(build.FileName), PackageId = ImportedPackageId, Version = build.Version,
        File = new CatalogBinary { Name = build.FileName, Arch = build.Arch, Url = build.Url ?? "", Sha256 = build.Id, Version = build.Version, FirstSeen = build.FirstSeen },
        Pinned = build,
        Reason = $"{build.FileName} is a Luma zip you imported; it is installed as it is.",
    };

    public static LumaChoice MissingCustom() => new()
    {
        Kind = LumaKind.None,
        Reason = "The Luma zip you imported for this game is no longer in the library. Import it again or reset to auto.",
    };
}

/// <summary>
/// Which Luma mod a game gets: a mod whose name is the game's name (exact, after normalising; no substring matching), else for a game on
/// the wiki's generic Unreal table the generic Unreal mod. Pure: it reads the catalog and what the scan found. Luma is never installed
/// without the user pressing Install; Auto only says what would go in.
/// </summary>
public sealed class LumaPicker
{
    public const string UnrealId = "unreal";

    private readonly CatalogLuma _catalog;
    private readonly List<(string Key, CatalogLumaMod Mod)> _mods;
    private readonly List<(string Key, CatalogLumaUnrealGame Game)> _unreal;

    public LumaPicker(CatalogLuma catalog)
    {
        _catalog = catalog;
        _mods = catalog.Mods.Select(m => (GameMatcher.Normalize(m.Name), m)).Where(m => m.Item1.Length > 0).ToList();
        _unreal = catalog.UnrealGames.Select(g => (GameMatcher.Normalize(g.Name), g)).Where(g => g.Item1.Length > 0).ToList();
    }

    public CatalogLuma Catalog => _catalog;

    /// <param name="manualMod">A catalog mod id or <see cref="UnrealId"/> the user picked by hand; null = automatic.</param>
    public LumaChoice Pick(GameInfo game, string? manualMod = null)
    {
        if (game.AntiCheat is { } ac)
            return None($"{ac} anti-cheat: no add-on is installed in games with anti-cheat.");
        var bits = game.Effective.Bits == 32 ? 32 : 64;
        if (manualMod is not null) return ByHand(game, manualMod, bits);

        var key = GameMatcher.Normalize(game.Name);
        if (key.Length > 0 && _mods.FirstOrDefault(m => m.Key == key).Mod is { } mod)
            return Named(mod, "its name", bits);

        if (key.Length > 0 && _unreal.FirstOrDefault(g => g.Key == key).Game is { } row)
            return Unreal(game, bits, row, "The Luma wiki lists this game for the generic Unreal mod. ", hand: false);
        return None("No Luma mod is known for this game.");
    }

    /// <summary>The wiki's own mod for the game (offered or not), for the method advice and the notes.</summary>
    public CatalogLumaMod? ModOf(GameInfo game)
    {
        var key = GameMatcher.Normalize(game.Name);
        return key.Length > 0 ? _mods.FirstOrDefault(m => m.Key == key).Mod : null;
    }

    /// <summary>
    /// The mod the user picked by hand. Soft rules (not DirectX 11, Unreal 5, blocked on the wiki) become notes; bitness and a missing
    /// download are facts and still refuse.
    /// </summary>
    private LumaChoice ByHand(GameInfo game, string id, int bits)
    {
        if (id.Equals(UnrealId, StringComparison.OrdinalIgnoreCase))
        {
            var key = GameMatcher.Normalize(game.Name);
            var row = key.Length > 0 ? _unreal.FirstOrDefault(g => g.Key == key).Game : null;
            return Unreal(game, bits, row, "You picked it by hand. ", hand: true);
        }
        if (_catalog.Mods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } mod)
            return Named(mod, "your pick", bits);
        return None($"The Luma mod you picked ({id}) isn't in the catalog now. Reset to auto or pick another.");
    }

    /// <summary>The mods the user may pick by hand: every mod with a download, and the generic Unreal one.</summary>
    public IReadOnlyList<(string Id, string Name)> ManualChoices()
    {
        var list = _catalog.Mods.Where(m => _catalog.PackageOf(m.Package)?.Files.Any(f => f.IsInstallable) == true).Select(m => (m.Id, m.Name)).ToList();
        if (_catalog.PackageOf(_catalog.UnrealPackage)?.Files.Any(f => f.IsInstallable) == true) list.Add((UnrealId, "Luma for Unreal Engine (generic)"));
        return list.DistinctBy(c => c.Item1, StringComparer.OrdinalIgnoreCase).OrderBy(c => c.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private LumaChoice Named(CatalogLumaMod mod, string how, int bits)
    {
        var notes = new List<string>();
        if (mod.Status == "in-progress") notes.Add("The mod is marked in progress on the Luma wiki.");
        if (mod.Notes is { Length: > 0 } n) notes.Add(n.Replace('\n', ' '));
        var link = mod.Links?.Discussion ?? mod.Links?.Nexus ?? mod.Links?.Discord ?? mod.FeaturesUrl;
        var dg = DgVoodoo(mod);
        if (_catalog.PackageOf(mod.Package) is not { } package)
            return None($"{mod.Name} has a Luma mod, but it isn't a release zip" + (link is null ? "." : "; get it from the author's page."), mod, link);
        var file = package.FileFor(bits);
        if (file is null)
            return None($"{mod.Name}: the catalog has no {(bits == 32 ? "32-bit" : "64-bit")} Luma build for this game.", mod, link);
        return new LumaChoice
        {
            Kind = LumaKind.Named, ModId = mod.Id, Title = mod.Name, Mod = mod, File = file, PackageId = package.Id, Version = _catalog.Release,
            Reason = $"{mod.Name} has its own Luma mod (matched by {how}).",
            Link = link, Notes = notes, NeedsDgVoodoo = mod.RequiresDgVoodoo, DgVoodooLines = dg,
        };
    }

    private LumaChoice Unreal(GameInfo game, int bits, CatalogLumaUnrealGame? row, string why, bool hand)
    {
        var tech = game.Effective;
        if (bits == 32) return None(why + "The generic Unreal mod is 64-bit only; this game is 32-bit.");
        if ((!tech.IsUnreal || tech.Engine == GameEngine.UnrealLegacy) && !hand)
            return None(why + "This isn't an Unreal Engine 4 or 5 game.");
        var package = _catalog.PackageOf(_catalog.UnrealPackage);
        var file = package?.FileFor(64);
        if (file is null) return None(why + "The catalog has no generic Unreal Luma build right now.");

        var notes = new List<string>();
        var dx11 = tech.Apis.HasFlag(GraphicsApis.Dx11);
        if (!dx11)
        {
            if (!hand) return None(why + "Luma's Unreal mod needs DirectX 11 and this game isn't detected as DirectX 11.");
            notes.Add("Luma's Unreal mod needs DirectX 11 and this game isn't detected as DirectX 11.");
        }
        if (tech.UeMajor >= 5 && !(game.Manual?.Apis?.HasFlag(GraphicsApis.Dx11) ?? false))
        {
            if (!hand) return None(why + "This is an Unreal Engine 5 game; the generic mod targets Unreal 4 with DirectX 11 (set the game's API to DirectX 11 by hand to try it).");
            notes.Add("This is an Unreal Engine 5 game; the generic mod targets Unreal 4 with DirectX 11.");
        }
        if (row is { Blocked: true })
        {
            if (!hand) return None(why + "The wiki lists this game as blocked (no DLSS/FSR support and no HDR).");
            notes.Add("The wiki lists this game as blocked: the generic mod does nothing useful there.");
        }
        if (row is null) notes.Add("The game isn't on the wiki's generic Unreal table: untested.");
        if (row?.Notes is { Length: > 0 } n) notes.Add(n.Replace('\n', ' '));
        if (row?.UeVersion is { Length: > 0 } ue) notes.Add($"Unreal version on the wiki: {ue}.");
        var args = row?.LaunchArgs ?? [];
        if (args.Count == 0) args = ["-dx11"];
        return new LumaChoice
        {
            Kind = LumaKind.Unreal, ModId = UnrealId, Title = "Luma for Unreal Engine", File = file, PackageId = package!.Id, Version = _catalog.Release,
            UnrealGame = row, Reason = why + "Generic Luma for Unreal Engine (DirectX 11).", Link = "https://github.com/Filoppi/Luma-Framework",
            Notes = notes, EngineIni = row?.EngineIni ?? [], LaunchArgs = args,
        };
    }

    private static IReadOnlyList<string> DgVoodoo(CatalogLumaMod mod)
    {
        if (!mod.RequiresDgVoodoo) return [];
        var lines = new List<string>
        {
            "Needs dgVoodoo2" + (mod.DgVoodooVersions.Count > 0 ? $" ({string.Join(", ", mod.DgVoodooVersions)})" : "") +
            ". The app doesn't download it: get it from github.com/dege-diosg/dgVoodoo2/releases and set it up as the wiki says.",
        };
        lines.AddRange(mod.DgVoodooSetup);
        return lines;
    }

    private static LumaChoice None(string reason, CatalogLumaMod? mod = null, string? link = null) =>
        new() { Kind = LumaKind.None, Reason = reason, Mod = mod, Title = mod?.Name ?? "", Link = link };
}

/// <summary>
/// A game's own pick of a Luma build (design.md §2 "Per-game pick"): one of the library's builds of the package the game is picked for.
/// Without one the game follows the catalog's current file.
/// </summary>
public static class LumaPin
{
    public static IReadOnlyList<LumaBuild> Candidates(LumaChoice choice, IEnumerable<LumaBuild> library)
    {
        if (!choice.IsOffered || choice.PackageId is null) return [];
        return library
            .Where(b => b.PackageId.Equals(choice.PackageId, StringComparison.OrdinalIgnoreCase) && b.Arch.Equals(choice.File!.Arch, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(b => b.Date)
            .ToList();
    }

    /// <summary>The choice with the pinned build in place of the catalog's; a pick that doesn't fit is ignored, so the game follows the catalog again.</summary>
    public static LumaChoice Apply(LumaChoice choice, string? pin, IEnumerable<LumaBuild> library)
    {
        if (pin is null || Candidates(choice, library).FirstOrDefault(b => b.Id.Equals(pin, StringComparison.OrdinalIgnoreCase)) is not { } build
            || !File.Exists(ComponentStore.LumaPathOf(build)))
            return choice;
        var file = new CatalogBinary { Name = build.FileName, Arch = build.Arch, Url = build.Url ?? "", Sha256 = build.Id, Version = build.Version, FirstSeen = build.FirstSeen };
        return choice with { File = file, Pinned = build, Version = build.Version, Reason = choice.Reason + $" You pinned {build.Version}." };
    }
}
