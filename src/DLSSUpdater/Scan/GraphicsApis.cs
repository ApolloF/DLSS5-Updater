namespace DLSSUpdater.Scan;

/// <summary>Graphics APIs a game exe links against or loads. Values are stored in the scan cache, so keep them stable.</summary>
[Flags]
public enum GraphicsApis
{
    None = 0,
    Dx8 = 1,
    Dx9 = 2,
    Dx10 = 4,
    Dx11 = 8,
    Dx12 = 16,
    Vulkan = 32,
    OpenGL = 64,
}

public static class GraphicsApiInfo
{
    /// <summary>Best API first: the one a mod has to hook when a game offers several.</summary>
    private static readonly GraphicsApis[] Priority =
        [GraphicsApis.Dx12, GraphicsApis.Vulkan, GraphicsApis.Dx11, GraphicsApis.Dx10, GraphicsApis.OpenGL, GraphicsApis.Dx9, GraphicsApis.Dx8];

    /// <summary>DX12 > Vulkan > DX11 > DX10 > OpenGL > DX9 > DX8; None when the set is empty.</summary>
    public static GraphicsApis Primary(GraphicsApis apis)
    {
        foreach (var a in Priority)
            if (apis.HasFlag(a)) return a;
        return GraphicsApis.None;
    }

    public static string Label(GraphicsApis api) => api switch
    {
        GraphicsApis.Dx8 => "DX8",
        GraphicsApis.Dx9 => "DX9",
        GraphicsApis.Dx10 => "DX10",
        GraphicsApis.Dx11 => "DX11",
        GraphicsApis.Dx12 => "DX12",
        GraphicsApis.Vulkan => "Vulkan",
        GraphicsApis.OpenGL => "OpenGL",
        _ => "",
    };

    /// <summary>All APIs in the set, best first: "DX12, DX11".</summary>
    public static string List(GraphicsApis apis) =>
        string.Join(", ", Priority.Where(a => apis.HasFlag(a)).Select(Label));

    /// <summary>The API a loaded system dll stands for, or None for any other dll.</summary>
    public static GraphicsApis FromDll(string dllName) => dllName.ToLowerInvariant() switch
    {
        "d3d8.dll" => GraphicsApis.Dx8,
        "d3d9.dll" => GraphicsApis.Dx9,
        "d3d10.dll" or "d3d10_1.dll" or "d3d10core.dll" => GraphicsApis.Dx10,
        "d3d11.dll" => GraphicsApis.Dx11,
        "d3d12.dll" => GraphicsApis.Dx12,
        "vulkan-1.dll" => GraphicsApis.Vulkan,
        "opengl32.dll" => GraphicsApis.OpenGL,
        _ => GraphicsApis.None,
    };
}
