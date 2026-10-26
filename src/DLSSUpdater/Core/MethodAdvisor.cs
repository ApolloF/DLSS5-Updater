using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>
/// The ways the app can improve upscaling, frame generation or neural rendering (NR) in a game. Letters in the comments are the
/// method letters of docs/advisor.md "Methods".
/// </summary>
public enum Method
{
    /// <summary>A: newer DLSS files in a game that ships them.</summary>
    DlssUpdate,
    /// <summary>B/C: an NR add-on on the game's own DLSS (ShortFuse, Lecram, Deep Fried Chicken, GITC Uplift).</summary>
    NeuralAddon,
    /// <summary>F: OptiScaler's NR build.</summary>
    OptiScalerNr,
    /// <summary>E: OptiScaler without NR (FSR, XeSS, frame generation).</summary>
    OptiScaler,
    /// <summary>D: DLSS5-Feeder with Deep Fried Chicken, for games without DLSS.</summary>
    Feeder,
    /// <summary>H: MFG Unlock.</summary>
    MfgUnlock,
    /// <summary>I: NVIDIA's driver frame generation.</summary>
    SmoothMotion,
    /// <summary>Not in the app yet: the DLSS5 DX11 bridge.</summary>
    Dx11Bridge,
    /// <summary>G, not in the app yet: a third-party upscaler mod for this game (REFramework PDUpscaler, ...).</summary>
    UpscalerMod,
    /// <summary>G: a Luma framework mod, installed from the Luma section of the game page (never picked for you, and not to be stacked with a RenoDX HDR mod).</summary>
    Luma,
    /// <summary>J, not in the app yet: a standalone all-in-one add-on.</summary>
    AioAddon,
    /// <summary>Row 15: the game's RenoDX HDR mod, installed from the HDR section of the game page (not a component of the methods).</summary>
    RenoDxHdr,
    /// <summary>Advice only: DXVK (Direct3D 8 to 11 over Vulkan), with the Lilium HDR fork for an HDR output the game lacks. Installed from the DXVK section of the game page.</summary>
    Dxvk,
}

public enum Availability
{
    /// <summary>Everything it needs is there or downloads by itself.</summary>
    Available,
    /// <summary>Possible, after the user does what <see cref="MethodOption.Needs"/> lists.</summary>
    NeedsSetup,
    /// <summary>Not for this game or this PC; the reason says why.</summary>
    NotPossible,
    /// <summary>A real method the app doesn't install yet.</summary>
    Coming,
}

/// <param name="Components">Component ids (<see cref="ComponentRegistry"/>) the method installs; the game page turns them on.</param>
/// <param name="Needs">What is missing before it can work (an import, a manual step).</param>
/// <param name="Mode">The install mode the components need; null when it doesn't matter.</param>
/// <param name="Unverified">The research marks something this advice rests on as unverified; the reason says what.</param>
public sealed record MethodOption(Method Method, string Title, Availability Availability, string Reason,
    IReadOnlyList<string> Components, IReadOnlyList<string> Needs, InstallMode? Mode, bool Unverified)
{
    /// <summary>Set when the method adds files to a game that is risky to add them to (anti-cheat, Store install): installing asks for a confirmation and auto never picks it.</summary>
    public string? Warning { get; init; }
    /// <summary>The neural route this method leaves in the game (null when it adds none); the advisor checks it against the routes already there.</summary>
    public NrAddon? Route { get; init; }
    /// <summary>One plain line on what the method does for the player; the longer <see cref="Reason"/> is the detail.</summary>
    public string Summary => MethodAdvisor.SummaryOf(Method);
    /// <summary>An add-on that goes on top of whichever method is picked (<see cref="MethodAdvisor.IsAddOn"/>), not a method of its own.</summary>
    public bool IsAddOn => MethodAdvisor.IsAddOn(Method);
    /// <summary>True when the game page can select this method's components.</summary>
    public bool Selectable => (Availability is Availability.Available or Availability.NeedsSetup) && Components.Count > 0;
}

/// <summary>
/// Ranked methods for one game. <see cref="Rows"/> are the decision-table rows (docs/advisor.md) that applied, the
/// shape first and the additive ones (15, 17, 18) after; <see cref="Blocked"/> is set when nothing may be added to the game.
/// </summary>
public sealed record MethodAdvice(IReadOnlyList<MethodOption> Methods, IReadOnlyList<int> Rows, string? Blocked)
{
    /// <summary>Shown above the methods when adding files to this game is risky; each such method also carries it as its <see cref="MethodOption.Warning"/>.</summary>
    public string? Caution { get; init; }
    /// <summary>
    /// The method to recommend: the first the game page can select without a warning. <see cref="MethodAdvisor.Advise"/> ranks
    /// Available before NeedsSetup, so a method that needs setup is only recommended when nothing available fits. A method that
    /// clashes with a neural route already in the game carries a warning, so it is never recommended; null when nothing qualifies.
    /// Add-ons (MFG Unlock) are never the recommendation: they go on top of the method.
    /// </summary>
    public MethodOption? Top => Methods.FirstOrDefault(m => !m.IsAddOn && m.Selectable && m.Warning is null);
    /// <summary>The methods the game page offers as exclusive choices: everything but the add-ons.</summary>
    public IEnumerable<MethodOption> Choices => Methods.Where(m => !m.IsAddOn);
    /// <summary>The add-ons the game page offers on top of whichever method is picked.</summary>
    public IEnumerable<MethodOption> AddOns => Methods.Where(m => m.IsAddOn);
    public IEnumerable<MethodOption> Possible => Methods.Where(m => m.Availability is Availability.Available or Availability.NeedsSetup);
    public IEnumerable<MethodOption> Unavailable => Methods.Where(m => m.Availability is Availability.NotPossible or Availability.Coming);
}

/// <summary>Everything the advisor looks at. Defaults are the plain case: nothing imported, nothing installed.</summary>
public sealed record AdvisorInput(GameInfo Game, GpuInfo Gpu)
{
    /// <summary>The game ships its own DLSS (files of ours don't count, see <see cref="Installer.HasNativeDlss"/>); null = read the game's files.</summary>
    public bool? NativeDlss { get; init; }
    /// <summary>A Microsoft Store / Xbox install whose folder is protected (<see cref="MethodAdvisor.IsProtectedStoreInstall"/>).</summary>
    public bool ProtectedInstall { get; init; }
    /// <summary>A Store / Xbox install whose folder the app can write to: allowed after a confirmation (<see cref="MethodAdvisor.IsStoreInstall"/>).</summary>
    public bool StoreInstall { get; init; }
    public CatalogOptiGame? OptiCompat { get; init; }
    public bool HudfixIncompatible { get; init; }
    /// <summary>The game's entry on RenoDX's list, when the catalog has one.</summary>
    public CatalogMod? RenoDxMod { get; init; }
    /// <summary>What Luma would install in the game (the catalog's mod or the generic Unreal one); null or not offered = the Luma catalog has nothing for it.</summary>
    public LumaChoice? Luma { get; init; }
    /// <summary>Component ids with a build in the app's library that the user imported: the NR runtime and the NR add-ons.</summary>
    public IReadOnlySet<string> Imported { get; init; } = new HashSet<string>();
    /// <summary>The newest Deep Fried Chicken build runs in games without their own DLSS (4.x or newer).</summary>
    public bool DfcRunsWithoutDlss { get; init; } = true;
    /// <summary>The neural routes already in the game (<see cref="NrAddons.PresentIn"/>).</summary>
    public IReadOnlyList<NrAddon> InstalledRoutes { get; init; } = [];
    /// <summary>A motion-vector provider shader for Feeder (LumeniteFX, ...) is already in the game's shader folder.</summary>
    public bool MotionProviderPresent { get; init; }
}

/// <summary>
/// Which methods fit a game, best first, with the reason in one line. Pure: the caller reads the game, the GPU, the catalog and the
/// library and passes them in. The rules are the decision table of docs/advisor.md (20 rows), whose
/// numbers <see cref="MethodAdvice.Rows"/> reports.
/// </summary>
public static class MethodAdvisor
{
    /// <summary>
    /// MFG Unlock adds frame generation next to the upscaler or NR route, and the installer combines it with every mode
    /// (OptiScaler + MFG, ReShade + Lecram + MFG, ...), so picking a method never takes it out.
    /// </summary>
    public static bool IsAddOn(Method m) => m == Method.MfgUnlock;

    public const string FeederCaveat = "DLAA only: no performance gain, a few ms per frame at 4K. Single-player games only.";

    public static string TitleOf(Method m) => m switch
    {
        Method.DlssUpdate => "Update the DLSS files",
        Method.NeuralAddon => "Neural rendering add-on (ReShade)",
        Method.OptiScalerNr => "OptiScaler with neural rendering",
        Method.OptiScaler => "OptiScaler (FSR, XeSS, frame generation)",
        Method.Feeder => "DLSS5-Feeder + Deep Fried Chicken",
        Method.MfgUnlock => "MFG Unlock",
        Method.SmoothMotion => "NVIDIA Smooth Motion",
        Method.Dx11Bridge => "DLSS5 DX11 bridge",
        Method.UpscalerMod => "Upscaler mod for this game",
        Method.Luma => "Luma framework mod",
        Method.AioAddon => "All-in-one add-on",
        Method.Dxvk => "DXVK (Vulkan translation, Lilium HDR)",
        _ => "RenoDX HDR mod",
    };

    /// <summary>What a method does for the player, in one line (the row's headline; <see cref="MethodOption.Reason"/> has the detail).</summary>
    public static string SummaryOf(Method m) => m switch
    {
        Method.DlssUpdate => "Replaces the game's DLSS files with the newest release.",
        Method.NeuralAddon => "Installs ReShade, nvngx_dlssnr.dll and an NR add-on that works on top of the game's own DLSS.",
        Method.OptiScalerNr => "Installs OptiScaler and nvngx_dlssnr.dll; OptiScaler replaces the game's upscaler and adds neural rendering.",
        Method.OptiScaler => "Installs OptiScaler, which replaces the game's upscaler and adds frame generation.",
        Method.Feeder => "Runs neural rendering through ReShade in a game without DLSS, at native resolution (no FPS gain).",
        Method.MfgUnlock => "Adds a mod that turns on multi frame generation in a game with DLSS frame generation: DLSS-G on RTX 40, Intel XeMFG on RTX 20/30.",
        Method.SmoothMotion => "Turns on NVIDIA's driver frame generation from the game's NVIDIA profile tab; adds no files.",
        Method.Dx11Bridge => "Neural rendering for a DX11 game that has DLSS. Not in the app yet.",
        Method.UpscalerMod => "A community mod that gives this game an upscaler. Not in the app yet.",
        Method.Luma => "Installs this game's Luma mod, which changes its rendering and adds HDR and optional DLSS.",
        Method.AioAddon => "One add-on for neural rendering, upscaling and frame generation. Not in the app yet.",
        Method.Dxvk => "Runs an older Direct3D game on Vulkan, with optional HDR output.",
        _ => "Installs this game's RenoDX mod, which adds HDR output.",
    };

    /// <summary>
    /// A Store / Xbox layout: under WindowsApps, or a MicrosoftGame.config game whose exe could not be read.
    /// </summary>
    public static bool IsStoreInstall(GameInfo game, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        if (game.Root.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return true;
        return game.Effective.Bits == 0 && game.Exes.Count > 0 && fileExists(Path.Combine(game.Root, "MicrosoftGame.config"));
    }

    /// <summary>A Store install whose folder the app can't write to (a probe file fails): nothing can be added, whatever the user confirms.</summary>
    public static bool IsProtectedStoreInstall(GameInfo game, Func<string, bool>? fileExists = null, Func<string, bool>? canWrite = null) =>
        IsStoreInstall(game, fileExists) && !(canWrite ?? FolderWritable)(game.Root);

    /// <summary>Creates and deletes a probe file in <paramref name="dir"/>.</summary>
    public static bool FolderWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".dlssu-probe-" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }

    public const string StoreCaution = "Microsoft Store install: Store updates can undo changes in this folder and the game may refuse to start modified. The folder is writable here, so you can install anyway.";

    public static string AntiCheatCaution(string ac) =>
        $"{ac} anti-cheat found: injecting files into this game can get your account banned. Online play is the risk; offline and single-player use is fine. A driver frame limit is still safe.";

    public static MethodAdvice Advise(AdvisorInput input)
    {
        var game = input.Game;
        // Row 20: the folder is locked, so nothing can be added whatever the user says.
        if (input.ProtectedInstall)
            return Blocked(20, "Microsoft Store install: the game folder is protected, so the app can't add files to it. The driver frame limit still works.");

        // Row 19: a GPU without tensor cores or from another vendor.
        MethodAdvice advice;
        if (!input.Gpu.MayRunNvidiaMethods)
            advice = NonNvidia(input);
        else
        {
            var ctx = new Ctx(input);
            var rows = new List<int>();
            var methods = new List<MethodOption>();
            Shape(ctx, rows, methods);
            AddExtras(ctx, rows, methods);
            advice = new MethodAdvice(Rank(methods), rows, null);
        }

        // Row 20, the risky kind: adding files stays possible, after a confirmation; auto never picks it.
        var caution = string.Join(" ", new[] { game.AntiCheat is { } ac ? AntiCheatCaution(ac) : null, input.StoreInstall ? StoreCaution : null }.Where(x => x is not null));
        if (caution.Length == 0) return advice;
        return advice with
        {
            Methods = [.. advice.Methods.Select(m => m.Selectable ? m with { Warning = caution } : m)],
            Rows = [.. advice.Rows.Append(20).Distinct()],
            Caution = caution,
        };
    }

    private static MethodAdvice Blocked(int row, string reason) => new([], [row], reason);

    // ---------- row 19 ----------

    private static MethodAdvice NonNvidia(AdvisorInput input)
    {
        var gpu = input.Gpu;
        var why = gpu.IsNvidia
            ? $"{gpu.Name} has no RTX tensor cores"
            : $"{gpu.Label} isn't an NVIDIA RTX card";
        var ctx = new Ctx(input);
        var methods = new List<MethodOption>
        {
            Opti(ctx, gpu.IsNvidia
                ? "Upscaling swap to FSR or XeSS inputs and frame generation, no NVIDIA features needed."
                : "FSR or XeSS inputs and frame generation. A game with only DLSS inputs needs DXGI spoofing or OptiPatcher (OptiScaler's Spoofing page); FSR 4 officially needs RDNA 3 or 4."),
        };
        foreach (var m in new[] { Method.DlssUpdate, Method.NeuralAddon, Method.OptiScalerNr, Method.Feeder, Method.MfgUnlock, Method.SmoothMotion })
            methods.Add(Opt(m, Availability.NotPossible, $"Needs an NVIDIA RTX card: {why}."));
        var rows = new List<int> { 19 };
        AddExtras(ctx, rows, methods);
        return new MethodAdvice(Rank(methods), rows, null);
    }

    // ---------- the shape rows ----------

    private sealed class Ctx(AdvisorInput input)
    {
        public AdvisorInput In { get; } = input;
        public GameInfo Game => In.Game;
        public GameTech Tech => In.Game.Effective;
        public GpuInfo Gpu => In.Gpu;
        public bool Native => In.NativeDlss ?? Game.Features.HasFlag(GameFeatures.SR);
        public GraphicsApis Api => Tech.PrimaryApi;
        public bool ApiKnown => Tech.Apis != GraphicsApis.None;
        public bool Is32 => Tech.Is32Bit;
        public bool Has(string id) => In.Imported.Contains(id);
        public bool HasFg => Game.Features.HasFlag(GameFeatures.FG);
    }

    private static void Shape(Ctx c, List<int> rows, List<MethodOption> list)
    {
        var tech = c.Tech;
        var plugin = tech.Upscalers.HasFlag(UpscalerSignals.DlssPlugin) || tech.Upscalers.HasFlag(UpscalerSignals.StreamlinePlugin);

        // Row 8: RE Engine needs REFramework before anything that loads as a proxy dll.
        if (tech.Engine == GameEngine.REEngine && !c.Is32)
        {
            rows.Add(8);
            const string refw = "REFramework (dinput8.dll) installed first, or ReShade crashes the game";
            if (c.Native)
            {
                list.Add(DlssUpdate(c, "Newest DLSS files (Resident Evil 4 and Requiem ship DLSS)."));
                list.Add(Neural(c, "NR on the game's own DLSS.", refw));
                list.Add(OptiNr(c, "OptiScaler's NR build on the game's DLSS."));
            }
            else
            {
                list.Add(Opt(Method.UpscalerMod, Availability.Coming, "RE Engine games without DLSS get one through REFramework's PDUpscaler (RE2, RE3, RE4 2023, RE7, RE8); the app doesn't install it yet."));
                list.Add(Opti(c, "Swaps the upscaler PDUpscaler adds for FSR or XeSS."));
            }
            return;
        }

        if (c.Native || (tech.IsUnreal && plugin))
        {
            NativeRows(c, rows, list, viaPlugin: !c.Native);
            return;
        }

        // Rows 10, 14, 12, 13, 11: the graphics API decides before the engine.
        var api = c.Api;
        if (api is GraphicsApis.Dx9 or GraphicsApis.Dx8)
        {
            rows.Add(10);
            list.Add(Feed(c, c.Is32 ? "beta" : null, "D3D9 and older games are translated to D3D11 first with dgVoodoo2 (freeware, not redistributable: you add it, Feeder's README has the settings); best motion vectors of all APIs.",
                "dgVoodoo2 placed by hand (D3D9.dll and dgVoodoo.conf beside the exe)"));
            if (LumaOffered(c) is { } luma) list.Add(luma);
            return;
        }
        if (api == GraphicsApis.Dx10)
        {
            rows.Add(14);
            list.Add(Feed(c, "beta", "Direct3D 10 needs nothing extra, but the README says it has not run in a game for 64-bit helper mode: low confidence.", null, unverified: true));
            return;
        }
        if (api == GraphicsApis.OpenGL)
        {
            rows.Add(12);
            list.Add(Feed(c, null, "OpenGL works (Worms Ultimate Mayhem, KOTOR, MX Bikes); the game must render on the NVIDIA GPU. Many OpenGL games are 32-bit.", null));
            return;
        }
        if (api == GraphicsApis.Vulkan)
        {
            rows.Add(13);
            list.Add(Feed(c, null, "Vulkan works through Feeder's layer (DOOM 2016 proven). Turn Smooth Motion off for this game's Vulkan path.",
                "ReShade's Vulkan layer registered machine-wide (one administrator prompt) and the exe listed in ReShadeApps.ini"));
            return;
        }
        if (c.Is32)
        {
            rows.Add(11);
            list.Add(Feed(c, "beta", "NGX is 64-bit only, so a 32-bit game gets dlss5-feed.addon32 plus a 64-bit helper in host64\\.", null));
            return;
        }

        // 64-bit DX11/DX12 (or an API we couldn't read) without any DLSS.
        var inputs = c.In.OptiCompat is { Compat: not "not-working" } oc && oc.Inputs.Count > 0 ? oc : null;
        var otherUpscaler = tech.Upscalers.HasFlag(UpscalerSignals.Fsr) || tech.Upscalers.HasFlag(UpscalerSignals.XeSS)
            || tech.Upscalers.HasFlag(UpscalerSignals.FsrPlugin) || tech.Upscalers.HasFlag(UpscalerSignals.XessPlugin);

        if (tech.IsUnreal && tech.Engine == GameEngine.Unreal)
        {
            rows.Add(7);
            var xessOnly = tech.Upscalers.HasFlag(UpscalerSignals.XessPlugin) && !tech.Upscalers.HasFlag(UpscalerSignals.FsrPlugin);
            if (xessOnly)
            {
                list.Add(Opti(c, "Unreal only takes XeSS as OptiScaler's input, and this game has the XeSS plugin; Engine.ini tweaks for crashes are on OptiScaler's wiki."));
                return;
            }
            if (otherUpscaler)
            {
                rows.Add(6);
                list.Add(Opti(c, "The game has an FSR plugin: OptiScaler swaps it for FSR 3.1/4 or XeSS (optional OptiFG on DX12)."));
                list.Add(Feed(c, null, "Neural rendering on top, only if OptiScaler is not used.", null));
                return;
            }
            if (c.In.OptiCompat is { List: "lumaUe" } && c.Api == GraphicsApis.Dx11)
            {
                list.Add(LumaOffered(c) ?? NoLumaDownload("Luma UE replaces this DX11 Unreal game's TAA with DLSS or DLAA (it is on OptiScaler's Luma UE list)"));
                list.Add(Opti(c, "OptiScaler on top of what Luma UE adds."));
            }
            list.Add(Feed(c, null, "Unreal game without an upscaler plugin: Feeder + Deep Fried Chicken adds NR. " + FeederCaveat, null));
            return;
        }

        if (inputs is not null || otherUpscaler)
        {
            rows.Add(6);
            var what = inputs is not null ? $"OptiScaler's list shows it working with {string.Join(", ", inputs.Inputs)}" : "the game ships FSR or XeSS";
            list.Add(Opti(c, $"{Capital(what)}: OptiScaler swaps it for FSR 3.1/4 or XeSS (optional OptiFG on DX12)."));
            list.Add(Feed(c, null, "NR on top, only if OptiScaler is not used. " + FeederCaveat, null));
            return;
        }

        if (tech.Engine == GameEngine.Unity)
        {
            rows.Add(9);
            list.Add(Feed(c, null, "Unity DX11 without DLSS: proven in Fell & Sell. Read the graphics API from Player.log when it says otherwise.", null));
            list.Add(SmoothMotion(c, "Frame generation only."));
            return;
        }

        rows.Add(5);
        list.Add(Feed(c, null, "No upscaler in the game: Feeder + Deep Fried Chicken adds NR. ShortFuse's renodx-dlss alone does nothing here. " + FeederCaveat,
            null, unverified: !c.ApiKnown, unverifiedWhy: "the graphics API wasn't detected, DX11/12 is assumed"));
        list.Add(Opt(Method.AioAddon, Availability.Coming, "A standalone all-in-one add-on (kibblerz DLSS5-Reshade-AIO) does NR, SR and FG in one; young project, the app doesn't install it yet."));
        list.Add(SmoothMotion(c, "Frame generation only, no ReShade needed."));
    }

    /// <summary>Rows 1, 2, 3 (and 4 for frame generation): the game has DLSS.</summary>
    private static void NativeRows(Ctx c, List<int> rows, List<MethodOption> list, bool viaPlugin)
    {
        if (c.Tech.IsUnreal) rows.Add(7);
        var plugin = viaPlugin ? " (Unreal's DLSS plugin is there, so the dll is assumed)" : "";
        var vulkan = c.Api == GraphicsApis.Vulkan;
        var dx11 = c.Api == GraphicsApis.Dx11;
        rows.Add(vulkan ? 3 : dx11 ? 2 : 1);

        list.Add(DlssUpdate(c, "Restore DLSS puts the game's original files back." + plugin, viaPlugin));
        if (vulkan)
        {
            list.Add(Opt(Method.NeuralAddon, Availability.NotPossible, "ReShade reaches Vulkan through a registered layer, not a proxy dll, so the NR add-ons can't load in the usual way."));
            list.Add(OptiNr(c, "OptiScaler covers Vulkan with the game's own DLSS and adds NR."));
        }
        else if (dx11)
        {
            list.Add(Neural(c, "NR on the game's DLSS. DX11 needs an add-on that bridges DX11 itself (renodx-dlss5 8.x) or the DX11 bridge; the app doesn't check which build you have.",
                null, unverified: true, unverifiedWhy: "the DX11 bridge requirement isn't checked against your add-on build"));
            list.Add(Opt(Method.Dx11Bridge, Availability.Coming, "NIGos' DLSS5 DX11 bridge with Deep Fried Chicken; never combine it with Feeder. The app doesn't install it yet."));
            list.Add(OptiNr(c, "OptiScaler's NR build on the game's DLSS."));
        }
        else
        {
            list.Add(Neural(c, "NR on top of the game's own DLSS.", null));
            list.Add(OptiNr(c, "OptiScaler's NR build; the better pick for games that churn their NGX features (Cyberpunk-style)."));
            list.Add(Opti(c, "Only for FSR/XeSS or frame-generation tweaks."));
        }
        if (c.HasFg)
        {
            rows.Add(4);
            list.Insert(1, MfgUnlock(c));
            list.RemoveAll(m => m.Method == Method.OptiScaler);
            list.Add(Opti(c, "Frame generation: OptiScaler can take the game's DLSS-G through Streamline 2+ (not Streamline 1 games such as The Witcher 3 DX11, Dying Light 2, A Plague Tale: Requiem or Returnal) or Nukem's dlssg-to-fsr3 for FSR3 frame generation."));
        }
    }

    // ---------- rows 15, 17 ----------

    private static void AddExtras(Ctx c, List<int> rows, List<MethodOption> list)
    {
        if (c.In.RenoDxMod is { Blocked: false } mod)
        {
            rows.Add(15);
            list.Add(Opt(Method.RenoDxHdr, Availability.Available, $"RenoDX has an HDR mod for this game ({mod.Name}); install it from the HDR section on this page. It works next to the methods above."));
        }
        // Not a table row: older Direct3D games with no RenoDX HDR mod can still get HDR output from the Lilium build of DXVK. Advice only (no components), so auto never picks it.
        if (DxvkDlls.Applies(c.In.Game.Effective.PrimaryApi) && c.In.RenoDxMod is null)
        {
            var dxvk = Opt(Method.Dxvk, Availability.Available,
                $"{GraphicsApiInfo.Label(c.In.Game.Effective.PrimaryApi)} game with no HDR mod of its own: DXVK runs it on Vulkan, and the Lilium HDR build can output scRGB HDR (start with the Safest preset). Install it from the DXVK section on this page; it is never picked for you.");
            list.Add(c.In.Game.AntiCheat is { } ac ? dxvk with { Warning = AntiCheatCaution(ac) } : dxvk);
        }
        // Row 16: a Luma mod the app can install, or a game OptiScaler's list shows running one that the Luma catalog has no download for.
        if (list.All(m => m.Method != Method.Luma))
        {
            if (LumaOffered(c) is { } luma)
            {
                rows.Add(16);
                list.Add(luma);
            }
            else if (c.In.OptiCompat is { } listed
                     && (listed.List == "lumaUe" || (listed.List == "upscalerMods" && listed.Notes?.Contains("Luma", StringComparison.OrdinalIgnoreCase) == true)))
            {
                rows.Add(16);
                list.Add(NoLumaDownload("OptiScaler's list shows this game running a Luma framework mod (DX11 only)"));
            }
        }
        if (c.In.HudfixIncompatible && list.Any(m => m.Method is Method.OptiScaler or Method.OptiScalerNr))
        {
            rows.Add(17);
            for (var i = 0; i < list.Count; i++)
                if (list[i].Method is Method.OptiScaler or Method.OptiScalerNr)
                    list[i] = list[i] with { Reason = list[i].Reason + " OptiFG can't work here: OptiScaler's wiki lists the game as HUDfix-incompatible, use the game's own frame generation or Smooth Motion." };
        }
        // Row 18: a second neural consumer. An install replaces the routes it doesn't keep (Installer.RoutesOf counts only the new selection
        // plus the DX11 bridge), so only the bridge is a clash the install would leave. A method whose route clashes with it
        // (NrAddons.Conflict, the rule the game page's conflict banner uses) carries the reason as a warning: installing it asks
        // for a confirmation and it is never recommended.
        if (c.In.InstalledRoutes.Count > 0)
        {
            rows.Add(18);
            var left = c.In.InstalledRoutes.Where(r => r == NrAddon.Bridge).ToList();
            for (var i = 0; i < list.Count; i++)
                if (list[i].Route is { } route && NrAddons.Conflict(route, left) is { } why)
                    list[i] = list[i] with { Warning = why + " Installing it asks you to confirm, or remove the bridge files first." };
        }
    }

    // ---------- option builders ----------

    /// <summary>Luma is advice only (installed from its own section, never picked for you): the game has a mod, or the generic Unreal one is listed for it.</summary>
    private static MethodOption? LumaOffered(Ctx c) => c.In.Luma is { IsOffered: true } l
        ? Opt(Method.Luma, Availability.Available,
            $"Luma has a mod for this game ({l.Title}); install it from the Luma section on this page. Don't stack it with a RenoDX HDR mod (the app asks before it lets you)." +
            (l.NeedsDgVoodoo ? " It needs dgVoodoo2, which you add yourself (the section lists the setup)." : ""))
        : null;

    private static MethodOption NoLumaDownload(string what) =>
        Opt(Method.Luma, Availability.NotPossible, $"{what}, but the Luma catalog has no download for this game that the app can place. Don't stack a Luma mod with a RenoDX HDR mod.");

    private static MethodOption Opt(Method m, Availability a, string reason, IReadOnlyList<string>? components = null,
        IReadOnlyList<string>? needs = null, InstallMode? mode = null, bool unverified = false) =>
        new(m, TitleOf(m), a, reason, components ?? [], needs ?? [], mode, unverified);

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string NrCost(GpuInfo g) => g.Tier switch
    {
        RtxTier.Rtx50 => "Full speed on RTX 50.",
        RtxTier.Rtx40 => "RTX 40 runs NR at moderate cost with a community build of nvngx_dlssnr.dll.",
        RtxTier.Rtx20 or RtxTier.Rtx30 => "RTX 20/30 run NR at heavy cost and need a community build of nvngx_dlssnr.dll.",
        _ => "",
    };

    private static MethodOption DlssUpdate(Ctx c, string reason, bool unverified = false) =>
        Opt(Method.DlssUpdate, Availability.Available, reason, [ComponentRegistry.Dlss], null, null, unverified);

    private static string? NrRuntimeNeed(Ctx c) =>
        c.Has(ComponentRegistry.DlssNr) ? null : "nvngx_dlssnr.dll (downloaded automatically at the next update check, or import it)";

    private static MethodOption Neural(Ctx c, string reason, string? extraNeed, bool unverified = false, string? unverifiedWhy = null)
    {
        // The add-on already in the game stays (the install keeps it); otherwise the first one the user has imported, and GITC Uplift downloads itself when none is.
        var addon = new[] { (NrAddon.RenoDx, ComponentRegistry.RenoDx), (NrAddon.Lecram, ComponentRegistry.Lecram), (NrAddon.Dfc, ComponentRegistry.Dfc), (NrAddon.Uplift, ComponentRegistry.Uplift) }
            .Where(x => c.In.InstalledRoutes.Contains(x.Item1)).Select(x => x.Item2).FirstOrDefault()
            ?? new[] { ComponentRegistry.RenoDx, ComponentRegistry.Lecram, ComponentRegistry.Dfc }.FirstOrDefault(c.Has) ?? ComponentRegistry.Uplift;
        var needs = new List<string>();
        if (NrRuntimeNeed(c) is { } nr) needs.Add(nr);
        if (extraNeed is not null) needs.Add(extraNeed);
        var cost = NrCost(c.Gpu);
        if (unverifiedWhy is not null) reason += $" Unverified: {unverifiedWhy}.";
        var route = addon == ComponentRegistry.RenoDx ? NrAddon.RenoDx : addon == ComponentRegistry.Lecram ? NrAddon.Lecram : addon == ComponentRegistry.Dfc ? NrAddon.Dfc : NrAddon.Uplift;
        return Opt(Method.NeuralAddon, needs.Count == 0 ? Availability.Available : Availability.NeedsSetup,
            $"{reason} {cost}".Trim() + (addon == ComponentRegistry.Uplift ? " GITC Uplift downloads itself; ShortFuse's, Lecram's and Deep Fried Chicken's are imported." : ""),
            [ComponentRegistry.ReShade, ComponentRegistry.DlssNr, addon], needs, InstallMode.ReShadeOnly, unverified) with { Route = route };
    }

    private static MethodOption OptiNr(Ctx c, string reason)
    {
        var needs = NrRuntimeNeed(c) is { } nr ? new List<string> { nr } : [];
        return Opt(Method.OptiScalerNr, needs.Count == 0 ? Availability.Available : Availability.NeedsSetup, $"{reason} {NrCost(c.Gpu)}".Trim(),
            [ComponentRegistry.OptiScaler, ComponentRegistry.DlssNr], needs, InstallMode.OptiScaler) with { Route = NrAddon.OptiScaler };
    }

    private static MethodOption Opti(Ctx c, string reason)
    {
        var compat = c.In.OptiCompat;
        if (compat is { Compat: "not-working" })
            return Opt(Method.OptiScaler, Availability.NotPossible, "OptiScaler's own list says it does not work in this game" + (string.IsNullOrWhiteSpace(compat.Notes) ? "." : $": {compat.Notes}"));
        if (compat is { Compat: "single-os" }) reason += " OptiScaler's list says it works on a single OS only.";
        if (compat is { Notes.Length: > 0 }) reason += $" Wiki note: {compat.Notes}";
        return Opt(Method.OptiScaler, Availability.Available, reason, [ComponentRegistry.OptiScaler], null, InstallMode.OptiScaler) with { Route = NrAddon.OptiScaler };
    }

    // The add-on the app installs (mavismmg's MFGAdaUnlock) has two backends since 1.5.0 (README "Choosing a backend"): DLSS-G, the
    // default, for RTX 40 only, and Intel XeMFG for RTX 20/30/40/50 (D3D12, Intel files from its panel, no exclusive fullscreen).
    // An unreadable GPU never hides it.
    private static MethodOption MfgUnlock(Ctx c)
    {
        if (c.Gpu.Tier == RtxTier.Rtx50)
            return Opt(Method.MfgUnlock, Availability.NotPossible, "Not needed: RTX 50 has multi frame generation built in.");
        if (!c.Gpu.IsKnown || c.Gpu.Tier == RtxTier.Rtx40)
            return Opt(Method.MfgUnlock, Availability.Available, "Unlocks DLSS multi frame generation on RTX 40 in a game that ships DLSS-G (its default DLSS-G backend).", [ComponentRegistry.MfgUnlock]);
        if (!c.Gpu.IsNvidia || !c.Gpu.IsRtx)
            return Opt(Method.MfgUnlock, Availability.NotPossible, "Needs a GeForce RTX card: DLSS-G on RTX 40, Intel XeMFG on RTX 20 and 30.");
        if (c.ApiKnown && c.Api != GraphicsApis.Dx12)
            return Opt(Method.MfgUnlock, Availability.NotPossible, "On RTX 20 and 30 it only works through its Intel XeMFG backend, which needs D3D12.");
        return Opt(Method.MfgUnlock, Availability.NeedsSetup,
            "Multi frame generation on RTX 20 and 30 through its Intel XeMFG backend (XeSS frame generation fed by the game's DLSS-G inputs, 2x to 6x). The DLSS-G backend stays RTX 40 only.",
            [ComponentRegistry.MfgUnlock],
            ["Set General → Frame generation method to Intel XeMFG in the MFG Unlock panel (ReShade overlay), then restart the game",
             "Install Intel's libxess_fg.dll and libxell.dll from the panel: Diagnostics → Intel runtime files → Install / update files",
             "Borderless or windowed display mode: XeMFG keeps frame generation off in exclusive fullscreen"]);
    }

    private static MethodOption SmoothMotion(Ctx c, string reason)
    {
        var ok = !c.Gpu.IsKnown || c.Gpu.Tier >= RtxTier.Rtx40;
        return ok
            ? Opt(Method.SmoothMotion, Availability.Available, $"{reason} NVIDIA's driver does it; switch it on in the game's NVIDIA tab. It needs RTX 40 or 50 (the research marks that as unverified).", unverified: true)
            : Opt(Method.SmoothMotion, Availability.NotPossible, "Smooth Motion needs an RTX 40 or 50 card (the research marks that as unverified).", unverified: true);
    }

    /// <summary>Feeder with Deep Fried Chicken, with what it needs: the imports, a motion-vector shader and the route's manual steps.</summary>
    private static MethodOption Feed(Ctx c, string? beta, string reason, string? routeNeed, bool unverified = false, string? unverifiedWhy = null)
    {
        var needs = new List<string>();
        if (NrRuntimeNeed(c) is { } nr) needs.Add(nr);
        if (!c.Has(ComponentRegistry.Dfc)) needs.Add("Import Deep Fried Chicken 4 or newer (it has no public download)");
        else if (!c.In.DfcRunsWithoutDlss) needs.Add("Import Deep Fried Chicken 4 or newer: 2.x needs the game's own DLSS");
        if (!c.In.MotionProviderPresent)
            needs.Add("A motion-vector shader in reshade-shaders\\Shaders (LumeniteFX Kernel is recommended; its licence stops the app from shipping it)");
        if (routeNeed is not null) needs.Add(routeNeed);
        if (c.Is32) needs.Add("The 64-bit half in host64\\: a 64-bit ReShade and Deep Fried Chicken's 64-bit set (the app places only Feeder's files there)");
        if (beta is not null) reason += " The README calls this route beta.";
        if (unverifiedWhy is not null) reason += $" Unverified: {unverifiedWhy}.";
        if (unverified && unverifiedWhy is null) reason += " Unverified: low confidence in the research.";
        return Opt(Method.Feeder, needs.Count == 0 ? Availability.Available : Availability.NeedsSetup, reason,
            [ComponentRegistry.ReShade, ComponentRegistry.DlssNr, ComponentRegistry.Dfc, ComponentRegistry.Feeder], needs, InstallMode.ReShadeOnly, unverified) with { Route = NrAddon.Dfc };
    }

    /// <summary>
    /// The order of the table: what can be used first (Available before NeedsSetup, ties in table order), then what the app doesn't
    /// install yet, then what can't be done (still shown, with its reason). The HDR and DXVK add-ons that work next to a method stay behind the methods.
    /// </summary>
    private static List<MethodOption> Rank(List<MethodOption> methods) =>
        methods.Select((m, i) => (m, i))
            .OrderBy(x => x.m.Availability switch { Availability.NotPossible => 3, Availability.Coming => 2, _ => 0 })
            .ThenBy(x => x.m.Method is Method.RenoDxHdr or Method.Dxvk ? 1 : 0)
            .ThenBy(x => x.m.Availability == Availability.NeedsSetup ? 1 : 0)
            .ThenBy(x => x.i).Select(x => x.m).ToList();
}
