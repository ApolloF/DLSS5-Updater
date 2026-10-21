using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>How a game's frame rate is capped. Exactly one is active per game.</summary>
public enum LimitMethod { None, Driver, ReLimiter, DisplayCommander, Unreal }

/// <summary>Ids, names and the component behind each <see cref="LimitMethod"/>.</summary>
public static class LimitMethods
{
    public static readonly IReadOnlyList<LimitMethod> All = [LimitMethod.Driver, LimitMethod.ReLimiter, LimitMethod.DisplayCommander, LimitMethod.Unreal, LimitMethod.None];

    public static string Id(LimitMethod m) => m switch
    {
        LimitMethod.Driver => "driver",
        LimitMethod.ReLimiter => ComponentRegistry.ReLimiter,
        LimitMethod.DisplayCommander => ComponentRegistry.DisplayCommander,
        LimitMethod.Unreal => "unreal",
        _ => "none",
    };

    /// <summary>The method an id (as stored in the settings) names; null for an unknown id.</summary>
    public static LimitMethod? Parse(string? id) => All.Cast<LimitMethod?>().FirstOrDefault(m => string.Equals(Id(m!.Value), id, StringComparison.OrdinalIgnoreCase));

    public static string Label(LimitMethod m) => m switch
    {
        LimitMethod.Driver => "NVIDIA driver limiter",
        LimitMethod.ReLimiter => "ReLimiter",
        LimitMethod.DisplayCommander => "Display Commander",
        LimitMethod.Unreal => "Unreal Engine (t.MaxFPS)",
        _ => "No limiter",
    };

    public static bool IsAddon(LimitMethod m) => m is LimitMethod.ReLimiter or LimitMethod.DisplayCommander;

    /// <summary>The registered component an add-on or the Unreal cap is recorded as; null for the driver (no files, see <see cref="NvidiaRecord"/>) and for none.</summary>
    public static string? ComponentOf(LimitMethod m) => m switch
    {
        LimitMethod.ReLimiter => ComponentRegistry.ReLimiter,
        LimitMethod.DisplayCommander => ComponentRegistry.DisplayCommander,
        LimitMethod.Unreal => ComponentRegistry.UnrealFrameCap,
        _ => null,
    };
}

/// <summary>
/// Whether a method can be used for a game, with the one-line reason when it can't. <see cref="Warning"/>: usable, but only after the user
/// confirms (an add-on in a game with anti-cheat); Auto never picks such a method.
/// </summary>
public sealed record LimitAvailability(LimitMethod Method, bool Available, string Reason, string Warning = "")
{
    public static LimitAvailability Yes(LimitMethod m) => new(m, true, "");
    public static LimitAvailability No(LimitMethod m, string reason) => new(m, false, reason);
    public static LimitAvailability Warn(LimitMethod m, string warning) => new(m, true, "", warning);
}

/// <summary>What the rules need to know about the machine and the catalog; the view model fills it, tests build it directly.</summary>
/// <param name="NvidiaGpu">An NVIDIA driver is present, so the driver profile can be written.</param>
/// <param name="HasAddon64">A 64-bit build of the add-on is in the library or the catalog.</param>
/// <param name="HasAddon32">Same for the 32-bit build.</param>
public sealed record LimitEnvironment(bool NvidiaGpu, IReadOnlyDictionary<LimitMethod, (bool Has64, bool Has32)> Addons, bool EngineIniKnown);

/// <summary>The rules for which methods a game may use and which one is picked without a choice. Pure.</summary>
public static class FrameLimitRules
{
    /// <summary>The limits every method accepts: the driver's own range starts at 20, ReLimiter's at 30; 30 is the common floor.</summary>
    public const int MinFps = 30, MaxFps = 1000, DefaultFps = 60;

    public static bool ValidFps(int fps) => fps is >= MinFps and <= MaxFps;

    public static int ClampFps(int fps) => Math.Clamp(fps, MinFps, MaxFps);

    public static LimitAvailability Availability(LimitMethod method, GameInfo game, LimitEnvironment env)
    {
        switch (method)
        {
            case LimitMethod.None:
                return LimitAvailability.Yes(method);
            case LimitMethod.Driver:
                // Works for every API and in games with anti-cheat: nothing is placed in the game.
                return env.NvidiaGpu ? LimitAvailability.Yes(method) : LimitAvailability.No(method, "Needs an NVIDIA graphics card and driver.");
            case LimitMethod.Unreal:
                if (!game.Tech.IsUnreal) return LimitAvailability.No(method, "Only for Unreal Engine games.");
                // An Engine.ini setting is no injection; it stays allowed with anti-cheat.
                return env.EngineIniKnown ? LimitAvailability.Yes(method) : LimitAvailability.No(method, "The game's Engine.ini location couldn't be worked out (Unreal project not found).");
            default:
                var name = LimitMethods.Label(method);
                if (!env.Addons.TryGetValue(method, out var have)) have = (false, false);
                var ok = game.Tech.Is32Bit ? have.Has32 : have.Has64;
                if (!ok)
                    return LimitAvailability.No(method, game.Tech.Is32Bit ? $"{name} has no 32-bit build, and this game is 32-bit."
                        : $"{name} isn't in the library yet and the catalog can't be reached. Import it in Settings or try again online.");
                return game.AntiCheat is { } ac
                    ? LimitAvailability.Warn(method, $"{game.Name} has {ac} anti-cheat: an injected add-on can get an account banned and online modes may refuse to start. The driver limiter places nothing and is the safe choice.")
                    : LimitAvailability.Yes(method);
        }
    }

    /// <summary>The method picked when neither the game nor the settings choose one: the driver on NVIDIA (no injection, every API), else the first that works.</summary>
    public static LimitMethod Auto(GameInfo game, LimitEnvironment env)
    {
        foreach (var m in new[] { LimitMethod.Driver, LimitMethod.Unreal, LimitMethod.DisplayCommander })
            if (Availability(m, game, env) is { Available: true, Warning.Length: 0 }) return m;
        return LimitMethod.None;
    }

    /// <summary>
    /// Method and fps for a game: its own choice, else the Settings default, else <see cref="Auto"/>. A chosen method the game can't use
    /// falls back to Auto and says so (<see cref="FrameLimitSetting.Note"/>).
    /// </summary>
    public static FrameLimitSetting Effective(GameInfo game, LimitEnvironment env, GameOverride? over, AppSettings settings)
    {
        var gameChoice = FrameLimitSettings.Method(over?.PickOf(ComponentRegistry.FrameLimit));
        var defaultChoice = FrameLimitSettings.Method(settings.PickOf(ComponentRegistry.FrameLimit));
        var fps = FrameLimitSettings.Fps(over?.PickOf(ComponentRegistry.FrameLimit)) ?? FrameLimitSettings.Fps(settings.PickOf(ComponentRegistry.FrameLimit)) ?? DefaultFps;
        var chosen = gameChoice ?? defaultChoice;
        var auto = Auto(game, env);
        if (chosen is null) return new FrameLimitSetting(auto, fps, Manual: false, AutoMethod: auto, null);
        var availability = Availability(chosen.Value, game, env);
        return availability.Available
            ? new FrameLimitSetting(chosen.Value, fps, gameChoice is not null, auto, availability.Warning.Length > 0 ? availability.Warning : null)
            : new FrameLimitSetting(auto, fps, Manual: false, auto, $"{LimitMethods.Label(chosen.Value)} can't be used here: {availability.Reason}");
    }
}

/// <summary>The method and cap a game gets. <see cref="Manual"/> = this game's own choice (the picker shows it); otherwise it is the Settings default or the automatic pick.</summary>
public sealed record FrameLimitSetting(LimitMethod Method, int Fps, bool Manual, LimitMethod AutoMethod, string? Note);

/// <summary>Reading and writing the frame-limit choices in the generic per-component settings (<c>frame-limit</c>, app-wide and per game).</summary>
public static class FrameLimitSettings
{
    private const string MethodKey = "method", FpsKey = "fps", OsdKey = "osd", IniKey = "ini", DriverKey = "driver";

    public static LimitMethod? Method(ComponentPick? pick) =>
        pick?.Options is { } o && o.TryGetValue(MethodKey, out var id) ? LimitMethods.Parse(id) : null;

    public static int? Fps(ComponentPick? pick) =>
        pick?.Options is { } o && o.TryGetValue(FpsKey, out var v) && int.TryParse(v, out var fps) && FrameLimitRules.ValidFps(fps) ? fps : null;

    /// <summary>ReLimiter's OSD: true/false when chosen, null = leave the add-on's own setting.</summary>
    public static bool? Osd(ComponentPick? pick) =>
        pick?.Options is { } o && o.TryGetValue(OsdKey, out var v) ? v switch { "on" => true, "off" => false, _ => null } : null;

    /// <summary>The user's own ini lines for the limiter's config, one <c>key=value</c> or <c>[Section] key=value</c> per line.</summary>
    public static string IniText(ComponentPick? pick) => pick?.Options is { } o && o.TryGetValue(IniKey, out var v) ? v : "";

    /// <summary>The cap the app wrote into the driver profile for this game (the driver method is active); null otherwise.</summary>
    public static int? DriverCap(ComponentPick? pick) =>
        pick?.Options is { } o && o.TryGetValue(DriverKey, out var v) && int.TryParse(v, out var fps) ? fps : null;

    public static void SetMethod(GameOverride g, LimitMethod? method) => Set(g, MethodKey, method is null ? null : LimitMethods.Id(method.Value));
    public static void SetFps(GameOverride g, int? fps) => Set(g, FpsKey, fps?.ToString());
    public static void SetOsd(GameOverride g, bool? on) => Set(g, OsdKey, on is null ? null : on.Value ? "on" : "off");
    public static void SetIniText(GameOverride g, string? text) => Set(g, IniKey, string.IsNullOrWhiteSpace(text) ? null : text.Trim());
    public static void SetDriverCap(GameOverride g, int? fps) => Set(g, DriverKey, fps?.ToString());

    public static void SetMethod(AppSettings s, LimitMethod? method) => Set(s, MethodKey, method is null ? null : LimitMethods.Id(method.Value));
    public static void SetFps(AppSettings s, int? fps) => Set(s, FpsKey, fps?.ToString());

    private static void Set(GameOverride g, string key, string? value) => g.Components = ComponentPicks.SetOption(g.Components, ComponentRegistry.FrameLimit, key, value);
    private static void Set(AppSettings s, string key, string? value) => s.Components = ComponentPicks.SetOption(s.Components, ComponentRegistry.FrameLimit, key, value);
}

/// <summary>The frame-limit hint: with frame generation on, cap at the refresh rate divided by the generation factor. Shown, never applied.</summary>
public static class FrameLimitHint
{
    /// <summary>The cap to suggest, or null when there is no refresh rate to work from. Rounded down: a cap above what the display shows is no help.</summary>
    public static int? Recommend(int refreshHz, int fgFactor) =>
        refreshHz <= 0 || fgFactor < 2 ? null : Math.Max(1, refreshHz / fgFactor);

    /// <summary>Frame generation signals in the game files: NVIDIA's (DLSS-G), AMD's and Intel's runtimes. Null when none.</summary>
    public static string? FrameGenerationSource(GameInfo game)
    {
        if (game.Features.HasFlag(GameFeatures.FG)) return "DLSS frame generation";
        if (game.Tech.Upscalers.HasFlag(UpscalerSignals.FsrFrameGen)) return "FSR frame generation";
        if (game.Tech.Upscalers.HasFlag(UpscalerSignals.XeSSFrameGen)) return "XeSS frame generation";
        return null;
    }

    public static string? Text(GameInfo game, int refreshHz, int fgFactor = 2)
    {
        if (FrameGenerationSource(game) is not { } source || Recommend(refreshHz, fgFactor) is not { } cap) return null;
        return $"This game has {source}. With {fgFactor}x, a cap of {cap} FPS suits your {refreshHz} Hz display (refresh rate ÷ {fgFactor}); with a higher factor, divide by that factor instead. This is a suggestion, nothing is set for you.";
    }
}
