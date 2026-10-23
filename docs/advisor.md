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
| Imported builds, DFC version | the component library |
| Routes already in the game | `NrAddons.PresentIn` (record and files) |
| Store install | `MethodAdvisor.IsProtectedStoreInstall` (under `WindowsApps`, or `MicrosoftGame.config` with an unreadable exe) |

## Output

An ordered list of `MethodOption`: title, `Availability` (available / needs setup / not possible / coming), a one-line reason,
the component ids it would install (and the install mode), what it needs, and an `Unverified` flag (also said in the reason)
where the research marks something unverified: Smooth Motion's RTX 40/50 requirement, the DX11 bridge requirement, the DX10
route, an Unreal DLSS plugin standing in for the DLL, an assumed graphics API. What cannot be done comes last with its reason.
Anti-cheat and protected Store installs return no methods and a `Blocked` line (row 20); a non-NVIDIA or GTX GPU leaves only
OptiScaler (row 19).

Methods the app does not install yet (Luma, third-party upscaler mods, the DX11 bridge, the all-in-one add-on, the RenoDX HDR
mod) are listed as **coming**, so the answer is complete without promising an install.

## The panel

Each method is a clickable card: a one-line plain summary (`MethodAdvisor.SummaryOf`), the availability badge, the warning and the
"Needs:" line; the long reason opens under **Details**. Clicking a card picks it; the green **Use: X** button turns on the
components of the picked method on the game page (and the install mode it needs). It never installs; the user presses Install.
Smooth Motion has no components: it is a driver setting on the game's NVIDIA tab, so its card isn't pickable.

The recommended method (`MethodAdvice.Top`, picked at first) is the first selectable one without a warning. `Rank` puts Available
before NeedsSetup (ties keep table order), so a method that needs setup is only recommended when nothing available fits, and the
button then says "Needs setup first". A method whose neural route clashes with one already in the game (`NrAddons.Conflict`,
the rule behind the page's conflict banner) carries that reason as a warning and is never recommended; when nothing qualifies, no
method is picked and the button is hidden.

## One neural consumer

`NrAddons` holds the rule (`Refusal`, `FeederRefusal`, `PresentIn`): DFC, GITC Uplift, ShortFuse, Lecram, the DX11 bridge and
OptiScaler's NR all detour the game's NGX calls, so a game gets one (ShortFuse + Lecram only with the Settings override, as
before). `Installer` checks it before anything is fetched (`EnsureOneNeuralConsumer`): a DX11 bridge in the folder blocks any
route, and DLSS5-Feeder needs ReShade + add-ons mode and Deep Fried Chicken alone. Routes the app places itself replace each
other as before.

## Methods

The method letters used by the decision table and by `MethodAdvisor`. [U] marks a requirement that is unverified.

| Method | Adds | Requires | Delivered as | Auto-downloadable? |
|---|---|---|---|---|
| **A. Native DLSS DLL swap** (nvngx_dlss/dlssg/dlssd, Streamline) | newer SR/FG/RR model | game has nvngx_* | DLL replace | yes (NVIDIA DLLs, app already does) |
| **B. RenoDX DLSS (ShortFuse) / renodx-dlss5 / Lecram NR add-on** | NR (+SR) on top of a game's own DLSS | native DLSS, ReShade add-on, 64-bit DX9/11/12 | `.addon64` | import-only (app already) |
| **C. DFC** | NR pass, also in no-DLSS games *with Feeder* | ReShade add-on, DLSS contract (native or Feeder), RTX | 3 files | import-only |
| **D. DLSS5-Feeder** (+MV provider + consumer) | fake DLSS contract -> NR in games with no DLSS (D3D9-12, Vulkan, GL, 32-bit) | ReShade add-on, depth+MV, RTX, single-player | zip from GitHub | zip yes; consumer/MV no |
| **E. OptiScaler (stock)** | swap upscaler (FSR2/3/4, XeSS, DLSS), FG (FSR-FG, XeFG), OptiFG, Nukem dlssg->fsr3 | game already has DLSS/FSR2+/XeSS input; (DX12 best) | proxy DLL + ini | yes (GPL, GitHub, 7z) |
| **F. OptiScaler DLSS-NR forks** | NR inside OptiScaler, best for games with DLSS where RenoDX churns | game with DLSS inputs, RTX 50 (or patched DLL) | zip | yes (GPL, hashes in wilsjo2 release) |
| **G. 3rd-party upscaler mods + OptiScaler** | DLSS/FSR inputs for games with none: REFramework PDUpscaler (RE Engine), SekiroTSR, ERSS-FG (Elden Ring), Luma (Prey, Dishonored 2, WD2, FC5, Persona 5, ...), Luma UE (UE4/5 DX11) | per-game mod | per-game | via OptiScaler wiki per game |
| **H. MFG unlocks** (mavismmg MFGAdaUnlock-RenoDx, dashdogy MFG-Unlocker (MIT), sdli1995 dlssg_for_sm86) | DLSS-MFG on RTX 20/30/40 | game with DLSS-G (Streamline) | DLL / addon | yes (GitHub) |
| **I. NVIDIA Smooth Motion** (driver) | 2x FG for any DX11/12/(Vulkan) game | RTX 40/50 [U], driver profile | driver setting | n/a (setting) |
| **J. Standalone AIO add-on** (kibblerz DLSS5-Reshade-AIO, Apache-2.0) | NR + SR + FG in one add-on, incl. FG in games without it | ReShade, 64-bit DX12/11/9/Vulkan | zip | yes (GitHub) but young |

## Decision table

Pre-checks that gate everything (stop with an explanation if true): anti-cheat marker present -> **no injection methods** (no ReShade add-ons, no OptiScaler, no DLL swaps; show "online game, skipped"; a driver-profile frame limiter is still safe); Microsoft Store protected install -> unsupported; non-NVIDIA GPU -> NR methods (B,C,D,F,J) impossible, only E (+spoof/OptiPatcher), G, H n/a. GPU tier: RTX 50 = NR full; RTX 40 = NR moderate (+patched DLL for FP8); RTX 20/30 = NR heavy (patched DLL needed); GTX = no NR/DLSS. Flag always: "NR is the leaked DLSS 5 build; community DLLs".

Legend for methods = section 7 letters. "SR" = upscaling gain. Items in [] are optional extras. Ranking aims at best result per effort/risk; the app should show the top 1-3 and a reason.

| # | Game signals (local + data) | Ranked methods | Why / notes |
|---|---|---|---|
| 1 | `nvngx_dlss.dll` present, **DX12**, 64-bit, UE or other, no anti-cheat | 1. **A** (update DLSS DLLs, keep SR/FG/RR) 2. **B** (RenoDX DLSS / renodx-dlss5 / DFC NR on top) 3. **F** (OptiScaler DLSS-NR fork) 4. **E** only if user wants FSR/XeSS or FG tweaks | Cheapest and safest first. NR add-ons hook the game's DLSS. Cyberpunk-style titles that churn NGX features: prefer F |
| 2 | `nvngx_dlss.dll` present, **DX11**, 64-bit | 1. **A** 2. **B** with renodx-dlss5 8.x (bridges DX11 itself) or NIGos `dlss5-dx11-bridge` + DFC 3. **F** | DX11+DLSS needs the bridge unless 8.x; never bridge + Feeder |
| 3 | `nvngx_dlss.dll` present, **Vulkan** | 1. **A** 2. **F** (OptiScaler covers Vulkan with native DLSS; ReShade engine cannot, because ReShade reaches Vulkan through a registered layer, not a proxy DLL) 3. **D** only if no native DLSS | one-click: "a Vulkan game with DLSS goes to OptiScaler" |
| 4 | `nvngx_dlssg.dll` / `sl.dlss_g.dll` present (native DLSS-G) | A (+update dlssg) -> **H** to unlock MFG on RTX 40 (the add-on the app installs targets RTX 40 only) -> **E** with FG Input "DLSSG via SL" (SL2+ only) or Nukem for FSR3-FG output (non-NVIDIA) | SL1 games (Witcher 3 DX11, Dying Light 2, Plague Tale Requiem, Returnal) must use Nukem/FSR3 route |
| 5 | **No** nvngx/sl/FSR/XeSS DLL, **64-bit DX11/12**, not UE-XeSS, no anti-cheat | 1. **D** (Feeder + DFC or renodx-dlss5; NR only, DLAA, no perf gain) 2. **J** AIO (if user wants one add-on) 3. **I** Smooth Motion (FG only, RTX 40/50) 4. frame limiter only | `renodx-dlss` alone does nothing here. Needs: Generic Depth, MV provider, single-player. Warn about cost (+several ms at 4K) |
| 6 | No upscaler DLL but **PCGW `Upscaling` lists FSR/XeSS/DLSS** or game on OptiScaler compat list with FSR/XeSS input | 1. **E** (OptiScaler: FSR3.1/4/XeSS->better upscaler; optional OptiFG on DX12) 2. **D** (NR) on top only if OptiScaler absent | FSR/XeSS may be statically linked (UE plugin), so DLL scan misses it: use PCGW `Upscaling` or UE plugin folders |
| 7 | **Unreal Engine** (Shipping.exe, Paks), DX12, upscaler plugins unknown | 1. check Engine\Plugins for DLSS/FSR/XeSS: if DLSS -> row 1; 2. if XeSS only -> **E** with XeSS input (UE only works with XeSS as input); 3. DX11 UE with no upscaler -> **G** Luma UE (+E); 4. else **D** | Engine.ini tweaks from OptiScaler wiki (FSR crash fixes); `t.MaxFPS` limiter. Offer Luma UE only if title is on Luma/OptiScaler UE list |
| 8 | **RE Engine** (`re_chunk_000.pak`) | install **REFramework (dinput8.dll)** first; then 1. **B/A** if native DLSS (RE4, RE Requiem), 2. **G** PDUpscaler for RE2/3/4(2023)/7/8 + E | ReShade crashes RE Engine without REFramework |
| 9 | **Unity**, DX11, no DLSS | **D** (proven: Fell & Sell); **I** for FG | API from `Player.log`; no static imports |
| 10 | **DX9 / DX8 exe** (imports `d3d9.dll`/`d3d8.dll`), 32 or 64-bit | 1. **D** via **dgVoodoo2** wrapper (Feeder "Install for a DirectX 9 game", proven Fable Anniversary); 2. Luma mod if listed (Borderlands 2/Pre-Sequel, BioShock, ... need dgVoodoo2 conf) | 32-bit uses `host64\` helper; DFC files go in `host64\` |
| 11 | **32-bit exe**, DX11 | **D** (addon32 + host64) only; note Feeder 32-bit "beta" | NGX is 64-bit only |
| 12 | **OpenGL** (`opengl32.dll`) | **D** (proven: Worms Ultimate Mayhem, KOTOR, MX Bikes, City of Heroes); must render on NVIDIA GPU | many are 32-bit |
| 13 | **Vulkan, no DLSS** (`vulkan-1.dll`, DXVK games, emulators) | **D** (Feeder Vulkan layer; install layer machine-wide + `ReShadeApps.ini` = UAC prompt) ; **turn off Smooth Motion for Vulkan** via `0xB0CC0875` bit 4 | DOOM 2016 proven; Detroit Become Human suspected Smooth Motion issue |
| 14 | **DX10** exe | **D** DX10 path (beta, "not yet run in any game" for 64-bit helper mode) | low confidence |
| 15 | Game on **RenoDX games-index** (`steam_appid` match, `compatibility: working`) | additionally offer RenoDX HDR mod (`renodx-<id>.addon64`) | independent of upscaling; ensure DX11/12 and `architecture` match |
| 16 | Game on **Luma wiki** (DX11 mods) | offer Luma mod + (optionally) its DLSS/FSR option; do not stack with RenoDX HDR | DX11 only |
| 17 | Game on **OptiScaler "Hudfix-incompatible"** | do not offer OptiFG; offer native FG route (row 4) or I | 24 titles list |
| 18 | OptiScaler already present (`OptiScaler.ini`) or any `renodx-dlss5` / DFC / Alex's Toolkit / DX11 bridge | enforce "one neural consumer": an install replaces the add-ons it doesn't keep (the neural method reuses the add-on already in the game), so only a DX11 bridge left in the folder is warned about; stock OptiScaler blocks Feeder consumers; only DLSS-NR forks cooperate | DFC silently does nothing if conflict |
| 19 | Non-NVIDIA GPU (AMD/Intel) | **E** only (FSR/XeSS inputs; spoof DXGI or OptiPatcher if game has DLSS-only input); **H/J/B/C/D/F** unavailable | OptiScaler FSR4 officially RDNA3/4 |
| 20 | Anti-cheat marker or protected Store install | **none** (show reason); optional frame limiter via driver profile | matches existing `GameInspector` behaviour |

Frame-limiter column to add to every row: NVIDIA driver `0x10835002` (NVIDIA GPU) > RTSS SDK profile > UE `t.MaxFPS` (UE only) > in-game setting. If FG is active (OptiFG, FSR-FG, MFG, Smooth Motion), recommend cap = refresh/ (FG factor) and vsync via driver; with OptiFG avoid RTSS or enable Detours hooking.

The one-neural-consumer rule behind row 18: DFC, renodx-dlss5, Alex's Toolkit and the DX11 bridge all take over the game's NGX calls, and DFC silently does nothing if another one is loaded beside it. Stock OptiScaler also takes over the NGX calls and so blocks them; only the DLSS-NR forks cooperate. `arm=0` in `deep-fried-chicken.cfg` is the real off switch for DFC (needs a restart); `enabled=0` only stops the passes.
