namespace DLSSUpdater.Scan;

/// <summary>
/// What the user set by hand for one game's detection; every field null follows the scan ("Auto"). Immutable: a change makes a new
/// record, so <see cref="GameInfo.Effective"/> can cache on the reference. Stored in <see cref="Core.GameOverride.Tech"/>.
/// </summary>
public sealed record TechOverride
{
    /// <summary>Graphics API(s) of the game; replaces the detected set.</summary>
    public GraphicsApis? Apis { get; init; }
    /// <summary>32 or 64.</summary>
    public int? Bits { get; init; }
    public GameEngine? Engine { get; init; }
    /// <summary>Unreal version "5.4"; only used while the effective engine is Unreal.</summary>
    public string? EngineVersion { get; init; }
    /// <summary>The exe that counts as the game's main one; the next scan reads API and bitness from it.</summary>
    public string? MainExe { get; init; }
    /// <summary>Folder the mods go into by default.</summary>
    public string? ModFolder { get; init; }

    public bool IsEmpty => Apis is null && Bits is null && Engine is null && EngineVersion is null && MainExe is null && ModFolder is null;

    /// <summary>The detected tech with the user's values on top. Returns <paramref name="detected"/> itself when there is nothing to apply.</summary>
    public static GameTech Apply(GameTech detected, TechOverride? manual)
    {
        if (manual is null || manual.IsEmpty) return detected;
        var engine = manual.Engine ?? detected.Engine;
        var version = engine == GameEngine.Unreal ? manual.EngineVersion ?? (manual.Engine is null || detected.Engine == GameEngine.Unreal ? detected.EngineVersion : null) : null;
        return new GameTech
        {
            Apis = manual.Apis ?? detected.Apis,
            ApisGuessed = manual.Apis is null && detected.ApisGuessed,
            Bits = manual.Bits ?? detected.Bits,
            Engine = engine,
            EngineVersion = version,
            UeProject = detected.UeProject,
            ModFolder = manual.ModFolder ?? detected.ModFolder,
            Upscalers = detected.Upscalers,
        };
    }

    public TechOverride Normalized() => this with
    {
        EngineVersion = string.IsNullOrWhiteSpace(EngineVersion) ? null : EngineVersion.Trim(),
        MainExe = string.IsNullOrWhiteSpace(MainExe) ? null : MainExe,
        ModFolder = string.IsNullOrWhiteSpace(ModFolder) ? null : ModFolder,
    };
}
