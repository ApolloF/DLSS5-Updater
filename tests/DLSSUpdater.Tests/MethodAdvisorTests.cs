using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// The decision table of docs/research/methods-2026-10.md section 8, row by row (the name of each test says which), plus the
/// pre-checks (anti-cheat, Store install, GPU). Games are built in memory; nothing touches a disk or a GPU.
/// </summary>
public sealed class MethodAdvisorTests
{
    private static readonly GpuInfo Rtx50 = new(GpuVendor.Nvidia, RtxTier.Rtx50, "GeForce RTX 5080");
    private static readonly GpuInfo Rtx40 = new(GpuVendor.Nvidia, RtxTier.Rtx40, "GeForce RTX 4070");
    private static readonly GpuInfo Rtx30 = new(GpuVendor.Nvidia, RtxTier.Rtx30, "GeForce RTX 3060");

    private static GameInfo Game(GraphicsApis apis = GraphicsApis.Dx12, int bits = 64, GameEngine engine = GameEngine.Unknown,
        UpscalerSignals up = UpscalerSignals.None, bool dlss = false, bool fg = false, string? antiCheat = null, string name = "Test Game")
    {
        var g = new GameInfo { Name = name, Root = @"C:\Games\Test", AntiCheat = antiCheat, Exes = [@"C:\Games\Test\game.exe"] };
        g.Tech = new GameTech { Apis = apis, Bits = bits, Engine = engine, EngineVersion = engine == GameEngine.Unreal ? "5.3.2" : null, Upscalers = up };
        if (dlss) g.Dlss.Add(new DlssDll { Name = "nvngx_dlss.dll", Path = @"C:\Games\Test\nvngx_dlss.dll" });
        if (fg) g.Dlss.Add(new DlssDll { Name = "nvngx_dlssg.dll", Path = @"C:\Games\Test\nvngx_dlssg.dll" });
        return g;
    }

    private static AdvisorInput Input(GameInfo game, GpuInfo? gpu = null) => new(game, gpu ?? Rtx40);

    private static MethodAdvice Advise(GameInfo game, GpuInfo? gpu = null) => MethodAdvisor.Advise(Input(game, gpu));

    private static Method[] Order(MethodAdvice a) => a.Methods.Select(m => m.Method).ToArray();

    private static MethodOption Option(MethodAdvice a, Method m) => a.Methods.Single(o => o.Method == m);

    // ---------- row 1-4: the game has DLSS ----------

    [Fact]
    public void Row1_DlssOnDx12_UpdatesFirst_ThenNr_ThenOptiScalerNr_ThenPlainOptiScaler()
    {
        var a = Advise(Game(dlss: true));

        Assert.Equal([1], a.Rows);
        Assert.Equal([Method.DlssUpdate, Method.NeuralAddon, Method.OptiScalerNr, Method.OptiScaler], Order(a));
        Assert.Equal(Method.DlssUpdate, a.Top!.Method);
        Assert.Contains("cheapest and safest", Option(a, Method.DlssUpdate).Reason);
        Assert.Equal([ComponentRegistry.Dlss], Option(a, Method.DlssUpdate).Components);
    }

    [Fact]
    public void Row2_DlssOnDx11_ListsTheBridgeAsComing_AndFlagsTheBridgeRequirementUnverified()
    {
        var a = Advise(Game(GraphicsApis.Dx11, dlss: true));

        Assert.Equal([2], a.Rows);
        Assert.Equal(Method.DlssUpdate, a.Top!.Method);
        Assert.Equal(Availability.Coming, Option(a, Method.Dx11Bridge).Availability);
        var nr = Option(a, Method.NeuralAddon);
        Assert.True(nr.Unverified);
        Assert.Contains("Unverified", nr.Reason);
        Assert.Contains("never combine it with Feeder", Option(a, Method.Dx11Bridge).Reason);
    }

    [Fact]
    public void Row3_DlssOnVulkan_NoReShadeAddon_OptiScalerNrInstead()
    {
        var a = Advise(Game(GraphicsApis.Vulkan, dlss: true));

        Assert.Equal([3], a.Rows);
        var addon = Option(a, Method.NeuralAddon);
        Assert.Equal(Availability.NotPossible, addon.Availability);
        Assert.Contains("registered layer", addon.Reason);
        Assert.Equal([Method.DlssUpdate, Method.OptiScalerNr], Order(a).Take(2));
        Assert.DoesNotContain(a.Methods, m => m.Method == Method.Feeder);
    }

    [Fact]
    public void Row4_DlssFrameGeneration_AddsMfgUnlockAfterTheUpdate_AndOptiScalerFgInputs()
    {
        var a = Advise(Game(dlss: true, fg: true));

        Assert.Contains(4, a.Rows);
        Assert.Equal([Method.DlssUpdate, Method.MfgUnlock], Order(a).Take(2));
        Assert.Equal([ComponentRegistry.MfgUnlock], Option(a, Method.MfgUnlock).Components);
        Assert.Contains("Streamline 2+", Option(a, Method.OptiScaler).Reason);
        Assert.Single(a.Methods, m => m.Method == Method.OptiScaler);
    }

    [Fact]
    public void Row4_OnRtx50_MfgUnlockIsNotNeeded()
    {
        var a = Advise(Game(dlss: true, fg: true), Rtx50);

        var mfg = Option(a, Method.MfgUnlock);
        Assert.Equal(Availability.NotPossible, mfg.Availability);
        Assert.Contains("RTX 50", mfg.Reason);
        Assert.Equal(Method.MfgUnlock, a.Unavailable.First().Method);
    }

    // ---------- row 5-6: no DLSS on DX11/12 ----------

    [Fact]
    public void Row5_NoUpscaler64BitDx12_FeederWithDfc_AioComing_SmoothMotion()
    {
        var a = Advise(Game());

        Assert.Equal([5], a.Rows);
        Assert.Equal(Method.Feeder, a.Top!.Method);
        Assert.Equal([Method.Feeder, Method.SmoothMotion, Method.AioAddon], Order(a));
        var feeder = Option(a, Method.Feeder);
        Assert.Equal(Availability.NeedsSetup, feeder.Availability);
        Assert.Equal([ComponentRegistry.ReShade, ComponentRegistry.DlssNr, ComponentRegistry.Dfc, ComponentRegistry.Feeder], feeder.Components);
        Assert.Equal(InstallMode.ReShadeOnly, feeder.Mode);
        Assert.Contains("DLAA only", feeder.Reason);
        Assert.Contains(feeder.Needs, n => n.Contains("nvngx_dlssnr.dll"));
        Assert.Contains(feeder.Needs, n => n.Contains("Deep Fried Chicken"));
        Assert.Contains(feeder.Needs, n => n.Contains("motion-vector"));
    }

    [Fact]
    public void Row5_WithEverythingImported_FeederIsAvailable()
    {
        var input = Input(Game()) with
        {
            Imported = new HashSet<string> { ComponentRegistry.DlssNr, ComponentRegistry.Dfc },
            MotionProviderPresent = true,
        };

        var feeder = Option(MethodAdvisor.Advise(input), Method.Feeder);

        Assert.Equal(Availability.Available, feeder.Availability);
        Assert.Empty(feeder.Needs);
    }

    [Fact]
    public void Row5_DfcTwoPointX_StillNeedsAnImport()
    {
        var input = Input(Game()) with
        {
            Imported = new HashSet<string> { ComponentRegistry.DlssNr, ComponentRegistry.Dfc },
            MotionProviderPresent = true,
            DfcRunsWithoutDlss = false,
        };

        var feeder = Option(MethodAdvisor.Advise(input), Method.Feeder);

        Assert.Equal(Availability.NeedsSetup, feeder.Availability);
        Assert.Contains(feeder.Needs, n => n.Contains("2.x needs the game's own DLSS"));
    }

    [Fact]
    public void Row5_UnknownGraphicsApi_AssumesDx12AndSaysItIsUnverified()
    {
        var a = Advise(Game(GraphicsApis.None));

        Assert.Equal([5], a.Rows);
        Assert.True(Option(a, Method.Feeder).Unverified);
        Assert.Contains("wasn't detected", Option(a, Method.Feeder).Reason);
    }

    [Fact]
    public void Row6_OptiScalerListShowsInputs_OptiScalerFirst_FeederOnlyAsAnExtra()
    {
        var input = Input(Game()) with { OptiCompat = new CatalogOptiGame { Name = "Test Game", Inputs = ["FSR3", "XeSS"], Notes = "Use -dx12" } };

        var a = MethodAdvisor.Advise(input);

        Assert.Equal([6], a.Rows);
        Assert.Equal([Method.OptiScaler, Method.Feeder], Order(a));
        Assert.Equal(Method.OptiScaler, a.Top!.Method);
        Assert.Contains("FSR3, XeSS", Option(a, Method.OptiScaler).Reason);
        Assert.Contains("Wiki note: Use -dx12", Option(a, Method.OptiScaler).Reason);
    }

    [Fact]
    public void Row6_FsrDllInTheGame_AlsoMeansOptiScaler()
    {
        var a = Advise(Game(up: UpscalerSignals.Fsr));

        Assert.Equal([6], a.Rows);
        Assert.Equal(Method.OptiScaler, a.Top!.Method);
        Assert.Contains("ships FSR or XeSS", Option(a, Method.OptiScaler).Reason);
    }

    [Fact]
    public void Row6_OptiScalerListSaysNotWorking_OptiScalerIsNotPossible()
    {
        var input = Input(Game()) with { OptiCompat = new CatalogOptiGame { Name = "Test Game", Compat = "not-working", Inputs = ["DLSS"], Notes = "EAC" } };

        var a = MethodAdvisor.Advise(input);

        Assert.DoesNotContain(a.Rows, r => r == 6);
        Assert.Equal(Method.Feeder, a.Top!.Method);
    }

    // ---------- row 7: Unreal ----------

    [Fact]
    public void Row7_UnrealWithDlssPlugin_FollowsRow1()
    {
        var a = Advise(Game(engine: GameEngine.Unreal, up: UpscalerSignals.DlssPlugin));

        Assert.Equal([7, 1], a.Rows);
        Assert.Equal(Method.DlssUpdate, a.Top!.Method);
        Assert.True(Option(a, Method.DlssUpdate).Unverified);
        Assert.Contains("plugin", Option(a, Method.DlssUpdate).Reason);
    }

    [Fact]
    public void Row7_UnrealWithXessPluginOnly_OptiScalerWithXessInput()
    {
        var a = Advise(Game(engine: GameEngine.Unreal, up: UpscalerSignals.XessPlugin));

        Assert.Equal([7], a.Rows);
        Assert.Equal([Method.OptiScaler], Order(a));
        Assert.Contains("XeSS", Option(a, Method.OptiScaler).Reason);
    }

    [Fact]
    public void Row7_UnrealDx11OnTheLumaList_LumaFirst_ThenOptiScaler_ThenFeeder()
    {
        var input = Input(Game(GraphicsApis.Dx11, engine: GameEngine.Unreal)) with { OptiCompat = new CatalogOptiGame { Name = "Test Game", List = "lumaUe", Inputs = ["DLSS"] } };

        var a = MethodAdvisor.Advise(input);

        Assert.Contains(7, a.Rows);
        Assert.Equal(Availability.Coming, Option(a, Method.Luma).Availability);
        Assert.Equal(Method.OptiScaler, Order(a)[0]);
        Assert.Contains(Method.Feeder, Order(a));
    }

    [Fact]
    public void Row7_UnrealWithoutAnyUpscaler_Feeder()
    {
        var a = Advise(Game(engine: GameEngine.Unreal));

        Assert.Equal([7], a.Rows);
        Assert.Equal(Method.Feeder, a.Top!.Method);
    }

    // ---------- row 8: RE Engine ----------

    [Fact]
    public void Row8_ReEngineWithDlss_NeedsReFrameworkFirst()
    {
        var a = Advise(Game(engine: GameEngine.REEngine, dlss: true));

        Assert.Equal([8], a.Rows);
        Assert.Equal(Method.DlssUpdate, a.Top!.Method);
        Assert.Contains(Option(a, Method.NeuralAddon).Needs, n => n.Contains("REFramework"));
    }

    [Fact]
    public void Row8_ReEngineWithoutDlss_UpscalerModComing_ThenOptiScaler()
    {
        var a = Advise(Game(engine: GameEngine.REEngine));

        Assert.Equal([8], a.Rows);
        Assert.Equal(Method.OptiScaler, a.Top!.Method);
        Assert.Equal(Availability.Coming, Option(a, Method.UpscalerMod).Availability);
        Assert.Contains("PDUpscaler", Option(a, Method.UpscalerMod).Reason);
    }

    // ---------- row 9-14: no DLSS, other shapes ----------

    [Fact]
    public void Row9_UnityDx11_FeederAndSmoothMotion()
    {
        var a = Advise(Game(GraphicsApis.Dx11, engine: GameEngine.Unity));

        Assert.Equal([9], a.Rows);
        Assert.Equal([Method.Feeder, Method.SmoothMotion], Order(a));
        Assert.Contains("Fell & Sell", Option(a, Method.Feeder).Reason);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(64, false)]
    public void Row10_Dx9_FeederThroughDgVoodoo_LumaComing(int bits, bool beta)
    {
        var a = Advise(Game(GraphicsApis.Dx9, bits));

        Assert.Equal([10], a.Rows);
        var feeder = Option(a, Method.Feeder);
        Assert.Contains(feeder.Needs, n => n.Contains("dgVoodoo2"));
        Assert.Equal(beta, feeder.Reason.Contains("beta"));
        Assert.Equal(Availability.Coming, Option(a, Method.Luma).Availability);
    }

    [Fact]
    public void Row11_32BitDx11_FeederWithHost64()
    {
        var a = Advise(Game(GraphicsApis.Dx11, bits: 32));

        Assert.Equal([11], a.Rows);
        var feeder = Option(a, Method.Feeder);
        Assert.Contains("host64", feeder.Reason);
        Assert.Contains(feeder.Needs, n => n.Contains("host64") && n.Contains("64-bit ReShade"));
    }

    [Fact]
    public void Row12_OpenGl_Feeder()
    {
        var a = Advise(Game(GraphicsApis.OpenGL));

        Assert.Equal([12], a.Rows);
        Assert.Contains("NVIDIA GPU", Option(a, Method.Feeder).Reason);
    }

    [Fact]
    public void Row13_VulkanWithoutDlss_FeederNeedsTheLayer_SmoothMotionOffAdvice()
    {
        var a = Advise(Game(GraphicsApis.Vulkan));

        Assert.Equal([13], a.Rows);
        var feeder = Option(a, Method.Feeder);
        Assert.Contains(feeder.Needs, n => n.Contains("Vulkan layer"));
        Assert.Contains("Smooth Motion off", feeder.Reason);
    }

    [Fact]
    public void Row14_Dx10_FeederIsLowConfidenceAndUnverified()
    {
        var a = Advise(Game(GraphicsApis.Dx10));

        Assert.Equal([14], a.Rows);
        var feeder = Option(a, Method.Feeder);
        Assert.True(feeder.Unverified);
        Assert.Contains("Unverified", feeder.Reason);
        Assert.Contains("low confidence", feeder.Reason);
    }

    // ---------- row 15-18: additive rules ----------

    [Fact]
    public void Row15_GameOnTheRenoDxList_OffersTheHdrModAsComing()
    {
        var input = Input(Game(dlss: true)) with { RenoDxMod = new CatalogMod { Id = "x", Name = "Hades" } };

        var a = MethodAdvisor.Advise(input);

        Assert.Equal([1, 15], a.Rows);
        Assert.Equal(Availability.Coming, Option(a, Method.RenoDxHdr).Availability);
        Assert.Contains("Hades", Option(a, Method.RenoDxHdr).Reason);
    }

    [Fact]
    public void Row15_ABlockedRenoDxEntry_IsNotOffered()
    {
        var input = Input(Game(dlss: true)) with { RenoDxMod = new CatalogMod { Id = "x", Name = "Hades", Blocked = true } };

        Assert.DoesNotContain(15, MethodAdvisor.Advise(input).Rows);
    }

    [Theory]
    [InlineData("lumaUe", "")]
    [InlineData("upscalerMods", "Requires Luma Framework mod")]
    public void Row16_GamesOnOptiScalersLumaLists_GetALumaPlaceholder(string list, string notes)
    {
        var input = Input(Game(GraphicsApis.Dx11)) with { OptiCompat = new CatalogOptiGame { Name = "Test Game", List = list, Notes = notes, Inputs = ["DLSS"] } };

        var a = MethodAdvisor.Advise(input);

        Assert.Contains(16, a.Rows);
        Assert.Equal(Availability.Coming, Option(a, Method.Luma).Availability);
    }

    [Fact]
    public void Row17_HudfixIncompatible_NoOptiFgAdvice()
    {
        var input = Input(Game(dlss: true)) with { HudfixIncompatible = true };

        var a = MethodAdvisor.Advise(input);

        Assert.Contains(17, a.Rows);
        Assert.All(a.Methods.Where(m => m.Method is Method.OptiScaler or Method.OptiScalerNr), m => Assert.Contains("HUDfix-incompatible", m.Reason));
    }

    [Fact]
    public void Row17_WithoutOptiScalerNothingChanges()
    {
        var input = Input(Game(GraphicsApis.Dx9)) with { HudfixIncompatible = true };

        Assert.DoesNotContain(17, MethodAdvisor.Advise(input).Rows);
    }

    [Fact]
    public void Row18_ABridgeInTheGame_RefusesEveryNeuralMethod_WithTheOneConsumerReason()
    {
        var input = Input(Game(dlss: true)) with { InstalledRoutes = [NrAddon.Bridge] };

        var a = MethodAdvisor.Advise(input);

        Assert.Contains(18, a.Rows);
        foreach (var m in new[] { Method.NeuralAddon, Method.OptiScalerNr })
        {
            var o = Option(a, m);
            Assert.Equal(Availability.NotPossible, o.Availability);
            Assert.Contains("can't be in the same game", o.Reason);
            Assert.Contains("DX11 bridge", o.Reason);
        }
        Assert.Equal(Availability.Available, Option(a, Method.DlssUpdate).Availability);
    }

    [Fact]
    public void Row18_FeederNextToABridge_IsRefused()
    {
        var input = Input(Game()) with { InstalledRoutes = [NrAddon.Bridge] };

        var feeder = Option(MethodAdvisor.Advise(input), Method.Feeder);

        Assert.Equal(Availability.NotPossible, feeder.Availability);
        Assert.False(feeder.Selectable);
    }

    [Fact]
    public void Row18_AnAddonOfOurOwn_IsReplacedNotRefused()
    {
        var input = Input(Game(dlss: true)) with { InstalledRoutes = [NrAddon.RenoDx] };

        var a = MethodAdvisor.Advise(input);

        Assert.Contains(18, a.Rows);
        Assert.NotEqual(Availability.NotPossible, Option(a, Method.NeuralAddon).Availability);
    }

    // ---------- row 19 and the GPU ----------

    [Fact]
    public void Row19_AmdGpu_OnlyOptiScaler()
    {
        var a = Advise(Game(dlss: true), new GpuInfo(GpuVendor.Amd, RtxTier.None, "Radeon RX 7900 XT"));

        Assert.Equal([19], a.Rows);
        Assert.Equal(Method.OptiScaler, a.Top!.Method);
        Assert.Equal([Method.OptiScaler], a.Possible.Select(m => m.Method));
        Assert.Contains("spoofing", Option(a, Method.OptiScaler).Reason);
        foreach (var m in new[] { Method.DlssUpdate, Method.NeuralAddon, Method.OptiScalerNr, Method.Feeder, Method.MfgUnlock, Method.SmoothMotion })
        {
            Assert.Equal(Availability.NotPossible, Option(a, m).Availability);
            Assert.Contains("NVIDIA RTX", Option(a, m).Reason);
        }
    }

    [Fact]
    public void Row19_GtxCard_HasNoTensorCores()
    {
        var a = Advise(Game(), new GpuInfo(GpuVendor.Nvidia, RtxTier.None, "GeForce GTX 1660"));

        Assert.Equal([19], a.Rows);
        Assert.Contains("no RTX tensor cores", Option(a, Method.Feeder).Reason);
    }

    [Fact]
    public void AnUnreadableGpu_NeverHidesAMethod()
    {
        var a = Advise(Game(), GpuInfo.Unknown);

        Assert.Equal([5], a.Rows);
        Assert.Equal(Method.Feeder, a.Top!.Method);
        Assert.Equal(Availability.Available, Option(a, Method.SmoothMotion).Availability);
    }

    [Theory]
    [InlineData(RtxTier.Rtx50, "Full speed")]
    [InlineData(RtxTier.Rtx40, "moderate cost")]
    [InlineData(RtxTier.Rtx30, "heavy cost")]
    public void NrCostFollowsTheGpuTier(RtxTier tier, string expected)
    {
        var a = Advise(Game(dlss: true), new GpuInfo(GpuVendor.Nvidia, tier, "GeForce RTX"));

        Assert.Contains(expected, Option(a, Method.OptiScalerNr).Reason);
    }

    [Fact]
    public void SmoothMotion_NeedsRtx40_AndSaysTheRequirementIsUnverified()
    {
        var on30 = Option(Advise(Game(GraphicsApis.Dx11, engine: GameEngine.Unity), Rtx30), Method.SmoothMotion);
        var on40 = Option(Advise(Game(GraphicsApis.Dx11, engine: GameEngine.Unity), Rtx40), Method.SmoothMotion);

        Assert.Equal(Availability.NotPossible, on30.Availability);
        Assert.Equal(Availability.Available, on40.Availability);
        Assert.True(on30.Unverified && on40.Unverified);
        Assert.Contains("unverified", on40.Reason);
        Assert.False(on40.Selectable);
    }

    // ---------- row 20: pre-checks ----------

    [Fact]
    public void Row20_AntiCheat_NothingIsOffered_AndTheReasonNamesIt()
    {
        var a = Advise(Game(dlss: true, antiCheat: "EasyAntiCheat"));

        Assert.Equal([20], a.Rows);
        Assert.Empty(a.Methods);
        Assert.Null(a.Top);
        Assert.Contains("EasyAntiCheat", a.Blocked);
        Assert.Contains("banned", a.Blocked);
    }

    [Fact]
    public void Row20_AntiCheatWinsOverANonNvidiaGpu()
    {
        var a = Advise(Game(antiCheat: "BattlEye"), new GpuInfo(GpuVendor.Amd, RtxTier.None, "Radeon"));

        Assert.Equal([20], a.Rows);
        Assert.Contains("BattlEye", a.Blocked);
    }

    [Fact]
    public void Row20_ProtectedStoreInstall_NothingIsOffered()
    {
        var a = MethodAdvisor.Advise(Input(Game(dlss: true)) with { ProtectedInstall = true });

        Assert.Equal([20], a.Rows);
        Assert.Empty(a.Methods);
        Assert.Contains("Microsoft Store", a.Blocked);
    }

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Studio.Game_1.0\Game", true)]
    [InlineData(@"D:\XboxGames\Game\Content", false)]
    public void ProtectedStoreInstall_IsTheWindowsAppsFolder(string root, bool expected)
    {
        var g = Game();
        g.Root = root;

        Assert.Equal(expected, MethodAdvisor.IsProtectedStoreInstall(g, _ => false));
    }

    [Fact]
    public void ProtectedStoreInstall_AlsoAnUnreadableExeNextToMicrosoftGameConfig()
    {
        var g = Game(bits: 0);
        g.Root = @"D:\XboxGames\Game\Content";

        Assert.True(MethodAdvisor.IsProtectedStoreInstall(g, p => p.EndsWith("MicrosoftGame.config")));
        Assert.False(MethodAdvisor.IsProtectedStoreInstall(g, _ => false));
        g.Tech.Bits = 64;
        Assert.False(MethodAdvisor.IsProtectedStoreInstall(g, p => p.EndsWith("MicrosoftGame.config")));
    }

    // ---------- shape of the result ----------

    [Fact]
    public void ImpossibleMethodsComeLast_ComingBeforeThem()
    {
        var a = Advise(Game(GraphicsApis.Vulkan, dlss: true));

        var availabilities = a.Methods.Select(m => m.Availability).ToList();
        Assert.Equal(availabilities.OrderBy(x => x switch { Availability.NotPossible => 2, Availability.Coming => 1, _ => 0 }), availabilities);
    }

    [Fact]
    public void ANeedsSetupMethod_IsSelectable_ButNotWhenItHasNoComponents()
    {
        var a = Advise(Game());

        Assert.True(Option(a, Method.Feeder).Selectable);
        Assert.False(Option(a, Method.AioAddon).Selectable);
    }

    [Fact]
    public void Neural_PicksTheFirstImportedAddon_ElseUpliftWhichDownloadsItself()
    {
        var plain = Option(Advise(Game(dlss: true)), Method.NeuralAddon);
        var imported = Option(MethodAdvisor.Advise(Input(Game(dlss: true)) with
        {
            Imported = new HashSet<string> { ComponentRegistry.DlssNr, ComponentRegistry.Lecram },
        }), Method.NeuralAddon);

        Assert.Contains(ComponentRegistry.Uplift, plain.Components);
        Assert.Equal(Availability.NeedsSetup, plain.Availability);
        Assert.Equal(Availability.Available, imported.Availability);
        Assert.Contains(ComponentRegistry.Lecram, imported.Components);
        Assert.Equal(InstallMode.ReShadeOnly, imported.Mode);
    }
}
