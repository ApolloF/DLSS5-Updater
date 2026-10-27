namespace DLSSUpdater.Core;

/// <summary>The rows of the game page's component table, in table order. The method cards name components with these same names.</summary>
public static class ComponentTable
{
    public sealed record Row(string Key, string Id, string Name);

    public static readonly IReadOnlyList<Row> Rows =
    [
        new("opti", ComponentRegistry.OptiScaler, "OptiScaler-NR"),
        new("reshade", ComponentRegistry.ReShade, "ReShade"),
        new("mfg", ComponentRegistry.MfgUnlock, "MFG Unlock"),
        new("dlssnr", ComponentRegistry.DlssNr, "NR runtime"),
        new("renodx", ComponentRegistry.RenoDx, "ShortFuse RenoDX DLSS add-on"),
        new("lecram", ComponentRegistry.Lecram, "Lecram DLSS5 add-on"),
        new("uplift", ComponentRegistry.Uplift, "GITC Uplift"),
        new("dfc", ComponentRegistry.Dfc, "Deep Fried Chicken"),
        new("feeder", ComponentRegistry.Feeder, "DLSS5-Feeder"),
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
        "renodx" or "lecram" or "dfc" or "uplift" or "feeder" when mode == InstallMode.OptiScaler => new(false, true, false),
        _ => new(true, true, null),
    };

    public sealed record RowRule(bool Visible, bool Editable, bool? On);
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
    public static MethodOption? Picked(IEnumerable<MethodOption> choices, IReadOnlyDictionary<string, TableRow> table, InstallMode mode, Method? clicked = null)
    {
        var matching = choices.Where(m => Matches(m, table, mode)).ToList();
        if (matching.Count == 0) return null;
        var most = matching.Max(m => m.Components.Count);
        var best = matching.Where(m => m.Components.Count == most).ToList();
        return best.FirstOrDefault(m => m.Method == clicked) ?? best[0];
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
