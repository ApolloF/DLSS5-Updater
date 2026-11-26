namespace DLSSUpdater.Core;

/// <summary>The rows of the game page's component table, in table order. The method cards name components with these same names.</summary>
public static class ComponentTable
{
    public sealed record Row(string Key, string Id, string Name);

    public static readonly IReadOnlyList<Row> Rows =
    [
        new("opti", ComponentRegistry.OptiScaler, "OptiScaler F5"),
        new("reshade", ComponentRegistry.ReShade, "ReShade"),
        new("mfg", ComponentRegistry.MfgUnlock, "MFG Unlock"),
        new("dlssnr", ComponentRegistry.DlssNr, "NR runtime"),
        new("renodx", ComponentRegistry.RenoDx, "ShortFuse RenoDX DLSS add-on"),
        new("lecram", ComponentRegistry.Lecram, "Lecram DLSS5 add-on"),
        new("uplift", ComponentRegistry.Uplift, "GITC Uplift"),
        new("dlss", ComponentRegistry.Dlss, "DLSS (SR · RR · FG)"),
        new("streamline", ComponentRegistry.Streamline, "Streamline (for Dynamic MFG)"),
    ];

    /// <summary>The table row key of a component id; null when the table has no row for it.</summary>
    public static string? KeyOf(string id) => Rows.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Key;

    public static string NameOf(string id) => Rows.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;

    /// <summary>The OptiScaler row's tick is the install mode: ticked, OptiScaler loads into the game; unticked, ReShade does.</summary>
    public static InstallMode ModeFor(bool optiOn) => optiOn ? InstallMode.OptiScaler : InstallMode.ReShadeOnly;

    /// <summary>
    /// How a row looks in <paramref name="mode"/>. The OptiScaler row always shows and its tick follows the mode. ReShade is the loader
    /// in ReShade-only mode, so it stays on there. The NR add-ons replace OptiScaler's NR, so they exist in ReShade-only mode alone.
    /// <see cref="RowRule.On"/> is the tick the mode forces; null leaves the user's tick.
    /// </summary>
    public static RowRule Rule(string key, InstallMode mode) => key switch
    {
        "opti" => new(true, true, mode == InstallMode.OptiScaler),
        "reshade" when mode == InstallMode.ReShadeOnly => new(true, false, true),
        "renodx" or "lecram" or "uplift" when mode == InstallMode.OptiScaler => new(false, true, false),
        _ => new(true, true, null),
    };

    public sealed record RowRule(bool Visible, bool Editable, bool? On);

    /// <summary>The NR add-ons: one neural consumer per game (<see cref="NrAddons"/>), each loaded by ReShade.</summary>
    public static readonly IReadOnlyList<string> NrAddonIds = [ComponentRegistry.RenoDx, ComponentRegistry.Lecram, ComponentRegistry.Uplift];

    /// <summary>
    /// The rows that follow when row <paramref name="id"/> goes on or off in <paramref name="mode"/>, by hand or from a card. An NR add-on
    /// needs ReShade and the NR runtime. MFG Unlock is a ReShade add-on: next to OptiScaler, OptiScaler loads ReShade64.dll (LoadReshade),
    /// so MFG Unlock brings the ReShade row on, and as it can't load there without ReShade, unticking ReShade takes it off.
    /// </summary>
    public static IReadOnlyList<(string Id, bool On)> Follow(string id, bool on, InstallMode mode)
    {
        if (on && NrAddonIds.Contains(id, StringComparer.OrdinalIgnoreCase)) return [(ComponentRegistry.DlssNr, true), (ComponentRegistry.ReShade, true)];
        if (mode != InstallMode.OptiScaler) return [];
        if (on && id.Equals(ComponentRegistry.MfgUnlock, StringComparison.OrdinalIgnoreCase)) return [(ComponentRegistry.ReShade, true)];
        if (!on && id.Equals(ComponentRegistry.ReShade, StringComparison.OrdinalIgnoreCase)) return [(ComponentRegistry.MfgUnlock, false)];
        return [];
    }

    /// <summary>The rows <paramref name="ids"/> bring on with them in <paramref name="mode"/> (<see cref="Follow"/>), the ids themselves first.</summary>
    public static IReadOnlyList<string> WithFollowers(IEnumerable<string> ids, InstallMode mode)
    {
        var list = ids.ToList();
        return list.Concat(list.SelectMany(id => Follow(id, true, mode)).Where(f => f.On).Select(f => f.Id))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

/// <summary>One component row as the picker sees it.</summary>
public sealed record TableRow(bool Visible, bool Enabled);

/// <summary>
/// Which method card the component table matches. The table is the truth: the picked card is worked out from the rows that are on
/// and the install mode, never kept from an earlier click, so a card is only picked while the table still installs it.
/// </summary>
public static class MethodMatch
{
    /// <summary>The page has the method's install mode (when it needs one) and every one of its components the table shows is on.</summary>
    public static bool Matches(MethodOption m, IReadOnlyDictionary<string, TableRow> table, InstallMode mode)
    {
        if (!m.Selectable || m.Mode is { } need && need != mode) return false;
        var shown = m.Components.Where(id => table.TryGetValue(id, out var r) && r.Visible).ToList();
        return shown.Count > 0 && shown.All(id => table[id].Enabled);
    }

    /// <summary>
    /// The card to mark: of the matching <paramref name="choices"/> the one with the most components, so a card whose rows are a subset
    /// of another's (the DLSS update inside every DLSS game) loses to it. A tie goes to <paramref name="clicked"/>, else to the
    /// higher-ranked card. Null: no card matches, the user made their own selection.
    /// </summary>
    public static MethodOption? Picked(IEnumerable<MethodOption> choices, IReadOnlyDictionary<string, TableRow> table, InstallMode mode, MethodOption? clicked = null)
    {
        var matching = choices.Where(m => Matches(m, table, mode)).ToList();
        if (matching.Count == 0) return null;
        var most = matching.Max(m => m.Components.Count);
        var best = matching.Where(m => m.Components.Count == most).ToList();
        return best.FirstOrDefault(m => SameCard(m, clicked)) ?? best[0];
    }

    /// <summary>The same card of the panel, also across a refresh of the advice: a method can have several cards (one per NR add-on), told apart by title.</summary>
    public static bool SameCard(MethodOption? a, MethodOption? b) => a is not null && b is not null && a.Method == b.Method && a.Title == b.Title;

    /// <summary>
    /// The rows picking <paramref name="next"/> turns off, besides the mode's own rules: the rows only the <paramref name="previous"/> card
    /// used, and the other NR add-ons when the card brings one (one neural consumer). A row the card needs, or an add-on that stays on
    /// (<paramref name="addOnsOn"/>, with what it brings in the card's mode, such as ReShade for MFG Unlock next to OptiScaler), stays on.
    /// </summary>
    public static IReadOnlyList<string> TurnsOff(MethodOption next, MethodOption? previous, IEnumerable<MethodOption> addOnsOn, InstallMode mode)
    {
        var cardMode = next.Mode ?? mode;
        var keep = new HashSet<string>(ComponentTable.WithFollowers(next.Components, cardMode), StringComparer.OrdinalIgnoreCase);
        foreach (var a in addOnsOn) keep.UnionWith(ComponentTable.WithFollowers(a.Components, cardMode));
        var off = new List<string>(previous?.Components ?? []);
        if (next.Components.Any(id => ComponentTable.NrAddonIds.Contains(id, StringComparer.OrdinalIgnoreCase))) off.AddRange(ComponentTable.NrAddonIds);
        return [.. off.Where(id => !keep.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The card's "Turns on: ..." line with the table's names: its rows and the rows they bring in the card's mode (ReShade for MFG Unlock
    /// next to OptiScaler), and the OptiScaler row going off for a card that needs ReShade + add-ons. Empty for a card that can't be picked.
    /// </summary>
    public static string TurnsOnText(MethodOption m, InstallMode mode, Func<string, string>? nameOf = null)
    {
        if (!m.Selectable) return "";
        var text = "Turns on: " + Names(ComponentTable.WithFollowers(m.Components, m.Mode ?? mode), nameOf);
        return m.Mode == InstallMode.ReShadeOnly ? $"{text}. Turns off: {(nameOf ?? ComponentTable.NameOf)(ComponentRegistry.OptiScaler)}" : text;
    }

    /// <summary>The components on in the table (shown rows only, table order) that neither <paramref name="picked"/> nor an add-on brings.</summary>
    public static IReadOnlyList<string> Outside(MethodOption? picked, IEnumerable<MethodOption> addOns, IReadOnlyDictionary<string, TableRow> table)
    {
        var covered = new HashSet<string>((picked?.Components ?? []).Concat(addOns.SelectMany(a => a.Components)), StringComparer.OrdinalIgnoreCase);
        return ComponentTable.Rows.Select(r => r.Id)
            .Where(id => table.TryGetValue(id, out var r) && r is { Visible: true, Enabled: true } && !covered.Contains(id))
            .ToList();
    }

    /// <summary>"A · B · C" with the table's names.</summary>
    public static string Names(IEnumerable<string> ids, Func<string, string>? nameOf = null) =>
        string.Join(" · ", ids.Select(nameOf ?? ComponentTable.NameOf));
}
