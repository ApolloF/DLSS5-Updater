using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>What an install does with the MFG Unlock add-on for one game, and what it warns about.</summary>
public sealed record MfgPlan(string? Skip, bool SwapStreamline, IReadOnlyList<string> Warnings)
{
    public bool Install => Skip is null;
}

/// <summary>
/// The Streamline / DLSS-G requirements of mavismmg's MFG Unlock add-on (README, checked 2026-10-18):
/// it only unlocks multi-frame generation that the game already ships through Streamline DLSS-G, needs an
/// nvngx_dlssg.dll from 310.x on, and Dynamic MFG additionally needs one of the validated DLSS-G + Streamline pairs
/// with every sl.*.dll from the same package.
/// </summary>
public static class MfgCompat
{
    /// <summary>DLSS-G build → the Streamline build it was validated with, and how far that went.</summary>
    public static readonly IReadOnlyList<(Version DlssG, Version Streamline, bool Dynamic, string Status)> Pairs =
    [
        (new(310, 9, 0), new(2, 12, 0), false, "validated (fixed MFG; Streamline 2.12.x, no Dynamic MFG)"),
        (new(310, 9, 1), new(2, 14, 1), true, "validated (Dynamic MFG on D3D12)"),
        (new(310, 9, 16), new(2, 14, 16), true, "offline checks passed, in-game validation pending"),
    ];

    private static readonly Version MinDlssG = new(310, 0, 0);
    private static readonly string[] CoreSlFiles = ["sl.interposer.dll", "sl.common.dll", "sl.dlss_g.dll", "sl.reflex.dll"];

    /// <param name="streamline">The game's sl.*.dll files.</param>
    /// <param name="dlssG">nvngx_dlssg.dll version the game has after this install (DLSS update applied).</param>
    /// <param name="sdk">Streamline SDK version the install would swap in, if one is available.</param>
    /// <param name="swapStreamline">The user turned on Replace Streamline files.</param>
    /// <param name="dynamic">Dynamic MFG is on in the add-on's settings.</param>
    /// <param name="nrAddon">A RenoDX DLSS5 NR add-on (ShortFuse or Lecram) is installed too.</param>
    public static MfgPlan Plan(IReadOnlyList<DlssDll> streamline, Version? dlssG, Version? sdk, bool swapStreamline, bool dynamic, bool nrAddon)
    {
        var sets = streamline
            .GroupBy(d => Path.GetDirectoryName(d.Path) ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(d => Is(d, "sl.dlss_g.dll")))
            .ToList();
        if (sets.Count == 0)
            return new("MFG Unlock only works in games that ship DLSS Frame Generation through Streamline (sl.dlss_g.dll); this game has none.", false, []);

        var warnings = new List<string>();
        if (dlssG is null)
            warnings.Add("The game has no nvngx_dlssg.dll, so MFG Unlock depends on the one NVIDIA's driver supplies. Turn on DLSS updates to install one.");
        else if (dlssG < MinDlssG)
            warnings.Add($"nvngx_dlssg.dll {FileUtil.Format(dlssG)} has no multi-frame code. MFG Unlock needs 310.x or newer: turn on DLSS updates.");

        var pair = dlssG is null ? default : Pairs.FirstOrDefault(p => Build(p.DlssG) == Build(dlssG));
        // Dynamic MFG needs the whole matching Streamline set; swap it in when the SDK at hand is that set.
        var swap = swapStreamline
            || (dynamic && pair.DlssG is not null && sdk is not null && Build(sdk) == Build(pair.Streamline) && sets.Any(s => !Matches(s, pair.Streamline)));
        var after = sets.Select(s => s.Select(d => (d.Name, Version: swap && sdk is not null ? sdk : d.Version)).ToList()).ToList();

        if (dynamic)
        {
            if (pair.DlssG is null || !pair.Dynamic)
                warnings.Add($"Dynamic MFG needs nvngx_dlssg.dll 310.9.1 or 310.9.16; this game will have {FileUtil.Format(dlssG)}. Pin DLSS to one of them.");
            else if (!after.Any(s => s.Where(f => CoreSlFiles.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).All(f => Build(f.Version) == Build(pair.Streamline))))
                warnings.Add($"Dynamic MFG needs the complete Streamline {Format(pair.Streamline)} set next to DLSS-G {Format(pair.DlssG)}" +
                             (sdk is null ? ", and no Streamline SDK is available right now." : $"; the available SDK is {Format(sdk)}. Pin DLSS to the build that matches it."));
        }

        foreach (var set in after.Where(s => Mixed(s)))
            warnings.Add("Streamline files from different versions sit together (" +
                         string.Join(", ", set.Where(f => CoreSlFiles.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).Select(f => $"{f.Name} {Format(f.Version)}")) +
                         "). MFG Unlock needs one matched package: turn on Replace Streamline files.");

        if (nrAddon && dlssG is { Major: 310, Minor: 9 } && after.Any(s => s.Any(f => Is(f.Name, "sl.interposer.dll") && Build(f.Version) == new Version(2, 14, 0))))
            warnings.Add("Streamline 2.14.0 with DLSS 310.9 and a RenoDX DLSS5 add-on caused severe menu slowdown in some games. Use Streamline 2.14.1 or newer.");

        return new(null, swap && !swapStreamline, warnings);
    }

    private static bool Matches(IEnumerable<DlssDll> set, Version sl) =>
        set.Where(d => CoreSlFiles.Contains(d.Name, StringComparer.OrdinalIgnoreCase)).All(d => Build(d.Version) == Build(sl));

    private static bool Mixed(IEnumerable<(string Name, Version? Version)> set) =>
        set.Where(f => CoreSlFiles.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).Select(f => Build(f.Version)).Distinct().Count() > 1;

    private static bool Is(DlssDll d, string name) => Is(d.Name, name);
    private static bool Is(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static Version? Build(Version? v) => v is null ? null : new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    private static string Format(Version? v) => Build(v)?.ToString() ?? "unknown";
}
