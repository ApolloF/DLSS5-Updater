using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>A plan for the installer plus where its pieces went, for the log and the tests.</summary>
public sealed record LimitPlan(ComponentPlan Plan, string? AddonFile, string IniFile, IReadOnlyList<string> Warnings);

/// <summary>
/// The config keys the frame limiters write, and nothing else. Only keys the projects document are written (docs/addons/frame-limiters.md):
/// <list type="bullet">
/// <item>ReLimiter: <c>[FrameLimiter] target_fps</c> and <c>osd_enabled</c> in relimiter.ini next to the add-on (its USER_GUIDE, "INI Configuration Reference").</item>
/// <item>Display Commander: <c>[DisplayCommander] fps_limit</c> and <c>fps_limiter_enabled</c> in DisplayCommander.ini next to the exe (the template of its default_settings file).</item>
/// <item>Unreal: <c>[SystemSettings] t.MaxFPS</c> in Engine.ini (Epic's ConsoleVariables.ini header: cvars go in the engine ini's [SystemSettings] section; the cvar's help text is "Caps FPS to the given value. Set to &lt;= 0 to be uncapped.").</item>
/// </list>
/// </summary>
public static class FrameLimitIni
{
    public const string ReLimiterFile = "relimiter.ini", ReLimiterSection = "FrameLimiter";
    public const string DisplayCommanderFile = "DisplayCommander.ini", DisplayCommanderSection = "DisplayCommander";
    public const string UnrealSection = HdrIni.EngineSystem, UnrealKey = "t.MaxFPS";

    public static string SectionOf(LimitMethod method) => method switch
    {
        LimitMethod.ReLimiter => ReLimiterSection,
        LimitMethod.DisplayCommander => DisplayCommanderSection,
        _ => UnrealSection,
    };

    /// <summary>The keys the app wants for a method; null OSD = leave ReLimiter's own setting.</summary>
    public static IReadOnlyList<(string Section, string Key, string Value)> Keys(LimitMethod method, int fps, bool? osd = null) => method switch
    {
        LimitMethod.ReLimiter => osd is { } on
            ? [(ReLimiterSection, "target_fps", fps.ToString()), (ReLimiterSection, "osd_enabled", on ? "true" : "false")]
            : [(ReLimiterSection, "target_fps", fps.ToString())],
        LimitMethod.DisplayCommander => [(DisplayCommanderSection, "fps_limit", fps.ToString()), (DisplayCommanderSection, "fps_limiter_enabled", "true")],
        LimitMethod.Unreal => [(UnrealSection, UnrealKey, fps.ToString())],
        _ => [],
    };

    /// <summary>
    /// The user's own lines: <c>key=value</c> (in the limiter's section) or <c>[Section]</c> headers above them; blank lines and lines
    /// starting with ; or # are ignored. Lines that aren't either are returned as errors.
    /// </summary>
    public static (IReadOnlyList<(string Section, string Key, string Value)> Items, IReadOnlyList<string> Errors) ParseOverrides(string? text, string defaultSection)
    {
        var items = new List<(string, string, string)>();
        var errors = new List<string>();
        var section = defaultSection;
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line[^1] == ']' && line.Length > 2)
            {
                section = line[1..^1].Trim();
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0 || section.Length == 0) { errors.Add($"\"{line}\" isn't key=value or a [Section] header"); continue; }
            var key = line[..eq].Trim();
            if (key.Length == 0) { errors.Add($"\"{line}\" has no key"); continue; }
            items.RemoveAll(i => i.Item1.Equals(section, StringComparison.OrdinalIgnoreCase) && i.Item2.Equals(key, StringComparison.OrdinalIgnoreCase));
            items.Add((section, key, line[(eq + 1)..].Trim()));
        }
        return (items, errors);
    }
}

/// <summary>Builds the component plan of a limiter: the add-on file or the Engine.ini key, and the config keys. No writes.</summary>
public static class FrameLimitPlanner
{
    public const string FpsKey = "fps", MethodKey = "method", IniFileKey = "iniFile", OsdKey = "osd", UserIniKey = "userIni";

    /// <param name="force">An explicit Apply: our keys are written even where the file holds another value (the user changed it in the game's own panel).
    /// Otherwise (a reinstall, an update) a key the user has set since is left alone, like the HDR component does.</param>
    public static LimitPlan Addon(GameInfo game, string targetDir, LimitMethod method, LimiterBuild build, string buildFile, int fps, bool? osd, string? userIni,
        bool force, InstalledComponent? existing)
    {
        if (!LimitMethods.IsAddon(method)) throw new ArgumentException("Not an add-on limiter.", nameof(method));
        targetDir = FileUtil.Normalize(targetDir);
        var warnings = new List<string>();
        var addonDir = ReShadeAddonPath.Resolve(targetDir, game.Root);
        var addonFile = Path.Combine(addonDir, build.FileName);
        // ReLimiter reads its ini beside its add-on file; Display Commander keeps its config next to the exe.
        var iniFile = method == LimitMethod.ReLimiter ? Path.Combine(addonDir, FrameLimitIni.ReLimiterFile) : Path.Combine(targetDir, FrameLimitIni.DisplayCommanderFile);

        var other = method == LimitMethod.ReLimiter ? LimitMethod.DisplayCommander : LimitMethod.ReLimiter;
        foreach (var f in LimiterFiles.All.Where(f => f.LimiterId == LimitMethods.ComponentOf(other)))
            if (File.Exists(Path.Combine(addonDir, f.Name)))
                warnings.Add($"{f.Name} is also in the add-on folder (not installed by this app). Two frame limiters in one game fight over the same frames; remove one of them.");

        var edits = Edits(game, targetDir, method, iniFile, FrameLimitIni.Keys(method, fps, osd), userIni, force, existing);
        var plan = new ComponentPlan(LimitMethods.ComponentOf(method)!)
        {
            BuildId = build.Id,
            Version = build.Version,
            Files = [(buildFile, addonFile)],
            ConfigEdits = edits,
            Extras = Extras(method, fps, osd, userIni, iniFile),
        };
        return new LimitPlan(plan, addonFile, iniFile, warnings);
    }

    /// <summary>The Engine.ini cap: one key, no files. The file is the one <see cref="EngineIniLocator"/> found (or the catalog's path for the game).</summary>
    public static LimitPlan Unreal(GameInfo game, string targetDir, EngineIniTarget engine, int fps, string? userIni, bool force, InstalledComponent? existing)
    {
        targetDir = FileUtil.Normalize(targetDir);
        var edits = Edits(game, targetDir, LimitMethod.Unreal, engine.Path, FrameLimitIni.Keys(LimitMethod.Unreal, fps), userIni, force, existing);
        var plan = new ComponentPlan(ComponentRegistry.UnrealFrameCap)
        {
            Version = $"{fps} FPS",
            ConfigEdits = edits,
            Extras = Extras(LimitMethod.Unreal, fps, null, userIni, engine.Path),
        };
        return new LimitPlan(plan, null, engine.Path, []);
    }

    private static List<(string File, string Section, string Key, string Value)> Edits(GameInfo game, string targetDir, LimitMethod method, string iniFile,
        IReadOnlyList<(string Section, string Key, string Value)> ours, string? userIni, bool force, InstalledComponent? existing)
    {
        var (own, errors) = FrameLimitIni.ParseOverrides(userIni, FrameLimitIni.SectionOf(method));
        if (errors.Count > 0) throw new InvalidOperationException("The extra ini lines for this limiter have a problem: " + string.Join("; ", errors) + ".");
        bool Overridden((string Section, string Key, string Value) k) =>
            own.Any(o => o.Section.Equals(k.Section, StringComparison.OrdinalIgnoreCase) && o.Key.Equals(k.Key, StringComparison.OrdinalIgnoreCase));

        var wanted = ours.Where(k => !Overridden(k)).ToList();
        if (!force)
        {
            var entry = InstallManifest.ConfigEntry(FileUtil.Normalize(iniFile), targetDir, FileUtil.Normalize(game.Root));
            wanted = HdrIni.MissingOnly(Read(iniFile), existing, entry, wanted).ToList();
        }
        // The user's own lines come last and always win over ours.
        return wanted.Concat(own).Select(k => (iniFile, k.Section, k.Key, k.Value)).ToList();
    }

    private static Dictionary<string, string> Extras(LimitMethod method, int fps, bool? osd, string? userIni, string iniFile) => new()
    {
        [MethodKey] = LimitMethods.Id(method),
        [FpsKey] = fps.ToString(),
        [OsdKey] = osd is { } on ? (on ? "on" : "off") : "",
        [UserIniKey] = (userIni ?? "").Trim(),
        [IniFileKey] = iniFile,
    };

    private static IniFile? Read(string path)
    {
        try { return File.Exists(path) ? IniFile.Load(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
