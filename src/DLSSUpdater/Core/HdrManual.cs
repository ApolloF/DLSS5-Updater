namespace DLSSUpdater.Core;

/// <summary>One ini value the user wants written for the HDR mod, on top of (and winning over) the keys the app writes itself.</summary>
public sealed class HdrIniEdit
{
    public const string ReShadeFile = "reshade", EngineFile = "engine";

    /// <summary>"reshade" (ReShade.ini beside the add-on) or "engine" (the game's Engine.ini).</summary>
    public string File { get; set; } = ReShadeFile;
    public string Section { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";

    public HdrIniEdit() { }
    public HdrIniEdit(string file, string section, string key, string value) => (File, Section, Key, Value) = (file, section, key, value);

    public bool IsReShade => File.Equals(ReShadeFile, StringComparison.OrdinalIgnoreCase);
    public bool IsEngine => File.Equals(EngineFile, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fits the ini format and the file's owner: a name without line breaks, brackets or "=", and in reshade.ini only a RenoDX
    /// section (the other sections belong to ReShade and the other components).
    /// </summary>
    public bool IsValid =>
        (IsReShade || IsEngine) && Plain(Section) && Plain(Key) && Value.IndexOfAny(['\r', '\n']) < 0 &&
        (!IsReShade || Section.Trim().StartsWith("renodx", StringComparison.OrdinalIgnoreCase));

    private static bool Plain(string s) => !string.IsNullOrWhiteSpace(s) && s.IndexOfAny(['\r', '\n', '[', ']', '=', ';']) < 0;
}

public static class HdrManual
{
    /// <summary>The "mod" pick of a game that uses an add-on the user imported; the add-on is the game's HDR build pick.</summary>
    public const string CustomMod = "custom";

    /// <summary>The library id prefix of <see cref="HdrBuild.ModId"/> for imported add-ons.</summary>
    public const string ImportedModId = "custom";

    /// <summary>
    /// The choice for an add-on the user imported: installed as it is, with no ini keys of ours (the author's defaults stay), pinned so
    /// the install uses the library copy and checks its hash.
    /// </summary>
    public static HdrChoice CustomChoice(HdrBuild build, int gameBits = 0) => new()
    {
        Notes = (gameBits == 64 && build.Arch == "x86") || (gameBits == 32 && build.Arch == "x64")
            ? [$"This is a {(build.Arch == "x86" ? "32" : "64")}-bit add-on and the game is {gameBits}-bit; ReShade will not load it."]
            : [],
        Kind = HdrKind.Custom, ModId = ImportedModId, Title = build.ModName,
        File = new CatalogBinary { Name = build.FileName, Arch = build.Arch, Url = build.Url ?? "", Sha256 = build.Id, Version = build.Version, FirstSeen = build.FirstSeen, License = build.License },
        Pinned = build,
        Reason = $"{build.ModName} is an add-on you imported; it is installed as it is.",
    };

    public static HdrChoice MissingCustom() => new()
    {
        Kind = HdrKind.None,
        Reason = "The add-on you imported for this game is no longer in the library. Import it again or reset to auto.",
    };
}

public static class HdrUserEdits
{
    /// <summary>The valid edits, trimmed, with a later edit of the same file, section and key replacing an earlier one.</summary>
    public static IReadOnlyList<HdrIniEdit> Clean(IEnumerable<HdrIniEdit>? edits)
    {
        var map = new Dictionary<string, HdrIniEdit>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in edits ?? [])
        {
            var t = new HdrIniEdit(e.File.Trim().ToLowerInvariant(), e.Section.Trim(), e.Key.Trim(), e.Value.Trim());
            if (t.IsValid) map[$"{t.File}|{t.Section}|{t.Key}"] = t;
        }
        return map.Values.ToList();
    }

    /// <summary>Goes into the install signature, so a changed edit shows as "settings changed, update to apply them".</summary>
    public static string Signature(IEnumerable<HdrIniEdit>? edits) =>
        string.Join(',', Clean(edits).OrderBy(e => e.File).ThenBy(e => e.Section, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .Select(e => $"{e.File}/{e.Section}/{e.Key}={e.Value}"));

    /// <summary>
    /// <paramref name="wanted"/> with the user's values in place: a key the user set takes their value, a key the app doesn't write is added.
    /// </summary>
    public static IReadOnlyList<(string Section, string Key, string Value)> Merge(
        IEnumerable<(string Section, string Key, string Value)> wanted, IEnumerable<HdrIniEdit> user)
    {
        var list = wanted.ToList();
        foreach (var e in user)
        {
            var i = list.FindIndex(w => w.Section.Equals(e.Section, StringComparison.OrdinalIgnoreCase) && w.Key.Equals(e.Key, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) list[i] = (list[i].Section, list[i].Key, e.Value);
            else list.Add((e.Section, e.Key, e.Value));
        }
        return list;
    }
}
