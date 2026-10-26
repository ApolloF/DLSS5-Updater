using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// The decision table of docs/advisor.md "Decision table", row by row (the name of each test says which), plus the
/// pre-checks (anti-cheat, Store install, GPU). Games are built in memory; nothing touches a disk or a GPU.
/// </summary>
public sealed class MethodAdvisorTests
{
    private static readonly GpuInfo Rtx50 = new(GpuVendor.Nvidia, RtxTier.Rtx50, "GeForce RTX 5080");
    private static readonly GpuInfo Rtx40 = new(GpuVendor.Nvidia, RtxTier.Rtx40, "GeForce RTX 4070");
    private static readonly GpuInfo Rtx30 = new(GpuVendor.Nvidia, RtxTier.Rtx30, "GeForce RTX 3060");
    private static readonly GpuInfo Rtx20 = new(GpuVendor.Nvidia, RtxTier.Rtx20, "GeForce RTX 2070");

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
        Assert.Equal([Method.DlssUpdate, Method.OptiScaler, Method.NeuralAddon, Method.OptiScalerNr], Order(a));
        Assert.Equal(Method.DlssUpdate, a.Top!.Method);
        Assert.Contains("Restore DLSS", Option(a, Method.DlssUpdate).Reason);
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

    [Fact]
    public void Row4_OnRtx30_MfgUnlockNeedsTheXeMfgBackend_AndAnUnknownGpuKeepsIt()
    {
        var mfg = Option(Advise(Game(dlss: true, fg: true), Rtx30), Method.MfgUnlock);
        Assert.Equal(Availability.NeedsSetup, mfg.Availability);
        Assert.Contains("XeMFG", mfg.Reason);
        Assert.Equal([ComponentRegistry.MfgUnlock], mfg.Components);
        Assert.Contains(mfg.Needs, n => n.Contains("Intel XeMFG"));
        Assert.Contains(mfg.Needs, n => n.Contains("libxess_fg.dll") && n.Contains("libxell.dll"));
        Assert.Contains(mfg.Needs, n => n.Contains("exclusive fullscreen"));

        Assert.Equal(Availability.NeedsSetup, Option(Advise(Game(dlss: true, fg: true), Rtx20), Method.MfgUnlock).Availability);
        Assert.Equal(Availability.Available, Option(Advise(Game(dlss: true, fg: true), GpuInfo.Unknown), Method.MfgUnlock).Availability);
    }

    [Fact]
    public void Row4_OnRtx40_MfgUnlockUsesTheDlssGBackendWithoutSetup()
    {
        var mfg = Option(Advise(Game(dlss: true, fg: true), Rtx40), Method.MfgUnlock);
        Assert.Equal(Availability.Available, mfg.Availability);
        Assert.Empty(mfg.Needs);
    }

    [Fact]
    public void Row4_XeMfgNeedsD3D12_AndAnRtxCard()
    {
        var vulkan = Option(Advise(Game(GraphicsApis.Vulkan, dlss: true, fg: true), Rtx30), Method.MfgUnlock);
        Assert.Equal(Availability.NotPossible, vulkan.Availability);
        Assert.Contains("D3D12", vulkan.Reason);

        var gtx = Option(Advise(Game(dlss: true, fg: true), new GpuInfo(GpuVendor.Nvidia, RtxTier.None, "GeForce GTX 1080")), Method.MfgUnlock);
        Assert.Equal(Availability.NotPossible, gtx.Availability);
        Assert.Contains("RTX", gtx.Reason);
    }

    // ---------- row 5-6: no DLSS on DX11/12 ----------

    [Fact]
    public void Row5_NoUpscaler64BitDx12_FeederWithDfc_AioComing_SmoothMotion()
    {
        var a = Advise(Game());

        Assert.Equal([5], a.Rows);
        Assert.Equal(Method.Feeder, a.Top!.Method);
        Assert.Equal([Method.SmoothMotion, Method.Feeder, Method.AioAddon], Order(a));
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
        Assert.Equal(Availability.NotPossible, Option(a, Method.Luma).Availability);
        Assert.Contains("no download", Option(a, Method.Luma).Reason);
        Assert.Equal(Method.OptiScaler, Order(a)[0]);
        Assert.Contains(Method.Feeder, Order(a));
    }

    [Fact]
    public void Row7_UnrealDx11OnTheLumaList_WithALumaMod_LumaIsAvailable()
    {
        var game = Game(GraphicsApis.Dx11, engine: GameEngine.Unreal);
        var input = Input(game) with
        {
            OptiCompat = new CatalogOptiGame { Name = "Test Game", List = "lumaUe", Inputs = ["DLSS"] },
            Luma = LumaFixtures.Picker().Pick(LumaFixtures.Unreal("Listed Game"), LumaPicker.UnrealId),
        };

        var luma = Option(MethodAdvisor.Advise(input), Method.Luma);

        Assert.Equal(Availability.Available, luma.Availability);
        Assert.Contains("Luma section", luma.Reason);
        Assert.Contains("RenoDX", luma.Reason);
        Assert.Empty(luma.Components);
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
        Assert.Equal([Method.SmoothMotion, Method.Feeder, Method.Dxvk], Order(a));
        Assert.Contains("Fell & Sell", Option(a, Method.Feeder).Reason);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(64, false)]
    public void Row10_Dx9_FeederThroughDgVoodoo_NoLumaOptionWithoutAMod(int bits, bool beta)
    {
        var a = Advise(Game(GraphicsApis.Dx9, bits));

        Assert.Equal([10], a.Rows);
        var feeder = Option(a, Method.Feeder);
        Assert.Contains(feeder.Needs, n => n.Contains("dgVoodoo2"));
        Assert.Equal(beta, feeder.Reason.Contains("beta"));
        Assert.DoesNotContain(Method.Luma, Order(a));
    }

    [Fact]
    public void Row10_Dx9_WithALumaMod_LumaIsAvailableAndMentionsDgVoodoo()
    {
        var game = Game(GraphicsApis.Dx9, 32);
        var input = Input(game) with { Luma = LumaFixtures.Picker().Pick(LumaFixtures.Plain("Borderlands 2", bits: 32, apis: GraphicsApis.Dx9)) };

        var luma = Option(MethodAdvisor.Advise(input), Method.Luma);

        Assert.Equal(Availability.Available, luma.Availability);
        Assert.Contains("dgVoodoo2", luma.Reason);
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
    public void Row15_GameOnTheRenoDxList_PointsToTheHdrSection()
    {
        var input = Input(Game(dlss: true)) with { RenoDxMod = new CatalogMod { Id = "x", Name = "Hades" } };

        var a = MethodAdvisor.Advise(input);

        Assert.Equal([1, 15], a.Rows);
        Assert.Equal(Availability.Available, Option(a, Method.RenoDxHdr).Availability);
        Assert.False(Option(a, Method.RenoDxHdr).Selectable);
        Assert.Contains("Hades", Option(a, Method.RenoDxHdr).Reason);
        Assert.Contains("HDR section", Option(a, Method.RenoDxHdr).Reason);
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
    public void Row16_GamesOnOptiScalersLumaLists_WithoutACatalogMod_SayThereIsNoDownload(string list, string notes)
    {
        var input = Input(Game(GraphicsApis.Dx11)) with { OptiCompat = new CatalogOptiGame { Name = "Test Game", List = list, Notes = notes, Inputs = ["DLSS"] } };

        var a = MethodAdvisor.Advise(input);

        Assert.Contains(16, a.Rows);
        Assert.Equal(Availability.NotPossible, Option(a, Method.Luma).Availability);
    }

    [Fact]
    public void Row16_AGameWithALumaMod_GetsLumaAsAdvice_EvenOffOptiScalersList()
    {
        var game = Game(GraphicsApis.Dx11, dlss: true);
        var input = Input(game) with { Luma = LumaFixtures.Picker().Pick(LumaFixtures.Plain("Prey")) };

        var a = MethodAdvisor.Advise(input);

        Assert.Contains(16, a.Rows);
        var luma = Option(a, Method.Luma);
        Assert.Equal(Availability.Available, luma.Availability);
        Assert.Contains("Don't stack it with a RenoDX HDR mod", luma.Reason);
        Assert.Empty(luma.Components);
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
    public void Row18_ABridgeInTheGame_WarnsOnEveryNeuralMethod_AndAutoNeverPicksThem()
    {
        var input = Input(Game(dlss: true)) with { InstalledRoutes = [NrAddon.Bridge] };

        var a = MethodAdvisor.Advise(input);

        Assert.Contains(18, a.Rows);
        foreach (var m in new[] { Method.NeuralAddon, Method.OptiScalerNr })
        {
            var o = Option(a, m);
            Assert.True(o.Selectable);
            Assert.Contains("don't work well in the same game", o.Warning);
            Assert.Contains("DX11 bridge", o.Warning);
        }
        Assert.DoesNotContain(a.Top?.Method, new Method?[] { Method.NeuralAddon, Method.OptiScalerNr });
        Assert.Equal(Availability.Available, Option(a, Method.DlssUpdate).Availability);
    }

    [Fact]
    public void Row18_FeederNextToABridge_IsWarned()
    {
        var input = Input(Game()) with { InstalledRoutes = [NrAddon.Bridge] };

        var feeder = Option(MethodAdvisor.Advise(input), Method.Feeder);

        Assert.NotNull(feeder.Warning);
        Assert.DoesNotContain("remove the bridge files first", feeder.Reason);
    }

    [Fact]
    public void Row18_AnAddonOfOurOwn_IsKeptByTheNeuralMethod_AndNothingIsWarned()
    {
        var input = Input(Game(dlss: true)) with { InstalledRoutes = [NrAddon.Lecram], Imported = new HashSet<string> { ComponentRegistry.DlssNr, ComponentRegistry.RenoDx, ComponentRegistry.Lecram } };

        var a = MethodAdvisor.Advise(input);

        Assert.Contains(18, a.Rows);
        var neural = Option(a, Method.NeuralAddon);
        Assert.Equal(Availability.Available, neural.Availability);
        Assert.Equal(NrAddon.Lecram, neural.Route);
        Assert.Contains(ComponentRegistry.Lecram, neural.Components);
        Assert.DoesNotContain(ComponentRegistry.RenoDx, neural.Components);
        foreach (var m in new[] { Method.NeuralAddon, Method.OptiScalerNr, Method.OptiScaler })
            Assert.Null(Option(a, m).Warning);
    }

    [Fact]
    public void Row18_OuterWilds_UpliftInstalled_FeederNeedsSetup_ButTheInstallReplacesUplift_SoNoWarning()
    {
        var game = Game(GraphicsApis.Dx11, engine: GameEngine.Unity, name: "Outer Wilds");
        var input = Input(game) with { InstalledRoutes = [NrAddon.Uplift] };

        var a = MethodAdvisor.Advise(input);

        Assert.Contains(9, a.Rows);
        Assert.Contains(18, a.Rows);
        var feeder = Option(a, Method.Feeder);
        Assert.Equal(Availability.NeedsSetup, feeder.Availability);
        Assert.Contains(feeder.Needs, n => n.Contains("motion-vector"));
        Assert.Null(feeder.Warning);
    }

    [Fact]
    public void Row18_UpliftInstalled_NeuralMethodKeepsUplift_AndNoMethodIsWarned()
    {
        var input = Input(Game(dlss: true)) with { InstalledRoutes = [NrAddon.Uplift] };

        var a = MethodAdvisor.Advise(input);

        Assert.Equal(NrAddon.Uplift, Option(a, Method.NeuralAddon).Route);
        Assert.All(a.Methods, m => Assert.Null(m.Warning));
    }

    [Fact]
    public void Row18_AnImportedAddonThatIsNotTheInstalledOne_IsNotUsed_TheInstalledOneStays()
    {
        var input = Input(Game(dlss: true)) with { InstalledRoutes = [NrAddon.Uplift], Imported = new HashSet<string> { ComponentRegistry.DlssNr, ComponentRegistry.RenoDx } };

        var neural = Option(MethodAdvisor.Advise(input), Method.NeuralAddon);

        Assert.Equal(NrAddon.Uplift, neural.Route);
        Assert.Null(neural.Warning);
    }

    [Fact]
    public void Rank_AvailableComesBeforeNeedsSetup_AndWhatCannotBeUsedComesLast()
    {
        foreach (var game in new[] { Game(), Game(dlss: true), Game(GraphicsApis.Dx11, engine: GameEngine.Unity), Game(GraphicsApis.Vulkan, dlss: true) })
        {
            var order = Advise(game).Methods.Where(m => m.Method is not (Method.Dxvk or Method.RenoDxHdr)).Select(m => m.Availability switch { Availability.Available => 0, Availability.NeedsSetup => 1, Availability.Coming => 2, _ => 3 }).ToList();
            Assert.Equal(order.OrderBy(x => x), order);
        }
    }

    [Fact]
    public void EveryMethodHasAPlainSummary()
    {
        foreach (var m in Enum.GetValues<Method>())
        {
            var summary = MethodAdvisor.SummaryOf(m);
            Assert.False(string.IsNullOrWhiteSpace(summary));
            Assert.EndsWith(".", summary);
            // Says what goes in or changes: no selling words, no advice, not the card title again.
            Assert.DoesNotContain(MethodAdvisor.TitleOf(m), summary);
            foreach (var word in new[] { "sharper", "steadier", "better", "safest", "best", "look" })
                Assert.DoesNotMatch($@"(?i)\b{word}\b", summary);
        }
        Assert.Equal(MethodAdvisor.SummaryOf(Method.MfgUnlock), Option(Advise(Game(dlss: true, fg: true)), Method.MfgUnlock).Summary);
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
    public void Row20_AntiCheat_MethodsStayOffered_WithAStrongWarning_AndAutoNeverPicksThem()
    {
        var a = Advise(Game(dlss: true, antiCheat: "EasyAntiCheat"));

        Assert.Contains(20, a.Rows);
        Assert.Null(a.Blocked);
        Assert.NotEmpty(a.Methods);
        Assert.Null(a.Top);
        Assert.Contains("EasyAntiCheat", a.Caution);
        Assert.Contains("banned", a.Caution);
        Assert.Contains("Online", a.Caution);
        var selectable = a.Methods.Where(m => m.Selectable).ToList();
        Assert.NotEmpty(selectable);
        Assert.All(selectable, m => Assert.Equal(a.Caution, m.Warning));
    }

    [Fact]
    public void Row20_ThePanelShowsTheCautionOnce_NotOnEveryLine()
    {
        var a = Advise(Game(dlss: true, antiCheat: "EasyAntiCheat"));

        Assert.All(a.Methods, m => Assert.False(new DLSSUpdater.ViewModels.MethodRow(m, false, a.Caution).HasWarning));
        var other = a.Methods.First() with { Warning = "Something else" };
        Assert.Equal("Something else", new DLSSUpdater.ViewModels.MethodRow(other, false, a.Caution).Warning);
    }

    [Fact]
    public void MethodRow_AdviceOnlyMethods_AreClickableCardsThatSayWhereTheyAreInstalled()
    {
        var advice = new MethodOption(Method.RenoDxHdr, "RenoDX HDR mod", Availability.Available, "reason", [], [], null, false);
        var row = new DLSSUpdater.ViewModels.MethodRow(advice, false);

        Assert.True(row.IsAdviceOnly);
        Assert.True(row.IsClickable);
        Assert.False(row.IsSelectable);
        Assert.Equal("Own section below", row.StateText);
        Assert.Contains("own section", row.CardTip);
        Assert.True(new DLSSUpdater.ViewModels.MethodRow(advice with { Availability = Availability.NeedsSetup }, false).IsAdviceOnly);
    }

    [Fact]
    public void MethodRow_MethodsWithComponents_AndDimOnes_KeepTheirBehaviour()
    {
        var picked = new MethodOption(Method.DlssUpdate, "DLSS", Availability.Available, "reason", [ComponentRegistry.Dlss], [], null, false);
        var row = new DLSSUpdater.ViewModels.MethodRow(picked, false);
        Assert.False(row.IsAdviceOnly);
        Assert.True(row.IsClickable);
        Assert.Equal("Available", row.StateText);

        foreach (var a in new[] { Availability.NotPossible, Availability.Coming })
        {
            var dim = new DLSSUpdater.ViewModels.MethodRow(picked with { Availability = a, Components = [] }, false);
            Assert.False(dim.IsClickable);
            Assert.False(dim.IsAdviceOnly);
            Assert.True(dim.IsDim);
        }
    }

    [Fact]
    public void Row20_NoAntiCheat_NoWarningAndATopMethod()
    {
        var a = Advise(Game(dlss: true));

        Assert.Null(a.Caution);
        Assert.All(a.Methods, m => Assert.Null(m.Warning));
        Assert.NotNull(a.Top);
    }

    [Fact]
    public void Row20_AntiCheatOnANonNvidiaGpu_IsWarnedToo()
    {
        var a = Advise(Game(antiCheat: "BattlEye"), new GpuInfo(GpuVendor.Amd, RtxTier.None, "Radeon"));

        Assert.Contains(20, a.Rows);
        Assert.Contains("BattlEye", a.Caution);
        Assert.Null(a.Top);
        Assert.All(a.Methods.Where(m => m.Selectable), m => Assert.NotNull(m.Warning));
    }

    [Fact]
    public void Row20_LockedStoreInstall_NothingIsOffered_AndCannotBeConfirmed()
    {
        var a = MethodAdvisor.Advise(Input(Game(dlss: true)) with { ProtectedInstall = true });

        Assert.Equal([20], a.Rows);
        Assert.Empty(a.Methods);
        Assert.Contains("Microsoft Store", a.Blocked);
    }

    [Fact]
    public void Row20_WritableStoreInstall_IsOfferedWithAWarning_AndAutoNeverPicksIt()
    {
        var a = MethodAdvisor.Advise(Input(Game(dlss: true)) with { StoreInstall = true });

        Assert.Null(a.Blocked);
        Assert.Contains(20, a.Rows);
        Assert.Null(a.Top);
        Assert.Contains("Microsoft Store", a.Caution);
        Assert.All(a.Methods.Where(m => m.Selectable), m => Assert.Equal(MethodAdvisor.StoreCaution, m.Warning));
    }

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Studio.Game_1.0\Game", true)]
    [InlineData(@"D:\XboxGames\Game\Content", false)]
    public void StoreInstall_IsTheWindowsAppsFolder(string root, bool expected)
    {
        var g = Game();
        g.Root = root;

        Assert.Equal(expected, MethodAdvisor.IsStoreInstall(g, _ => false));
    }

    [Fact]
    public void StoreInstall_AlsoAnUnreadableExeNextToMicrosoftGameConfig()
    {
        var g = Game(bits: 0);
        g.Root = @"D:\XboxGames\Game\Content";

        Assert.True(MethodAdvisor.IsStoreInstall(g, p => p.EndsWith("MicrosoftGame.config")));
        Assert.False(MethodAdvisor.IsStoreInstall(g, _ => false));
        g.Tech.Bits = 64;
        Assert.False(MethodAdvisor.IsStoreInstall(g, p => p.EndsWith("MicrosoftGame.config")));
    }

    [Fact]
    public void ProtectedStoreInstall_IsAStoreFolderTheAppCannotWriteTo()
    {
        var g = Game();
        g.Root = @"C:\Program Files\WindowsApps\Studio.Game_1.0\Game";

        Assert.True(MethodAdvisor.IsProtectedStoreInstall(g, _ => false, canWrite: _ => false));
        Assert.False(MethodAdvisor.IsProtectedStoreInstall(g, _ => false, canWrite: _ => true));
        g.Root = @"D:\Games\Plain";
        Assert.False(MethodAdvisor.IsProtectedStoreInstall(g, _ => false, canWrite: _ => false));
    }

    [Fact]
    public void FolderWritable_ProbesWithAFileAndLeavesNothingBehind()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlssu-probe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            Assert.True(MethodAdvisor.FolderWritable(dir));
            Assert.Empty(Directory.GetFiles(dir));
            Assert.False(MethodAdvisor.FolderWritable(Path.Combine(dir, "missing")));
        }
        finally { Directory.Delete(dir, true); }
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
