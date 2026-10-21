using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>One named Lilium HDR preset: the keys of the fork's HDR-mod.md that the author's shipped preset files set together.</summary>
public sealed record LiliumTier(string Id, string Label, string Description, bool D3d9, IReadOnlyList<(string Key, string Value)> Keys);

/// <summary>
/// The dxvk.conf the app writes, and nothing else. Facts (docs/addons/dxvk.md): every key and its values come from DXVK's own dxvk.conf
/// (the four defaults below, all documented there) or from the Lilium fork's HDR-mod.md (the HDR keys, only meaningful in a fork build).
/// The tiers are the groupings of the preset files the fork ships in its release; their keys and values are the documented ones, except
/// where noted. dxvk.conf has no sections: keys sit at the top level, so every edit uses the empty section name.
/// </summary>
public static class DxvkConf
{
    public const string FileName = "dxvk.conf";
    public const string Section = "";
    /// <summary>An option name only the Lilium fork's dlls contain; how an imported build is told apart (a stock DXVK build has none of the HDR keys).</summary>
    public const string LiliumSignature = "upgradeSwapChainColorSpaceTo";
    public const string TierOption = "lilium-tier", PresentOption = "present-method", UserLinesOption = "conf-lines";
    public const string TierOff = "off", TierAuto = "safest";

    /// <summary>
    /// What a fresh dxvk.conf gets in every build: HDR exposed to the game (dxgi.enableHDR; DXVK defaults it from the DXVK_HDR variable), and the
    /// stock values of exclusive full-screen (off), latency sleep (auto) and DPI awareness (on), written so they are visible and easy to change.
    /// Written only where the file has no value of its own.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Value)> Defaults =
    [
        ("dxgi.enableHDR", "True"),
        ("dxvk.allowFse", "False"),
        ("dxvk.latencySleep", "Auto"),
        ("d3d9.dpiAware", "True"),
    ];

    private static readonly string[] D3d9Formats =
        ["B5G6R5_UNORM", "BGR5A1_UNORM", "BGR5X1_UNORM", "BGRA4_UNORM", "BGRX4_UNORM", "RGBA8_UNORM", "RGBX8_UNORM", "BGRA8_UNORM", "BGRX8_UNORM", "RGB10A2_UNORM", "BGR10A2_UNORM", "RGBA16_UNORM"];

    private static readonly string[] D3d11Eight =
        ["RGBA8_UNORM", "BGRA8_UNORM", "BGRX8_UNORM", "RGBA8_UNORM_SRGB", "BGRA8_UNORM_SRGB", "BGRX8_UNORM_SRGB"];

    private static readonly string[] D3d11Typeless = ["RGBA8_TYPELESS", "BGRA8_TYPELESS", "BGRX8_TYPELESS"];

    public static IReadOnlyList<LiliumTier> Tiers(bool d3d9) => d3d9 ? D3d9Tiers : D3d11Tiers;

    public static LiliumTier? Tier(bool d3d9, string? id) => Tiers(d3d9).FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Direct3D 8 and 9 use the d3d9.* keys (d3d8.dll translates through DXVK's D3D9 front end; the fork documents d3d9 only); DirectX 10 and 11 the d3d11.* ones.</summary>
    public static bool UsesD3d9Keys(GraphicsApis primary) => primary is GraphicsApis.Dx8 or GraphicsApis.Dx9;

    private static (string, string) Kv(string key, string value) => (key, value);

    private static List<(string Key, string Value)> Swap(string d, string format = "rgba16_sfloat") =>
    [
        Kv($"{d}.enableSwapChainUpgrade", "true"),
        Kv($"{d}.upgradeSwapChainFormatTo", format),
        Kv($"{d}.upgradeSwapChainColorSpaceTo", "scRGB"),
    ];

    private static List<(string Key, string Value)> Back(string d, string format) =>
        [Kv($"{d}.enableBackBufferUpgrade", "true"), Kv($"{d}.upgradeBackBufferTo", format)];

    private static IEnumerable<(string Key, string Value)> Targets(string d, IEnumerable<string> formats, string to) =>
        formats.Select(f => Kv($"{d}.upgrade_{f}_renderTargetTo", to));

    private static LiliumTier T9(string id, string label, string description, IEnumerable<(string, string)> keys) =>
        new(id, label, description, true, [.. keys, Kv("d3d9.enforceWindowModeInternally", "disabled")]);

    private static IReadOnlyList<LiliumTier> D3d9Tiers { get; } =
    [
        T9("safest", "Safest", "Swap chain only: scRGB, 16-bit float. The author's first choice; it should work in nearly every game.", Swap("d3d9")),
        T9("second-safest", "Second safest", "Swap chain, plus 16-bit unorm back buffer.", [.. Back("d3d9", "rgba16_unorm"), .. Swap("d3d9")]),
        T9("slightly-unsafe", "Slightly unsafe", "Swap chain, plus 16-bit float back buffer.", [.. Swap("d3d9"), .. Back("d3d9", "rgba16_sfloat")]),
        T9("unsafer", "Unsafer", "Render targets of every format upgraded to 16-bit unorm (less banding), back buffer 16-bit unorm. The author's preset also sets upgrade_RGBA16_UNORM to rgba16_unorm, which HDR-mod.md does not list as a supported value.",
            [Kv("d3d9.enableRenderTargetUpgrades", "true"), .. Targets("d3d9", D3d9Formats, "rgba16_unorm"), .. Back("d3d9", "rgba16_unorm"), .. Swap("d3d9")]),
        T9("even-unsafer", "Even unsafer", "Like Unsafer with a 16-bit float back buffer.",
            [Kv("d3d9.enableRenderTargetUpgrades", "true"), .. Targets("d3d9", D3d9Formats, "rgba16_unorm"), .. Back("d3d9", "rgba16_sfloat"), .. Swap("d3d9")]),
        T9("experimental", "Experimental", "Render targets of every format upgraded to 16-bit float ('overbright' values survive). Needs further post processing to look right.",
            [Kv("d3d9.enableRenderTargetUpgrades", "true"), .. Targets("d3d9", D3d9Formats, "rgba16_sfloat"), .. Back("d3d9", "rgba16_sfloat"), .. Swap("d3d9")]),
    ];

    private static LiliumTier T11(string id, string label, string description, IEnumerable<(string, string)> keys) => new(id, label, description, false, [.. keys]);

    private static IEnumerable<(string Key, string Value)> Targets11(string eight, string typeless, bool withFloats)
    {
        foreach (var k in Targets("d3d11", D3d11Eight, eight)) yield return k;
        foreach (var k in Targets("d3d11", D3d11Typeless, typeless)) yield return k;
        yield return Kv("d3d11.upgrade_RGB10A2_UNORM_renderTargetTo", eight);
        yield return Kv("d3d11.upgrade_RGB10A2_TYPELESS_renderTargetTo", typeless);
        if (!withFloats) yield break;
        yield return Kv("d3d11.upgrade_RG11B10_UFLOAT_renderTargetTo", "rgba16_sfloat");
        yield return Kv("d3d11.upgrade_RGBA16_UNORM_renderTargetTo", "rgba16_sfloat");
    }

    private static IReadOnlyList<LiliumTier> D3d11Tiers { get; } =
    [
        T11("safest", "Safest", "Swap chain only: scRGB, 16-bit float. The author's first choice; it should work in nearly every game.", Swap("d3d11")),
        T11("second-safest", "Second safest", "Swap chain, plus 16-bit unorm back buffer.", [.. Back("d3d11", "rgba16_unorm"), .. Swap("d3d11")]),
        T11("slightly-unsafe", "Slightly unsafe", "Swap chain, plus 16-bit float back buffer.", [.. Back("d3d11", "rgba16_sfloat"), .. Swap("d3d11")]),
        T11("unsafer", "Unsafer", "Render targets upgraded to 16-bit unorm (less banding; typeless formats to rgba16_typeless), back buffer 16-bit unorm.",
            [Kv("d3d11.enableRenderTargetUpgrades", "true"), .. Targets11("rgba16_unorm", "rgba16_typeless", false), .. Back("d3d11", "rgba16_unorm"), .. Swap("d3d11")]),
        T11("even-unsafer", "Even unsafer", "Like Unsafer with a 16-bit float back buffer.",
            [Kv("d3d11.enableRenderTargetUpgrades", "true"), .. Targets11("rgba16_unorm", "rgba16_typeless", false), .. Back("d3d11", "rgba16_sfloat"), .. Swap("d3d11")]),
        T11("slightly-experimental", "Slightly experimental", "Render targets upgraded to 16-bit float ('overbright' values survive; typeless formats to rgba16_typeless). Needs further post processing to look right.",
            [Kv("d3d11.enableRenderTargetUpgrades", "true"), .. Targets11("rgba16_sfloat", "rgba16_typeless", false), .. Back("d3d11", "rgba16_sfloat"), .. Swap("d3d11")]),
        T11("fully-experimental", "Fully experimental", "Like Slightly experimental, also upgrading R11G11B10 and 16-bit unorm render targets to 16-bit float.",
            [Kv("d3d11.enableRenderTargetUpgrades", "true"), .. Targets11("rgba16_sfloat", "rgba16_typeless", true), .. Back("d3d11", "rgba16_sfloat"), .. Swap("d3d11")]),
    ];

    /// <summary>
    /// The user's own lines: <c>key = value</c>, one per line; blank lines and lines starting with # or ; are ignored. dxvk.conf has no sections, so
    /// a [Section] header is an error. A key given twice keeps the last.
    /// </summary>
    public static (IReadOnlyList<(string Key, string Value)> Items, IReadOnlyList<string> Errors) ParseLines(string? text)
    {
        var items = new List<(string Key, string Value)>();
        var errors = new List<string>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            var eq = line.IndexOf('=');
            if (line[0] == '[' || eq <= 0) { errors.Add($"\"{line}\" isn't key = value (dxvk.conf has no sections)"); continue; }
            var key = line[..eq].Trim();
            if (key.Length == 0) { errors.Add($"\"{line}\" has no key"); continue; }
            items.RemoveAll(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            items.Add((key, line[(eq + 1)..].Trim()));
        }
        return (items, errors);
    }

    /// <summary>The Lilium keys a build with the fork's options gets for a tier choice (null/"safest" = the first tier, "off" = none).</summary>
    public static IReadOnlyList<(string Key, string Value)> TierKeys(GraphicsApis primary, bool lilium, string? tierId)
    {
        if (!lilium || tierId == TierOff) return [];
        return (Tier(UsesD3d9Keys(primary), tierId ?? TierAuto) ?? Tier(UsesD3d9Keys(primary), TierAuto))?.Keys ?? [];
    }
}

/// <summary>Builds the component plan of a DXVK install: the dlls and the dxvk.conf keys. No writes.</summary>
public static class DxvkPlanner
{
    public const string ChannelKey = "channel", TierKey = "tier", ApiKey = "api", ArchKey = "arch", ConfKey = "conf", UserKey = "userConf", PresentKey = "present";

    /// <param name="force">An explicit Apply: our tier keys replace what the file holds. Otherwise (a reinstall, an update) a key the user has changed since is left alone.
    /// The four defaults are written only where the file has no value either way.</param>
    public static ComponentPlan Plan(GameInfo game, string targetDir, LibraryBuild build, string? tier, bool present, string? userConf, bool force, InstalledComponent? existing)
    {
        targetDir = FileUtil.Normalize(targetDir);
        var primary = game.Effective.PrimaryApi;
        var lilium = DxvkLibrary.HasLiliumOptions(build);
        var conf = Path.Combine(targetDir, DxvkConf.FileName);
        var entry = InstallManifest.ConfigEntry(FileUtil.Normalize(conf), targetDir, FileUtil.Normalize(game.Root));
        var (own, errors) = DxvkConf.ParseLines(userConf);
        if (errors.Count > 0) throw new InvalidOperationException("The extra dxvk.conf lines have a problem: " + string.Join("; ", errors) + ".");
        bool Overridden(string key) => own.Any(o => o.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        var current = Read(conf);
        var defaults = HdrIni.MissingOnly(current, existing, entry, DxvkConf.Defaults.Where(d => !Overridden(d.Key)).Select(d => (DxvkConf.Section, d.Key, d.Value))).ToList();
        var tierKeys = DxvkConf.TierKeys(primary, lilium, tier).Where(k => !Overridden(k.Key)).Select(k => (DxvkConf.Section, k.Key, k.Value)).ToList();
        if (!force) tierKeys = HdrIni.MissingOnly(current, existing, entry, tierKeys).ToList();

        var edits = defaults.Concat(tierKeys).Concat(own.Select(o => (DxvkConf.Section, o.Key, o.Value))).Select(k => (conf, k.Item1, k.Item2, k.Item3)).ToList();
        return new ComponentPlan(ComponentRegistry.Dxvk)
        {
            BuildId = build.Id,
            Version = build.Label,
            Files = DxvkLibrary.Files(build, game, targetDir),
            ConfigEdits = edits,
            Extras = new Dictionary<string, string>
            {
                [ChannelKey] = DxvkChannels.Id(DxvkLibrary.ChannelOf(build)),
                [TierKey] = lilium ? tier ?? DxvkConf.TierAuto : "",
                [ApiKey] = GraphicsApiInfo.Label(primary),
                [ArchKey] = DxvkDlls.Arch(game.Effective.Is32Bit),
                [ConfKey] = conf,
                [UserKey] = (userConf ?? "").Trim(),
                [PresentKey] = present ? "on" : "off",
            },
        };
    }

    private static IniFile? Read(string path)
    {
        try { return File.Exists(path) ? IniFile.Load(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
