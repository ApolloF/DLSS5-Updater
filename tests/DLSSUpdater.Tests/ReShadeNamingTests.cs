using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>The dll name ReShade is installed under: API x bitness x OptiScaler present, plus the manual override.</summary>
public class ReShadeNamingTests
{
    private static GameTech Tech(GraphicsApis apis, int bits = 64) => new() { Apis = apis, Bits = bits };

    [Theory]
    [InlineData(GraphicsApis.Dx12, 64, "dxgi.dll")]
    [InlineData(GraphicsApis.Dx11, 64, "dxgi.dll")]
    [InlineData(GraphicsApis.Dx10, 64, "dxgi.dll")]
    [InlineData(GraphicsApis.Dx12, 32, "dxgi.dll")]
    [InlineData(GraphicsApis.Dx11, 32, "dxgi.dll")]
    [InlineData(GraphicsApis.Dx9, 64, "d3d9.dll")]
    [InlineData(GraphicsApis.Dx9, 32, "d3d9.dll")]
    [InlineData(GraphicsApis.Dx8, 32, "d3d8.dll")]
    [InlineData(GraphicsApis.OpenGL, 64, "opengl32.dll")]
    [InlineData(GraphicsApis.OpenGL, 32, "opengl32.dll")]
    [InlineData(GraphicsApis.Dx11 | GraphicsApis.Dx9, 32, "dxgi.dll")]
    [InlineData(GraphicsApis.Dx12 | GraphicsApis.Dx11 | GraphicsApis.Dx9, 64, "dxgi.dll")]
    [InlineData(GraphicsApis.Dx9 | GraphicsApis.OpenGL, 64, "opengl32.dll")]
    [InlineData(GraphicsApis.None, 64, "dxgi.dll")]
    public void WithoutOptiScaler_TheApiPicksTheProxyName_AndTheBitnessTheDll(GraphicsApis apis, int bits, string expected)
    {
        var t = ReShadeNaming.Resolve(Tech(apis, bits), optiScalerLoads: false, manualName: null);
        Assert.Equal(expected, t.File);
        Assert.False(t.VulkanLayer);
        Assert.Equal(bits, t.Bits);
        Assert.Equal(expected, t.DllName);
    }

    [Theory]
    [InlineData(GraphicsApis.Vulkan, 64)]
    [InlineData(GraphicsApis.Vulkan | GraphicsApis.Dx11, 64)]
    [InlineData(GraphicsApis.Vulkan | GraphicsApis.OpenGL, 32)]
    public void Vulkan_UsesTheImplicitLayer_AndNoDllInTheGame(GraphicsApis apis, int bits)
    {
        var t = ReShadeNaming.Resolve(Tech(apis, bits), false, null);
        Assert.True(t.VulkanLayer);
        Assert.Null(t.DllName);
        Assert.Equal(bits, t.Bits);
    }

    [Theory]
    [InlineData(GraphicsApis.Dx12, 64)]
    [InlineData(GraphicsApis.Dx9, 32)]
    [InlineData(GraphicsApis.OpenGL, 64)]
    [InlineData(GraphicsApis.Vulkan, 64)]
    [InlineData(GraphicsApis.None, 0)]
    public void WithOptiScaler_ReShadeKeepsItsOwnName_WhateverTheApi(GraphicsApis apis, int bits)
    {
        var t = ReShadeNaming.Resolve(Tech(apis, bits), optiScalerLoads: true, manualName: "d3d9.dll");
        Assert.Equal("ReShade64.dll", t.File);
        Assert.Equal(64, t.Bits);
        Assert.False(t.VulkanLayer);
    }

    [Fact]
    public void AManualName_WinsOverTheApi_AndAnUnknownOneIsIgnored()
    {
        var dx11 = Tech(GraphicsApis.Dx11);
        Assert.Equal("d3d11.dll", ReShadeNaming.Resolve(dx11, false, "D3D11.dll").File);
        Assert.Equal("dxgi.dll", ReShadeNaming.Resolve(dx11, false, "kernel32.dll").File);
        // DXVK games run Direct3D on Vulkan: a manual dxgi.dll is how people still use the proxy there.
        Assert.Equal("dxgi.dll", ReShadeNaming.Resolve(Tech(GraphicsApis.Vulkan), false, "dxgi.dll").File);
    }

    [Fact]
    public void EveryNameTheNamingCanGive_IsAKnownProxyName()
    {
        foreach (var api in new[] { GraphicsApis.Dx8, GraphicsApis.Dx9, GraphicsApis.Dx11, GraphicsApis.Dx12, GraphicsApis.OpenGL })
            Assert.True(AppSettings.IsKnownProxy(ReShadeNaming.ForApi(api)), api.ToString());
    }

    [Fact]
    public void TheInstallOptions_DecideLikeTheNaming()
    {
        var game = new GameInfo { Name = "G", Root = @"C:\G", Tech = Tech(GraphicsApis.Dx9, 32) };
        Assert.Equal("d3d9.dll", ReShadeInstall.TargetFor(game, new InstallOptions { Mode = InstallMode.ReShadeOnly, Proxy = "d3d9.dll" }).File);
        Assert.Equal("ReShade64.dll", ReShadeInstall.TargetFor(game, new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true }).File);
        Assert.True(ReShadeInstall.TargetFor(game, new InstallOptions { Mode = InstallMode.ReShadeOnly, ReShadeVulkan = true }).VulkanLayer);
    }
}
