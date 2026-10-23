using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>Graphics API, bitness, engine and upscaler detection, on synthetic PE files and fake game trees in a temp folder.</summary>
public class DetectTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-detect-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _game;
    private readonly string _appRoot = AppPaths.Root;

    public DetectTests()
    {
        _game = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(_game);
        AppPaths.Root = Path.Combine(_tmp, "data");
        DetectCache.Reset();
    }

    public void Dispose()
    {
        AppPaths.Root = _appRoot;
        DetectCache.Reset();
        if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true);
    }

    private string Put(string rel, byte[]? bytes = null)
    {
        var p = Path.Combine(_game, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, bytes ?? new byte[16]);
        return p;
    }

    private string Pe(string rel, bool plus, string[] imports, string[]? delay = null) => Put(rel, SyntheticPe.Build(plus, imports, delay));

    private GameInfo Scan(Func<string, Version?>? versionOf = null, string name = "Game")
    {
        var g = new GameInfo { Root = _game, Name = name };
        GameInspector.Inspect(g, versionOf ?? (_ => null));
        return g;
    }

    private static Func<string, Version?> Versions(params (string FileName, Version Version)[] known) =>
        path => known.Where(k => Path.GetFileName(path).Equals(k.FileName, StringComparison.OrdinalIgnoreCase)).Select(k => k.Version).FirstOrDefault();

    // ---------- PE parser ----------

    [Fact]
    public void Pe64_ReadsImports()
    {
        var pe = PeReader.Read(new MemoryStream(SyntheticPe.Build(true, ["KERNEL32.dll", "D3D12.dll", "dxgi.dll"])))!;
        Assert.Equal(64, pe.Bits);
        Assert.Equal(["kernel32.dll", "d3d12.dll", "dxgi.dll"], pe.Imports);
        Assert.Empty(pe.DelayImports);
    }

    [Fact]
    public void Pe32_ReadsImportsAndDelayLoads()
    {
        var pe = PeReader.Read(new MemoryStream(SyntheticPe.Build(false, ["d3d9.dll"], ["vulkan-1.dll"])))!;
        Assert.Equal(32, pe.Bits);
        Assert.Equal(["d3d9.dll"], pe.Imports);
        Assert.Equal(["vulkan-1.dll"], pe.DelayImports);
    }

    [Fact]
    public void DelayLoad_WithVirtualAddresses_IsResolved()
    {
        var pe = PeReader.Read(new MemoryStream(SyntheticPe.Build(false, [], ["d3d11.dll"], delayWithVirtualAddresses: true)))!;
        Assert.Equal(["d3d11.dll"], pe.DelayImports);
    }

    [Fact]
    public void Arm64_CountsAs64Bit()
    {
        Assert.Equal(64, PeReader.Read(new MemoryStream(SyntheticPe.Build(true, [], machine: 0xAA64)))!.Bits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(100)]
    public void Pe_CutOffAnywhere_ReturnsNullOrPartial_NeverThrows(int keep)
    {
        var bytes = SyntheticPe.Build(true, ["d3d11.dll"]);
        PeReader.Read(new MemoryStream(bytes[..Math.Min(keep, bytes.Length)]));
    }

    [Fact]
    public void Pe_TableCutOffInsideTheFile_KeepsWhatItCanRead()
    {
        var bytes = SyntheticPe.Build(true, ["d3d11.dll", "d3d12.dll"]);
        var pe = PeReader.Read(new MemoryStream(bytes[..(0x400 + 30)]));
        Assert.True(pe is null || pe.Imports.Count <= 2);
    }

    [Fact]
    public void RealWindowsExes_ParseAs64And32Bit()
    {
        var sys = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var wow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "cmd.exe");
        var pe64 = PeReader.Read(sys)!;
        Assert.Equal(64, pe64.Bits);
        Assert.Contains(pe64.Imports.Concat(pe64.DelayImports), i => i.EndsWith(".dll") && i.Contains("kernel32") || i.StartsWith("api-ms-"));
        if (File.Exists(wow)) Assert.Equal(32, PeReader.Read(wow)!.Bits);
    }

    [Fact]
    public void NotAPe_Missing_And_Locked_FilesReturnNull()
    {
        Assert.Null(PeReader.Read(Put("junk.exe", new byte[2000])));
        Assert.Null(PeReader.Read(Path.Combine(_game, "missing.exe")));
        var locked = Put("locked.exe", SyntheticPe.Build(true, ["d3d11.dll"]));
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Null(PeReader.Read(locked));
        Assert.NotNull(PeReader.Read(locked));
    }

    // ---------- graphics API ----------

    [Theory]
    [InlineData("d3d8.dll", GraphicsApis.Dx8)]
    [InlineData("d3d9.dll", GraphicsApis.Dx9)]
    [InlineData("d3d10.dll", GraphicsApis.Dx10)]
    [InlineData("d3d10_1.dll", GraphicsApis.Dx10)]
    [InlineData("d3d11.dll", GraphicsApis.Dx11)]
    [InlineData("d3d12.dll", GraphicsApis.Dx12)]
    [InlineData("vulkan-1.dll", GraphicsApis.Vulkan)]
    [InlineData("opengl32.dll", GraphicsApis.OpenGL)]
    public void EachApi_IsDetectedFromItsImport(string dll, GraphicsApis expected)
    {
        Pe("Game.exe", true, ["kernel32.dll", dll]);
        var tech = Scan().Tech;
        Assert.Equal(expected, tech.Apis);
        Assert.Equal(expected, tech.PrimaryApi);
        Assert.False(tech.ApisGuessed);
    }

    [Fact]
    public void MultiApi_ReturnsTheFullSet_AndPicksTheBestPrimary()
    {
        Pe("Game.exe", true, ["d3d9.dll", "d3d11.dll", "vulkan-1.dll"]);
        var tech = Scan().Tech;
        Assert.Equal(GraphicsApis.Dx9 | GraphicsApis.Dx11 | GraphicsApis.Vulkan, tech.Apis);
        Assert.Equal(GraphicsApis.Vulkan, tech.PrimaryApi);
    }

    [Theory]
    [InlineData(GraphicsApis.Dx12 | GraphicsApis.Vulkan | GraphicsApis.Dx11, GraphicsApis.Dx12)]
    [InlineData(GraphicsApis.Vulkan | GraphicsApis.Dx11, GraphicsApis.Vulkan)]
    [InlineData(GraphicsApis.Dx11 | GraphicsApis.Dx10 | GraphicsApis.OpenGL, GraphicsApis.Dx11)]
    [InlineData(GraphicsApis.Dx10 | GraphicsApis.OpenGL | GraphicsApis.Dx9, GraphicsApis.Dx10)]
    [InlineData(GraphicsApis.OpenGL | GraphicsApis.Dx9 | GraphicsApis.Dx8, GraphicsApis.OpenGL)]
    [InlineData(GraphicsApis.Dx9 | GraphicsApis.Dx8, GraphicsApis.Dx9)]
    [InlineData(GraphicsApis.None, GraphicsApis.None)]
    public void PrimaryApi_FollowsThePriorityOrder(GraphicsApis set, GraphicsApis expected)
    {
        Assert.Equal(expected, GraphicsApiInfo.Primary(set));
    }

    [Fact]
    public void DxgiAlone_IsADx12Hint()
    {
        Pe("Game.exe", true, ["kernel32.dll", "dxgi.dll"]);
        var tech = Scan().Tech;
        Assert.Equal(GraphicsApis.Dx12, tech.Apis);
        Assert.True(tech.ApisGuessed);
    }

    [Fact]
    public void DxgiWithDx11_DoesNotAddDx12()
    {
        Pe("Game.exe", true, ["dxgi.dll", "d3d11.dll"]);
        Assert.Equal(GraphicsApis.Dx11, Scan().Tech.Apis);
    }

    [Fact]
    public void DelayLoad_IsReadWhenNoDx11PlusImportExists()
    {
        Pe("Game.exe", true, ["kernel32.dll", "d3d9.dll"], ["d3d12.dll"]);
        Assert.Equal(GraphicsApis.Dx9 | GraphicsApis.Dx12, Scan().Tech.Apis);
    }

    [Fact]
    public void DelayLoad_IsIgnoredWhenAnImportIsDx11OrBetter()
    {
        Pe("Game.exe", true, ["d3d11.dll"], ["d3d12.dll", "vulkan-1.dll"]);
        Assert.Equal(GraphicsApis.Dx11, Scan().Tech.Apis);
    }

    [Fact]
    public void Dlls_NextToTheExe_AreScannedWhenTheExeHasNoApi()
    {
        Pe("Game.exe", true, ["kernel32.dll"]);
        Pe("engine.dll", true, ["d3d11.dll"]);
        Pe("msvcp140.dll", true, ["d3d9.dll"]); // a runtime, never the game's own renderer
        Assert.Equal(GraphicsApis.Dx11, Scan().Tech.Apis);
    }

    [Fact]
    public void Dlls_NextToTheExe_AreSkippedWhenTheExeHasAnApi()
    {
        Pe("Game.exe", true, ["d3d9.dll"]);
        Pe("engine.dll", true, ["d3d12.dll"]);
        Assert.Equal(GraphicsApis.Dx9, Scan().Tech.Apis);
    }

    [Fact]
    public void ProxyDlls_NextToTheExe_AreNotMistakenForTheGame()
    {
        Pe("Game.exe", true, ["kernel32.dll"]);
        Pe("d3d9.dll", true, ["d3d12.dll"]); // a ReShade or DXVK proxy
        Assert.Equal(GraphicsApis.None, Scan().Tech.Apis);
    }

    [Fact]
    public void BinFolderExes_AreScannedLast()
    {
        var game = Pe("Game.exe", true, ["kernel32.dll"]);
        Pe(@"Bin64\Game64.exe", true, ["d3d12.dll"]);
        var g = Scan();
        Assert.Equal(game, g.Exes[0]);
        Assert.Equal(GraphicsApis.Dx12, g.Tech.Apis);
    }

    [Fact]
    public void D3D12CoreAnywhere_MeansDx12()
    {
        Pe("Game.exe", true, ["d3d11.dll"]);
        Put(@"D3D12\D3D12Core.dll");
        var tech = Scan().Tech;
        Assert.Equal(GraphicsApis.Dx11 | GraphicsApis.Dx12, tech.Apis);
        Assert.Equal(GraphicsApis.Dx12, tech.PrimaryApi);
        Assert.False(tech.ApisGuessed);
    }

    // ---------- bitness ----------

    [Fact]
    public void Bitness_ComesFromTheChosenExe()
    {
        Pe("Game.exe", false, ["d3d9.dll"]);
        var tech = Scan().Tech;
        Assert.True(tech.Is32Bit);
        Assert.Equal(32, tech.Bits);
    }

    [Fact]
    public void Pe64_Is64Bit()
    {
        Pe("Game.exe", true, ["d3d11.dll"]);
        var tech = Scan().Tech;
        Assert.False(tech.Is32Bit);
        Assert.Equal(64, tech.Bits);
    }

    [Fact]
    public void UnreadableExe_GivesUnknown_NotAnException()
    {
        var exe = Pe("Game.exe", true, ["d3d11.dll"]);
        using var hold = new FileStream(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var tech = Scan().Tech;
        Assert.Equal(0, tech.Bits);
        Assert.Equal(GraphicsApis.None, tech.Apis);
        Assert.Equal("", tech.Label);
    }

    [Fact]
    public void GameFolderWithoutExe_GivesAnEmptyResult()
    {
        Put("readme.txt");
        var tech = Scan().Tech;
        Assert.Equal(GraphicsApis.None, tech.Apis);
        Assert.Equal(GameEngine.Unknown, tech.Engine);
    }

    // ---------- Unity ----------

    [Fact]
    public void Unity_IsFoundByUnityPlayer_GameAssembly_Or_DataFolder()
    {
        Pe("Game.exe", true, ["d3d11.dll"]);
        Put("UnityPlayer.dll");
        Assert.Equal(GameEngine.Unity, Scan().Tech.Engine);
        File.Delete(Path.Combine(_game, "UnityPlayer.dll"));

        Put("GameAssembly.dll");
        Assert.Equal(GameEngine.Unity, Scan().Tech.Engine);
        File.Delete(Path.Combine(_game, "GameAssembly.dll"));

        Directory.CreateDirectory(Path.Combine(_game, "Game_Data"));
        Assert.Equal(GameEngine.Unity, Scan().Tech.Engine);
    }

    [Theory]
    [InlineData(18, GraphicsApis.Dx12)]
    [InlineData(21, GraphicsApis.Vulkan)]
    [InlineData(2, GraphicsApis.Dx11)]
    [InlineData(1, GraphicsApis.Dx9)]
    [InlineData(17, GraphicsApis.OpenGL)]
    public void UnityBootConfig_GfxDeviceType_SetsTheApi(int type, GraphicsApis expected)
    {
        Pe("Game.exe", true, ["d3d11.dll"]);
        Put("UnityPlayer.dll");
        Put(@"Game_Data\boot.config", System.Text.Encoding.ASCII.GetBytes($"wait-for-native-debugger=0\ngfx-device-type={type}\nplayer-connection-debug=0\n"));
        var tech = Scan().Tech;
        Assert.Equal(expected, tech.Apis);
        Assert.False(tech.ApisGuessed);
    }

    [Fact]
    public void UnityBootConfig_WithoutAnApi_FallsBackToTheExe()
    {
        Pe("Game.exe", true, ["d3d12.dll"]);
        Put("UnityPlayer.dll");
        Put(@"Game_Data\boot.config", System.Text.Encoding.ASCII.GetBytes("gfx-device-type=-1\n"));
        Assert.Equal(GraphicsApis.Dx12, Scan().Tech.Apis);
    }

    [Fact]
    public void Unity_OpenGlOnly_MeansDx11()
    {
        Pe("Game.exe", true, ["opengl32.dll"]);
        Put("UnityPlayer.dll");
        var tech = Scan().Tech;
        Assert.Equal(GraphicsApis.Dx11, tech.Apis);
        Assert.True(tech.ApisGuessed);
    }

    [Fact]
    public void NonEngineGame_KeepsOpenGl()
    {
        Pe("Game.exe", false, ["opengl32.dll"]);
        Assert.Equal(GraphicsApis.OpenGL, Scan().Tech.Apis);
    }

    [Fact]
    public void Unity_WithoutAnyApiSignal_DefaultsToDx11_AsAGuess()
    {
        Pe("Game.exe", true, ["kernel32.dll"]);
        Put("UnityPlayer.dll");
        var tech = Scan().Tech;
        Assert.Equal(GraphicsApis.Dx11, tech.Apis);
        Assert.True(tech.ApisGuessed);
    }

    // ---------- Unreal ----------

    private string UnrealTree(bool plus = true, params string[] imports)
    {
        Pe(@"Game.exe", plus, ["kernel32.dll"]);
        return Pe(@"MyGame\Binaries\Win64\MyGame-Win64-Shipping.exe", plus, imports.Length == 0 ? ["d3d12.dll"] : imports);
    }

    [Fact]
    public void Unreal_ShippingExe_GivesProjectAndModFolder()
    {
        var shipping = UnrealTree();
        var g = Scan();
        Assert.Equal(shipping, g.Exes[0]);
        Assert.Equal(GameEngine.Unreal, g.Tech.Engine);
        Assert.Equal("MyGame", g.Tech.UeProject);
        Assert.Equal(Path.GetDirectoryName(shipping), g.Tech.ModFolder);
    }

    [Fact]
    public void Unreal_WinGdk_IsAnUnrealBinariesFolderToo()
    {
        var exe = Pe(@"MyGame\Binaries\WinGDK\MyGame-WinGDK-Shipping.exe", true, ["d3d12.dll"]);
        var g = Scan();
        Assert.Equal(GameEngine.Unreal, g.Tech.Engine);
        Assert.Equal(Path.GetDirectoryName(exe), g.Tech.ModFolder);
    }

    [Fact]
    public void Unreal_PrefersTheFolderWithAShippingExe_AndSkipsTheEngineFolder()
    {
        Pe(@"Engine\Binaries\Win64\UnrealEditor.exe", true, ["d3d12.dll"]);
        Pe(@"Other\Binaries\Win64\Tool.exe", true, ["d3d11.dll"]);
        var shipping = Pe(@"MyGame\Binaries\Win64\MyGame-Win64-Shipping.exe", true, ["d3d12.dll"]);
        var tech = Scan().Tech;
        Assert.Equal("MyGame", tech.UeProject);
        Assert.Equal(Path.GetDirectoryName(shipping), tech.ModFolder);
    }

    [Fact]
    public void UnrealVersion_ComesFromCrashReportClient()
    {
        UnrealTree();
        Put(@"Engine\Binaries\Win64\CrashReportClient.exe");
        var tech = Scan(Versions(("CrashReportClient.exe", new Version(5, 4, 3, 0)))).Tech;
        Assert.Equal("5.4.3", tech.EngineVersion);
        Assert.Equal(5, tech.UeMajor);
        Assert.Equal("Unreal 5.4", tech.EngineLabel);
    }

    [Fact]
    public void UnrealVersion_FallsBackToBuildVersionJson()
    {
        UnrealTree();
        Put(@"Engine\Binaries\Win64\CrashReportClient.exe");
        Put(@"Engine\Build\Build.version", System.Text.Encoding.UTF8.GetBytes("""{ "MajorVersion": 4, "MinorVersion": 27, "PatchVersion": 2, "Changelist": 1 }"""));
        // The crash client's version is not an engine version (major 1), so it is passed over.
        var tech = Scan(Versions(("CrashReportClient.exe", new Version(1, 0, 0, 0)))).Tech;
        Assert.Equal("4.27.2", tech.EngineVersion);
    }

    [Fact]
    public void UnrealVersion_FallsBackToTheShippingExe()
    {
        UnrealTree();
        var tech = Scan(Versions(("MyGame-Win64-Shipping.exe", new Version(5, 1, 1, 0)))).Tech;
        Assert.Equal("5.1.1", tech.EngineVersion);
    }

    [Fact]
    public void UnrealVersion_OutsideFourToSix_IsIgnored()
    {
        UnrealTree();
        Put(@"Engine\Build\Build.version", System.Text.Encoding.UTF8.GetBytes("""{ "MajorVersion": 3 }"""));
        var tech = Scan(Versions(("MyGame-Win64-Shipping.exe", new Version(1, 2, 3, 4)))).Tech;
        Assert.Null(tech.EngineVersion);
        Assert.Equal("Unreal", tech.EngineLabel);
    }

    [Fact]
    public void UnrealVersion_BrokenBuildVersion_IsIgnored()
    {
        UnrealTree();
        Put(@"Engine\Build\Build.version", System.Text.Encoding.UTF8.GetBytes("{ not json"));
        Assert.Null(Scan().Tech.EngineVersion);
    }

    [Fact]
    public void Unreal5_NeverReportsDx11Only()
    {
        UnrealTree(true, "d3d11.dll");
        Put(@"Engine\Binaries\Win64\CrashReportClient.exe");
        var tech = Scan(Versions(("CrashReportClient.exe", new Version(5, 3, 0, 0)))).Tech;
        Assert.Equal(GraphicsApis.Dx12, tech.Apis);
        Assert.True(tech.ApisGuessed);
    }

    [Fact]
    public void Unreal5_WithDx11AndDx12_KeepsBoth()
    {
        UnrealTree(true, "d3d11.dll", "d3d12.dll");
        Put(@"Engine\Binaries\Win64\CrashReportClient.exe");
        var tech = Scan(Versions(("CrashReportClient.exe", new Version(5, 3, 0, 0)))).Tech;
        Assert.Equal(GraphicsApis.Dx11 | GraphicsApis.Dx12, tech.Apis);
    }

    [Fact]
    public void Unreal4_Dx11Only_StaysDx11()
    {
        UnrealTree(true, "d3d11.dll");
        Put(@"Engine\Binaries\Win64\CrashReportClient.exe");
        var tech = Scan(Versions(("CrashReportClient.exe", new Version(4, 27, 2, 0)))).Tech;
        Assert.Equal(GraphicsApis.Dx11, tech.Apis);
        Assert.False(tech.ApisGuessed);
    }

    [Fact]
    public void Unreal_OpenGlOnly_MeansDx11()
    {
        UnrealTree(true, "opengl32.dll");
        var tech = Scan(Versions(("MyGame-Win64-Shipping.exe", new Version(4, 26, 0, 0)))).Tech;
        Assert.Equal(GraphicsApis.Dx11, tech.Apis);
        Assert.True(tech.ApisGuessed);
    }

    [Fact]
    public void Unreal_DelayLoadedDx12_IsNotPromotedOverDx11()
    {
        Pe(@"MyGame\Binaries\Win64\MyGame-Win64-Shipping.exe", true, ["d3d11.dll"], ["d3d12.dll"]);
        Put(@"Engine\Binaries\Win64\CrashReportClient.exe");
        var tech = Scan(Versions(("CrashReportClient.exe", new Version(4, 25, 0, 0)))).Tech;
        Assert.Equal(GraphicsApis.Dx11, tech.Apis);
    }

    [Fact]
    public void UnrealLegacy_IsFoundByUpkAndUFiles()
    {
        Pe(@"Binaries\Win32\Game.exe", false, ["d3d9.dll"]);
        Put(@"Content\Maps\Level.upk");
        var g = Scan();
        Assert.Equal(GameEngine.UnrealLegacy, g.Tech.Engine);
        Assert.Equal("Unreal legacy", g.Tech.EngineLabel);
        Assert.Equal(GraphicsApis.Dx9, g.Tech.Apis);
        Assert.True(g.Tech.Is32Bit);
    }

    [Fact]
    public void UnrealLegacy_IsFoundByBaseEngineIni_WhenThereIsNoShippingExe()
    {
        Pe(@"Binaries\Win64\Game.exe", true, ["d3d11.dll"]);
        Put(@"Engine\Config\BaseEngine.ini");
        Assert.Equal(GameEngine.UnrealLegacy, Scan().Tech.Engine);
    }

    [Fact]
    public void BaseEngineIni_WithAShippingExe_IsStillModernUnreal()
    {
        UnrealTree();
        Put(@"Engine\Config\BaseEngine.ini");
        Assert.Equal(GameEngine.Unreal, Scan().Tech.Engine);
    }

    [Fact]
    public void UnrealLegacy_WithoutAnyApiSignal_GuessesDx9()
    {
        Pe("Game.exe", false, ["kernel32.dll"]);
        Put("Package.u");
        var tech = Scan().Tech;
        Assert.Equal(GraphicsApis.Dx9, tech.Apis);
        Assert.True(tech.ApisGuessed);
    }

    // ---------- other engines ----------

    [Fact]
    public void REEngine_IsFoundByItsPak()
    {
        Pe("Game.exe", true, ["d3d11.dll"]);
        Put("re_chunk_000.pak");
        Put("D3D12Core.dll");
        var tech = Scan().Tech;
        Assert.Equal(GameEngine.REEngine, tech.Engine);
        Assert.Equal("RE Engine", tech.EngineLabel);
        Assert.Equal(GraphicsApis.Dx12, tech.PrimaryApi);
    }

    [Fact]
    public void UnknownEngine_UsesTheExeFolderAsModFolder()
    {
        var exe = Pe(@"bin\Game.exe", true, ["d3d11.dll"]);
        var tech = Scan().Tech;
        Assert.Equal(GameEngine.Unknown, tech.Engine);
        Assert.Equal(Path.GetDirectoryName(exe), tech.ModFolder);
    }

    // ---------- upscaler signals ----------

    [Theory]
    [InlineData("amd_fidelityfx_dx12.dll", UpscalerSignals.Fsr)]
    [InlineData("amd_fidelityfx_upscaler_dx12.dll", UpscalerSignals.Fsr)]
    [InlineData("ffx_fsr2_api_x64.dll", UpscalerSignals.Fsr)]
    [InlineData("amd_fidelityfx_framegeneration_dx12.dll", UpscalerSignals.Fsr | UpscalerSignals.FsrFrameGen)]
    [InlineData("ffx_opticalflow_x64.dll", UpscalerSignals.Fsr | UpscalerSignals.FsrFrameGen)]
    [InlineData("libxess.dll", UpscalerSignals.XeSS)]
    [InlineData("libxess_dx11.dll", UpscalerSignals.XeSS)]
    [InlineData("libxess_fg.dll", UpscalerSignals.XeSSFrameGen)]
    [InlineData("kernel32.dll", UpscalerSignals.None)]
    [InlineData("ffx.txt", UpscalerSignals.None)]
    public void UpscalerFiles_AreClassifiedByName(string file, UpscalerSignals expected)
    {
        Assert.Equal(expected, UpscalerFiles.OfFile(file));
    }

    [Fact]
    public void Upscaler_DllsAndPluginFolders_AreCollected()
    {
        Pe("Game.exe", true, ["d3d12.dll"]);
        Put(@"bin\amd_fidelityfx_dx12.dll");
        Put(@"bin\libxess.dll");
        Put(@"Engine\Plugins\Runtime\Nvidia\DLSS\Binaries\ThirdParty\Win64\nvngx_dlss.dll");
        Directory.CreateDirectory(Path.Combine(_game, @"Engine\Plugins\Marketplace\XeSS"));
        Directory.CreateDirectory(Path.Combine(_game, @"Engine\Plugins\Runtime\FSR3"));
        var g = Scan();
        Assert.Equal(UpscalerSignals.Fsr | UpscalerSignals.XeSS | UpscalerSignals.DlssPlugin | UpscalerSignals.XessPlugin | UpscalerSignals.FsrPlugin,
            g.Tech.Upscalers);
        Assert.Single(g.Dlss);
    }

    [Fact]
    public void PluginNamedFolders_OutsideAPluginsTree_AreIgnored()
    {
        Pe("Game.exe", true, ["d3d12.dll"]);
        Directory.CreateDirectory(Path.Combine(_game, @"Data\DLSS"));
        Assert.Equal(UpscalerSignals.None, Scan().Tech.Upscalers);
    }

    [Fact]
    public void DlssG_StaysInTheExistingFeatureFlags()
    {
        Pe("Game.exe", true, ["d3d12.dll"]);
        Put("sl.interposer.dll");
        Put("sl.dlss_g.dll");
        var g = Scan();
        Assert.True(g.Features.HasFlag(GameFeatures.FG));
        Assert.True(g.Features.HasFlag(GameFeatures.Streamline));
    }

    // ---------- cache ----------

    [Fact]
    public void Rescan_UsesTheCache_UntilTheExeChanges()
    {
        var exe = Pe("Game.exe", true, ["d3d11.dll"]);
        var stamp = File.GetLastWriteTimeUtc(exe);
        Assert.Equal(GraphicsApis.Dx11, Scan().Tech.Apis);

        // Same write time: the cached answer stands, so the new imports are not read.
        File.WriteAllBytes(exe, SyntheticPe.Build(true, ["d3d12.dll"]));
        File.SetLastWriteTimeUtc(exe, stamp);
        Assert.Equal(GraphicsApis.Dx11, Scan().Tech.Apis);

        File.SetLastWriteTimeUtc(exe, stamp.AddMinutes(5));
        Assert.Equal(GraphicsApis.Dx12, Scan().Tech.Apis);
    }

    [Fact]
    public void Cache_SurvivesARestart()
    {
        var exe = Pe("Game.exe", false, ["d3d9.dll"]);
        var stamp = File.GetLastWriteTimeUtc(exe);
        Scan();
        DetectCache.Save();
        Assert.True(File.Exists(AppPaths.DetectCacheFile));

        DetectCache.Reset();
        File.WriteAllBytes(exe, SyntheticPe.Build(true, ["d3d12.dll"]));
        File.SetLastWriteTimeUtc(exe, stamp);
        var tech = Scan().Tech;
        Assert.Equal(GraphicsApis.Dx9, tech.Apis);
        Assert.Equal(32, tech.Bits);
    }

    [Fact]
    public void Cache_DropsExesThatAreGone_WhenSaved()
    {
        var exe = Pe("Game.exe", true, ["d3d11.dll"]);
        Scan();
        File.Delete(exe);
        DetectCache.Save();
        Assert.DoesNotContain("Game.exe", File.ReadAllText(AppPaths.DetectCacheFile), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cache_IgnoresACorruptFile()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.DetectCacheFile, "{ broken");
        Pe("Game.exe", true, ["d3d11.dll"]);
        Assert.Equal(GraphicsApis.Dx11, Scan().Tech.Apis);
    }

    [Fact]
    public void GameInfo_RoundTripsItsTechThroughGamesJson()
    {
        Pe(@"MyGame\Binaries\Win64\MyGame-Win64-Shipping.exe", true, ["d3d12.dll"]);
        Put(@"bin\libxess.dll");
        var g = Scan(Versions(("MyGame-Win64-Shipping.exe", new Version(5, 4, 3, 0))));
        var json = JsonSerializer.Serialize(new List<GameInfo> { g }, JsonCtx.Default.ListGameInfo);
        var back = JsonSerializer.Deserialize(json, JsonCtx.Default.ListGameInfo)![0].Tech;
        Assert.Equal(GraphicsApis.Dx12, back.Apis);
        Assert.Equal(64, back.Bits);
        Assert.Equal(GameEngine.Unreal, back.Engine);
        Assert.Equal("5.4.3", back.EngineVersion);
        Assert.Equal("MyGame", back.UeProject);
        Assert.Equal(UpscalerSignals.XeSS, back.Upscalers);
    }

    [Fact]
    public void OldGamesJson_WithoutTech_StillLoads()
    {
        var back = JsonSerializer.Deserialize("""[{ "Id": "c:\\g", "Name": "G", "Root": "C:\\g" }]""", JsonCtx.Default.ListGameInfo)!;
        Assert.Equal("", back[0].Tech.Label);
    }

    // ---------- badge text ----------

    [Fact]
    public void Label_ShowsApiBitnessAndEngine()
    {
        var tech = new GameTech { Apis = GraphicsApis.Dx12 | GraphicsApis.Dx11, Bits = 64, Engine = GameEngine.Unreal, EngineVersion = "5.4.3" };
        Assert.Equal("DX12 · 64-bit · Unreal 5.4", tech.Label);
    }

    [Theory]
    [InlineData(GraphicsApis.Vulkan, 32, GameEngine.Unity, null, "Vulkan · 32-bit · Unity")]
    [InlineData(GraphicsApis.None, 64, GameEngine.Unknown, null, "64-bit")]
    [InlineData(GraphicsApis.Dx9, 0, GameEngine.UnrealLegacy, null, "DX9 · Unreal legacy")]
    [InlineData(GraphicsApis.None, 0, GameEngine.Unknown, null, "")]
    [InlineData(GraphicsApis.OpenGL, 64, GameEngine.Unreal, null, "OpenGL · 64-bit · Unreal")]
    public void Label_LeavesOutWhatIsUnknown(GraphicsApis apis, int bits, GameEngine engine, string? version, string expected)
    {
        Assert.Equal(expected, new GameTech { Apis = apis, Bits = bits, Engine = engine, EngineVersion = version }.Label);
    }

    [Fact]
    public void Details_ListsEveryApi_AndSaysWhenGuessed()
    {
        var tech = new GameTech { Apis = GraphicsApis.Dx11 | GraphicsApis.Dx12, ApisGuessed = true, UeProject = "MyGame", ModFolder = @"C:\g\MyGame\Binaries\Win64" };
        Assert.Contains("DX12, DX11", tech.Details);
        Assert.Contains("guessed", tech.Details);
        Assert.Contains("MyGame", tech.Details);
    }
}
