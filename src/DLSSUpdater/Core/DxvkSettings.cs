namespace DLSSUpdater.Core;

/// <summary>A game's own DXVK choices, kept as options of its <c>dxvk</c> component pick (the build pick is the pick's Build). Null/empty = the default.</summary>
public static class DxvkSettings
{
    /// <summary>The Lilium preset: a tier id, <see cref="DxvkConf.TierOff"/>, or null for the safest tier.</summary>
    public static string? Tier(ComponentPick? pick) => Get(pick, DxvkConf.TierOption);

    /// <summary>Whether the NVIDIA present method is set with a Lilium build; on unless the game says off.</summary>
    public static bool PresentMethod(ComponentPick? pick) => Get(pick, DxvkConf.PresentOption) != "off";

    /// <summary>The user's own dxvk.conf lines for this game.</summary>
    public static string UserLines(ComponentPick? pick) => Get(pick, DxvkConf.UserLinesOption) ?? "";

    public static void SetTier(GameOverride g, string? tier) => Set(g, DxvkConf.TierOption, tier);
    public static void SetPresentMethod(GameOverride g, bool on) => Set(g, DxvkConf.PresentOption, on ? null : "off");
    public static void SetUserLines(GameOverride g, string? text) => Set(g, DxvkConf.UserLinesOption, string.IsNullOrWhiteSpace(text) ? null : text.Trim());

    private static string? Get(ComponentPick? pick, string key) => pick?.Options is { } o && o.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    private static void Set(GameOverride g, string key, string? value) => g.Components = ComponentPicks.SetOption(g.Components, ComponentRegistry.Dxvk, key, value);
}
