namespace DLSSUpdater.Core;

/// <summary>
/// The routes that run NR (the "neural consumers"). The first four are the add-ons of ReShade + add-ons mode; a game normally
/// gets one of them. <see cref="Bridge"/> (the DX11 bridge, placed by other tools) and <see cref="OptiScaler"/> (OptiScaler's own
/// NR) are never Settings defaults, but they hook the same NGX calls, so they count for the one-consumer rule.
/// </summary>
public enum NrAddon { RenoDx, Lecram, Uplift, Dfc, Bridge, OptiScaler }

/// <summary>
/// Which NR routes clash (docs/addons). No pair is blocked: a clash is a reason shown with "Install anyway", so every add-on can
/// be tried next to the others for comparison. Defaults in Settings and the method picks never choose a clashing set.
/// </summary>
public static class NrAddons
{
    /// <summary>The route the mod picker treats as the one in use when several are on.</summary>
    public static readonly NrAddon[] Priority = [NrAddon.Uplift, NrAddon.Dfc, NrAddon.Lecram, NrAddon.RenoDx];

    public static string Name(NrAddon a) => a switch
    {
        NrAddon.RenoDx => "the ShortFuse RenoDX DLSS add-on",
        NrAddon.Lecram => "Lecram's DLSS5 add-on",
        NrAddon.Uplift => "GITC Uplift",
        NrAddon.Dfc => "Deep Fried Chicken",
        NrAddon.Bridge => "the DLSS5 DX11 bridge",
        _ => "OptiScaler",
    };

    /// <summary>
    /// Why <paramref name="adding"/> clashes with the routes already in the game (<paramref name="present"/>), as one line; null
    /// when it doesn't. One neural consumer per game is the rule (docs/advisor.md, decision table row 18):
    /// they all detour the game's NGX calls, and Deep Fried Chicken does nothing at all when another one is loaded beside it.
    /// </summary>
    public static string? Conflict(NrAddon adding, IEnumerable<NrAddon> present)
    {
        foreach (var other in present.Where(p => p != adding).Distinct())
        {
            if ((adding, other) is (NrAddon.RenoDx, NrAddon.Lecram) or (NrAddon.Lecram, NrAddon.RenoDx))
                return "ShortFuse's and Lecram's add-ons both on: NR runs twice, which costs more FPS, and not every pair of builds works together.";
            var why = adding == NrAddon.Dfc || other == NrAddon.Dfc ? "Deep Fried Chicken does nothing when another neural route is loaded beside it"
                : adding == NrAddon.OptiScaler || other == NrAddon.OptiScaler ? "OptiScaler takes over the NGX calls the other route needs"
                : adding == NrAddon.Bridge || other == NrAddon.Bridge ? "the bridge is for games with their own DLSS and is never combined with another neural route"
                : "only one neural route can hook the game's DLSS calls";
            return $"{Capitalize(Name(adding))} and {Name(other)} don't work well in the same game: {why}.";
        }
        return null;
    }

    /// <summary>Every clash among <paramref name="routes"/>, one line per pair.</summary>
    public static IReadOnlyList<string> Conflicts(IEnumerable<NrAddon> routes)
    {
        var list = routes.Distinct().ToList();
        var found = new List<string>();
        for (var i = 0; i < list.Count; i++)
            if (Conflict(list[i], list.Skip(i + 1)) is { } why && !found.Contains(why)) found.Add(why);
        return found;
    }

    /// <summary>
    /// What clashes with DLSS5-Feeder in a game with <paramref name="present"/> routes; null when it fits. Feeder only makes the DLSS
    /// calls a game without DLSS never makes, so it needs exactly one neural consumer to act on them, and the app only pairs it
    /// with Deep Fried Chicken (the consumer Feeder's README recommends, and the one the app can install in a game without DLSS).
    /// </summary>
    public static string? FeederConflict(IEnumerable<NrAddon> present)
    {
        var routes = present.Distinct().ToList();
        if (routes.Where(r => r != NrAddon.Dfc).Select(r => (NrAddon?)r).FirstOrDefault() is { } other)
            return other switch
            {
                NrAddon.OptiScaler => "DLSS5-Feeder can't run next to stock OptiScaler: it takes over the NGX calls Feeder makes.",
                NrAddon.Bridge => "DLSS5-Feeder can't run next to the DLSS5 DX11 bridge: the bridge is for games with their own DLSS, never both.",
                NrAddon.RenoDx => "DLSS5-Feeder can't run with ShortFuse's RenoDX DLSS add-on: it replaces Feeder instead of working with it.",
                _ => $"DLSS5-Feeder can't run with {Name(other)}: this app only pairs Feeder with Deep Fried Chicken, and a game gets one neural consumer.",
            };
        return routes.Contains(NrAddon.Dfc) ? null : "DLSS5-Feeder needs one neural consumer in the game: turn on Deep Fried Chicken.";
    }

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>File names of the DX11 bridge add-on.</summary>
    public static readonly string[] BridgeFiles = ["dlss5-bridge.addon64", "dlss5-dx11-bridge.addon64"];

    /// <summary>
    /// The neural routes in the folder <paramref name="dir"/>: ours by the install record, anyone's by their files. The record
    /// matters because OptiScaler's NR needs no add-on file.
    /// </summary>
    public static IReadOnlyList<NrAddon> PresentIn(string dir, InstallManifest? record)
    {
        bool Has(IEnumerable<string> names) => names.Any(n => File.Exists(Path.Combine(dir, n)));
        var found = new List<NrAddon>();
        if (record?.RenoDx == true || Has([ComponentStore.RenoDxFile, .. ComponentStore.LegacyRenoDxFiles])) found.Add(NrAddon.RenoDx);
        if (record?.Lecram == true || Has(ComponentStore.LecramFiles)) found.Add(NrAddon.Lecram);
        if (record?.Uplift == true || Has([ComponentStore.UpliftFile, .. ComponentStore.LegacyUpliftFiles])) found.Add(NrAddon.Uplift);
        if (record?.Dfc == true || Has([ComponentStore.DfcFile])) found.Add(NrAddon.Dfc);
        if (Has(BridgeFiles)) found.Add(NrAddon.Bridge);
        if (record?.Opti == true || File.Exists(Path.Combine(dir, "OptiScaler.ini"))) found.Add(NrAddon.OptiScaler);
        return found;
    }

    /// <summary>Switches a default add-on in Settings. Other defaults stay as they are; a clash is a warning the user confirms.</summary>
    public static void SetDefault(AppSettings s, NrAddon addon, bool on) => Apply(s, addon, on);

    /// <summary>What clashes among the defaults in Settings, one line per pair; empty when they fit.</summary>
    public static IReadOnlyList<string> DefaultConflicts(AppSettings s) => Conflicts(Defaults(s));

    private static void Apply(AppSettings s, NrAddon addon, bool on)
    {
        switch (addon)
        {
            case NrAddon.RenoDx: s.InstallRenoDx = on; break;
            case NrAddon.Lecram: s.InstallLecram = on; break;
            case NrAddon.Uplift: s.InstallUplift = on; break;
            default: throw new ArgumentOutOfRangeException(nameof(addon), addon, "Deep Fried Chicken has no default in Settings");
        }
    }

    /// <summary>The add-ons Settings installs by default (Deep Fried Chicken is chosen per game only).</summary>
    public static IEnumerable<NrAddon> Defaults(AppSettings s)
    {
        if (s.InstallRenoDx) yield return NrAddon.RenoDx;
        if (s.InstallLecram) yield return NrAddon.Lecram;
        if (s.InstallUplift) yield return NrAddon.Uplift;
    }
}
