using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>Reads and writes the per-game detection overrides in the settings and keeps <see cref="GameInfo.Manual"/> in step with them.</summary>
public static class TechOverrides
{
    /// <summary>Copies the game's saved overrides onto the scanned info, so <see cref="GameInfo.Effective"/> sees them.</summary>
    public static void Attach(AppSettings settings, GameInfo game) => game.Manual = settings.Games.GetValueOrDefault(game.Id)?.Tech;

    /// <summary>Saves <paramref name="change"/> applied to the game's current overrides (all-automatic when it has none). An emptied override is dropped.</summary>
    public static TechOverride? Update(AppSettings settings, GameInfo game, Func<TechOverride, TechOverride> change)
    {
        var next = change(game.Manual ?? new TechOverride()).Normalized();
        if (next.IsEmpty)
        {
            if (settings.Games.TryGetValue(game.Id, out var o)) o.Tech = null;
            game.Manual = null;
        }
        else
        {
            settings.For(game.Id).Tech = next;
            game.Manual = next;
        }
        return game.Manual;
    }

    /// <summary>Every field back to what the scan found.</summary>
    public static void ResetAll(AppSettings settings, GameInfo game) => Update(settings, game, _ => new TechOverride());

    public static string ApiText(GraphicsApis apis) => apis == GraphicsApis.None ? "not detected" : GraphicsApiInfo.List(apis);

    /// <summary>The graphics API choices: single APIs and the usual pairs.</summary>
    public static readonly IReadOnlyList<GraphicsApis> ApiChoices =
    [
        GraphicsApis.Dx12, GraphicsApis.Dx11, GraphicsApis.Dx11 | GraphicsApis.Dx12, GraphicsApis.Dx10, GraphicsApis.Dx9, GraphicsApis.Dx8,
        GraphicsApis.Vulkan, GraphicsApis.OpenGL,
    ];

    public static string EngineText(GameEngine engine) => engine switch
    {
        GameEngine.Unreal => "Unreal Engine 4/5",
        GameEngine.UnrealLegacy => "Unreal Engine 3 and older",
        GameEngine.Unity => "Unity",
        GameEngine.REEngine => "RE Engine",
        _ => "Unknown / other",
    };

    /// <summary>Unreal versions offered by hand.</summary>
    public static readonly IReadOnlyList<string> UnrealVersions = ["4.27", "5.0", "5.1", "5.2", "5.3", "5.4", "5.5", "5.6", "5.7"];
}
