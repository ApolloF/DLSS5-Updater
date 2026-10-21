namespace DLSSUpdater.Core;

/// <summary>
/// The routes that run NR (the "neural consumers"). The first four are the add-ons of ReShade + add-ons mode; a game normally
/// gets one of them. <see cref="Bridge"/> (the DX11 bridge, placed by other tools) and <see cref="OptiScaler"/> (OptiScaler's own
/// NR) are never Settings defaults, but they hook the same NGX calls, so they count for the one-consumer rule.
/// </summary>
public enum NrAddon { RenoDx, Lecram, Uplift, Dfc, Bridge, OptiScaler }

/// <summary>
/// Which NR add-ons may be on together (docs/addons). Only ShortFuse's and Lecram's can share a game: Lecram then
/// observes instead of standing down and NR runs twice. GITC Uplift refuses to run next to the RenoDX add-ons, and
/// Deep Fried Chicken replaces the others, so those two always stay on their own.
/// </summary>
public static class NrAddons
{
    /// <summary>Who stays when only one may: the route the mod picker treats as the one in use.</summary>
    public static readonly NrAddon[] Priority = [NrAddon.Uplift, NrAddon.Dfc, NrAddon.Lecram, NrAddon.RenoDx];

    public static bool CanRunTogether(NrAddon a, NrAddon b, bool allowSeveral) =>
        allowSeveral && (a, b) is (NrAddon.RenoDx, NrAddon.Lecram) or (NrAddon.Lecram, NrAddon.RenoDx);

    /// <summary>The enabled add-ons to switch off after the user switched <paramref name="turnedOn"/> on.</summary>
    public static IReadOnlyList<NrAddon> ToSwitchOff(NrAddon turnedOn, IEnumerable<NrAddon> enabled, bool allowSeveral) =>
        enabled.Where(a => a != turnedOn && !CanRunTogether(turnedOn, a, allowSeveral)).Distinct().ToList();

    /// <summary>The add-ons that survive the rule, kept by <see cref="Priority"/>.</summary>
    public static IReadOnlySet<NrAddon> Normalize(IEnumerable<NrAddon> enabled, bool allowSeveral)
    {
        var on = enabled.ToHashSet();
        var kept = new List<NrAddon>();
        foreach (var a in Priority)
            if (on.Contains(a) && kept.All(k => CanRunTogether(k, a, allowSeveral))) kept.Add(a);
        return kept.ToHashSet();
    }

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
    /// Why <paramref name="adding"/> may not join the routes already in the game (<paramref name="present"/>), as one line; null when
    /// it may. One neural consumer per game (docs/research/methods-2026-10.md section 2 and section 8 row 18): they all detour the
    /// game's NGX calls, and Deep Fried Chicken does nothing at all when another one is loaded beside it.
    /// </summary>
    public static string? Refusal(NrAddon adding, IEnumerable<NrAddon> present, bool allowSeveral)
    {
        foreach (var other in present.Where(p => p != adding).Distinct())
        {
            if (CanRunTogether(adding, other, allowSeveral)) continue;
            var why = adding == NrAddon.Dfc || other == NrAddon.Dfc ? "Deep Fried Chicken does nothing when another neural route is loaded beside it"
                : adding == NrAddon.OptiScaler || other == NrAddon.OptiScaler ? "OptiScaler takes over the NGX calls the other route needs"
                : "only one neural route can hook the game's DLSS calls";
            return $"{Capitalize(Name(adding))} and {Name(other)} can't be in the same game: {why}.";
        }
        return null;
    }

    /// <summary>
    /// What stops DLSS5-Feeder in a game with <paramref name="present"/> routes; null when it fits. Feeder only makes the DLSS
    /// calls a game without DLSS never makes, so it needs exactly one neural consumer to act on them, and the app only pairs it
    /// with Deep Fried Chicken (the consumer Feeder's README recommends, and the one the app can install in a game without DLSS).
    /// </summary>
    public static string? FeederRefusal(IEnumerable<NrAddon> present)
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

    /// <summary>File names of the DX11 bridge add-on (docs/research/methods-2026-10.md section 6.1).</summary>
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

    /// <summary>The one-line note shown while ShortFuse and Lecram are both on; null otherwise.</summary>
    public static string? Warning(bool renoDx, bool lecram) =>
        renoDx && lecram ? "ShortFuse and Lecram both on: NR runs twice, which costs more FPS, and not every pair of builds works together." : null;

    /// <summary>
    /// Settings saved before the rule may have several add-ons on. A ShortFuse + Lecram setup keeps working with
    /// <see cref="AppSettings.AllowSeveralNrAddons"/> turned on; with GITC Uplift among them Uplift stays, as installs already did.
    /// </summary>
    public static bool Migrate(AppSettings s)
    {
        var before = (s.InstallRenoDx, s.InstallLecram, s.InstallUplift, s.AllowSeveralNrAddons);
        if (s.InstallRenoDx && s.InstallLecram && !s.InstallUplift) s.AllowSeveralNrAddons = true;
        SetAllowSeveral(s, s.AllowSeveralNrAddons);
        return before != (s.InstallRenoDx, s.InstallLecram, s.InstallUplift, s.AllowSeveralNrAddons);
    }

    /// <summary>Switches a default add-on in Settings; switching one on switches off those it can't run with.</summary>
    public static void SetDefault(AppSettings s, NrAddon addon, bool on)
    {
        if (on)
            foreach (var other in ToSwitchOff(addon, Defaults(s), s.AllowSeveralNrAddons)) Apply(s, other, false);
        Apply(s, addon, on);
    }

    /// <summary>Turning the override off leaves one default add-on, kept by <see cref="Priority"/>.</summary>
    public static void SetAllowSeveral(AppSettings s, bool allow)
    {
        s.AllowSeveralNrAddons = allow;
        var kept = Normalize(Defaults(s), allow);
        foreach (var a in Defaults(s).ToList()) Apply(s, a, kept.Contains(a));
    }

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
