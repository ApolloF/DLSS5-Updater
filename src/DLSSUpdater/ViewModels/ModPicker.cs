namespace DLSSUpdater.ViewModels;

/// <summary>A mod whose option cards the "MOD" dropdown can show on its own. OptiScalerNr covers the NR, tone mapping and HDR output cards.</summary>
public enum Mod { OptiScalerNr, ReShade, Mfg, RenoDx, Lecram, Dfc, Uplift, All }

public sealed record ModChoice(Mod Mod, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Which mod's options the config pages show: the dropdown entries, the preselection and what each choice shows.</summary>
public static class ModPicker
{
    public static readonly ModChoice AllMods = new(Mod.All, "All mods");

    private static readonly ModChoice[] Always =
    [
        new(Mod.OptiScalerNr, "OptiScaler-NR"),
        new(Mod.ReShade, "ReShade"),
        new(Mod.Mfg, "MFG Unlock"),
    ];

    private static readonly ModChoice[] AddOns =
    [
        new(Mod.RenoDx, "ShortFuse RenoDX"),
        new(Mod.Lecram, "Lecram DLSS5"),
        new(Mod.Dfc, "Deep Fried Chicken"),
        new(Mod.Uplift, "GITC Uplift"),
    ];

    /// <summary>The add-ons replace OptiScaler's NR pass, so their options exist in ReShade-only mode alone.</summary>
    public static IReadOnlyList<ModChoice> Available(bool reShadeOnly) =>
        [.. Always, .. (reShadeOnly ? AddOns : []), AllMods];

    /// <summary>Settings keeps OptiScaler-NR on its own tab, so the ReShade tab lists everything else.</summary>
    public static IReadOnlyList<ModChoice> AvailableInSettings { get; } = [Always[1], Always[2], .. AddOns, AllMods];

    public static ModChoice Choice(IReadOnlyList<ModChoice> list, Mod mod) => list.First(c => c.Mod == mod);

    /// <summary>
    /// The NR route in use: GITC Uplift, else Deep Fried Chicken, else Lecram (also when ShortFuse runs next to it), else ShortFuse.
    /// Without an add-on route it is ReShade in ReShade-only mode, else OptiScaler-NR.
    /// </summary>
    public static Mod DefaultFor(bool reShadeOnly, bool uplift, bool dfc, bool lecram, bool renoDx)
    {
        if (!reShadeOnly) return Mod.OptiScalerNr;
        if (uplift) return Mod.Uplift;
        if (dfc) return Mod.Dfc;
        if (lecram) return Mod.Lecram;
        if (renoDx) return Mod.RenoDx;
        return Mod.ReShade;
    }

    /// <summary>The same rule for the Settings ReShade tab, which has no OptiScaler-NR entry: that route lands on ReShade.</summary>
    public static Mod DefaultInSettings(bool reShadeOnly, bool uplift, bool dfc, bool lecram, bool renoDx) =>
        DefaultFor(reShadeOnly, uplift, dfc, lecram, renoDx) is var mod && mod == Mod.OptiScalerNr ? Mod.ReShade : mod;

    /// <summary>Whether the card of <paramref name="card"/> is shown while <paramref name="selected"/> is picked.</summary>
    public static bool Shows(Mod selected, Mod card) => selected is Mod.All || selected == card;
}
