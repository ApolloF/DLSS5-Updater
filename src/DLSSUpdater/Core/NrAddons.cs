namespace DLSSUpdater.Core;

/// <summary>The NR add-ons of ReShade + add-ons mode. Each one runs NR itself, so a game normally gets one of them.</summary>
public enum NrAddon { RenoDx, Lecram, Uplift, Dfc }

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
