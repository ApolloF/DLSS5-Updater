namespace DLSSUpdater.Core;

/// <summary>
/// The routes that run NR (the "neural consumers"). The first three are the add-ons of ReShade + add-ons mode; a game normally
/// gets one of them. <see cref="Bridge"/> (the DX11 bridge, placed by other tools) and <see cref="OptiScaler"/> (OptiScaler's own
/// NR) are never Settings defaults, but they hook the same NGX calls, so they count for the one-consumer rule.
/// </summary>
public enum NrAddon { RenoDx, Lecram, Uplift, Bridge, OptiScaler }

/// <summary>
/// Which NR routes clash (docs/addons). No pair is blocked: a clash is a reason shown with "Install anyway", so every add-on can
/// be tried next to the others for comparison. Defaults in Settings and the method picks never choose a clashing set.
/// </summary>
public static class NrAddons
{
    /// <summary>The route the mod picker treats as the one in use when several are on.</summary>
    public static readonly NrAddon[] Priority = [NrAddon.Uplift, NrAddon.Lecram, NrAddon.RenoDx];

    public static string Name(NrAddon a) => a switch
    {
        NrAddon.RenoDx => "the ShortFuse RenoDX DLSS add-on",
        NrAddon.Lecram => "Lecram's DLSS5 add-on",
        NrAddon.Uplift => "GITC Uplift",
        NrAddon.Bridge => "the DLSS5 DX11 bridge",
        _ => "OptiScaler",
    };

    /// <summary>
    /// Why <paramref name="adding"/> clashes with the routes already in the game (<paramref name="present"/>), as one line; null
    /// when it doesn't. One neural consumer per game is the rule (docs/advisor.md, decision table row 18):
    /// they all detour the game's NGX calls.
    /// </summary>
    public static string? Conflict(NrAddon adding, IEnumerable<NrAddon> present)
    {
        foreach (var other in present.Where(p => p != adding).Distinct())
        {
            if ((adding, other) is (NrAddon.RenoDx, NrAddon.Lecram) or (NrAddon.Lecram, NrAddon.RenoDx))
                return "ShortFuse's and Lecram's add-ons both on: NR runs twice, which costs more FPS, and not every pair of builds works together.";
            var why = adding == NrAddon.OptiScaler || other == NrAddon.OptiScaler ? "OptiScaler takes over the NGX calls the other route needs"
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
        if (record?.Lecram == true || Has([.. ComponentStore.LecramFiles, ComponentStore.Lecram32File])) found.Add(NrAddon.Lecram);
        if (record?.Uplift == true || Has([ComponentStore.UpliftFile, .. ComponentStore.LegacyUpliftFiles])) found.Add(NrAddon.Uplift);
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
            default: throw new ArgumentOutOfRangeException(nameof(addon), addon, "Only the NR add-ons have a default in Settings");
        }
    }

    /// <summary>The add-ons Settings installs by default.</summary>
    public static IEnumerable<NrAddon> Defaults(AppSettings s)
    {
        if (s.InstallRenoDx) yield return NrAddon.RenoDx;
        if (s.InstallLecram) yield return NrAddon.Lecram;
        if (s.InstallUplift) yield return NrAddon.Uplift;
    }
}
