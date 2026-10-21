using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>How ReShade gets into a game: as a proxy dll under this name, or (Vulkan) through the implicit layer with no dll in the game.</summary>
public sealed record ReShadeTarget(string? File, bool VulkanLayer, int Bits, string Reason)
{
    /// <summary>The dll that ends up in the game folder: the proxy name, or null for the Vulkan layer.</summary>
    public string? DllName => VulkanLayer ? null : File;

    /// <summary>"dxgi.dll", "Vulkan layer" ...: what the game page shows.</summary>
    public string Display => VulkanLayer ? "Vulkan layer (no dll in the game)" : File ?? "?";
}

/// <summary>
/// The file name ReShade is installed under. ReShade works as a proxy for the graphics dll the game loads, so the name follows the
/// API: dxgi.dll for DirectX 10 to 12, d3d9.dll, d3d8.dll, opengl32.dll; Vulkan has no proxy and uses the implicit layer.
/// </summary>
public static class ReShadeNaming
{
    /// <summary>What OptiScaler loads when its LoadReshade is on (its ini says "Loads Reshade64.dll from game's exe folder").</summary>
    public const string WithOptiScaler = ComponentStore.ReShadeFile;

    public const string Default = "dxgi.dll";

    /// <summary>The proxy name for the API a game mainly uses; null when that API has no proxy (Vulkan).</summary>
    public static string? ForApi(GraphicsApis primary) => primary switch
    {
        GraphicsApis.Dx12 or GraphicsApis.Dx11 or GraphicsApis.Dx10 => "dxgi.dll",
        GraphicsApis.Dx9 => "d3d9.dll",
        GraphicsApis.Dx8 => "d3d8.dll",
        GraphicsApis.OpenGL => "opengl32.dll",
        GraphicsApis.Vulkan => null,
        _ => Default,
    };

    /// <summary>
    /// The name and bitness for a game.
    /// <paramref name="optiScalerLoads"/>: OptiScaler owns the proxy name and loads ReShade64.dll itself, so ReShade keeps
    /// that name whatever the API. <paramref name="manualName"/>: the user's own choice for this game wins over the API.
    /// </summary>
    /// <param name="dxvkDlls">The dlls DXVK has in the game, when it is installed: ReShade can't share their names, so it loads through its Vulkan layer on top of DXVK
    /// (unless the user chose a proxy name DXVK doesn't use).</param>
    public static ReShadeTarget Resolve(GameTech tech, bool optiScalerLoads, string? manualName, IReadOnlyCollection<string>? dxvkDlls = null)
    {
        if (optiScalerLoads)
            return new(WithOptiScaler, false, 64, "OptiScaler owns the proxy name and loads ReShade64.dll (LoadReshade)");
        var bits = tech.Bits == 32 ? 32 : 64;
        var manual = AppSettings.ReShadeProxyNames.Contains(manualName ?? "", StringComparer.OrdinalIgnoreCase);
        if (dxvkDlls is { Count: > 0 } && !(manual && !dxvkDlls.Contains(manualName!, StringComparer.OrdinalIgnoreCase)))
            return new(null, true, bits, "DXVK uses the game's graphics dll names, so ReShade loads through its Vulkan layer on top of it");
        if (manual)
            return new(manualName!.ToLowerInvariant(), false, bits, "chosen by you for this game");
        var primary = tech.PrimaryApi;
        var name = ForApi(primary);
        if (name is null) return new(null, true, bits, "Vulkan games load ReShade through its implicit layer");
        var reason = primary == GraphicsApis.None
            ? "graphics API not detected, so the usual dxgi.dll"
            : $"{GraphicsApiInfo.Label(primary)} games load ReShade as {name}";
        return new(name, false, bits, reason);
    }
}
