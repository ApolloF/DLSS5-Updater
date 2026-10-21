using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

public enum MatchKind
{
    None,
    /// <summary>Hand-maintained override of the catalog (alias or AppID fix).</summary>
    Override,
    AppId,
    Exact,
    /// <summary>The game's name contains the entry's name ("Code Vein GOTY" for "Code Vein"); the longest entry name wins.</summary>
    Contains,
    /// <summary>The entry's name starts with the game's name ("Alien" for "Alien Isolation"), unless the rest is only a sequel number.</summary>
    StartsWith,
}

/// <param name="Mod">The matched entry. It can be <see cref="CatalogMod.Blocked"/>: callers check that and say why.</param>
public sealed record CatalogMatch(CatalogMod? Mod, MatchKind Kind)
{
    public static readonly CatalogMatch None = new(null, MatchKind.None);
}

/// <summary>
/// Matches a game to the catalog's RenoDX entries: overrides first, then Steam AppID, then the normalised name (exact,
/// "game contains entry", "entry starts with game" with a sequel guard). Only main and engine entries take part; related
/// and deprecated ones are for information.
/// </summary>
public sealed partial class GameMatcher
{
    /// <summary>Entry names shorter than this are only matched exactly: "Portal" would otherwise claim "Portal Knights".</summary>
    private const int MinContainedLength = 4;

    /// <summary>Game names shorter than this never match by prefix.</summary>
    private const int MinPrefixLength = 4;

    private readonly List<(string Key, CatalogMod Mod)> _names = [];
    private readonly Dictionary<int, List<CatalogMod>> _byAppId = [];
    private readonly Dictionary<string, CatalogMod> _aliasOverrides = [];
    private readonly Dictionary<int, CatalogMod> _appIdOverrides = [];

    public GameMatcher(CatalogRenodx catalog)
    {
        var byId = new Dictionary<string, CatalogMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in catalog.Mods.Where(Eligible))
        {
            byId.TryAdd(mod.Id, mod);
            foreach (var n in mod.Aliases.Prepend(mod.Name))
                if (Normalize(n) is { Length: > 0 } key) _names.Add((key, mod));
            foreach (var id in mod.SteamAppIds)
            {
                if (!_byAppId.TryGetValue(id, out var list)) _byAppId[id] = list = [];
                list.Add(mod);
            }
        }
        foreach (var fix in catalog.Overrides.NameFixes)
            if (byId.TryGetValue(fix.Id, out var mod))
                foreach (var n in fix.Aliases.Concat(fix.Name is null ? [] : [fix.Name]))
                    if (Normalize(n) is { Length: > 0 } key) _aliasOverrides[key] = mod;
        foreach (var fix in catalog.Overrides.AppIdFixes)
            if (byId.TryGetValue(fix.Id, out var mod))
                foreach (var id in fix.SteamAppIds) _appIdOverrides[id] = mod;
    }

    public CatalogMatch Match(GameInfo game) => Match(game.SteamAppId, game.Name);

    public CatalogMatch Match(int? steamAppId, string gameName)
    {
        var key = Normalize(gameName);

        if (key.Length > 0 && _aliasOverrides.TryGetValue(key, out var aliased)) return new CatalogMatch(aliased, MatchKind.Override);
        if (steamAppId is { } fixedId && _appIdOverrides.TryGetValue(fixedId, out var fixedMod)) return new CatalogMatch(fixedMod, MatchKind.Override);

        if (steamAppId is { } id && _byAppId.TryGetValue(id, out var byApp))
            return new CatalogMatch(Best(byApp), MatchKind.AppId);
        if (key.Length == 0) return CatalogMatch.None;

        var exact = _names.Where(n => n.Key == key).Select(n => n.Mod).ToList();
        if (exact.Count > 0) return new CatalogMatch(Best(exact), MatchKind.Exact);

        // "Hades II" is not "Hades": a game that is an entry plus only a sequel number is skipped here as well.
        var contained = _names
            .Where(n => n.Key.Length >= MinContainedLength && key.Contains(n.Key, StringComparison.Ordinal)
                        && !(key.StartsWith(n.Key, StringComparison.Ordinal) && IsSequelSuffix(key[n.Key.Length..])))
            .ToList();
        if (contained.Count > 0)
        {
            var longest = contained.Max(n => n.Key.Length);
            return new CatalogMatch(Best(contained.Where(n => n.Key.Length == longest).Select(n => n.Mod).ToList()), MatchKind.Contains);
        }

        if (key.Length >= MinPrefixLength)
        {
            var starting = _names
                .Where(n => n.Key.Length > key.Length && n.Key.StartsWith(key, StringComparison.Ordinal) && !IsSequelSuffix(n.Key[key.Length..]))
                .ToList();
            if (starting.Count > 0)
            {
                var shortest = starting.Min(n => n.Key.Length);
                return new CatalogMatch(Best(starting.Where(n => n.Key.Length == shortest).Select(n => n.Mod).ToList()), MatchKind.StartsWith);
            }
        }
        return CatalogMatch.None;
    }

    private static bool Eligible(CatalogMod mod) => mod.Listing is "main" or "engine";

    /// <summary>Several entries for one game (index and wiki variants): the one with files in the main list first, then by id for a stable pick.</summary>
    private static CatalogMod Best(List<CatalogMod> candidates) => candidates
        .OrderBy(m => m.Blocked ? 1 : 0)
        .ThenBy(m => m.Files.Count > 0 || m.Generic is not null ? 0 : 1)
        .ThenBy(m => m.Listing == "main" ? 0 : 1)
        .ThenBy(m => m.Id, StringComparer.Ordinal)
        .First();

    /// <summary>"Hades" + "ii", "Doom" + "2": a sequel, not the same game. Digits, or a roman numeral up to 39.</summary>
    internal static bool IsSequelSuffix(string suffix) =>
        suffix.Length > 0 && (suffix.All(char.IsAsciiDigit) || RomanNumeral().IsMatch(suffix));

    /// <summary>Lowercase letters and digits only; diacritics and trademark, registered and copyright signs are dropped.</summary>
    public static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        // The signs go first: their compatibility decomposition would otherwise leave "TM" in the name.
        var cleaned = name.Replace("™", "").Replace("®", "").Replace("©", "").Replace("℠", "");
        foreach (var c in cleaned.Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    [GeneratedRegex("^(?=[ivx])x{0,3}(ix|iv|v?i{0,3})$")]
    private static partial Regex RomanNumeral();
}
