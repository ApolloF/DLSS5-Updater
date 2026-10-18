namespace DLSSUpdater.Core;

public sealed record CompatReport(string Tag, IReadOnlyList<string> MissingKeys, bool NewerThanTested, string TestedTag)
{
    public bool HasIssues => MissingKeys.Count > 0 || NewerThanTested;

    /// <summary>Managed keys whose ini comment differs from the tested release: values or defaults may have changed.</summary>
    public IReadOnlyList<string> ChangedKeys { get; init; } = [];

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (NewerThanTested)
                parts.Add($"OptiScaler-NR {Tag} is a newer major line than this app was tested with ({TestedTag}).");
            if (MissingKeys.Count > 0)
                parts.Add($"{MissingKeys.Count} setting(s) this app writes no longer exist in its OptiScaler.ini and would be ignored or appended " +
                          "as unknown keys:\n  " + string.Join("\n  ", MissingKeys.Take(12)) + (MissingKeys.Count > 12 ? "\n  …" : ""));
            if (ChangedKeys.Count > 0)
                parts.Add($"{ChangedKeys.Count} setting(s) this app writes are described differently than in {TestedTag}; their values or defaults may have changed:\n  " +
                          string.Join("\n  ", ChangedKeys.Take(12)) + (ChangedKeys.Count > 12 ? "\n  …" : ""));
            return parts.Count == 0 ? $"OptiScaler-NR {Tag}: config compatible." : string.Join("\n\n", parts);
        }
    }
}

/// <summary>
/// Compares a release's OptiScaler.ini with the keys this app manages, before anything is installed.
/// Only the ini is fetched (~60 KB, straight from the tag), not the 130 MB package.
/// </summary>
public static class OptiCompat
{
    public static async Task<CompatReport> CheckAsync(GitHubClient gh, string tag, IEnumerable<(string Section, string Key)> managed, CancellationToken ct)
    {
        var source = OptiSources.OfTag(tag);
        var cached = await FetchIniAsync(gh, tag, ct);
        var ini = IniFile.Load(cached);

        var keys = managed
            .Where(k => !string.IsNullOrWhiteSpace(k.Section) && !string.IsNullOrWhiteSpace(k.Key) && !source.IsUnsupported(k.Section, k.Key))
            .Distinct()
            .ToList();
        var missing = keys
            .Where(k => ini.Get(k.Section, k.Key) is null)
            .Select(k => $"[{k.Section}] {k.Key}")
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        IReadOnlyList<string> changed = [];
        var testedTag = source.TestedTag + source.TagSuffix;
        if (!testedTag.Equals(tag, StringComparison.OrdinalIgnoreCase))
        {
            var then = KeyDocs(File.ReadAllText(await FetchIniAsync(gh, testedTag, ct)));
            changed = ChangedDocs(then, KeyDocs(File.ReadAllText(cached)), keys);
        }

        var v = FileUtil.ParseTag(tag);
        var tested = FileUtil.ParseTag(source.TestedTag)!;
        var newer = v is not null && (v.Major > tested.Major || (v.Major == tested.Major && v.Minor > tested.Minor));
        return new CompatReport(tag, missing, newer, source.TestedTag) { ChangedKeys = changed };
    }

    private static async Task<string> FetchIniAsync(GitHubClient gh, string tag, CancellationToken ct)
    {
        var cached = Path.Combine(AppPaths.Cache, "optiscaler", $"ini-{string.Concat(tag.Where(char.IsLetterOrDigit))}.ini");
        if (!File.Exists(cached))
            await gh.DownloadAsync($"https://raw.githubusercontent.com/{OptiSources.OfTag(tag).Repo}/{OptiSources.RawTag(tag)}/OptiScaler.ini", cached, "OptiScaler.ini", null, ct);
        return cached;
    }

    /// <summary>"[Section] Key" of the keys present in both versions whose comment changed.</summary>
    internal static IReadOnlyList<string> ChangedDocs(IReadOnlyDictionary<(string, string), string> then, IReadOnlyDictionary<(string, string), string> now,
        IEnumerable<(string Section, string Key)> keys) =>
        keys.Select(k => (Id: (k.Section.ToLowerInvariant(), k.Key.ToLowerInvariant()), Name: $"[{k.Section}] {k.Key}"))
            .Where(k => then.TryGetValue(k.Id, out var a) && now.TryGetValue(k.Id, out var b) && a != b)
            .Select(k => k.Name)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The comment block written directly above each key, whitespace-normalised, keyed by lower-case (section, key).
    /// A blank line ends a block; a key that follows another key without its own comment gets "".
    /// </summary>
    internal static IReadOnlyDictionary<(string, string), string> KeyDocs(string iniText)
    {
        var docs = new Dictionary<(string, string), string>();
        var section = "";
        var comment = new List<string>();
        foreach (var raw in iniText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { comment.Clear(); continue; }
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim().ToLowerInvariant(); comment.Clear(); continue; }
            if (line.StartsWith(';')) { comment.Add(line[1..].Trim()); continue; }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            docs.TryAdd((section, line[..eq].Trim().ToLowerInvariant()), string.Join(' ', comment.Where(c => c.Length > 0)));
            comment.Clear();
        }
        return docs;
    }
}
