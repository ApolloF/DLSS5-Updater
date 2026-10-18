namespace DLSSUpdater.Core;

/// <summary>What the imported Deep Fried Chicken differs in from the release this app was tested with.</summary>
public sealed record DfcCompatReport(string? Version, int? Schema, IReadOnlyList<string> MissingKeys)
{
    public bool VersionDiffers => Version != DfcConfig.TestedVersion;
    public bool SchemaDiffers => Schema != DfcConfig.TestedSchema;
    public bool HasIssues => VersionDiffers || SchemaDiffers || MissingKeys.Count > 0;

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (VersionDiffers)
                parts.Add($"Deep Fried Chicken {Version ?? "(unknown version)"} was imported; this app was tested with {DfcConfig.TestedVersion}.");
            if (SchemaDiffers)
                parts.Add($"Its config_schema is {(Schema?.ToString() ?? "missing")}, the app was tested with {DfcConfig.TestedSchema}. Values may mean something else.");
            if (MissingKeys.Count > 0)
                parts.Add($"{MissingKeys.Count} setting(s) this app writes are not in its default config and are hidden:\n  " +
                          string.Join("\n  ", MissingKeys.Take(12)) + (MissingKeys.Count > 12 ? "\n  …" : ""));
            return parts.Count == 0 ? $"Deep Fried Chicken {Version}: config compatible." : string.Join("\n\n", parts);
        }
    }
}

/// <summary>
/// deep-fried-chicken.cfg is a plain key=value file without sections. The app keeps its DFC options in the ReShade
/// override list under the pseudo-section <see cref="Section"/> and writes the keys without a section.
/// </summary>
public static class DfcConfig
{
    public const string Section = "DFC";
    public const string TestedVersion = "4.0.0";
    public const int TestedSchema = 13;
    private const string Header = "[" + Section + "]";

    public static bool IsDfc(IniOverride o) => o.Section.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase);

    /// <summary>The cfg's config_schema, or null when the key is missing or not a number.</summary>
    public static int? SchemaOf(string cfgText) =>
        int.TryParse(IniFile.Parse(Wrap(cfgText)).Get(Section, "config_schema"), out var n) ? n : null;

    public static DfcCompatReport Check(string? version, string? cfgText, IEnumerable<string> managedKeys)
    {
        var ini = cfgText is null ? null : IniFile.Parse(Wrap(cfgText));
        var missing = ini is null ? []
            : managedKeys.Distinct(StringComparer.OrdinalIgnoreCase).Where(k => ini.Get(Section, k) is null).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        return new DfcCompatReport(version, cfgText is null ? null : SchemaOf(cfgText), missing);
    }

    /// <summary>
    /// Merges the DFC options into the game's cfg like the ReShade.ini merge: keys the game's cfg already holds win
    /// unless this app wrote them last time (or <paramref name="overwrite"/>), keys the profile doesn't set stay as they are.
    /// DFC's cfg lists every key, so a value that still equals the imported default counts as untouched and is replaced.
    /// </summary>
    public static MergeResult Merge(string baseCfg, string? currentCfg, string? defaultCfg, IEnumerable<IniOverride> profile,
        IReadOnlyDictionary<string, string> lastApplied, bool overwrite)
    {
        var wanted = profile.Where(IsDfc).ToList();
        var known = new Dictionary<string, string>(lastApplied, StringComparer.OrdinalIgnoreCase);
        if (currentCfg is not null && defaultCfg is not null)
        {
            var current = IniFile.Parse(Wrap(currentCfg));
            var defaults = IniFile.Parse(Wrap(defaultCfg));
            foreach (var o in wanted)
                if (!known.ContainsKey(o.Id) && current.Get(Section, o.Key.Trim()) is { } cur
                    && Same(defaults.Get(Section, o.Key.Trim()), cur)) known[o.Id] = cur;
        }
        var merged = ConfigProfile.Merge(Wrap(baseCfg), currentCfg is null ? null : Wrap(currentCfg), wanted, carryOver: true, known, overwrite);
        return merged with { Text = Unwrap(merged.Text) };
    }

    /// <summary>
    /// ReShade finds effects only in [GENERAL] EffectSearchPaths, resolved against the folder of its dll; without the key
    /// it searches ".\" alone, not recursively (crosire/reshade runtime.cpp). DFC ships its effects in reshade-shaders\Shaders.
    /// Returns the list with that folder appended, or null when an entry already covers it.
    /// </summary>
    public static string? EffectSearchPathsFor(string? current, string baseDir)
    {
        var shaders = Path.GetFullPath(Path.Combine(baseDir, ComponentStore.DfcShaderDir)).TrimEnd('\\');
        var entries = (current ?? @".\").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (var entry in entries)
        {
            var p = entry.Trim('"').Replace('/', '\\');
            var recursive = p.EndsWith("**", StringComparison.Ordinal);
            if (recursive) p = p[..^2];
            if (p.Length == 0) p = @".\";
            string full;
            try { full = Path.GetFullPath(p, baseDir).TrimEnd('\\'); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { continue; }
            if (full.Equals(shaders, StringComparison.OrdinalIgnoreCase) || (recursive && FileUtil.IsUnder(shaders, full))) return null;
        }
        entries.Add(@".\" + ComponentStore.DfcShaderDir + @"\**");
        return string.Join(',', entries);
    }

    private static bool Same(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
        || (double.TryParse(a, System.Globalization.CultureInfo.InvariantCulture, out var x)
            && double.TryParse(b, System.Globalization.CultureInfo.InvariantCulture, out var y) && Math.Abs(x - y) < 1e-6);

    // IniFile needs a section header to address keys; the header is never written to the cfg.
    private static string Wrap(string cfg) => Header + "\n" + cfg;

    private static string Unwrap(string text)
    {
        var nl = text.IndexOf('\n');
        return nl < 0 ? "" : text[(nl + 1)..];
    }
}
