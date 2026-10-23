namespace DLSSUpdater.Core;

/// <summary>
/// Which ReShade add-ons, shader packs and preset are wanted, app-wide and per game, kept in the generic component picks
/// (<see cref="ComponentPick.Options"/>): ids as a comma list. A key a game doesn't have follows the app-wide value; an empty
/// string means none. Shader packs the app-wide setting never touched follow the catalog's defaults (the HDR essentials).
/// </summary>
public static class ExtrasPicks
{
    public const string AddonsKey = "ids";
    public const string PacksKey = "packs";
    public const string PresetKey = "preset";
    public const string BitsKey = "bits";

    private static string? Raw(ComponentPick? pick, string key) =>
        pick?.Options is { } o && o.TryGetValue(key, out var v) ? v : null;

    private static HashSet<string> Split(string? raw) =>
        new((raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

    private static string Join(IEnumerable<string> ids) => string.Join(",", ids.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));

    /// <summary>The add-ons wanted in a game: its own list when it has one, else the app-wide list.</summary>
    public static HashSet<string> AddonIds(AppSettings settings, GameOverride? game) =>
        Split(Raw(game?.PickOf(ComponentRegistry.ReShadeAddons), AddonsKey) ?? Raw(settings.PickOf(ComponentRegistry.ReShadeAddons), AddonsKey));

    public static bool GameHasAddons(GameOverride? game) => Raw(game?.PickOf(ComponentRegistry.ReShadeAddons), AddonsKey) is not null;

    /// <summary>The shader packs wanted in a game: its own list, else the app-wide list, else the catalog's defaults.</summary>
    public static HashSet<string> PackIds(AppSettings settings, GameOverride? game, CatalogShaderPacks? catalog)
    {
        var raw = Raw(game?.PickOf(ComponentRegistry.ReShadeShaders), PacksKey) ?? Raw(settings.PickOf(ComponentRegistry.ReShadeShaders), PacksKey);
        return raw is not null ? Split(raw) : new HashSet<string>((catalog?.Packs ?? []).Where(p => p.Default).Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
    }

    public static bool GameHasPacks(GameOverride? game) => Raw(game?.PickOf(ComponentRegistry.ReShadeShaders), PacksKey) is not null;

    /// <summary>The preset name chosen for a game; null = none.</summary>
    public static string? PresetOf(GameOverride? game) => Raw(game?.PickOf(ComponentRegistry.ReShadePreset), PresetKey) is { Length: > 0 } p ? p : null;

    /// <summary>The bitness a game's add-ons are chosen for when the user overrides the one detected from the exe (32 or 64); null = automatic.</summary>
    public static int? BitsOf(GameOverride? game) => Raw(game?.PickOf(ComponentRegistry.ReShadeAddons), BitsKey) switch { "32" => 32, "64" => 64, _ => null };

    private static Dictionary<string, string>? With(ComponentPick? pick, string key, string? value)
    {
        var options = pick?.Options is { } o ? new Dictionary<string, string>(o, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (value is null) options.Remove(key); else options[key] = value;
        return options.Count == 0 ? null : options;
    }

    public static void SetAddons(AppSettings settings, IEnumerable<string> ids)
    {
        var joined = Join(ids);
        settings.Components = ComponentPicks.SetOptions(settings.Components, ComponentRegistry.ReShadeAddons,
            With(settings.PickOf(ComponentRegistry.ReShadeAddons), AddonsKey, joined.Length == 0 ? null : joined));
    }

    /// <summary>Gives a game its own add-on list; null makes it follow the app-wide list again.</summary>
    public static void SetGameAddons(GameOverride game, IEnumerable<string>? ids) =>
        game.SetPickOptions(ComponentRegistry.ReShadeAddons, With(game.PickOf(ComponentRegistry.ReShadeAddons), AddonsKey, ids is null ? null : Join(ids)));

    public static void SetGameBits(GameOverride game, int? bits) =>
        game.SetPickOptions(ComponentRegistry.ReShadeAddons, With(game.PickOf(ComponentRegistry.ReShadeAddons), BitsKey, bits?.ToString()));

    public static void SetPacks(AppSettings settings, IEnumerable<string> ids) =>
        settings.Components = ComponentPicks.SetOptions(settings.Components, ComponentRegistry.ReShadeShaders,
            With(settings.PickOf(ComponentRegistry.ReShadeShaders), PacksKey, Join(ids)));

    public static void SetGamePacks(GameOverride game, IEnumerable<string>? ids) =>
        game.SetPickOptions(ComponentRegistry.ReShadeShaders, With(game.PickOf(ComponentRegistry.ReShadeShaders), PacksKey, ids is null ? null : Join(ids)));

    public static void SetGamePreset(GameOverride game, string? preset) =>
        game.SetPickOptions(ComponentRegistry.ReShadePreset, With(game.PickOf(ComponentRegistry.ReShadePreset), PresetKey, string.IsNullOrWhiteSpace(preset) ? null : preset));
}
