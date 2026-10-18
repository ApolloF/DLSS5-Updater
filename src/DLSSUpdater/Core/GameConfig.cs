namespace DLSSUpdater.Core;

/// <summary>
/// A game's config on top of the global one: base = its preset or the current settings, plus the game's own
/// layer of changes, ini mode and game-only presets. Plain functions so they can be tested without WPF.
/// </summary>
public static class GameConfig
{
    public static readonly string[] IniModes = ["keep", "apply", "fresh"];

    public static IReadOnlyList<IniOverride> BaseOpti(AppSettings s, string gameId) => s.PresetFor(gameId)?.Opti ?? s.IniOverrides;
    public static IReadOnlyList<IniOverride> BaseReShade(AppSettings s, string gameId) => s.PresetFor(gameId)?.ReShade ?? s.ReShadeOverrides;

    public static IReadOnlyList<IniOverride> EffectiveOpti(AppSettings s, string gameId) =>
        ConfigProfile.Layer(BaseOpti(s, gameId), s.Games.GetValueOrDefault(gameId)?.Opti);

    public static IReadOnlyList<IniOverride> EffectiveReShade(AppSettings s, string gameId) =>
        ConfigProfile.Layer(BaseReShade(s, gameId), s.Games.GetValueOrDefault(gameId)?.ReShade);

    /// <summary>The game's ini mode, or the global one when it has none (or an unknown one).</summary>
    public static string EffectiveIniMode(AppSettings s, string gameId) =>
        s.Games.GetValueOrDefault(gameId)?.IniMode is { } m && IniModes.Contains(m) ? m : s.IniMode;

    public static IReadOnlyList<ConfigPreset> LocalPresets(AppSettings s, string gameId) =>
        (IReadOnlyList<ConfigPreset>?)s.Games.GetValueOrDefault(gameId)?.Presets ?? [];

    // ---------- layer entries ----------

    private static List<IniOverride>? LayerOf(GameOverride? g, bool reShade) => g is null ? null : reShade ? g.ReShade : g.Opti;

    public static IniOverride? LayerEntry(GameOverride? g, bool reShade, string section, string key) =>
        LayerOf(g, reShade)?.FirstOrDefault(o => Is(o, section, key));

    public static IniOverride? BaseEntry(IEnumerable<IniOverride> baseList, string section, string key) =>
        baseList.FirstOrDefault(o => Is(o, section, key));

    private static bool Is(IniOverride o, string section, string key) =>
        o.Section.Trim().Equals(section, StringComparison.OrdinalIgnoreCase) && o.Key.Trim().Equals(key, StringComparison.OrdinalIgnoreCase);

    /// <summary>Sets one key in the game's layer: a value overrides the base; null means "not set", which only needs an Unset entry when the base sets the key.</summary>
    public static void SetLayer(GameOverride g, bool reShade, string section, string key, string? value, bool baseSetsKey)
    {
        ClearLayer(g, reShade, section, key);
        if (value is null && !baseSetsKey) return;
        var entry = value is null ? new IniOverride(section, key, "") { Unset = true } : new IniOverride(section, key, value);
        if (reShade) (g.ReShade ??= []).Add(entry);
        else (g.Opti ??= []).Add(entry);
    }

    /// <summary>Back to inheriting the key; an emptied layer becomes null so it isn't saved.</summary>
    public static void ClearLayer(GameOverride g, bool reShade, string section, string key)
    {
        var list = LayerOf(g, reShade);
        if (list is null) return;
        list.RemoveAll(o => Is(o, section, key));
        if (list.Count > 0) return;
        if (reShade) g.ReShade = null;
        else g.Opti = null;
    }

    public static void ResetLayer(GameOverride g) => (g.Opti, g.ReShade) = (null, null);

    // ---------- presets ----------

    /// <summary>Game-only presets can't share a name with a global or built-in preset.</summary>
    public static string? LocalNameError(AppSettings s, string name) =>
        s.AllPresets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ? $"'{name}' is already a global or built-in preset. Pick another name."
            : null;

    /// <summary>Global presets can't share a name with a built-in preset or a game-only preset (that game would shadow it).</summary>
    public static string? GlobalNameError(AppSettings s, string name, string? exceptGameId = null)
    {
        if (s.AllPresets.Any(p => p.BuiltIn && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return $"'{name}' is built in and can't be replaced. Pick another name.";
        return s.Games.Any(kv => !kv.Key.Equals(exceptGameId, StringComparison.OrdinalIgnoreCase)
                                 && kv.Value.Presets?.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == true)
            ? $"'{name}' is already a preset of a single game. Pick another name."
            : null;
    }

    private static List<IniOverride> Copy(IEnumerable<IniOverride> list) =>
        list.Where(o => !string.IsNullOrWhiteSpace(o.Section) && !string.IsNullOrWhiteSpace(o.Key))
            .Select(o => new IniOverride(o.Section.Trim(), o.Key.Trim(), o.Value.Trim())).ToList();

    /// <summary>
    /// Saves the game's effective config (base + layer) as a preset, for this game only or for all games, replacing
    /// a preset of the same name and scope. Then it becomes the game's base and the layer is cleared, which leaves
    /// the effective config as it was. Returns an error message, or null.
    /// </summary>
    public static string? SavePreset(AppSettings s, string gameId, string name, bool gameOnly)
    {
        name = name.Trim();
        if (name.Length == 0) return "Enter a name.";
        var error = gameOnly ? LocalNameError(s, name) : GlobalNameError(s, name, gameId);
        if (error is not null) return error;
        var preset = new ConfigPreset { Name = name, Opti = Copy(EffectiveOpti(s, gameId)), ReShade = Copy(EffectiveReShade(s, gameId)) };
        var g = s.For(gameId);
        if (gameOnly)
        {
            g.Presets ??= [];
            g.Presets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            g.Presets.Add(preset);
        }
        else
        {
            s.Presets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            s.Presets.Add(preset);
            g.Presets?.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (g.Presets is { Count: 0 }) g.Presets = null;
        }
        g.Preset = name;
        ResetLayer(g);
        return null;
    }

    /// <summary>Deletes a game-only preset; the game falls back to the current settings if it used it.</summary>
    public static bool DeleteLocalPreset(AppSettings s, string gameId, string name)
    {
        if (s.Games.GetValueOrDefault(gameId) is not { Presets: { } list } g) return false;
        if (list.RemoveAll(p => p.Name == name) == 0) return false;
        if (list.Count == 0) g.Presets = null;
        if (g.Preset == name) g.Preset = null;
        return true;
    }

    /// <summary>Moves a game-only preset to the global presets. Returns an error message when the name is taken, or null.</summary>
    public static string? MakeGlobal(AppSettings s, string gameId, string name)
    {
        if (s.Games.GetValueOrDefault(gameId) is not { Presets: { } list } g || list.FirstOrDefault(p => p.Name == name) is not { } preset)
            return "That isn't a preset of this game.";
        var error = GlobalNameError(s, name, gameId)
                    ?? (s.Presets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ? $"'{name}' is already a global preset." : null);
        if (error is not null) return error;
        list.Remove(preset);
        if (list.Count == 0) g.Presets = null;
        s.Presets.Add(preset);
        return null;
    }
}
