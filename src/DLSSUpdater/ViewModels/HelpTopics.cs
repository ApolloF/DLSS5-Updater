using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

public sealed record HelpTopic(string Id, string Group, string Title, string Body);

/// <summary>A guide row: the topic and, for dropdowns, what each choice does.</summary>
public sealed record GuideEntry(HelpTopic Topic, IReadOnlyList<OptionChoice> Choices)
{
    public bool HasChoices => Choices.Count > 0;
    public string? Advice => HelpTopics.AdviceFor(Topic.Id);
}

/// <summary>Text behind the ⓘ buttons; the About page lists all of them as the guide.</summary>
public static class HelpTopics
{
    internal static string LecramSourceIntro(bool channelEnabled) => channelEnabled
        ? "Lecram v11 builds are fetched from the author's public MediaFire channel during update checks. In Local components, turn automatic checks on or off, use Check now and see the last result. Older builds can still be imported. "
        : "This app doesn't fetch Lecram's builds: you import the add-on (dlss5-nr.addon64 or renodx-dlss5.addon64) in Local components. The builds are available to supporters on the author's Buy Me a Coffee. ";

    internal static string LecramUpdates(bool channelEnabled) => channelEnabled
        ? "The public MediaFire folder is the same source used by the author's DLSS5-NR-Setup installer. " +
          "The newest upload wins; ties prefer the raw add-on, then ZIP, then setup EXE. Each download is checked against the listing's SHA-256. " +
          "Only dlss5-nr.addon64 is extracted; downloaded installers are never run and are removed afterwards. Channel moves are followed once. " +
          "Builds enter the existing library and only a newer build becomes its default. Games keep their installed build until you choose to change it. " +
          "Offline or failed checks keep the library. New builds can have options this app does not list; tune those in the ReShade overlay. No nvngx_dlssnr.dll is downloaded."
        : "Lecram's builds are available to supporters on the author's Buy Me a Coffee, so this app no longer checks for or downloads them. " +
          "Import a new build in Local components (the add-on file itself; extract archives first). " +
          "Builds enter the existing library and only a newer build becomes its default. Games keep their installed build until you choose to change it. " +
          "Builds already in the library keep working. New builds can have options this app does not list; tune those in the ReShade overlay. No nvngx_dlssnr.dll is downloaded.";

    public static readonly HelpTopic[] All =
    [
        new("uplift", "GITC Uplift", "GITC Uplift",
            "GITC Uplift is an open-source ReShade add-on (MIT licence) that runs DLSS-NR on the game image. Single-player only: injection can trigger anti-cheat. " +
            "Use it instead of ShortFuse, Lecram, Deep Fried Chicken or OptiScaler-NR; these NR routes cannot coexist. " +
            "This app installs the 64-bit add-on, its helper and Uplift effects. ReShade with full add-on support is required; Vulkan and OpenGL need ReShade 6.8+. " +
            "Import your own nvngx_dlssnr.dll. It goes in GITC-Uplift beside the add-on because Uplift blocks a runtime mapped from the game folder (NVIDIA DLSS can load that copy first on RTX 50). " +
            "The app writes Enabled=1. In the game press Home, then open the GITC Uplift tab. Tested with 1.4.2; newer tags warn in the log. " +
            "Preset B needs NR 310.9.16. NGX hooks Off is safe mode, Present only. Use D3D9Ex changes exclusive fullscreen to borderless. Mask blend values are undocumented."),
        // ---------- General ----------
        new("install-mode", "General", "Install mode",
            "OptiScaler (default; the build chosen under OptiScaler build) is the main use: OptiScaler is the proxy, loads ReShade and its add-ons, and runs the NR runtime. " +
            "ReShade + add-ons installs ReShade itself as the proxy (dxgi.dll by default) with MFG Unlock, the DLSS and Streamline updates, and one NR route instead of OptiScaler's: the ShortFuse RenoDX DLSS add-on, the Lecram DLSS5 add-on, Deep Fried Chicken or GITC Uplift. Turning one on turns the others off; ShortFuse and Lecram can run together only with Allow more than one NR add-on. Each needs an imported NR runtime. " +
            "Switching a game between the two removes the other mode's files and puts back what they replaced. Each game can use its own mode."),
        new("proxy", "General", "Proxy DLL name",
            "The file name the game loads the mod as: OptiScaler's in OptiScaler mode, ReShade's in ReShade + add-ons mode (dxgi.dll for DirectX 10-12, d3d9.dll for DirectX 9, opengl32.dll for OpenGL). dxgi.dll works for most DX12 games. " +
            "In OptiScaler mode, pick another (winmm.dll, version.dll, dbghelp.dll) when the game or another mod already needs dxgi.dll, " +
            "or when a guide says so (e.g. Cyberpunk uses dbghelp.dll). Existing OptiScaler installs keep their name."),
        new("dlss-version", "General", "DLSS version",
            "Latest only ever upgrades: a game DLL that is already newer is left alone. " +
            "A pinned version is installed exactly, also downgrading, useful when a newer DLSS preset looks worse in a game. " +
            "Older SDKs lack files (before 310.4 no FG, 3.x only SR); missing ones are skipped. A game can pin its own version above its DLSS file list."),
        new("opti-build", "General", "OptiScaler build",
            "Which fork of OptiScaler is installed. OptiScaler F5 (janblade) is the default for new installs; OptiScaler-NR (ShyVortex) is the fork this app was first built around. " +
            "F5 is experimental and releases often; it needs its own open-source NGX forwarder (nvngx.dll_dlssnr.dll), which is installed next to OptiScaler and removed again when you switch back. " +
            "A few DLSSNR options it does not have are hidden. Games keep the build they have until you update them."),
        new("prereleases", "General", "Include prereleases",
            "The OptiScaler-NR fork only publishes prereleases, so turning this off usually means no OptiScaler updates. Applies to every source."),
        new("streamline", "General", "Replace Streamline files",
            "Streamline (sl.*.dll) is NVIDIA's layer between the game and DLSS Frame Generation. " +
            "Dynamic MFG only works with FG 310.9.1 plus the complete Streamline 2.14.1 set, or FG 310.9.16 plus 2.14.16. This replaces every sl.*.dll the game already ships " +
            "with the signed files from one SDK release, never mixing versions and never adding plugins. Restore DLSS puts the originals back. " +
            "Off by default: an unusual game setup can break frame generation until restored."),
        new("add-missing", "General", "Add nvngx_dlss.dll when missing",
            "Games without DLSS (FSR/XeSS only) have no nvngx_dlss.dll. OptiScaler needs it to use DLSS as the upscaler, so in OptiScaler mode it is placed next to the game's .exe."),
        new("ini-mode", "General", "Existing game configs",
            "The app remembers which config values it wrote itself. Fresh config rebuilds each installed mod's config from its defaults plus your settings: " +
            "OptiScaler.ini from the release, the add-on's sections in ReShade.ini, and deep-fried-chicken.cfg from the import. " +
            "ReShade's own settings stay. Old files are backed up first and Uninstall restores them."),
        new("presets", "General", "Presets and defaults",
            "Out of the box every setting is what the upstream projects ship: the release's OptiScaler.ini untouched, ReShade and MFG Unlock without an ini. " +
            "Only two keys are added because the components would otherwise do nothing: LoadReshade=true when ReShade is installed with OptiScaler and " +
            "[DlssNr] Enabled=true when the NR runtime is installed. '" + Core.ConfigProfile.RecommendedName + "' is a built-in preset with the tuned setup " +
            "(DLSS upscaler, preset override, Delete menu key, NR strengths and HDR tone mapping, ReShade tutorial skipped). Load it here or assign it to single games."),
        new("nr-library", "General", "NR versions and sources",
            "OptiScaler-NR updates come from ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass. Existing cached releases still work. " +
            "The RenoDX DLSS add-on (ShortFuse's renodx-dlss.addon64) is imported by you, not downloaded. It needs ReShade + add-ons mode, ReShade 6+ with full add-on support, a game with native DLSS and an imported NR runtime. " +
            "The runtime nvngx_dlssnr.dll is always imported by you too. " +
            "ShortFuse openNR describes compatible FP16 builds, including compat-fp16-310.9.16-cuda13.4.3 for RTX 20/30/40. " +
            "Versions come from DLL resources; hashes distinguish originals, compat builds and unverified files. " +
            "Choose a default in Settings or a per-game version, then Switch NR. Switch back restores the previous build without changing other components."),
        new("local-components", "General", "Local components",
            "nvngx_dlssnr.dll (DLSS 5 neural rendering model) is always user-supplied. ShortFuse compat-fp16-310.9.16-cuda13.4.3 targets RTX 20/30/40. Each imported version is kept in your library " +
            "and copied into each game. ReShade64.dll (add-on build) is taken from the official setup on reshade.me whenever a new version is out; an imported " +
            "ReShade64.dll replaces the download until you press Use download. Import a newer file here and Update all rolls it out. " +
            "ShortFuse's renodx-dlss.addon64 is imported the same way and only accepted when the file identifies itself as renodx-dlss.addon64; every import stays in a library of builds, with a default here, an optional pick per game and your own name per build. " +
            "Deep Fried Chicken is imported from its extracted release folder or its 64-bit folder (Folder…), from deep-fried-chicken.addon64 in that folder or from a .zip of the release (Import…); the NGX bridge deep-fried-chicken-nvngx.dll must be beside the add-on. From 4.x the whole 64-bit folder is taken, as the package's README asks. A .7z can't be read: extract it first. " +
            "Each import is kept as a build of its own, so several DFC versions sit side by side; importing the same add-on again replaces that build's files. The default is Newest unless you pick a build. " +
            "Lecram's DLSS5 add-on is imported as renodx-dlss5.addon64 (v10 and older) or dlss5-nr.addon64 (v11 and newer); all builds are kept side by side and each game picks its own."),
        new("dfc", "General", "Deep Fried Chicken",
            "Deep Fried Chicken (DFC) is a closed-source ReShade add-on that runs NVIDIA DLSS neural rendering. 2.x needs a game with native DLSS; 4.x and newer also run in games without their own DLSS (see Deep Fried Chicken without DLSS). " +
            "Its licence is personal use only, so this app never downloads or redistributes it: you import your own copy (add-on, NGX bridge and default config) in Local components, and the files are copied into games unchanged. " +
            "Every import stays in a library of builds. The default in Settings is Newest (the highest version, follows new imports) until you pick a build; a game can pick its own build on its page, and that build decides the files, the default cfg and whether games without DLSS work. " +
            "Switching a game to another build replaces the whole set; its deep-fried-chicken.cfg stays. Builds stay until you remove them: the automatic cleanup never deletes an import. " +
            "From 4.x the whole 64-bit folder goes beside the game exe, as the package asks: in 4.0.6 and 5.0 that is the add-on, the bridge, deep-fried-chicken-present-support.dll, the deep-fried-chicken-neural folder and reshade-shaders\\Shaders\\DFC_OutputState.addonfx. " +
            "Logs, NVIDIA's nvngx_*.dll and ReShade's own files in that folder are left out (the app places those itself), and ReShade.ini's EffectSearchPaths gets .\\reshade-shaders\\Shaders\\** when it doesn't cover that folder yet. " +
            "DFC's 32-bit route (addon32 plus a 64-bit companion host) is not supported by this app. " +
            "It needs ReShade + add-ons mode, ReShade 6+ with add-on support and an imported nvngx_dlssnr.dll (placed beside the exe), and it replaces the RenoDX DLSS add-on and OptiScaler-NR (a game gets only one of them). " +
            "The default deep-fried-chicken.cfg is placed when the game has none. The few options in Settings are merged into it like into ReShade.ini: only keys you set are written, and values you tuned in the overlay survive updates. " +
            "Everything else (hook mode, placement, Auto Balance, motion stability, glow suppression, per-pass model, preset and intensity, the 4.x Refine tab, pass controls, frame generation, 5.0's Texture Boost and NR preset (Game, Preset A or Preset B) and so on) is tuned in the ReShade overlay (Home, Deep Fried Chicken tab); the cfg numbers for those are undocumented."),
        new("dfc-no-dlss", "Deep Fried Chicken", "Deep Fried Chicken without DLSS",
            "From 4.x (INSTALL.txt of 4.0.6 preview) DFC also runs in games without their own DLSS. There it runs NR with its own motion vectors (the package's motion model, dfc-neural-motion.onnx, is in the deep-fried-chicken-neural folder, which goes beside the exe whole), " +
            "or with the vectors of an external ReShade motion provider when one is running (Lumenite Kernel or QuantMotion, VORT, Launchpad, or a texMotionVectors effect such as DRME, qUINT or dh_uber_motion; DX10, DX11, DX12, Vulkan and OpenGL, not DX9). " +
            "NR works on top of the game's own anti-aliasing; lower Resolution under Pass controls in the overlay for speed. There is no DLSS upscaling in these games: 4.x removed DFC's own upscaler (SR), scene jitter and the virtual display. " +
            "INSTALL.txt asks for nvngx_dlss.dll and nvngx_dlssnr.dll beside the game exe; DFC ships neither. The app places your imported nvngx_dlssnr.dll and adds nvngx_dlss.dll from NVIDIA's DLSS release (the version picked for DLSS files), and takes both out again when DFC goes. " +
            "In games with native DLSS nothing is added: DFC uses the game's own DLSS. A 2.x import is refused in games without DLSS. " +
            "Frame generation for these games is switched on in the overlay (Rendering card); it needs borderless or windowed mode and pauses while the ReShade menu is open. DLSS-G needs an RTX 40/50 GPU and NVIDIA's nvngx_dlssg.dll beside the exe: tick Add NVIDIA's frame generation file (nvngx_dlssg.dll) on the game's Install tab and the app places it from the DLSS release. " +
            "FSR 3.1 frame generation needs AMD's amd_fidelityfx_*_dx12.dll, which this app does not provide. " +
            "The cfg has frame_generation_coexistence, experimental_fg2x_enabled and nr_interpolation_enabled, but no comment or note says what they do or which value picks DLSS-G or FSR 3.1, so the app does not set them."),

        new("lecram", "General", "Lecram DLSS5 add-on",
            LecramSourceIntro(BuildFlags.LecramChannel) +
            "There are two generations: v10 and older (renodx-dlss5.addon64, recognised by its version resource) and v11 and newer (dlss5-nr.addon64, recognised by strings in the file because it has no version resource). " +
            "Every import is kept as its own build, named by the highest vX.Y.Z string in the file or by you; a game only ever gets one file, because v11 goes inert when a renodx-dlss5.addon64 file sits next to it (from v11 beta.22 not when ReShade has that add-on disabled). Settings picks the default build and each game can pick its own, whose generation decides the options shown. " +
            "It needs ReShade + add-ons mode, ReShade 6+ with add-on support, a game with native DLSS and an imported nvngx_dlssnr.dll, and it replaces OptiScaler-NR and Deep Fried Chicken. " +
            "It can run next to ShortFuse's RenoDX add-on when Allow more than one NR add-on is on (NR then runs twice): the app then sets ForeignNr=observe. The 0928 pair (ShortFuse 0.2026.0928 and Lecram v8.5.0-rc10) was tested working. Each exact pair of builds has a status (Works, Crashed or not tested together yet); only Crashed shows a warning. Mark the current build pair on the Install tab. " +
            "A copy of either Lecram file already in the game is moved to the backup folder and restored by Uninstall. Options that exist in only one generation are hidden for the other, following the build the game uses. " +
            "From v11 Patch 4 (beta.18) the author also ships its own installer (DLSS5 NR Setup). It finds the add-on in every game on every drive and its Update all replaces it there, games this app manages included, so manage a game with one of the two. " +
            "It keeps a shared nvngx_dlssnr.dll in %LOCALAPPDATA%\\DLSS5-NR\\runtime, which v11 also finds by itself."),

        new("lecram-updates", "General", "Lecram update source", LecramUpdates(BuildFlags.LecramChannel)),

        // ---------- Keybinds ----------
        new("keybinds", "Keybinds", "Keybinds",
            "OptiScaler keys are single keys (no modifiers); ReShade keys may include Ctrl / Shift / Alt. " +
            "Default leaves the game's own default, Off disables the key. Changes apply on the next install or update; " +
            "a key you already changed in-game keeps your in-game choice only when Existing configs is set to Keep game settings."),

        // ---------- ReShade ----------
        new("skip-tutorial", "ReShade", "Skip the tutorial",
            "Marks ReShade's first-run guide as finished (TutorialProgress=4), so the overlay opens straight to the effect list. Lecram v11 beta.29 and newer write 4 themselves when the key is absent or 0."),
        new("performance-mode", "ReShade", "Performance mode",
            "ReShade compiles effects with their current values baked in as constants. Gives more FPS, but effect settings can't be tweaked in the " +
            "overlay while it's on. Tweak with it off, then turn it on for playing. Only affects ReShade effects, not OptiScaler or DLSS."),
        new("show-fps", "ReShade", "Show FPS",
            "ReShade's own FPS counter in a screen corner. OptiScaler has a separate, more detailed overlay on its own key."),
        new("load-early", "ReShade", "Load MFG Unlock early",
            "Adds the add-on to ReShade's LoadFromDllMain list so it loads while ReShade itself starts. Some games (e.g. Cyberpunk) start Streamline before " +
            "ReShade normally loads add-ons; without this, MFG Unlock can't apply its runtime settings there. Leave off unless a game needs it."),

        // ---------- RenoDX DLSS ----------
        new("rdx-stage", "RenoDX DLSS", "Where it runs",
            "Render works on the game's native input before DLSS SR / DLAA / RR. Upscaled works on the DLSS output. Present works on the Streamline swapchain buffer " +
            "just before presentation. The add-on's own default is Off, so leaving this on Add-on default means the model does not run until you pick a stage here or in the overlay."),
        new("rdx-passes", "RenoDX DLSS", "Passes",
            "Model passes per frame, 1 to 10 (default 1). The add-on doesn't document the cost per pass; expect each pass to add roughly one model evaluation."),
        new("rdx-strengths", "RenoDX DLSS", "Model strengths",
            "Intensity (DLSSNR.Intensity) scales everything. Structure adds fine detail, tone changes local contrast and lighting. All three go from 0 to 1.0; the add-on ignores higher values. " +
            "Defaults: intensity 1.0, structure 0.7, tone 0.3 (builds before 2026-10-16 used 1.0 for both)."),
        new("rdx-model", "RenoDX DLSS", "Model",
            "Model A, B or C. The add-on passes this to the DLL's DLSSNR.Style, the same setting OptiScaler calls Style. " +
            "The add-on doesn't say which look each letter is; compare them in-game."),
        new("rdx-preset", "RenoDX DLSS", "Preset",
            "Sent to the DLL's DLSSNR.Hint.Render.Preset, the same hint OptiScaler's Preset sets: Preset A is the main model, Preset B the small one in 310.9 DLLs. " +
            "Default lets the DLL choose. The setting exists from the 2026-10-16 build on; older builds ignore it."),
        new("rdx-mask", "RenoDX DLSS", "Character mask",
            "Finds people in the picture. Character intensity sets the detail strength on them separately."),
        new("rdx-reuse", "RenoDX DLSS", "DLSS resources",
            "Optional reuses the motion vectors and depth of DLSS SR / DLAA / RR when available and uses dummy inputs otherwise. " +
            "Required skips Present neural rendering when those resources are unavailable. Never always uses dummy inputs."),
        new("rdx-scaling", "RenoDX DLSS", "Scaling filters",
            "How the image is shrunk to the resolution scale and enlarged again: the colour space it is resized in, the down- and upsample filters, " +
            "and whether the model's change (RGB residual) or the processed image is scaled back. They only matter below or above 100%. " +
            "The add-on documents none of these values; only the names and defaults are known."),
        new("rdx-signal", "RenoDX DLSS", "NR signal encoding",
            "The encoding handed to the model after the add-on's HDR-to-SDR bridge: sRGB (default), PQ SDR or Linear SDR. The add-on does not explain when to change it."),
        new("rdx-exposure", "RenoDX DLSS", "Exposure",
            "Exposure multiplies the model's exposure independently of the game's exposure texture (default 1.0x). " +
            "Use exposure value is Off, On (default) or Inverse; the add-on does not document what each does."),
        // ---------- Lecram DLSS5 ----------
        new("lc-enable", "Lecram DLSS5", "Neural rendering",
            "NeuralUplift in [RenoDX.DLSS5]: 0 turns neural rendering off (v11 and newer; v10 does not read it). The add-on's default is not documented beyond that, so Add-on default writes nothing."),
        new("lc-hook", "Lecram DLSS5", "Where it runs",
            "NRHookPoint: 0 After upscaling (the add-on calls it Upscaled), 1 Before upscaling (Render), 2 Present, 3 Auto. After upscaling runs on DLSS's output, before the game draws its HUD and screen effects; Present reads the finished frame, effects included. Auto exists from v11 and means Present with After upscaling as the fallback. " +
            "Defaults: v11 beta.18 and newer (Patch 4) After upscaling, v11 beta.6 (Patch 3) Auto, v10.7 Present (no Auto; it converts a stored 0 to Present when it migrates an older config). In v11 an invalid value reads as the build's default. " +
            "When a v11 build first loads a ReShade.ini written by v10 (ConfigVersion 10 or lower), it sets NRHookPoint once to its default (beta.18+ After upscaling, beta.6 Auto). The app applies v11's one-time migration itself when it installs a v11 build with a hook point chosen, so the chosen hook point is kept; without a chosen hook point v11's own migration sets its default."),
        new("lc-strengths", "Lecram DLSS5", "Model strengths",
            "Strength (NRIntensity) goes from 0 to 1 and defaults to 1; higher values clamp (the 0928 build allowed up to 2). Local tone goes from 0 to 2 (default 1). " +
            "Structure goes from 0 to 2 in v10 and 0 to 1 in v11, where higher values clamp (default 1). Character / skin: in v10 it goes from -1 to 1 and -1 follows the stock value (default); in v11 it goes from 0 to 1 (default 0) and negative values read as 0. " +
            "The add-on does not document what a value looks like beyond these ranges."),
        new("lc-style", "Lecram DLSS5", "Style",
            "NRStyle 0, 1 or 2 (default 0). v10 labels them Default, Natural and Cinematic; v11 labels them Model A, B and C. The add-on does not say how they differ."),
        new("lc-mask", "Lecram DLSS5", "Character mask",
            "NRAutoMask: v10 and older only (default off). v11 retired the key and always has the mask on, so the row is hidden there."),
        new("lc-ui", "Lecram DLSS5", "UI correction",
            "NRUICorrection, off by default in both generations. The add-on does not document how it protects the HUD."),
        new("lc-passes", "Lecram DLSS5", "Passes",
            "NRPasses, 1 to 4 (default 1). v11 also has per-pass keys; the app offers the per-pass model (Pass 2 to 4 preset) and leaves the per-pass look (style, tone, structure, skin) to the add-on's overlay. The cost per pass is not documented."),
        new("lc-scale", "Lecram DLSS5", "Resolution",
            "NRResolutionScale, 0.33 to 1 (default 1): the fraction of the frame the model works at."),
        new("lc-preset", "Lecram DLSS5", "Preset",
            "NRPreset, v11 and newer. 0 is the DLL's shipping default (documented); the value is sent as the DLSSNR render preset hint and changing it recreates the NR feature. " +
            "It only matters when the DLL ships several models (otherwise the add-on sends 0 and hides the control). The add-on documents the ids for DLL 310.9.16, which ships two: 1 honest_tench (the model the model lab edits), 2 the small FP8 model. " +
            "From beta.45 (v11 Patch 10) its panel calls the control Model version, with 1 as Old (the first model) and 2 as New (newer, faster and NVIDIA's default); the key and its values did not change. " +
            "It accepts 0 to 3; 3 names no model in 310.9.16, so the app does not offer it, and other DLL versions may number their models differently."),
        new("lc-pass-preset", "Lecram DLSS5", "Pass preset",
            "NRPass2Preset, NRPass3Preset and NRPass4Preset, v11 beta.29 and newer (mixed models): the model one extra pass runs. 0 (default) runs the model pass 1 runs (Preset's choice); 1 to 3 is the DLL's preset id for that pass alone, as for Preset (310.9.16: 1 honest_tench, 2 small FP8 model). " +
            "It only takes effect when Passes reaches that pass and the DLL ships several models; otherwise the add-on sends 0. Other values read as 0. Changing it recreates NR. " +
            "Older v11 builds ignore the keys, so the app hides them and does not write them there. From beta.45 the panel lists the values as As pass 1, Old, New and Preset 3. The pass's own look (\"Same as pass 1\" off in the overlay) is a separate setting."),
        new("lc-vram", "Lecram DLSS5", "VRAM buffering",
            "NRVramBuffering, v11 and newer: 0 Full (default), 1 Reduced, 2 Minimal."),
        new("lc-present", "Lecram DLSS5", "Present runs on",
            "NRPresentGuides: which frames get the model at Present (and Auto). v11: 1 only the game's DLSS frames, 2 Always on (also menus, loading and pause screens, with flat depth and no motion where the game gives none). " +
            "0 is the stock value and flipped meaning: Always on in beta.18+, DLSS frames in beta.6, so the app does not offer it for v11. " +
            "v10.7: 0 Optional (default: the game's guides on DLSS frames, neutral guides on the frames drawn without DLSS), 1 Required (undocumented beyond its name), 2 Never (neutral guides on every frame)."),
        new("lc-fg-path", "Lecram DLSS5", "Present with frame generation",
            "NRPresentFgPath, v11 beta.48 (Patch 11) and newer, a test option the author added for players who see NR blink (diagnostic ROUTE-02, a real frame without NR) at Present with frame generation. " +
            "1 runs NR once per real frame at frame generation's own call and never again on the final frame; 0 (default) keeps the usual path. Only 1 switches: the add-on reads other values as the usual path. " +
            "It only matters when NR runs at Present (or Auto) and frame generation is on. The add-on reads the key when it loads, so restart the game after changing it. Older builds ignore it, so the app hides it and does not write it there."),
        new("lc-feed", "Lecram DLSS5", "HDR brightness source",
            "NRFeedMode: 0 Auto (default), 1 NR's own frame meter, 2 the game's exposure. v10.7 labels them Auto, v1 frame meter and v2 game exposure. " +
            "v11 beta.18+ reads it again: at After upscaling in a Direct3D 12 HDR game it takes the brightness from the game's exposure texture when the game hands one to DLSS (Vulkan keeps the meter); the author offers 1 and 2 to compare. " +
            "v11 beta.6 ignores the key (logged as retired)."),
        new("lc-foreign", "Lecram DLSS5", "With another NR add-on",
            "ForeignNr: yield (default) makes the add-on stand down when another NR producer (NGX feature 18) evaluates in the game; observe keeps it running. " +
            "Running together with ShortFuse's add-on needs observe, so the app writes it automatically when both are installed, unless you set this option yourself."),
        new("lc-hooks", "Lecram DLSS5", "Hooks",
            "EnableHooks: 0 all hooks off, 1 NGX and Streamline, 2 NGX only. v10 defaults to 2; v11 has no value by default and picks automatically, escalating when needed. " +
            "With a Streamline game and DLSS on, 1 is the setting to try; if the game crashes at boot, use 2."),
        new("lc-dx11", "Lecram DLSS5", "Direct3D 11 source",
            "DX11Source: auto (default), native, foreign or off. Present in Direct3D 11 needs native. Direct3D 11 support starts with v10.7; the 0928 build is Direct3D 12 only."),
        new("lc-crash", "Lecram DLSS5", "GPU crash reports",
            "NRGpuCrashWitness, Direct3D 12 only. v11 beta.29 and newer watch the GPU by default: after a freeze or GPU reset they save dlss5-nr-crash-<time>.txt beside ReShade.log (where the GPU stopped relative to NR's work, GPU, driver, NR's last log lines), name it on the panel's Home page at the next launch and turn on Windows' DRED crash breadcrumbs for the next runs. " +
            "0 turns all of it off (the author's documented switch). A non-zero value turns DRED on from every launch. beta.22 reads the key too, with no watcher: there non-zero turns DRED on and the default is off. " +
            "The author asks for the crash file when reporting a freeze. DRED's cost is not documented."),

        // ---------- Deep Fried Chicken ----------
        new("dfc-arm", "Deep Fried Chicken", "Deep Fried Chicken (master switch)",
            "arm in deep-fried-chicken.cfg. Off disarms DFC: its hooks stay out until it is armed again. Restart the game after changing it. Default writes nothing (shipped: on)."),
        new("dfc-enabled", "Deep Fried Chicken", "Neural rendering",
            "enabled in deep-fried-chicken.cfg: live on/off while the hooks stay loaded. Default writes nothing (shipped: on)."),
        new("dfc-passes", "Deep Fried Chicken", "Passes",
            "DFC takes a continuous value from 1.0 to 30.0. Each pass is about one more full model evaluation. The decimal part scales the last pass's contribution, " +
            "but that pass still costs a full evaluation. High counts are a stress test, as the author puts it."),
        new("dfc-scale", "Deep Fried Chicken", "Work scale",
            "neural_work_percent: the resolution the model works at, as a percentage; the installer accepts 10 to 150. 100% is exact full-size processing, 125% and 150% are supersampling and very expensive. " +
            "Below 100% is DFC's experimental native-anchor matched residual mode (on an RTX 5080 at 4K 75% went from about 44 to 57-60 FPS, with a softer image). " +
            "Native Vulkan only supports 100%. Restart the game after changing it."),
        new("dfc-vulkan", "Deep Fried Chicken", "Native Vulkan DLSS",
            "native_vulkan_enabled: lets DFC hook DLSS in native Vulkan games. Off by default since 2.0 Hotfix 6. DirectX games and the external DLSS feeder do not need it. Restart the game fully after changing it."),
        new("dfc-native-look", "Deep Fried Chicken", "Native look",
            "preserve_native_tone_color: keeps the game's own tone and colour instead of the model's. This weakens the neural effect. Its strength is left to the overlay."),
        new("dfc-cleanup", "Deep Fried Chicken", "Multipass cleanup",
            "clean_fry_enabled: cleans the colour cast that builds up over several passes. It can remove some detail. Its strengths are left to the overlay."),
        new("dfc-output", "Deep Fried Chicken", "Output protection and deband",
            "New in 4.x. The shipped cfg calls output_hud_enabled, output_highlights_enabled and output_deband_enabled \"optional output protection; disabled by default\" and says the regions use normalized screen coordinates. " +
            "HUD protection uses the regions output_hud_rect0 to rect3 (four numbers from 0 to 1, by their values x, y, width and height; the order is not documented); the shipped rect0 is a 10% box in the centre of the screen, the others are empty. " +
            "Highlight protection and deband have their own strength (and a deband threshold). What each one does beyond that is not documented, so this app leaves the regions and strengths as they are in the game's cfg. Default writes nothing (shipped: off)."),
        new("dfc-colour", "Deep Fried Chicken", "Colour strength",
            "color_strength: how much of the model's colour is kept. It is a bounded final-composition control and DFC caps it at 1.0. HDR needs no setting: DFC detects SDR, Linear BT.709, scRGB, PQ and scRGB-nl itself."),
        new("rdx-scale", "RenoDX DLSS", "Resolution scale",
            "Percentage of the native DLSS input/output size the model works at; 0% uses the smallest size the model supports. Lower is cheaper and softer."),
        new("rdx-ui", "RenoDX DLSS", "UI correction",
            "Protects the HUD from the model. Auto and Off both turn DLSSNR.UICorrection off; On turns it on."),
        new("rdx-encoding", "RenoDX DLSS", "Colour encoding",
            "How the add-on reads the DLSS input and output. Auto treats them as linear BT.709 RGB; pick another entry to override that when colours look wrong, " +
            "for example HDR10, scRGB or scRGB-nl in an HDR game."),

        // ---------- DLSSNR ----------
        new("nr-enabled", "DLSSNR", "Neural rendering",
            "DLSS 5 neural rendering: NVIDIA's model synthesises detail and lighting on top of the upscaled image, before frame generation sees it. " +
            "Needs nvngx_dlssnr.dll and an NVIDIA driver with NGX feature 18. Automatic turns it on whenever the runtime is installed (upstream default is off). " +
            "Toggle in-game with the DLSSNR key. Undocumented feature, so results vary per game."),
        new("nr-before-sr", "DLSSNR", "Run before upscaling",
            "On: the model edits the lower-resolution input before DLSS SR / RR, which is cheaper and the upscaler cleans up its output. " +
            "Off: it runs on the upscaled image, sharper but costlier. Before RR it sees noisy ray-traced colour, so test per game. " +
            "OptiScaler F5: off by default, and Ray Reconstruction always forces NR after the upscaler, so this only matters without RR."),
        new("nr-finished", "DLSSNR", "Apply to finished picture",
            "Applies NR after the game's post-processing (bloom, grading) instead of at the upscaler. DX12 and the D3D11 bridge only. " +
            "Can also alter the HUD and menus. With Run before upscaling on, the edit is made early and carried to the final image."),
        new("nr-denoise-first", "DLSSNR", "Denoise first",
            "DX12 only. Runs Ray Reconstruction's denoiser first and the model after it, so the model sees a clean image. Takes priority over Run before upscaling."),
        new("nr-deferred", "DLSSNR", "Generate before, apply after upscaling",
            "The model's edit is generated from the low-resolution input and applied to the upscaled image. Cheaper than running the model at output resolution."),
        new("nr-residual", "DLSSNR", "Residual across RR",
            "Only matters with Ray Reconstruction and Run before upscaling: the model's change is carried through the denoiser instead of being cleaned away by it."),
        new("nr-automask", "DLSSNR", "Character mask",
            "Finds people in the picture and treats them separately. On upstream by default; turn it off if faces get odd edges."),
        new("nr-private-upscaler", "DLSSNR", "Private upscaler",
            "The upscaler the model uses for its own work: DLSS (default), the built-in FSR 2.2, the FSR FidelityFX runtime or XeSS. Leave on default unless a guide says otherwise."),
        new("nr-passes", "DLSSNR", "Passes",
            "Number of model layers per frame, each feeding the next. Costs roughly 1x / 2x / 3x the model time. The result is composed once, " +
            "so colours don't compound, but 2 and 3 are deliberately over-processed looks. Multipass is D3D12 only."),
        new("nr-scale", "DLSSNR", "Model resolution",
            "Fraction of the image the model works at, relative to its stage (before SR: render resolution; after: output). " +
            "Cost falls with the square: 50% is about a quarter. Above 100% supersamples and averages down."),
        new("nr-preset", "DLSSNR", "Preset",
            "Picks the network inside nvngx_dlssnr.dll. 310.9 DLLs carry two: Preset A, the main model, and Preset B, a small one at about half the cost " +
            "that looks softer. 310.8 DLLs only have A and ignore B. Compare models here instead of switching NR versions. " +
            "Changing it rebuilds the model feature, which causes a short hitch in-game. Later passes use the same preset."),
        new("nr-style", "DLSSNR", "Style",
            "Built-in model profiles inside the same NVIDIA DLL. Changing it rebuilds the model feature, which causes a short hitch in-game."),
        new("nr-strengths", "DLSSNR", "Model strengths",
            "Intensity scales everything. Local structure adds fine detail and texture. Local tone changes local contrast and lighting. " +
            "Skin structure sets detail on faces separately (follow = same as local structure). Values are undocumented; 1.0 is the model's default."),
        new("nr-colour", "DLSSNR", "Colour strength",
            "Whether the model's colour arrives with its light. 0 keeps the game's hues exactly and lets only brightness change; 1 takes the model's colour. " +
            "Low values keep the game's art direction."),

        // ---------- Tone mapping ----------
        new("tm-mode", "Tone mapping", "Tone mapping",
            "The model expects a display-like image. For linear / HDR frames the pass maps the frame into that range, lets the model work, and maps it back " +
            "(a reversible tone map). The curve decides how highlights are compressed; composed modes blend the model's answer against the original " +
            "luminance, replace modes use the model's picture directly. Frames the game already tone-mapped are passed through untouched. " +
            "OptiScaler F5 adds HLG, PQ and linear curves."),
        new("tm-white", "Tone mapping", "White point source",
            "Which linear value counts as display white for that mapping. Games' exposure differs hugely (measured means from 0.065 to 185 in one game), " +
            "so a wrong white point makes the model see a too dark or blown-out picture. " +
            "OptiScaler F5 adds scanned exposure and, left on default, uses the game's exposure when it has one, else automatic exposure."),
        new("tm-paper", "Tone mapping", "Paper white",
            "Multiplies the white point before the model sees the frame. Below 1 treats the scene as brighter (more highlight headroom, darker midtones for the model); " +
            "above 1 lifts dark scenes. Changes the result, not only what the model sees."),
        new("tm-highlight", "Tone mapping", "Highlight protection",
            "For automatic HDR exposure: 0–100 how much bright areas are protected from being pushed toward white. Higher keeps lamps and sky intact but darkens overall."),
        new("tm-maxratio", "Tone mapping", "Max brightening",
            "The most the pass may brighten any pixel, as a multiple. Darkening is never capped. Guards against bright lights turning into coloured blocks."),
        new("tm-transfer", "Tone mapping", "Transfer strength",
            "How far the frame moves toward the model's picture: 0 is exactly the upscaler's image, 1 the model's, above 1 exaggerates."),
        new("tm-hdrtransfer", "Tone mapping", "HDR brightness transfer",
            "Experimental. Measures the HDR brightness transfer when the edit is generated before SR and applied to the finished picture. " +
            "HDR10 / scRGB with scene-linear input only; adds a clean-frame copy and a small analysis pass."),

        // ---------- HDR output ----------
        new("hdr-force", "HDR output", "Force HDR colour space",
            "OptiScaler makes the game's swapchain HDR even if the game only outputs SDR. Useful with RenoDX-style HDR mods; otherwise leave off."),
        new("hdr-10", "HDR output", "Use HDR10",
            "When forcing HDR, use the 10-bit HDR10 format instead of 16-bit float scRGB. Less bandwidth; scRGB keeps more precision."),
        new("hdr-skip", "HDR output", "Skip colour space",
            "Don't set the HDR colour space; for games or mods that set it themselves and get wrong colours when OptiScaler does too."),

        // ---------- NVIDIA driver ----------
        new("nv-features", "NVIDIA", "Detected game features",
            "The NVIDIA tab reads which DLSS runtimes the game ships: nvngx_dlss / dlssd / dlssg.dll and Streamline's sl.dlss / sl.dlss_d / sl.dlss_g / sl.reflex.dll. " +
            "Presets for features the game doesn't have are moved under 'settings this game doesn't use', since the driver override can't add them. " +
            "OptiScaler counts as DLSS Super Resolution once it has nvngx_dlss.dll. Games that fetch DLSS only through NVIDIA's OTA updates can't be detected from files."),
        new("nv-sr-preset", "NVIDIA", "DLSS Super Resolution preset",
            "Tells the driver which DLSS SR model preset to use for this game, whatever the game asks for (like the NVIDIA App's DLSS Override - Model Presets). " +
            "The preset and the SR override switch are written together, as the NVIDIA App stores them; Off writes both as off; Use global removes both. " +
            "OptiScaler has its own preset override (RenderPresetOverride in the profile); if both are set, OptiScaler's wins in games it handles."),
        new("nv-sr-mode", "NVIDIA", "DLSS render resolution",
            "Forces the internal render resolution DLSS upscales from, independent of the game's quality menu. Custom uses an exact percentage per axis."),
        new("nv-rr-preset", "NVIDIA", "DLSS Ray Reconstruction preset",
            "Model preset for Ray Reconstruction (the DLSS denoiser in path-traced games). Only matters in games that use RR. Current presets are D and F (A–C were removed)."),
        new("nv-fg-preset", "NVIDIA", "DLSS Frame Generation preset",
            "Which DLSS FG model the driver loads, together with the FG override switch. NVIDIA default is what the NVIDIA App calls 'Default'; A and B are the presets drivers currently offer."),
        new("nv-mfg", "NVIDIA", "Multi frame generation",
            "Driver-side multiplier override for DLSS FG (the NVIDIA App's 'Multi Frame Generation' override). Native 3x/4x needs an RTX 50 card; " +
            "on RTX 40 use MFG Unlock instead."),
        new("nv-smooth-motion", "NVIDIA", "Smooth Motion",
            "Driver-level frame generation that works in games without DLSS FG (DX11, DX12, Vulkan). Needs RTX 40/50 and driver 571.86+. " +
            "Don't combine with in-game FG."),
        new("nv-rtx-hdr", "NVIDIA", "RTX HDR",
            "AI conversion of SDR games to HDR. Needs Windows HDR on. If it doesn't kick in, enable RTX HDR once in the NVIDIA App so its global flags exist. " +
            "Doesn't work together with DLDSR or some overlays."),
        new("nv-vibrance", "NVIDIA", "RTX Dynamic Vibrance",
            "AI saturation / clarity boost for SDR output. The SDR counterpart of RTX HDR; not used when RTX HDR is on."),
        new("nv-fps", "NVIDIA", "Max frame rate",
            "Driver FPS limiter. Values a few frames under refresh keep G-Sync active; with frame generation the cap applies to the output frame rate."),
        new("nv-vsync", "NVIDIA", "Vertical sync",
            "Driver VSync override, written as the NVIDIA Control Panel stores it (VSync mode + tear control). With G-Sync / VRR the usual setup is On here, " +
            "in-game VSync off, and an FPS cap a few frames under refresh."),
        new("nv-power", "NVIDIA", "Power management",
            "How the GPU manages clocks. Prefer max performance keeps clocks high, avoiding clock-change stutter in light games at the cost of power."),

        // ---------- MFG Unlock ----------
        new("force-multiplier", "MFG Unlock", "Force frame multiplier",
            "Game setting uses whatever multiplier the game's menu selects. 2x–6x forces it, for games that only offer an FG on/off switch. " +
            "Ignored while Dynamic MFG is on."),
        new("max-count", "MFG Unlock", "Max frame count",
            "The highest multiplier reported to the game (DLSS-G MultiFrameCountMax). Games build their FG menu from it, so 4 shows up to 4x. The add-on accepts 2 to 5; 6x is only reachable through Force frame multiplier."),
        new("dynamic-mfg", "MFG Unlock", "Dynamic MFG",
            "Lets NVIDIA's runtime pick the multiplier every frame to hold a target frame rate. Needs D3D12, nvngx_dlssg.dll 310.9.1 or 310.9.16, the matching " +
            "Streamline set (2.14.1 / 2.14.16) (see Replace Streamline files), driver 595.41+, and the NVIDIA App's DLSS Frame Generation override set to " +
            "'Use the 3D application setting'. Fully close and restart the game. Takes priority over the forced multiplier."),
        new("dynamic-target", "MFG Unlock", "Dynamic target FPS",
            "The frame rate Dynamic MFG aims for. Refresh rate follows the display. Ignored while VSync is on."),
        new("runtime-selection", "MFG Unlock", "Runtime selection",
            "NVIDIA can deliver DLSS FG over the air and use that instead of the game's files. Game default keeps the game's policy. " +
            "Prefer local files turns OTA off so the DLLs installed here are really used; recommended with pinned DLSS or the Streamline swap. " +
            "Force NVIDIA OTA does the opposite. Needs a game restart. If the add-on log shows a path under ProgramData\\NVIDIA\\NGX\\models, the OTA copy is active."),
        new("mfg-reflex", "MFG Unlock", "Reflex mode",
            "Game setting leaves NVIDIA Reflex alone. Off (FG-safe) turns it off, which some games need while frame generation is forced. On and On + Boost force Reflex on."),
        new("mfg-fps-cap", "MFG Unlock", "Output FPS cap",
            "Highest frame rate the generated output may reach (ReflexSourceFpsCap). Off adds no cap."),
        new("mfg-vrr-headroom", "MFG Unlock", "Reflex VRR headroom",
            "Keeps the output a little under the refresh rate on G-Sync / VRR displays so the limiter, not VSync, sets the pace. " +
            "VRR headroom amount sets the margin: 0.5% to 3% (add-on default 1%)."),
        new("mfg-boundary", "MFG Unlock", "Boundary artifact fix",
            "Since 1.4.x the add-on cleans artifacts that generated frames leave at screen and object boundaries. Balanced is the default, Aggressive cleans harder, " +
            "Off restores the old behaviour. Needs a game restart."),
        new("mfg-raise-ceiling", "MFG Unlock", "Raise frame ceiling",
            "Old Streamline plugins stop at a lower multiplier. This raises their limit to 6x. Some games break with it; turn it off again if so."),
        new("mfg-flip-metering", "MFG Unlock", "Force flip metering off",
            "Patches Streamline's DLSS-G plugin to use its legacy software frame pacing instead of hardware flip metering. Use it only if 3x or 4x freezes the game, and restart the game afterwards."),
        new("mfg-depth-edge", "MFG Unlock", "Depth edge guard",
            "Game setting leaves the game's behaviour. 1 to 4 lower the thresholds that separate foreground from background, which can reduce smearing around edges."),
        new("hdr-compat", "MFG Unlock", "HDR compatibility",
            "Workarounds for HDR games where frame generation breaks the UI or colours. Native changes nothing. UI Composition handles the HUD separately. " +
            "Auto guard + UI adds automatic detection on top. Final color fallback is the most invasive, for games the others don't fix. Try in that order."),
        // ---------- Components (the rows on a game's Install tab) ----------
        new("comp-reshade", "Components", "ReShade",
            "Post-processing tool and the loader for every add-on here (MFG Unlock and the NR add-ons). The app uses the add-on build from reshade.me, or the ReShade64.dll you imported. " +
            "In OptiScaler mode OptiScaler loads it; in ReShade + add-ons mode it is the proxy itself and can't be turned off. " +
            "Turn it off in OptiScaler mode if you don't use MFG Unlock or effects, or another ReShade is already in the game."),
        new("comp-mfg", "Components", "MFG Unlock",
            "ReShade add-on (renodx-mfgunlock.addon64, downloaded from mavismmg/MFGAdaUnlock-RenoDx) that unlocks 3x and 4x multi frame generation on RTX 40 cards. " +
            "Needs ReShade and a game with DLSS frame generation through Streamline; other games are not affected. " +
            "Turn it off on RTX 50 (it has MFG natively), on cards without frame generation, or when the game misbehaves with frame generation forced."),
        new("comp-dlssnr", "Components", "NR runtime",
            "NVIDIA's DLSS 5 neural rendering model, nvngx_dlssnr.dll. You import it in Settings, Local components; the app never downloads it. " +
            "NVIDIA's original needs an RTX 50 card; for RTX 20/30/40 import ShortFuse's compat build. OptiScaler's NR and every NR add-on need it. " +
            "Turn it off to install without neural rendering."),
        new("comp-renodx", "Components", "ShortFuse RenoDX DLSS add-on",
            "ShortFuse's ReShade add-on (renodx-dlss.addon64) that runs DLSS neural rendering itself. You import it; the dropdown picks which imported build this game gets. " +
            "Needs ReShade + add-ons mode, ReShade 6+ with add-on support, a game with its own DLSS and the NR runtime. " +
            "It is one NR route: turning it on turns Lecram, GITC Uplift and Deep Fried Chicken off, unless Allow more than one NR add-on lets Lecram run next to it."),
        new("comp-lecram", "Components", "Lecram DLSS5 add-on",
            "Lecram's ReShade add-on that runs DLSS neural rendering itself: renodx-dlss5.addon64 (v10 and older) or dlss5-nr.addon64 (v11). You import it or the app fetches v11 builds; the dropdown picks the build. " +
            "Needs ReShade + add-ons mode, ReShade 6+ with add-on support, a game with its own DLSS and the NR runtime. " +
            "It is one NR route: turning it on turns the others off. With Allow more than one NR add-on it can run next to ShortFuse's add-on, then NR runs twice."),
        new("comp-dlss", "Components", "DLSS (SR · RR · FG)",
            "NVIDIA's DLSS files from NVIDIA's GitHub: Super Resolution (nvngx_dlss.dll), Ray Reconstruction (nvngx_dlssd.dll) and Frame Generation (nvngx_dlssg.dll). The game's own files are backed up before they are replaced. " +
            "Latest only ever upgrades; a pinned version is installed exactly. Restore DLSS puts the game's own files back. " +
            "Turn it off to keep the game's DLSS, for example when a newer DLSS looks worse in that game."),
        new("comp-streamline", "Components", "Streamline (for Dynamic MFG)",
            "NVIDIA's Streamline files (sl.*.dll), the layer between the game and DLSS frame generation. Dynamic MFG needs FG 310.9.1 with Streamline 2.14.1, or 310.9.16 with 2.14.16. " +
            "Only files the game already ships are replaced, always as one set; Restore DLSS puts them back. " +
            "Off by default. Turn it on only for Dynamic MFG in a game with an older Streamline, and off again if frame generation breaks."),
    ];

    /// <summary>The help topic behind the ⓘ button of a component row on the Install tab.</summary>
    public static string ForComponent(string key) => key switch
    {
        "opti" => "opti-build",
        "reshade" => "comp-reshade",
        "mfg" => "comp-mfg",
        "dlssnr" => "comp-dlssnr",
        "renodx" => "comp-renodx",
        "lecram" => "comp-lecram",
        "uplift" => "uplift",
        "dfc" => "dfc",
        "dlss" => "comp-dlss",
        "streamline" => "comp-streamline",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No help topic for this component"),
    };

    /// <summary>"Which should I pick" guidance shown in the guide under each topic.</summary>
    private static readonly Dictionary<string, string> Advice = new()
    {
        ["uplift"] = "Use ReShade + add-ons mode and one NR route. Start with one pass and the default preset A; press Home to tune Uplift in-game.",
        ["proxy"] = "Start with dxgi.dll. If the game doesn't show the OptiScaler overlay or crashes at start, try winmm.dll, then version.dll. " +
                    "If the game already has a dxgi.dll from another mod, pick another name such as winmm.dll. A ReShade installed by this app is loaded by OptiScaler as ReShade64.dll, so that one is fine. " +
                    "Special K would be replaced, not loaded: OptiScaler's wiki describes a plugins folder and a LoadSpecialK setting for it, which this app does not set up.",
        ["dlss-version"] = "Keep Latest globally. Pin a game only when a new DLSS build looks worse there (ghosting, shimmer), or for Dynamic MFG pin 310.9.1 or 310.9.16 " +
                           "together with the Streamline swap.",
        ["opti-build"] = "OptiScaler F5 (janblade). Switch to OptiScaler-NR (ShyVortex) if F5 misbehaves in a game.",
        ["prereleases"] = "Leave on: the OptiScaler-NR fork publishes nothing else.",
        ["streamline"] = "Only turn on for games where you want Dynamic MFG and the game ships an older Streamline (the DLSS files list shows the SL version). " +
                         "If FG disappears or the game crashes afterwards, use Restore DLSS on that game.",
        ["add-missing"] = "Leave on. It only adds a file to games that have none, and Uninstall removes it again.",
        ["ini-mode"] = "Keep game settings for everyday use: updates never undo tuning you did in-game. Apply app settings once after you changed something here and want " +
                       "it everywhere, then switch back. Fresh config when a mod's config got messy or a new release changed a lot.",
        ["local-components"] = "Import each nvngx_dlssnr.dll to keep it in the local library. Choose the default here or a version on a game, then use Switch NR. Switch back restores its previous file. No NR binary is downloaded by this app. The ShortFuse and Lecram add-ons and Deep Fried Chicken keep a library too: import every build you want, pick the default, and name builds yourself.",
        ["keybinds"] = "Pick keys the game doesn't use: Delete, Insert, End, Page Up/Down and \\ are usually free. Avoid Home if you use ReShade's default overlay key.",
        ["skip-tutorial"] = "On, unless you are new to ReShade.",
        ["performance-mode"] = "Off while you set up effects, on for playing. With only add-ons (RenoDX, MFG Unlock) and no effects it makes no difference.",
        ["show-fps"] = "Off if you use OptiScaler's or the NVIDIA overlay; on for a quick counter.",
        ["load-early"] = "Off by default. Turn on only if MFG Unlock's panel says the game initialised Streamline before the add-on loaded (e.g. Cyberpunk).",
        ["force-multiplier"] = "Game setting whenever the game has its own MFG menu. Force 3x or 4x for games that only have an FG on/off switch. " +
                               "Above 4x rarely looks good below 60 base FPS.",
        ["max-count"] = "Default. Raise to 5 only for a game that builds its menu from this value; for 6x use Force frame multiplier.",
        ["mfg-boundary"] = "Add-on default. Aggressive only when edges of generated frames still flicker.",
        ["dynamic-mfg"] = "Try it for high-refresh displays when the base frame rate varies a lot. Keep off when you prefer a fixed multiplier or it isn't accepted " +
                          "(the add-on log says why).",
        ["dynamic-target"] = "Refresh rate for G-Sync / VRR displays. A fixed value slightly below refresh (e.g. 138 for 144 Hz) if you cap FPS anyway.",
        ["runtime-selection"] = "Prefer local files when you use a pinned DLSS version or the Streamline swap, otherwise NVIDIA may silently replace them. Game default otherwise.",
        ["hdr-compat"] = "Native until you see HUD smearing or wrong colours with FG in HDR, then UI Composition, then Auto guard + UI, then Final color fallback.",
        ["nr-denoise-first"] = "Off. Try it in DX12 games with Ray Reconstruction when the model amplifies noise.",
        ["nr-deferred"] = "Off, unless the cost of running at output resolution is too high.",
        ["nr-residual"] = "Off. Only try it with Ray Reconstruction and Run before upscaling both on.",
        ["nr-automask"] = "Default. Turn off only if people get odd edges.",
        ["nr-private-upscaler"] = "Default (DLSS).",
        ["mfg-reflex"] = "Game setting. Off (FG-safe) only when a game misbehaves with Reflex while frame generation is forced.",
        ["mfg-fps-cap"] = "Off, or a value a few frames under your refresh rate.",
        ["mfg-vrr-headroom"] = "On with G-Sync / VRR displays when frame times look uneven near the refresh rate.",
        ["mfg-raise-ceiling"] = "Off. Only for games with an old Streamline plugin that cap the multiplier too low.",
        ["mfg-flip-metering"] = "Off. Turn on only when 3x / 4x freezes the game.",
        ["mfg-depth-edge"] = "Game setting. Try 1 and work up if you see smearing around edges.",
        ["rdx-stage"] = "Render with one pass is the cheapest and the recommended start. Try Upscaled or Present only if Render looks wrong.",
        ["rdx-passes"] = "1. Heavier on RTX 20/30.",
        ["rdx-strengths"] = "Leave on the add-on default first, then lower tone if the image looks re-lit.",
        ["rdx-reuse"] = "Add-on default (Optional).",
        ["rdx-scaling"] = "Add-on default.",
        ["rdx-signal"] = "Add-on default.",
        ["rdx-exposure"] = "Add-on default; lower Exposure if the model blows out highlights.",
        ["rdx-model"] = "Add-on default; compare the three models per game.",
        ["rdx-mask"] = "Add-on default.",
        ["lecram"] = "Import every build you want to keep and pick the one a game should use. Do not also copy the other Lecram file into the game. Check the chosen pair's tested status on the Install tab and record your results there.",
        ["lc-enable"] = "Add-on default. Off only to compare with the game's native image.",
        ["lc-hook"] = "Add-on default (After upscaling in v11 beta.18+, Auto in beta.6, Present in v10.7). Try Present if the HUD should be processed too, or Before upscaling if After upscaling looks wrong.",
        ["lc-present"] = "Add-on default. DLSS frames only if menus or loading screens look wrong under the model.",
        ["lc-fg-path"] = "Add-on default. Real frames only just to test, when NR blinks at Present with frame generation.",
        ["lc-feed"] = "Add-on default. Frame meter or Game exposure only to compare when an HDR game looks too dark or washed out.",
        ["lc-strengths"] = "Add-on default first. Lower Strength in steps if the image looks over-processed.",
        ["lc-style"] = "Add-on default; compare the three per game.",
        ["lc-mask"] = "Add-on default.",
        ["lc-ui"] = "Off; turn on if the HUD looks processed.",
        ["lc-passes"] = "1. Add passes only with headroom to spare.",
        ["lc-scale"] = "1.0; lower it to save FPS if you accept a softer image.",
        ["lc-preset"] = "Add-on default. Changing it recreates NR, which causes a short hitch in-game.",
        ["lc-pass-preset"] = "Add-on default. Try New (preset 2) on an extra pass to compare a mixed-model stack; it recreates NR.",
        ["lc-crash"] = "Add-on default. Off only if you never want the crash files; On only while chasing a repeat crash.",
        ["lc-vram"] = "Full. Reduced or Minimal only if video memory runs out.",
        ["lc-foreign"] = "Add-on default alone; the app sets observe for you next to ShortFuse's add-on.",
        ["lc-hooks"] = "Add-on default. With a Streamline game and DLSS on set 1; if the game crashes at boot set 2.",
        ["lc-dx11"] = "Auto.",
        ["dfc-no-dlss"] = "Use a 4.x import. Play in borderless or windowed mode when you want frame generation, and tick Add nvngx_dlssg.dll only on an RTX 40/50 card.",
        ["dfc-arm"] = "Default. Switch off only to take DFC out of a game without uninstalling, then restart the game.",
        ["dfc-enabled"] = "Default. Switch off to compare with the game's native image.",
        ["dfc-passes"] = "1.0 first. Add passes in whole steps only with headroom to spare.",
        ["dfc-scale"] = "100%. Use 75% to save FPS if you accept a softer image; 125% and above only on very strong cards.",
        ["dfc-vulkan"] = "Off, unless the game is native Vulkan. Then switch on and restart the game fully.",
        ["dfc-native-look"] = "Off. Try it if the model shifts the game's colours too far.",
        ["dfc-cleanup"] = "Off for one or two passes. Try it when colour casts show up with several passes.",
        ["dfc-colour"] = "Default (1.0). Lower it in steps if colours look off.",
        ["dfc-output"] = "Off. Try HUD protection for a crosshair or HUD the model changes, highlight protection for blown-out lights, deband for banding in skies.",
        ["rdx-scale"] = "100% on fast cards; 75% or 50% to save FPS.",
        ["rdx-ui"] = "Add-on default; On if the HUD looks processed.",
        ["rdx-encoding"] = "Add-on default. In HDR games with wrong colours, try HDR10 or scRGB.",
        ["nr-enabled"] = "Automatic; toggle it in-game with the DLSSNR key to compare.",
        ["install-mode"] = "OptiScaler for everything it supports. ReShade + add-ons for games where OptiScaler causes trouble but you still want MFG Unlock or ReShade effects.",
        ["presets"] = "Start from the upstream defaults and load the Recommended preset if you want the tuned setup; save your own variations as presets.",
        ["nr-before-sr"] = "On (cheaper, cleaner). Turn off if a game shows smeared detail or odd colours with RR, and compare.",
        ["nr-finished"] = "Off. Try on for games whose post-processing (bloom, grading) fights the model, and accept that the HUD may change.",
        ["nr-passes"] = "1. 2 only for a deliberately stronger look with headroom to spare; 3 is mostly for experiments.",
        ["nr-scale"] = "100% at 1440p output. 75% at 4K or on RTX 40 / lower cards to keep the cost down; 50% if FPS drops too much.",
        ["rdx-preset"] = "Preset A for the full look; Preset B when the model costs too much on RTX 20/30/40.",
        ["nr-preset"] = "Preset A for the full look; Preset B when the model costs too much on RTX 20/30/40.",
        ["nr-style"] ="Standard. Natural if the result looks over-processed, Cinematic for a graded look.",
        ["nr-strengths"] = "Start at the Recommended preset (local structure 0.7, local tone 0.25, skin 0.5): detail without changing the game's lighting too much. " +
                           "Lower local tone first if the image looks re-lit; lower skin structure if faces look aged.",
        ["nr-colour"] = "Low (0.2) keeps the game's art direction. Raise toward 1 only if you like the model's colour grading.",
        ["tm-mode"] = "SDR games: leave it, it doesn't apply. HDR games: Hybrid composed as the safe default; Neutwo composed if highlights look dull; " +
                      "replace modes only if composed looks too weak.",
        ["tm-white"] = "Auto HDR exposure for HDR games; Game exposure if the game reports exposure to the upscaler reliably (fewer brightness jumps); Manual as fallback.",
        ["tm-paper"] = "1.0. Lower (0.75) if bright skies or lights lose detail; raise (1.5) only for very dark games.",
        ["tm-highlight"] = "0 to 25. Raise if lamps and the sky get blown out after NR.",
        ["tm-maxratio"] = "2x. Lower to 1.5x if bright lights break into coloured blocks.",
        ["tm-transfer"] = "1.0. 0.75 for a subtler effect without changing the model's strengths.",
        ["tm-hdrtransfer"] = "Off unless you use Run before upscaling + Apply to finished picture in an HDR game and brightness looks wrong.",
        ["hdr-force"] = "Off. Only with an HDR mod that expects OptiScaler to make the swapchain HDR.",
        ["hdr-10"] = "Off (scRGB, more precision). On if your capture tool or display chain works better with HDR10.",
        ["nv-sr-preset"] = "Use global for most games. Recommended lets DLSS pick per quality mode. Force K for Quality / DLAA, M for Performance, L for Ultra Performance; Off stops the driver override for that game.",
        ["nv-sr-mode"] = "Use the game's setting. DLAA if you have headroom; Performance with an L/M preset at 4K looks close to Quality with older presets.",
        ["nv-rr-preset"] = "Use global or F. Try D only if F shows smearing in a specific game.",
        ["nv-fg-preset"] = "Use global or NVIDIA default. Try A or B only when a game shows FG artifacts that a different model fixes.",
        ["nv-mfg"] = "Leave it to the game unless it only offers 2x; RTX 40 owners use MFG Unlock in the ReShade add-ons tab instead.",
        ["nv-smooth-motion"] = "Per game, for titles without DLSS FG and a base frame rate above ~50 FPS.",
        ["nv-rtx-hdr"] = "Per game for SDR-only titles on an HDR monitor. Off for games with native HDR or a RenoDX HDR mod.",
        ["nv-vibrance"] = "Taste. Per game for dull-looking SDR games; off when using RTX HDR.",
        ["nv-fps"] = "Use global (set a cap 3–4 FPS under your refresh in the NVIDIA App, e.g. 237 for 240 Hz). Per game for a lower cap, e.g. 117 for a game that can't hold 140+.",
        ["nv-vsync"] = "Use global. With G-Sync: On plus an FPS cap under refresh. Without VRR: Adaptive avoids stutter below refresh; Off for the lowest latency.",
        ["nv-power"] = "Use global. Prefer max performance for a game that stutters when the GPU clocks down.",
        ["hdr-skip"] = "Off. On only if colours look wrong while another HDR mod is active.",
    };

    public static string? AdviceFor(string id) => Advice.GetValueOrDefault(id);

    private static readonly Dictionary<string, HelpTopic> ById = All.ToDictionary(t => t.Id);

    public static HelpTopic? Get(string? id) => id is not null && ById.TryGetValue(id, out var t) ? t : null;
    public static string Tip(string? id) => Get(id)?.Body ?? "";
}
