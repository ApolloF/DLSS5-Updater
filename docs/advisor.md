# Method advisor

The game page's **Works in this game** panel answers "which ways of improving this game does the app know, and which fit
here". `MethodAdvisor.Advise(AdvisorInput)` (`Core/MethodAdvisor.cs`) is pure: the caller reads the game, the GPU, the catalog
and the library and passes them in. It implements the [decision table](#decision-table) below (20
rows); `MethodAdvice.Rows` reports which rows applied, and `tests/DLSSUpdater.Tests/MethodAdvisorTests.cs` has a test per row.

## Input

| Input | From |
|---|---|
| Graphics APIs, bitness, engine, upscaler DLL and plugin signals, anti-cheat | `GameInfo` / `GameTech` (the detection scan) |
| Native DLSS | `Installer.HasNativeDlss`: DLLs our own install placed don't count |
| GPU vendor and RTX generation | `GpuDetector` (display adapter class in the registry, read once at startup); tests inject a `GpuInfo`. An unreadable GPU never hides a method. |
| OptiScaler compatibility list, HUDfix-incompatible list | the catalog's `optiscalerCompat` section (`docs/catalog.md`), matched by title with `GameMatcher.MatchName` |
| RenoDX game list | the catalog's `renodx` section, `GameMatcher` |
| Imported builds, the Lecram build the game uses (has NR's host, runs without DLSS) | the component library (`LecramBuildInfo.Of`) |
| Routes already in the game | `NrAddons.PresentIn` (record and files) |
| Store install | `MethodAdvisor.IsProtectedStoreInstall` (under `WindowsApps`, or `MicrosoftGame.config` with an unreadable exe) |

## Output

An ordered list of `MethodOption`: title, `Availability` (available / needs setup / not possible / coming), a one-line reason,
the component ids it would install (and the install mode), what it needs, and an `Unverified` flag (also said in the reason)
where the research marks something unverified: Smooth Motion's RTX 40/50 requirement, the DX10
route, D3D8 through Uplift, an Unreal DLSS plugin standing in for the DLL, an assumed graphics API. What cannot be done comes last with its reason.
Anti-cheat and protected Store installs return no methods and a `Blocked` line (row 20); a non-NVIDIA or GTX GPU leaves only
OptiScaler (row 19).

Methods the app does not install yet (Luma, third-party upscaler mods, the all-in-one add-on, the RenoDX HDR
mod) are listed as **coming**, so the answer is complete without promising an install.

HSF (Capt-Hunter's frame generation add-on, import only, see docs/addons/hsf.md) is advice only, like Luma and DXVK: once the user has imported a release it is listed as "HSF frame generation (preview)" for a game with no frame generation of its own, with its needs (a flow source for DirectX 9/11 and DirectX 12 games without DLSS, MFG Unlock on RTX 20/30, borderless or windowed mode) and the anti-cheat caution. It is installed from the HSF section of the game page and never picked for you.

## The panel

Each method is a clickable card: a one-line plain summary (`MethodAdvisor.SummaryOf`), the availability badge, the warning and the
"Needs:" line; the long reason opens under **Details**. Clicking a card applies it: the method's components go on on the game
page and the install mode follows (the OptiScaler row's tick is the mode: ticked OptiScaler, unticked ReShade + add-ons). It never installs; the user presses Install. The picked card is the one the page is set to:
after a refresh, or when the user changes the mode or a component by hand, the app marks the most specific method whose mode and
components are all on (or none).
MFG Unlock (H) is an add-on, not a method (`MethodAdvisor.IsAddOn`): it shows as a checkbox card under the methods ("Add to the
method you picked") and goes on or off by itself, so picking a method never takes it out and it works with no method picked too.
It is never the recommended one.
Smooth Motion has no components: it is a driver setting on the game's NVIDIA tab, so its card isn't pickable.

The recommended method (`MethodAdvice.Top`, badged "Recommended") is the first selectable one without a warning. `Rank` puts
Available before NeedsSetup (ties keep table order), so a method that needs setup is only recommended when nothing available fits;
its card then shows the "Needs:" line. A method whose neural route clashes with one already in the game (`NrAddons.Conflict`,
the rule behind the page's conflict banner) carries that reason as a warning and is never recommended; when nothing qualifies, no
card carries the badge.

## One neural consumer

`NrAddons` holds the rule (`Refusal`, `PresentIn`): GITC Uplift, ShortFuse, Lecram, the DX11 bridge and
OptiScaler's NR all detour the game's NGX calls, so a game gets one (ShortFuse + Lecram only with the Settings override, as
before). `Installer` checks it before anything is fetched (`EnsureOneNeuralConsumer`): a DX11 bridge in the folder blocks any
route. DFC files found in a game (from an older version) count as a foreign NR injector and the other add-ons move them aside. Routes the app places itself replace each
other as before.

## Lecram without DLSS

Lecram's add-on runs NR without the game's own DLSS from v12.0.0 (the game's motion vectors where NR finds them, else optical flow), and
from v12.9 it runs Direct3D 9, Direct3D 10, OpenGL and 32-bit games through its 64-bit host (docs/addons/lecram-dlss5.md, "Host and
32-bit"). `MethodAdvisor.AddLecramWithoutDlss` adds it to the rows without DLSS (5 to 14) wherever the GITC Uplift card is offered, right before
it, as "Neural rendering with Lecram DLSS5 add-on" (method B, route Lecram, components ReShade + NR runtime + Lecram, ReShade +
add-ons mode). It needs no motion shader and no dgVoodoo2, so with the NR runtime in place it is Available and ranks above
Uplift's card. It is only listed when a Lecram build is in the library, and follows the build the game would use (`AdvisorInput.Lecram`):

| Game | Build with the host (v12.9+ from a setup or zip) | v12.x without the host (bare `.addon64`) | v11.x |
|---|---|---|---|
| 64-bit DX11 / DX12 / API unknown | available | available | not possible: needs the game's DLSS |
| DX9, DX10, OpenGL, 32-bit DX9/10/11 | available | not possible: names the setup or zip to import | not possible (no host) |
| 32-bit DX12 | not possible (not supported by the author yet) | same | same |
| Vulkan (any bitness), DX8 | not listed: Vulkan goes through ReShade's layer (as in row 3), DX8 is not a route the author names | | |

The anti-cheat and Store caution (row 20) applies to it like to every selectable method. The installer checks the same rules
(`LecramRoute.Refusal`).

## Methods

The method letters used by the decision table and by `MethodAdvisor`. [U] marks a requirement that is unverified.

| Method | Adds | Requires | Delivered as | Auto-downloadable? |
|---|---|---|---|---|
| **A. Native DLSS DLL swap** (nvngx_dlss/dlssg/dlssd, Streamline) | newer SR/FG/RR model | game has nvngx_* | DLL replace | yes (NVIDIA DLLs, app already does) |
| **B. Neural rendering add-on** (ShortFuse's RenoDX DLSS, Lecram's add-on, GITC Uplift) | NR (+SR) on top of a game's own DLSS (ShortFuse, Lecram); without DLSS: Lecram v12+ (v12.9+ with its host also DX9/DX10/OpenGL/32-bit) and Uplift (on the game image, 64-bit games only, no FPS gain) | ShortFuse: native DLSS, ReShade add-on, 64-bit DX11/12. Lecram: ReShade add-on, native DLSS up to v11.9. Uplift: ReShade add-on, RTX | `.addon64` (Lecram v12.9+: also `.addon32` + `dlss5-nr-host\`) | ShortFuse and Uplift yes; Lecram import-only |
| **E. OptiScaler (stock)** | swap upscaler (FSR2/3/4, XeSS, DLSS), FG (FSR-FG, XeFG), OptiFG, Nukem dlssg->fsr3 | game already has DLSS/FSR2+/XeSS input; (DX12 best) | proxy DLL + ini | yes (GPL, GitHub, 7z) |
| **F. OptiScaler F5** (janblade) | NR inside OptiScaler, best for games with DLSS where RenoDX churns | game with DLSS inputs, RTX 50 (or patched DLL) | zip | yes (GPL, GitHub) |
| **G. 3rd-party upscaler mods + OptiScaler** | DLSS/FSR inputs for games with none: REFramework PDUpscaler (RE Engine), SekiroTSR, ERSS-FG (Elden Ring), Luma (Prey, Dishonored 2, WD2, FC5, Persona 5, ...), Luma UE (UE4/5 DX11) | per-game mod | per-game | via OptiScaler wiki per game |
| **H. MFG unlocks** (mavismmg MFGAdaUnlock-RenoDx, dashdogy MFG-Unlocker (MIT), sdli1995 dlssg_for_sm86) | DLSS-MFG on RTX 20/30/40 | game with DLSS-G (Streamline) | DLL / addon | yes (GitHub) |
| **I. NVIDIA Smooth Motion** (driver) | 2x FG for any DX11/12/(Vulkan) game | RTX 40/50 [U], driver profile | driver setting | n/a (setting) |
| **J. Standalone AIO add-on** (kibblerz DLSS5-Reshade-AIO, Apache-2.0) | NR + SR + FG in one add-on, incl. FG in games without it | ReShade, 64-bit DX12/11/9/Vulkan | zip | yes (GitHub) but young |

## Decision table

Pre-checks that gate everything (stop with an explanation if true): anti-cheat marker present -> **no injection methods** (no ReShade add-ons, no OptiScaler, no DLL swaps; show "online game, skipped"; a driver-profile frame limiter is still safe); Microsoft Store protected install -> unsupported; non-NVIDIA GPU -> NR methods (B,F,J) impossible, only E (+spoof/OptiPatcher), G, H n/a. GPU tier: RTX 50 = NR full; RTX 40 = NR moderate (+patched DLL for FP8); RTX 20/30 = NR heavy (patched DLL needed); GTX = no NR/DLSS. Flag always: "NR is the leaked DLSS 5 build; community DLLs".

Legend for methods = section 7 letters. "SR" = upscaling gain. Items in [] are optional extras. Ranking aims at best result per effort/risk; the app should show the top 1-3 and a reason.

| # | Game signals (local + data) | Ranked methods | Why / notes |
|---|---|---|---|
| 1 | `nvngx_dlss.dll` present, **DX12**, 64-bit, UE or other, no anti-cheat | 1. **A** (update DLSS DLLs, keep SR/FG/RR) 2. **B** (RenoDX DLSS / renodx-dlss5 NR on top) 3. **F** (OptiScaler F5) 4. **E** only if user wants FSR/XeSS or FG tweaks | Cheapest and safest first. NR add-ons hook the game's DLSS. Cyberpunk-style titles that churn NGX features: prefer F |
| 2 | `nvngx_dlss.dll` present, **DX11**, 64-bit | 1. **A** 2. **B** with renodx-dlss5 8.x (bridges DX11 itself) 3. **F** | DX11+DLSS needs an add-on build that supports DX11 itself; the app doesn't check which build you have |
| 3 | `nvngx_dlss.dll` present, **Vulkan** | 1. **A** 2. **F** (OptiScaler covers Vulkan with native DLSS; ReShade engine cannot, because ReShade reaches Vulkan through a registered layer, not a proxy DLL) | one-click: "a Vulkan game with DLSS goes to OptiScaler" |
| 4 | `nvngx_dlssg.dll` / `sl.dlss_g.dll` present (native DLSS-G) | A (+update dlssg) -> **H** to unlock MFG: its DLSS-G backend on RTX 40, its Intel XeMFG backend (1.5.0+) on RTX 20/30 as needs-setup (D3D12 only, Intel files from its panel, no exclusive fullscreen) -> **E** with FG Input "DLSSG via SL" (SL2+ only) or Nukem for FSR3-FG output (non-NVIDIA) | SL1 games (Witcher 3 DX11, Dying Light 2, Plague Tale Requiem, Returnal) must use Nukem/FSR3 route |
| 5 | **No** nvngx/sl/FSR/XeSS DLL, **64-bit DX11/12**, not UE-XeSS, no anti-cheat | 0. **B** Lecram v12+ when imported ([Lecram without DLSS](#lecram-without-dlss)) 1. **B** with GITC Uplift (NR on the game image, no perf gain) 2. **J** AIO (if user wants one add-on) 3. **I** Smooth Motion (FG only, RTX 40/50) 4. frame limiter only | `renodx-dlss` alone does nothing here. Uplift is 64-bit only in this app. Warn about cost (+several ms at 4K) |
| 6 | No upscaler DLL but **PCGW `Upscaling` lists FSR/XeSS/DLSS** or game on OptiScaler compat list with FSR/XeSS input | 1. **E** (OptiScaler: FSR3.1/4/XeSS->better upscaler; optional OptiFG on DX12) 2. **B** (Lecram v12+ when imported, else Uplift) NR only if OptiScaler is not used | FSR/XeSS may be statically linked (UE plugin), so DLL scan misses it: use PCGW `Upscaling` or UE plugin folders |
| 7 | **Unreal Engine** (Shipping.exe, Paks), DX12, upscaler plugins unknown | 1. check Engine\Plugins for DLSS/FSR/XeSS: if DLSS -> row 1; 2. if XeSS only -> **E** with XeSS input (UE only works with XeSS as input); 3. DX11 UE with no upscaler -> **G** Luma UE (+E); 4. else **B** (Lecram v12+ when imported, then Uplift) | Engine.ini tweaks from OptiScaler wiki (FSR crash fixes); `t.MaxFPS` limiter. Offer Luma UE only if title is on Luma/OptiScaler UE list |
| 8 | **RE Engine** (`re_chunk_000.pak`) | install **REFramework (dinput8.dll)** first; then 1. **B/A** if native DLSS (RE4, RE Requiem), 2. **G** PDUpscaler for RE2/3/4(2023)/7/8 + E | ReShade crashes RE Engine without REFramework |
| 9 | **Unity**, DX11, no DLSS | **B** (Lecram v12+ when imported, then Uplift); **I** for FG | API from `Player.log`; no static imports |
| 10 | **DX9 / DX8 exe** (imports `d3d9.dll`/`d3d8.dll`), 32 or 64-bit | 0. DX9: **B** Lecram with its host when imported 1. **B** with Uplift (D3D9 runs through Uplift's 64-bit helper; D3D8 unverified, Uplift's docs don't name it; 32-bit not possible); 2. Luma mod if listed (Borderlands 2/Pre-Sequel, BioShock, ... need dgVoodoo2 conf) | Uplift's docs name D3D9, not D3D8 |
| 11 | **32-bit exe**, DX11 | **B** Lecram with its host when imported; Uplift **not possible** (64-bit games only in this app) | NGX is 64-bit only: Lecram runs it in its 64-bit host |
| 12 | **OpenGL** (`opengl32.dll`) | **B** Lecram with its host when imported, then **B** with Uplift (needs ReShade 6.8+, 64-bit only); game must render on the NVIDIA GPU | many are 32-bit |
| 13 | **Vulkan, no DLSS** (`vulkan-1.dll`, DXVK games, emulators) | **B** with Uplift through ReShade's Vulkan layer (the layer must be registered: Settings -> ReShade -> Vulkan layer, one administrator prompt); **turn off Smooth Motion for Vulkan** via `0xB0CC0875` bit 4 | Lecram isn't listed for Vulkan; Detroit Become Human suspected Smooth Motion issue |
| 14 | **DX10** exe | **B** Lecram with its host when imported, then **B** with Uplift, unverified (Uplift's docs don't name Direct3D 10) | low confidence |
| 15 | Game on **RenoDX games-index** (`steam_appid` match, `compatibility: working`) | additionally offer RenoDX HDR mod (`renodx-<id>.addon64`) | independent of upscaling; ensure DX11/12 and `architecture` match |
| 16 | Game on **Luma wiki** (DX11 mods) | offer Luma mod + (optionally) its DLSS/FSR option; do not stack with RenoDX HDR | DX11 only |
| 17 | Game on **OptiScaler "Hudfix-incompatible"** | do not offer OptiFG; offer native FG route (row 4) or I | 24 titles list |
| 18 | OptiScaler already present (`OptiScaler.ini`) or any `renodx-dlss5` / Alex's Toolkit / DX11 bridge | enforce "one neural consumer": an install replaces the add-ons it doesn't keep (the neural method reuses the add-on already in the game), so only a DX11 bridge left in the folder is warned about; only OptiScaler F5 cooperates | Two consumers hook the same NGX calls |
| 19 | Non-NVIDIA GPU (AMD/Intel) | **E** only (FSR/XeSS inputs; spoof DXGI or OptiPatcher if game has DLSS-only input); **H/J/B/F** unavailable | OptiScaler FSR4 officially RDNA3/4 |
| 20 | Anti-cheat marker or protected Store install | **none** (show reason); optional frame limiter via driver profile | matches existing `GameInspector` behaviour |

Frame-limiter column to add to every row: NVIDIA driver `0x10835002` (NVIDIA GPU) > RTSS SDK profile > UE `t.MaxFPS` (UE only) > in-game setting. If FG is active (OptiFG, FSR-FG, MFG, Smooth Motion), recommend cap = refresh/ (FG factor) and vsync via driver; with OptiFG avoid RTSS or enable Detours hooking.

The one-neural-consumer rule behind row 18: renodx-dlss5, Alex's Toolkit and the DX11 bridge all take over the game's NGX calls. Stock OptiScaler also takes over the NGX calls and so blocks them; only OptiScaler F5 cooperates. DFC files found in a game (from an older version) are treated as a foreign NR injector and moved aside by the other add-ons.
