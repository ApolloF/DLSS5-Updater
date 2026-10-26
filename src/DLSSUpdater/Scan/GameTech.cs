using System.Text.Json.Serialization;

namespace DLSSUpdater.Scan;

/// <summary>Game engine, as far as the files tell. Values are stored in games.json, so keep them stable.</summary>
public enum GameEngine
{
    Unknown = 0,
    /// <summary>Unreal Engine 4 or 5 (a Binaries\Win64|Win32|WinGDK folder with an exe).</summary>
    Unreal = 1,
    /// <summary>Unreal Engine 3 and older: .u/.upk packages, or the engine config without a Shipping exe.</summary>
    UnrealLegacy = 2,
    Unity = 3,
    REEngine = 4,
}

/// <summary>Upscaler and frame-generation runtimes besides the NVIDIA ones, which <see cref="GameFeatures"/> already covers (DLSS-G included).</summary>
[Flags]
public enum UpscalerSignals
{
    None = 0,
    /// <summary>FSR 2/3/4 runtime dll (amd_fidelityfx_*.dll, ffx_*.dll).</summary>
    Fsr = 1,
    /// <summary>FSR frame generation or optical flow dll.</summary>
    FsrFrameGen = 2,
    /// <summary>Intel XeSS runtime (libxess*.dll).</summary>
    XeSS = 4,
    /// <summary>Intel XeSS frame generation (libxess_fg.dll).</summary>
    XeSSFrameGen = 8,
    /// <summary>Unreal DLSS plugin folder under a Plugins tree.</summary>
    DlssPlugin = 16,
    /// <summary>Unreal Streamline plugin folder under a Plugins tree.</summary>
    StreamlinePlugin = 32,
    /// <summary>Unreal FSR plugin folder under a Plugins tree.</summary>
    FsrPlugin = 64,
    /// <summary>Unreal XeSS plugin folder under a Plugins tree.</summary>
    XessPlugin = 128,
}

public static class UpscalerFiles
{
    // The exact FSR and XeSS file names below are unverified: they are the names those SDKs ship under, but no game was checked
    // for each one.

    /// <summary>The signal a dll in a game folder stands for, or None.</summary>
    public static UpscalerSignals OfFile(string fileName)
    {
        var n = fileName.ToLowerInvariant();
        if (!n.EndsWith(".dll", StringComparison.Ordinal)) return UpscalerSignals.None;
        if (n.StartsWith("libxess", StringComparison.Ordinal))
            return n.StartsWith("libxess_fg", StringComparison.Ordinal) ? UpscalerSignals.XeSSFrameGen : UpscalerSignals.XeSS;
        if (n.StartsWith("amd_fidelityfx_", StringComparison.Ordinal) || n.StartsWith("ffx_", StringComparison.Ordinal))
            return n.Contains("framegeneration", StringComparison.Ordinal) || n.Contains("frameinterpolation", StringComparison.Ordinal) ||
                   n.Contains("opticalflow", StringComparison.Ordinal)
                ? UpscalerSignals.Fsr | UpscalerSignals.FsrFrameGen
                : UpscalerSignals.Fsr;
        return UpscalerSignals.None;
    }

    public static bool IsUpscalerFile(string fileName) => OfFile(fileName) != UpscalerSignals.None;

    /// <summary>The signal for a plugin folder name inside an Unreal Plugins tree ([U]: the FSR and XeSS folder names vary by plugin version).</summary>
    public static UpscalerSignals OfPluginFolder(string folder)
    {
        if (folder.Equals("DLSS", StringComparison.OrdinalIgnoreCase)) return UpscalerSignals.DlssPlugin;
        if (folder.StartsWith("Streamline", StringComparison.OrdinalIgnoreCase)) return UpscalerSignals.StreamlinePlugin;
        if (folder.StartsWith("FSR", StringComparison.OrdinalIgnoreCase) || folder.StartsWith("FFXFSR", StringComparison.OrdinalIgnoreCase))
            return UpscalerSignals.FsrPlugin;
        if (folder.StartsWith("XeSS", StringComparison.OrdinalIgnoreCase)) return UpscalerSignals.XessPlugin;
        return UpscalerSignals.None;
    }
}

/// <summary>What the files of a game say about how it renders: API, bitness, engine and non-NVIDIA upscalers.</summary>
public sealed class GameTech
{
    /// <summary>Every graphics API found, not only the primary one.</summary>
    public GraphicsApis Apis { get; set; }

    /// <summary>True when <see cref="Apis"/> was inferred (dxgi.dll alone, the engine's usual API) rather than read from an import.</summary>
    public bool ApisGuessed { get; set; }

    /// <summary>32 or 64 from the PE machine type of the chosen exe; 0 when that exe could not be read.</summary>
    public int Bits { get; set; }

    public GameEngine Engine { get; set; }

    /// <summary>Unreal "5.4.3" (major 4..6), when found.</summary>
    public string? EngineVersion { get; set; }

    /// <summary>Unreal project folder (the one above Binaries).</summary>
    public string? UeProject { get; set; }

    /// <summary>Where mods go: the Binaries\Win64|WinGDK folder for Unreal, else the folder of the chosen exe.</summary>
    public string? ModFolder { get; set; }

    public UpscalerSignals Upscalers { get; set; }

    [JsonIgnore]
    public GraphicsApis PrimaryApi => GraphicsApiInfo.Primary(Apis);

    [JsonIgnore]
    public bool Is32Bit => Bits == 32;

    [JsonIgnore]
    public bool IsUnreal => Engine is GameEngine.Unreal or GameEngine.UnrealLegacy;

    /// <summary>Unreal major version (3 for the legacy engine), or 0 when unknown.</summary>
    [JsonIgnore]
    public int UeMajor =>
        Engine == GameEngine.UnrealLegacy ? 3
        : Engine == GameEngine.Unreal && EngineVersion is { } v && int.TryParse(v.Split('.')[0], out var m) ? m
        : 0;

    [JsonIgnore]
    public string EngineLabel => Engine switch
    {
        GameEngine.Unreal => EngineVersion is { } v ? "Unreal " + MajorMinor(v) : "Unreal",
        GameEngine.UnrealLegacy => "Unreal legacy",
        GameEngine.Unity => "Unity",
        GameEngine.REEngine => "RE Engine",
        _ => "",
    };

    /// <summary>One read-only line for the game page: "DX12 · 64-bit · Unreal 5.4". Empty when nothing is known.</summary>
    [JsonIgnore]
    public string Label => string.Join(" · ", new[]
    {
        GraphicsApiInfo.Label(PrimaryApi),
        Bits == 0 ? "" : Bits + "-bit",
        EngineLabel,
    }.Where(p => p.Length > 0));

    /// <summary>Longer text for the badge tooltip: all APIs, and whether they were read or guessed.</summary>
    [JsonIgnore]
    public string Details
    {
        get
        {
            var parts = new List<string>();
            if (Apis != GraphicsApis.None)
                parts.Add((ApisGuessed ? "Graphics API (guessed from the engine): " : "Graphics API: ") + GraphicsApiInfo.List(Apis));
            if (UeProject is not null) parts.Add("Unreal project: " + UeProject);
            if (ModFolder is not null) parts.Add("Mod folder: " + ModFolder);
            return string.Join("\n", parts);
        }
    }

    private static string MajorMinor(string version)
    {
        var p = version.Split('.');
        return p.Length >= 2 ? p[0] + "." + p[1] : version;
    }
}
