# Per-game upscaling / frame-gen / DLSS 5 method detection: research report

Date of research: 2026-10-21. Nothing was written into any repo.
Legend: **[V]** = verified today by reading the primary source (GitHub API, raw wiki, live HTTP call). **[S]** = secondary source (news, forum, Nexus page). **[U]** = unverified / from memory / inferred; check before relying on it.

Context note: "DLSS 5" here is the *leaked* NVIDIA neural-rendering (NR) build (`nvngx_dlssnr.dll`, "feature 18"), used through community ReShade add-ons. It is not an NVIDIA-supported product (only NBA 2K27 reportedly ships it natively [S]). The ecosystem is moving weekly (Feeder had 4 releases in the last 4 weeks), so anything pinned here will age fast.

---------------------------------------------------------------------------

## 1. DLSSFeeder = "DLSS5-Feeder"

Name: the project is **DLSS5-Feeder** (also written "DLSS 5 Feeder"). There is no separate "DLSSFeeder".

| Item | Finding |
|---|---|
| Author | Jean-Laurent Rouzies, GitHub `jlrouzies-fr` [V] (LICENSE: "Copyright (c) 2026 Jean-Laurent ROUZIES", plus portions from NIGos dlss5-dx11-bridge, MIT) |
| Repo | https://github.com/jlrouzies-fr/DLSS5-Feeder (created 2026-08-29, last push 2026-10-18, ~1.1k stars) [V] |
| Only official download | GitHub Releases: https://github.com/jlrouzies-fr/DLSS5-Feeder/releases . Latest **v1.19.0** (2026-10-11, stable). Betas are tagged `-beta.N`; the README says betas carry fixes the last stable lacks [V] |
| Release assets | Exactly 3: `DLSS5-Feeder-<ver>.zip`, `AUTOMATIC_INSTALLATION_AVAILABLE.txt`, `CAREFUL_FAKE_MALICIOUS_FEEDER.txt`. Fake lookalike sites/repos exist (issues #88, #115), so a tool must only fetch from this exact repo [V] |
| Zip contents (v1.19.0, listed locally) | `dlss5-feed.addon64`, `dlss5-feed.addon32`, `dlss5-feed-helper.addon64`, `host64/dlss5-feed-host64.exe`, `reshade-shaders/Shaders/DLSS5_Feed.fx`, `layer-x64/VkLayer_feed_vk.dll` (+json,+bat), `layer-x86/...`, `READ-ME-FIRST.txt`, `Verify-DLSS5Feeder.ps1` [V] |
| Licence | Code: MIT ("covers only src/, shaders/, host/"; GitHub API reports NOASSERTION because of the extra notice line) [V]. Dependencies keep own licences. README carries an "AI declaration" (copilot) badge [V] |
| Maintenance risk | Single maintainer, README says he cannot test most games; asks users to fix with an AI assistant and PR [V]. A fork `RazielGenshin/DLSS5-Feeder` adds DLSS SR support (Nexus Bayonetta mod) [S] |

### How it works [V, README]
- DLSS 5 NR add-ons (DFC, RenoDX `renodx-dlss5`) only work by **detouring a game's own NGX DLSS-SR calls**. A game without DLSS never makes them.
- Feeder **manufactures the calls**: a ReShade add-on takes the backbuffer + ReShade depth (Generic Depth add-on) + **estimated motion vectors** from a ReShade shader (optical flow), builds a synthetic **DLAA** "contract" (render size = output size, no jitter), runs a real `NVSDK_NGX_D3D12_EvaluateFeature`, the neural consumer hooks in, and the result is copied back over the frame.
- It is **DLAA only**: no Quality/Balanced/Performance, no upscaling gain, no performance gain. It costs frame time (README: several ms at 4K). Optional "work resolution" 50-100% on D3D11 only [V].
- Temporal quality is limited by estimated MVs (ghosting in fast motion), HUD is processed with the scene [V].

### Supported APIs / bitness [V]
D3D11, D3D12, Vulkan, OpenGL (64-bit directly); D3D10 native via private D3D11 relay (beta); D3D9 via **dgVoodoo2** (2.87.5 pinned by one-click tool because Defender flags 2.87.3/4 [V]); 32-bit via a bundled 64-bit helper process (`host64\`), because NGX is 64-bit only; 32-bit Vulkan via DXVK. Vulkan: bundled layer fallback. OpenGL requires rendering on the NVIDIA GPU.
Status table in README lists proven games: Metro 2033 Redux, Subnautica, LOTR War in the North (D3D12), Splinter Cell Blacklist (32-bit), BioShock Remastered, Fable Anniversary, DOOM 2016 (Vulkan), Worms Ultimate Mayhem (GL), Guild Wars Reforged, WoW 3.3.5a (DXVK), KOTOR (GL), MX Bikes (GL) [V].

### Requirements (all of them) [V]
1. NVIDIA RTX GPU. **The neural model runs on RTX 50 only** with NVIDIA's signed `nvngx_dlssnr.dll` 310.8.0 and the "310.8.Lecram" build; RTX 20/30/40 need a community-modified `nvngx_dlssnr.dll` (README points to RenoDX Discord; Nexus Bayonetta mod credits "victord.ho1: Modified nvngx_dlssnr.dll for RTX20-30") [V/S]. One-click tool: RTX 50 full speed, RTX 40 moderate, RTX 20/30 heavy cost [V].
2. ReShade **6.8+ with add-on support** (API version 20; some 6.8.0 builds expose only 18 -> add-on load error 1114 [S, Nexus AIO page]).
3. ReShade **Generic Depth** add-on picking the right depth buffer (often needs manual choice + `RESHADE_DEPTH_INPUT_IS_*` defines; "wrong depth buffer" warnings in v1.19 for shadow atlases/MSAA) [V/S].
4. A **motion-vector provider shader**, chosen by `DLSS5_MV_PROVIDER`: 0 = anything writing `texMotionVectors`, 1 = iMMERSE Launchpad, 2 = VORT (MIT), **3 = LumeniteFX Kernel (recommended)**, 4 = LumeniteFX QuantMotion. Nothing third-party is bundled in the Feeder zip. LumeniteFX licence is the custom **AGNYA licence** ("All rights reserved unless explicitly stated"), so redistribution is not clear [V]. Must be enabled and ordered **above** `DLSS 5 Feed` in the technique list [V].
5. **Exactly one neural consumer** (see section 2): Deep Fried Chicken (recommended), Krish's `renodx-dlss5.addon64`, or an OptiScaler DLSS-NR fork. Never two [V].
6. `nvngx_dlssnr.dll` (not bundled by anyone but community mirrors) and `nvngx_dlss.dll` next to the game (or in `host64\` for 32-bit) [V].
7. Game must be **single player** (anticheat) and MSAA/SSAA off [V/S].
8. Smooth Motion (NVIDIA driver FG) must be **off for Vulkan** games; fine on D3D11/12 since 0.11 [V].
9. Driver/consumer matrix matters (driver 616.64+ breaks renodx-dlss5 v4.6/4.7, DFC OK; README table) [V].

### Can our app auto-download it?
- The Feeder zip itself: **yes**. Public GitHub release, MIT, stable URL pattern `https://github.com/jlrouzies-fr/DLSS5-Feeder/releases/download/v<ver>/DLSS5-Feeder-<ver>.zip`. Release body contains a SHA-256 ("the SHA-256 printed in the release notes") and VirusTotal report [V]; GitHub API also gives asset `digest`. Fits the repo's "no digest = no install" rule if the digest is taken from API/body [U: check that the API returns `digest` for these assets].
- Everything else: **no or not cleanly**:
  - DFC: **no public download** (Discord only; installer links are Discord CDN and expire) [V].
  - `renodx-dlss5.addon64` and `nvngx_dlssnr.dll`: community mirror `RankFTW/rhi-repo` releases (no licence, no hashes per our own scoper notes) [V/S]. Conflicts with the app hard rule "nvngx_dlssnr.dll must never be bundled/redistributed; user supplies it".
  - LumeniteFX: public GitHub, AGNYA licence (unclear), no releases (branch ZIP, "mainline") [V].
  - ReShade: already handled by the app (reshade.me addon setup).
  - dgVoodoo2: public GitHub releases (licence field empty in API; dgVoodoo2 is freeware, redistribution restrictions [U]).
- Copy-cat competitors that do this automatically already exist (useful as reference for detection logic): `faisalkindi/DLSS5oneclick` (Rust, MIT, 1.1k stars), `rakanki911/DLSS5-Swapper` (MIT; repo currently 404 for me, only mirrors/forks listed; site getdlss5.com), `NODIX-TECH/DLSS-5-MANAGER`, Nexus "Easy AIO Installer" (Kry0genik, mod 2251), `SenjuWoo/dlss5-aio`, `kibblerz/DLSS5-Reshade-AIO` (Apache-2.0, one add-on doing NR + SR + FG) [V]. Several of these were reported as fake/malicious by the Feeder maintainer ("at least one such repository distributes a 500 MB binary with no source" per one-click README) [V].

### Compatibility signal for "is Feeder needed?"
Feeder README, "Before you install" [V]:
| Game | Use |
|---|---|
| 64-bit DX9/11/12 **with native DLSS** | `renodx-dlss` (ShortFuse) alone, or `renodx-dlss5`/DFC alone. Feeder not needed |
| 64-bit DX9/11/12 **without native DLSS** | Feeder + consumer. `renodx-dlss` alone sits at "Auto: Waiting" with zero counters (measured on Monster Hunter: World, issue #120) |
| 32-bit (any API), Vulkan, D3D9 (best MVs) | Feeder |
| DX11 with native DLSS | `dlss5-dx11-bridge` (NIGos) or renodx-dlss5 8.x (bridges DX11 itself). Do not combine with Feeder |

---------------------------------------------------------------------------

## 2. Deep Fried Chicken (DFC)

| Item | Finding |
|---|---|
| What | A closed-source ReShade add-on (`deep-fried-chicken.addon64`) by "Alexander/Alex" that does the DLSS 5 NR pass. It is a *neural consumer*: it detours NGX feature-1 (DLSS SR) create/evaluate entry points with Detours and inserts the NR pass; has its own panel in ReShade's Add-ons tab; supports up to 30 passes, work-scale 10-150% (1.4.4+), presets. DFC 3.0 adds **Neural Upstream** (NR runs *before* the upscaler, much cheaper) [V/S] |
| Files | `deep-fried-chicken.addon64`, `deep-fried-chicken-nvngx.dll`, `deep-fried-chicken.cfg` (+ user-supplied `nvngx_dlssnr.dll`, `nvngx_dlss.dll`) [V] |
| Distribution | **Discord only** (https://discord.gg/g2v2XGqvR per Feeder README). No GitHub, no Nexus page of its own found. Bundled into Nexus mods: Easy AIO Installer 3.0 "Deep Fried Chicken Update" (DFC 2.0), Bayonetta DLSS 5 Enhanced (DFC 1.6.8.7 -> 3.0) [S]. No licence, no public hashes -> **import-only** for our app (matches current `AddonLibrary` Dfc design) |
| Versions seen | 1.3.0-alpha, 1.4.0/1.4.4/1.4.8-alpha, 1.6.8, 1.7.1, 1.7.4, 2.0, 3.0 [V/S]; Feeder wants >= 1.4.8 (earlier versions rescanned all modules every 300 frames -> stutter) [V] |
| Anti-malware | Defender flags it (Detours hooking NGX), Feeder installer adds exclusion only with user consent [V] |

### What makes a game DFC-compatible
1. **Game provides a DLSS SR feature-1 evaluate for DFC to hook.** Two ways:
   - Native DLSS in the game (D3D12 clearest; DX12 route in Easy AIO: DFC + `nvngx_dlssnr.dll` + ReShade renamed `d3d12.dll`) [S]. DX11 native-DLSS games additionally need the DLSS5 DX11 bridge in Easy AIO's route 2 [S].
   - No native DLSS: the Feeder supplies a synthetic contract. Feeder's FEEDBACK-DFC.md shows that DFC 1.3 only "lit up in games that already ship DLSS SR on D3D12"; Alexander then implemented "ABI 1" (`FEEDER-INTEROP-v1.md`: producer sets 4 unsigned NGX parameters `DFC.Feeder.ContractVersion/ProviderId/HostMode/EvaluateCadence` before every feature-1 create/evaluate) in 1.4.0 [V]. A forum post (City of Heroes, OpenGL) shows DFC status "standalone neural pipeline active" with Feeder [S].
2. **ReShade add-on build** (full add-on support, 6.8+/API 20) loaded in the process; DFC appends itself to `[ADDON] LoadFromDllMain` in `ReShade.ini` on first run and asks for one extra restart [V].
3. **Depth + motion vectors**: with native DLSS the game supplies them through NGX. Without it, they come from ReShade Generic Depth + MV provider (via Feeder). DFC itself needs nothing else [V].
4. **API coverage per Feeder README (game-validated list)**: DFC validated on 64-bit Vulkan in-process (DOOM 2016), 32-bit D3D9 via host64, 64-bit D3D11; **D3D12, OpenGL and 32-bit Vulkan "source-contract compatible per its author but not game-validated"** (README as of that text). OpenGL reported working in City of Heroes by a user [V/S]. Does **not** claim Frame Generation (live test: black screen) or Ray Reconstruction (pass-through) [V].
5. **Exclusivity**: DFC does nothing (silently) if RenoDX `renodx-dlss5`, "Alex's Toolkit" or the DX11 bridge is loaded beside it; stock OptiScaler also conflicts (it takes over the NGX calls). Detect and refuse to co-install. `arm=0` in `deep-fried-chicken.cfg` is the real off switch (needs restart); `enabled=0` only stops the passes [V].
6. **GPU**: same as NR: RTX 50 native; 20/30/40 need patched DLL. DFC 1.7+/3.0 shipped presets for RTX 2xxx/3xxx with "fp16 + int8" DLL [S].
7. Status read-back: DFC exports a data symbol `NAME` ("Deep Fried Chicken <ver>") and a state int (0 DISARMED, 1 CLAIMING, 2 ARMED, 3 CONFLICT, 4 FAILED); log file `deep-fried-chicken.log` next to exe [V, feed_dfc.h in Feeder repo]. Our app could read `dlss5-feed.log` / `deep-fried-chicken.log` for post-install verification (lines: `feature ready ... DLAA`, `frame N delivered`, `ARMED`).

Not required by DFC (per sources): any game-specific profile; game-specific engine. What *does* decide success is correct depth buffer selection and MV quality (Feeder path), and no anti-cheat.

---------------------------------------------------------------------------

## 3. OptiScaler

Repo https://github.com/optiscaler/OptiScaler , GPL-3.0, latest **v0.9.4** (2026-07-18), release asset is a **`.7z`** (`Optiscaler_0.9.4-final.20260718._MM.7z`); the Feeder-compatible NR forks ship `.zip` (~130 MB) [V]. No website; fake sites exist [V].

### When OptiScaler can work [V, README + wiki]
- **Needs an existing upscaler input in the game**: DLSS2+, FSR2+ (2.x/3.x), or XeSS. It *replaces/redirects* that upscaler with FSR2/3.x/4, XeSS or DLSS. It cannot add an upscaler to a game that has none (except via 3rd-party "upscaler mods" for those games, see below).
- **Unreal Engine: only works with UE's XeSS input** (README) [V]; UE games usually with DLSS plugin work through the DLSS input as per the compat list; OptiFG with XeSS inputs in UE has no depth [V].
- APIs: **DX12** (XeSS default, FSR2.1.2/2.2.1, FSR3.x, FSR4 (RDNA3/4 only officially), DLSS); **DX11** (FSR2.2.1 default, unofficial FSR3.1.2, DLSS, XeSS 2.x Arc only; some go through a background DX12 device, up to ~10% cost; FSR4 via DX12 interop); **Vulkan** (FSR2.1.2 default, 2.2.1, FSR3.1, DLSS, XeSS 2.x, FSR4 via DX12 interop) [V].
- **Spoofing** (`Spoofing.md` in repo) [V]: non-Nvidia GPUs need DXGI/Vulkan/Nvapi spoofing to unlock DLSS inputs (built-in DXGI spoof default on when not loaded as `nvngx.dll`; Vulkan spoof default off; FakeNvapi for Nvapi; d3d12-proxy, vulkan-spoofer alternatives). **OptiPatcher** (MIT ASI plugin, https://github.com/optiscaler/OptiPatcher, `GameSupport.md`) exposes DLSS/DLSS-FG inputs without spoofing in ~200 supported games [V]. For an NVIDIA user spoofing is irrelevant; for DLSS inputs on AMD/Intel it matters.
- Install = rename `OptiScaler.dll` to one of the supported proxy names (`dxgi.dll` default; `winmm.dll`, `version.dll`, `d3d12.dll`, `nvngx.dll`, ...); conflicts with ReShade on `dxgi.dll`. OptiScaler can load ReShade itself (`ReShade64.dll` + `LoadReshade=true`) or via `plugins\` folder / ASI loader (`LoadAsiPlugins=true`) [V, wiki "Compatibility with other mods (Reshade, SpecialK)"]. Needs `OptiScaler.ini` edits (`[Upscalers]`, `[FrameGen]`/FGInput/FGOutput, `[Spoofing]`, `[Hooks]`, `[DlssNr]` in forks).

### Frame generation in OptiScaler (0.9 rewrite) [V, wiki Frame-Generation-Options + OptiFG]
Both **FG Input** and **FG Output** must be set (INI), then restart. Overlay key `Insert`, FG toggle `End` by default.
- FG Input options: **DLSSG via Streamline** (recommended; DX12 + Streamline 2+; not SL1 games like Witcher 3, Dying Light 2, A Plague Tale Requiem, Returnal), **Nukem's dlssg-to-fsr3** (DX12 + Vulkan, locked to FSR3-FG output, needs in-game DLSS-FG enabled; bundled since 0.9), **FSR3/3.1 FG** native input, **OptiFG/Upscaler** (experimental, DX12 only, for games with no FG).
- FG Output: FSR-FG (auto becomes FSR4-FG on RDNA4), XeFG; DLSSG as output not supported.
- **OptiFG**: needs HUDfix; FSR3-FG always, XeFG/FSR4-FG "variably". UE + XeSS inputs: no depth, so no OptiFG. Wiki page **Hudfix-incompatible** lists 24 games where it cannot work (Alan Wake 2, AC Mirage, Cyberpunk 2077, Dying Light 2, GTA V Enhanced, Spider-Man series, Star Wars Outlaws, Stellar Blade, TLOU1, ...). Auto-disables overlays (Steam, RTSS, Ubisoft, EA, Overwolf); RTSS not recommended, needs "Use MS Detours API hooking" [V].
- Fakenvapi bundled (Reflex hooking, Anti-Lag 2, LatencyFlex, XeLL) [V].
- RTX 20/30 "MFG unlock": ShyVortex/wilsjo2 fork + `sdli1995/dlssg_for_sm86` (one-click README) [V/S].
- `Unreal-Engine-Tweaks` wiki lists Engine.ini cvars per OptiScaler situation (FSR2/3 crashes: `r.FidelityFX.FSR2.UseNativeDX12=1`, `r.FidelityFX.FSR3.UseNativeDX12=1`, `r.FidelityFX.FSR3.UseRHI=0`; XeFG: `r.NGX.DLSS.DilateMotionVectors=0`, `r.Streamline.DilateMotionVectors=0`; force swapchain `r.FidelityFX.FI.OverrideSwapChainDX12=1`; enable DLSS/Streamline `r.ngx.dlss.enable=1`, `r.Streamline.InitializePlugin=1`, `r.Streamline.DLSSG.Enable=1`, `t.Streamline.Reflex.Enable=1`; upscaler plugin `r.AntiAliasingMethod=4`, `r.TemporalAA.Upscaler=1`) [V].

### OptiScaler DLSS-NR forks (neural rendering inside OptiScaler) [V]
- **wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass** (GPL-3.0; v0.8.91 on 2026-09-23; assets `OptiScaler-NR-v0.8.91.zip` and `-rtx40-mfg.zip` + SHA256SUMS.txt; has hashes!). The project's v0.8.92 fix lives in jlrouzies-fr's fork release.
- **Dagherbou/OptiScaler_DLSSNR** (GPL-3.0; v0.2.0-dlssnr, 2026-09-03; asset `OptiScaler-DLSSNR-v0.2.0.zip`; ships forwarder `nvngx.dll_dlssnr.dll`).
- Both: `[DlssNr] Enabled=true`, `[Upscalers] Dx12Upscaler=dlss`, `nvngx_dlssnr.dll` user-supplied, rename to `winmm.dll`/`version.dll` when ReShade+Feeder also present. These give NR *on top of a native-DLSS game's upscaler* (best choice for Cyberpunk per one-click issue #95) and act as the Feeder's third consumer.

### Compatibility list [V]
- URL: https://github.com/optiscaler/OptiScaler/wiki/Compatibility-List
- **Machine-readable raw Markdown, no auth**: `https://raw.githubusercontent.com/wiki/optiscaler/OptiScaler/Compatibility-List.md` (HTTP 200, 172 KB, `Cache-Control: max-age=300`). Verified downloadable. Wiki is also a git repo (`OptiScaler.wiki.git`). GitHub API `repos/.../contents` does not serve wiki paths.
- Format: GitHub-flavoured Markdown tables. Main table header: `| Game | Compatibility | Upscaler <br>Inputs | OptiPatcher <br>Support | Notes | Images |`. Row example:
  `| [Cyberpunk 2077](Cyberpunk-2077) | ✅ | DLSS, FSR3, FSR3.1/4, XeSS |  | FSR4 is a preset ... | [1](https://github.com/user-attachments/assets/...) |`
  Compatibility cell: ✅ works, ❌ not working, ➖ working on a single OS (legend text says 711 working / 2 single OS / 200 OptiPatcher / 3 not working, "Last updated 20 October 2026"). Game name may be a markdown link to a per-game page (`/wiki/<slug>`), or plain text. Inputs is a comma list (`DLSS`, `FSR2`, `FSR3`, `FSR3.1/4`, `XeSS`). OptiPatcher column holds `✨` or empty. Notes are free text (launch options like `-dx12`, "EAC must be disabled", Engine.ini hints).
- Two more tables after `## Upscaler mods support` (line ~723: games with *no native upscaler* that got one via a 3rd-party mod: AC Origins, DMC5, Dishonored 2, Elden Ring (ERSS-FG), Far Cry 5, FF7 Remake (Luma, `-dx11`), RE2/3/4/7/8 (REFramework pd-upscaler + PDUpscaler), Sekiro (SekiroTSR), Total War Warhammer III, Watch Dogs 2, Prey, ...) and `## Luma Unreal Engine` (UE4/5 DX11 games where Luma replaces TAA with DLSS/DLAA; ~18 games: Octopath Traveler, Psychonauts 2 (`-dx11`), Little Nightmares I/II, Remnant From the Ashes, GUILTY GEAR -STRIVE-, ...). Those have 5 columns (no OptiPatcher column).
- Parsing hazards: HTML `<br>`, emoji, links with nested parentheses (`Elden-Ring-(ERSS‐FG)` contains a non-ASCII hyphen), a commented-out template row, multi-line notes joined with `<br>`. Names have no Steam AppID: **match by normalised title**, not id. Also separate FSR4 page (`FSR4-Compatibility-List.md`) now says it was merged into the main list [V].
- Anti-cheat: the list's own banner: "ATM the only games not working are Anti-Cheat enabled, with some exceptions" and OptiScaler README warns against online games [V].
- Licence of wiki text: repo is GPL-3.0; wiki content licence not stated [U]. Rate limit: raw.githubusercontent.com unauthenticated is generous but cache with ETag; the file changes daily.

---------------------------------------------------------------------------

## 4. Per-game data sources a tool could query

### 4.1 PCGamingWiki (API changed in August 2026, important)
Primary source: https://www.pcgamingwiki.com/wiki/PCGamingWiki:API (read via `action=parse`), updated 2026-09-21 [V].

**Big finding: anonymous `cargoquery` no longer works.** Every anonymous call I made returned `permissiondenied: "You don't have permission to run arbitrary Cargo queries."` [V]. The API page says: since the August 2026 server migration (Canasta/Docker, WikiWorks) `cargoquery` requires **login with a MediaWiki bot password** (`Special:BotPasswords`, grant "Create, query and delete data through the Cargo extension", then `action=login`). That means a shipped app would need a user-created bot password or a project-owned account embedded as a secret: not viable for a distributed app. Alternatives that still work anonymously [V]:
- `GET https://www.pcgamingwiki.com/w/api.php?action=idlookup&field=steamappid&value=1091500&format=json&formatversion=2` -> `{"idlookup":[{"title":{"Page":"Cyberpunk 2077"}}]}` (Steam AppID -> page name).
- `https://www.pcgamingwiki.com/api/appid.php?appid=1091500` -> HTTP 302 to the wiki page; `https://www.pcgamingwiki.com/api/gog.php?page=<id>` for GOG (the page said these were broken after the migration; the Steam one worked today).
- `GET /w/api.php?action=parse&page=<Page>&prop=wikitext&format=json` or `https://www.pcgamingwiki.com/wiki/<Page>?action=raw` returns the raw page with its template parameters. These work anonymously. Parse the infobox/section templates yourself.
- `action=opensearch&search=<title>&redirects=resolve` for title -> page.
- Legacy SMW `askargs` is explicitly dead.

**Rate limit/etiquette** [V]: 60 requests/minute; exceeding gives HTTP 429 and a 60 s IP block; custom User-Agent with contact info mandatory (generic UA -> 403/ban); "cache as much as you can, infrastructure not designed for scraping". **Licence of content**: CC BY-NC-SA 3.0 (`rightsinfo` returns it) [V]; the one-click tool credits it as such. NC + SA means: attribute, don't embed in a paid product, and derived data may need SA. A free open-source app that fetches per user per game and caches locally is the safe pattern; don't ship a bulk dump.

**Cargo tables and fields** (listed live via `action=cargotables` / `cargofields`, which still work anonymously) [V]:
Tables: `Game` (was `Infobox_game`, renamed 2026-08-25), `GameEngine` (was `Infobox_game_engine`), `API`, `Video`, `Input`, `Middleware`, `Audio`, `Availability`, `GameData`, `Engine`, `Mods`, `VR` (was `VR_support`), `Tags`, `StoreFeature`, `StoreKeys`, `L10n`, `Multiplayer`, `Cloud`, `Company`, `Software`, `_pageData`, `PCGW_*` internal ones.
- `Game`: `Steam_AppID`, `GOGcom_ID`, `Engines` (page list), `Developers`, `Publishers`, `Released_Windows`, `Wrappers_Windows` (e.g. dgVoodoo, DXVK), `Available_on`, ...
- `API`: `Direct3D_versions` (list), `DirectDraw_versions`, `OpenGL_versions`, `Vulkan_versions`, `Glide_versions`, `Mantle_support`, `Windows_32bit_executable`, `Windows_64bit_executable`, ...
- `Video`: **`Upscaling`** (list of techs, e.g. "DLSS 4, FSR 2.1, FSR 3, ..."), **`Frame_gen`** (list), `Antialiasing`, `HDR`, `Ray_tracing`, `Vsync`, `60_FPS`, `120_FPS`, `Windowed`, `Borderless_fullscreen_windowed`, `Ultrawidescreen`, `4K_Ultra_HD`, `Field_of_view`, ...
- `Middleware`: `Anticheat` (list), `Multiplayer`, `Physics`, `Audio`, `Interface`, `Input`, `Cutscenes`.
- `GameEngine`: `Engine`, `Build`.
- `Availability`: renamed 2026-08-30: `Available_from`->`Present`, `..._historically`->`Past`, `..._future`->`Future`; store keys moved to `StoreKeys`.

**Exact cargo query URLs (work only with a bot-password session, shown for completeness)**:
```
# one join, by Steam AppID (HOLDS is needed because Steam_AppID is a list-like field)
https://www.pcgamingwiki.com/w/api.php?action=cargoquery&format=json
 &tables=Game,API,Video,Middleware,GameEngine
 &join_on=Game._pageID=API._pageID,Game._pageID=Video._pageID,Game._pageID=Middleware._pageID,Game._pageID=GameEngine._pageID
 &fields=Game._pageName=Page,Game.Steam_AppID,Game.Engines,API.Direct3D_versions,API.Vulkan_versions,API.OpenGL_versions,API.Windows_64bit_executable,API.Windows_32bit_executable,Video.Upscaling,Video.Frame_gen,Video.HDR,Video.Ray_tracing,Video.Vsync,Video.120_FPS,Middleware.Anticheat,GameEngine.Engine,GameEngine.Build
 &where=Game.Steam_AppID HOLDS "1091500"
```
This is built from the official API-page example (`tables=Game,Video ... join_on=Game._pageID=Video._pageID&where=Game.Steam_AppID HOLDS "1245620"`) [V pattern; the 5-table join itself is [U] since I could not execute it]. `_pageName` / `_pageID` need an alias in `fields`. Case-insensitive again since 2026-09-21; add `BINARY` for case sensitive.

**Anonymous equivalent in practice** (what a shipped app should do): `idlookup` for the page name -> `action=raw` page -> parse these wikitext keys (seen in the Cyberpunk page today [V]): `|direct3d versions = 12`, `|vulkan versions =`, `|opengl versions =`, `|windows 64-bit exe = true`, `|upscaling = true`, `|upscaling tech = DLSS 4, FSR 2.1, ...`, `|framegen notes = ...` (the `frame gen` field exists in Cargo as `Video.Frame_gen`; wikitext key is likely `|frame gen =` [U]), `|hdr = limited`, `|ray tracing = true`, `|anticheat = ` (Middleware), `|120 fps = true`, `|vsync = true`. Quality caveat: fields are community-maintained and often empty or stale (Cyberpunk's `anticheat` empty; Direct3D only "12"). Treat as a hint, never as a gate. The one-click tool does the same: reads the "per-game graphics API line" from PCGW only when exe + game logs do not show the API [V].

### 4.2 OptiScaler compat list: see section 3 (raw Markdown, daily). OptiPatcher supported games: https://github.com/optiscaler/OptiPatcher/blob/main/GameSupport.md [V exists; format not inspected].

### 4.3 RenoDX mod list (HDR mods; also the home of ShortFuse's DLSS add-on)
- Machine-readable: **`https://github.com/clshortfuse/renodx/releases/download/snapshot/games-index.json`** (generated by `scripts/generate-release-manifest.mjs`, same release has `generated-metadata.json`, `renodx-metadata-schema.json`) [V]. Today: 241 games, 207 with `steam_appid`; fields per game: `id`, `title`, `aliases[]`, `steam_appid`, `deploy{steam_appid,architecture,game_exe,api,process_name,platform,microsoft_store_id,xbox_game,nexus_mods_game_id,reshade_preferred_name}`, `mods[]{id,title,variant,support,category,compatibility (unknown|working|in-progress),status (stable|beta|experimental|null),maintainers[],summary,notes[],tags[],urls[{url,kind:nexus_mods...}],deploy,artifacts[{name,arch,size,url,path}]}` [V]. Artifacts are same-release assets `renodx-<id>.addon64/.addon32` (547 assets in snapshot) [V]. Licence of repo MIT [V]; mod binaries' licences are per-mod [U].
- Wiki mod list: `https://raw.githubusercontent.com/wiki/clshortfuse/renodx/Mods.md` (HTTP 200, 483 KB; Markdown table `Name | Maintainer | Links (badges) | Status` with ✅/🚧 and hover notes). Wiki editable by outsiders, so treat links as untrusted (one-click tool restricts hosts) [V].
- Scope caveat: these are *HDR / tone-mapping* mods per game, not DLSS. Games here are usually DX11/12, 64-bit (some `.addon32`). The generic DLSS add-on `renodx-dlss` (ShortFuse) is not on GitHub, comes from RenoDX Discord / mirror [V per Feeder README].

### 4.4 Luma Framework wiki
- Repo https://github.com/Filoppi/Luma-Framework (licence NOASSERTION); wiki raw: `https://raw.githubusercontent.com/wiki/Filoppi/Luma-Framework/Home.md` (HTTP 200, 47 KB) [V]. Tables per status ("Completed Mods", "In progress", "Planned") `| Name | Author | Download Link | Status | Special Notes | Features |`. Mods are **DX11-only** ReShade add-ons (Prey, BioShock, Borderlands, Unity/Unreal generic ones...), HDR baseline, some add DLSS/FSR. A UE section lists games that work with the generic **Luma UE** mod (replaces UE4/5 TAA with DLSS/DLAA/FSR3; needs DX11 + Engine.ini lines) [V per OptiScaler list]. Releases are zips (`Luma-<Game>.zip`, tags `latest-<n>`) on GitHub [V]. No JSON index.

### 4.5 NVIDIA DLSS game list
- Page: https://www.nvidia.com/en-us/geforce/news/nvidia-rtx-games-engines-apps/ (article dated 2025-01-30; "over 1000 games"). The table is **not exposed as JSON/HTML table to a plain fetch**: I found zero `<table>` tags and no data endpoint; the feature-support chart is a PNG [V]. Not usable programmatically. No mention of DLSS 5 there [V].
- NVIDIA's announced DLSS 5 list (GTC 2026-03, 15 titles: AION 2, Assassin's Creed Shadows, Black State, CINDER CITY, Delta Force, Hogwarts Legacy, Justice, NARAKA, NTE, Phantom Blade Zero, Resident Evil Requiem, Sea of Remnants, Starfield, Oblivion Remastered, Where Winds Meet) [S: NVIDIA news post + press]; shipped state unclear (NBA 2K27 live per one tracker) [S].
- Better: detect **locally** from DLLs (section 6); DLSS Swapper's own manifest (`beeradmoore/dlss-swapper`, GPL-3.0) lists DLL versions, not games [U].

### 4.6 Other useful machine-readable sources
- Steam: `appmanifest_*.acf` + store API `https://store.steampowered.com/api/appdetails?appids=<id>` (categories/anti-cheat not reliable) [U].
- DLSS5oneclick README documents its detection chain: exe/engine DLL import tables, `D3D12\D3D12Core.dll` => DX12, `UnityPlayer.dll`, Unity `Player.log` ("Version:" graphics line in `%USERPROFILE%\AppData\LocalLow\<company>\<product>\`), Unreal `Saved\Logs` RHI line, then PCGW, then default DX12 [V]. Good template to copy.

---------------------------------------------------------------------------

## 5. Frame limiters usable automatically

Principle: **a limiter and a frame-gen/neural path interact**: OptiFG disables RTSS overlay; FG wants vsync/limit at ~half refresh; NVIDIA Smooth Motion is driver-level; Feeder says neural cost at 4K can force 30 fps under 60 Hz vsync. Choose exactly one limiter per game.

| Limiter | How to set per game | Confidence |
|---|---|---|
| **NVIDIA driver profile "Frame Rate Limiter V3"**, setting id **`0x10835002`** (min driver 441.87), DWORD = target fps (0 = off; 20..N). Group "Sync and Refresh". Companion ids: `0x10835005` "Frame Rate Limiter - Background Application", `0x1083500A` FRL for NVCPL, `0x10834FEE` legacy "Frame Rate Limiter" (bit31 enable, low byte fps), `0x10835000` older FRL. Source: NVIDIA Profile Inspector `CustomSettingNames.xml` (Orbmu2k/nvidiaProfileInspector, `nvidiaProfileInspector/CustomSettingNames.xml` lines ~2958, ~1576) [V] | Write through NvAPI DRS to the game's app profile (the app already has `NvDrs`): create/find profile for exe, `DRS_SetSetting(0x10835002, fps)`, save. Works for every API (driver level) but needs NVIDIA GPU and admin-free (DRS writes need no elevation for user profiles, usually [U]). Hard rule of repo: tests must not call NvAPI Save | V for id, U for admin note |
| Smooth Motion (driver FG): `0xB0D384C0` Enable (0/1 per app), `0xB0CC0875` Enabled APIs (bitfield 1=DX12, 2=DX11, 4=Vulkan, default 7), `0xB01B8B02` Debug Bars. From the Feeder README [V]. Non-injection FG option for RTX 40/50 [U: GPU requirement]. Must clear the Vulkan bit when Feeder is used on Vulkan games |
| **RTSS** | (a) RTSS SDK `RTSSHooks.dll` in the RTSS install dir (registry `HKLM\SOFTWARE\WOW6432Node\Unwinder\RTSS` value `InstallDir`): `LoadProfile("<exe>.exe")`, `SetProfileProperty("FramerateLimit", DWORD)`, `SaveProfile`, `UpdateProfiles()`. This is exactly what `xanderfrangos/rtss-cli` does [V from source]. Property names: `FramerateLimit`, `AppDetectionLevel`, `EnableDynamicOffsetDetection` (custom D3D support), `EnableOSD`, ... (rtss-cli README) [V]. (b) Direct file edit: `<RTSS>\Profiles\<GameExe>.exe.cfg` with `[Framerate]` / `Limit=<n>` (+ optional `LimitDenominator`, e.g. 5994/100 = 59.94) per a Guru3D post; global in `Profiles\Global`; templates in `ProfileTemplates` [S, single forum source, unverified syntax] -> prefer the SDK. RTSS runs elevated; the edit may need admin | V for (a) source; U for (b) exact syntax |
| **Special K** | Per-game `SpecialK.ini` (next to the injected wrapper's data under `Documents\My Mods\SpecialK\Profiles\<game>\`) or the global `master_<DLLName>.ini` under `...\SpecialK\Global\` [V: path strings in `config.cpp`: `\Global\master_<undecorated_name>.ini`, `Profiles\AppCache\<id>\SpecialK.AppCache`]. Key: section **`[Render.FrameRate]`**, **`TargetFPS`** (float; "negative signed values are non-limiting"), `BackgroundFPS`, `WaitForVBLANK`, `BackBufferCount`, `LastRefreshRate` [V from `SpecialKO/SpecialK` `src/config.cpp` lines 2127-2132, GPL-3.0]. Exact per-game profile directory name is derived from app name/id; I did not verify the folder naming rule [U]. Special K is a DLL injector (global injection or local wrapper `dxgi.dll`), conflicts with ReShade/OptiScaler on proxy names, and is flagged by many anti-cheats | V keys, U folder rule |
| **ReShade** | **No built-in frame limiter and no known official limiter add-on.** crosire has long declined; a community "FPS Limiter for Vulkan, OpenGL, DirectX 9-12" exists as a separate loader (reshade.me forum thread 4600) [S]. Not recommended as a target |
| **Unreal Engine games** | `Engine.ini` `[SystemSettings]` `t.MaxFPS=<n>` (also `r.VSync=0/1`, `r.VSyncEditor`) — standard UE console variable [U from memory; OptiScaler wiki confirms Engine.ini location approach: via PCGW "Configuration file(s) location", e.g. `%LOCALAPPDATA%\<Project>\Saved\Config\Windows` or `WindowsNoEditor`; file may need to be created and made Read-Only] [V for location guidance]. Also `-dx11`/`-dx12` launch flags (see Opti Luma notes) [V]. Many shipping builds disable the console or override MaxFPS in-game settings; test per game |
| **Unity games** | No standard config file; frame cap is set in-game or via `-fps`-style args only if the game supports it [U]. Use driver limiter or RTSS instead |
| In-game limiter | Prefer when the game has `60_FPS/120_FPS` and menu cap (PCGW `Video.120_FPS`, `Vsync`); cannot be automated generically |

Recommended automation order: (1) NVIDIA driver FRL V3 `0x10835002` if NVIDIA and app already writes DRS; (2) RTSS profile via SDK if RTSS installed and no OptiFG/FG-in-OptiScaler; (3) UE `t.MaxFPS` for UE titles when the user wants engine-level pacing; (4) Special K only if the user already uses it.

---------------------------------------------------------------------------

## 6. Local signals from game files

All DLL names are matched case-insensitively, recursively (the one-click tool scans up to 4 folders deep and the Unreal `Plugins` tree [V]).

### 6.1 Upscaler / frame-gen markers
| File | Meaning | Notes |
|---|---|---|
| `nvngx_dlss.dll` | DLSS Super Resolution (SR) shipped by game. Version in file version resource (3xx.x.x = DLSS 3.x/4.x; 310.x = the 2026 "DLSS 4.5/5-era" line, 310.8/310.9 seen in Swapper) [V/U] | A *stray* copy placed by another tool makes a no-DLSS game look native (one-click marks its own copy with a sidecar `nvngx_dlss.dll.dlss5oneclick`) [V]. Our app should record what it placed |
| `nvngx_dlssd.dll` | DLSS Ray Reconstruction (RR) [V] | |
| `nvngx_dlssg.dll` | DLSS Frame Generation (FG/MFG) [V] | with `sl.*` => Streamline. Needed by MFG-unlock mods |
| `nvngx_dlssnr.dll` | DLSS 5 neural rendering model (leaked/modified). **Presence in a game folder normally means a mod placed it** [V] | |
| `sl.interposer.dll`, `sl.common.dll`, `sl.dlss.dll`, `sl.dlss_g.dll`, `sl.dlss_d.dll`, `sl.reflex.dll`, `sl.pcl.dll`, `sl.nis.dll`, `sl.deepdvc.dll` | **NVIDIA Streamline** SDK. `sl.dlss_g.dll` => DLSS-G through Streamline; interposer alone = SL present. SL1 vs SL2 matters for OptiScaler "DLSSG via SL" (SL1 games excluded) [V for sl.* family as "Streamline sl.*.dll" in one-click README; individual names U] | OptiScaler wiki: "List of SL1 games" exists [V] |
| `libxess.dll`, `libxess_dx11.dll`, `libxell.dll`, `libxess_fg.dll` | Intel XeSS (SR / DX11 backend / low latency / FG) [U for exact names] | OptiScaler XeSS input; UE accepts only the XeSS input |
| `amd_fidelityfx_dx12.dll`, `amd_fidelityfx_vk.dll`, `amd_fidelityfx_upscaler_dx12.dll`, `amd_fidelityfx_framegeneration_dx12.dll`, `amd_fidelityfx_loader_dx12.dll` (FSR 3.1+/FidelityFX SDK 2.x) | FSR3.1/FSR4/FG. Also `ffx_fsr2_api_x64.dll`, `ffx_fsr2_api_dx12_x64.dll`, `ffx_fsr2_api_vk_x64.dll` (FSR2), `ffx_fsr3upscaler_x64.dll`, `ffx_backend_dx12_x64.dll`, `ffx_opticalflow_*.dll`, `ffx_frameinterpolation_*.dll` (FSR3 FG) [U exact names] | UE games often have FSR compiled into the exe (no DLL): check `Engine\Plugins\Runtime\AMD\FSR*` or `FFXFSR*` folders, and PCGW `Upscaling` |
| `OptiScaler.ini`, `OptiScaler.log`, `fakenvapi.ini`, `nvapi64.dll` (fake), `dlssg_to_fsr3_amd_is_better.dll` | OptiScaler (or fork) already installed [V] | OptiScaler proxy names: `dxgi.dll`, `winmm.dll`, `version.dll`, `d3d12.dll`, `nvngx.dll`, ... A proxy `dxgi.dll` could be ReShade, OptiScaler, Special K, ENB, ... identify by file version / strings, not name |
| `ReShade.ini`, `ReShade64.dll`, `ReShade.log`, `dxgi.dll`/`d3d11.dll`/`d3d12.dll`/`opengl32.dll`/`vulkan-1.dll` with ReShade version resource, `*.addon64`, `*.addon32` | ReShade + add-ons present. Add-on names: `dlss5-feed.addon64`, `renodx-*.addon64`, `deep-fried-chicken.addon64`, `dlss5-bridge.addon64`/`dlss5-dx11-bridge.addon64`, `renodx-mfgunlock.addon64` [V] | Feeder logs: `dlss5-feed.log`, `host64\dlss5-feed-host.log`; DFC: `deep-fried-chicken.log`; RenoDX: `RenoDX-DLSS5-crash.log` [V] |
| `dgVoodoo.conf`, `D3D8.dll`/`D3DImm.dll`/`DDraw.dll`/`d3d9.dll` from dgVoodoo | legacy wrapper in place [V] | |
| `dxvk.conf`, `d3d9.dll`+`d3d11.dll`+`dxgi.dll` (DXVK, with `d3d11.dll` size ~ MBs) | DXVK translation (Vulkan) | Feeder has special DXVK 32-bit path |

### 6.2 Graphics API from the exe (PE imports) [U unless noted; the one-click README confirms the approach [V]]
- Static imports (parse the import table, `IMAGE_IMPORT_DESCRIPTOR`): `d3d9.dll` (DX9), `d3d10.dll`/`d3d10_1.dll` (DX10), `d3d11.dll` (DX11), `d3d12.dll` (DX12), `dxgi.dll`, `vulkan-1.dll` (Vulkan), `opengl32.dll` (GL), `d3d8.dll`, `ddraw.dll`. 
- **Many games load these dynamically** (Unity, Unreal, UE shipping exes): on one machine ~half of 169 games returned "unknown" statically [V one-click]. Fallbacks: sibling engine DLLs (`UnityPlayer.dll` + Unity `Player.log` graphics line; for Unreal read `Saved\Logs\<Project>.log` RHI line), `D3D12\D3D12Core.dll` (Agility SDK) => DX12 even if only `d3d11.dll` imported (RE Engine) [V], PCGW `API.Direct3D_versions`, then default (DX12) with a visible "assumed" flag.
- Bitness: PE `IMAGE_FILE_HEADER.Machine` (0x14c = x86, 0x8664 = x64) — the app already has bitness only for Uplift per scoper notes.
- DX12 vs DX11 for dual-API games: launch args `-dx11`/`-dx12`/`-d3d11` (UE), registry/config; Max Payne 3 can be DX9 or DX11 [V].

### 6.3 Engine markers [U unless noted]
- **Unreal**: `<Project>\Binaries\Win64\<Project>-Win64-Shipping.exe` (also `WinGDK`), `<Project>\Content\Paks\*.pak|*.utoc|*.ucas`, `Engine\` folder, `<Project>\Plugins` tree with DLSS/Streamline/FSR/XeSS plugins (UE ships these as `Engine\Plugins\Marketplace\nvidia\DLSS` or `Engine\Plugins\Runtime\Nvidia\DLSS`; one-click scans "under an Unreal project's Plugins tree" [V]). UE4 vs UE5: `Engine\Binaries\ThirdParty\...` layout and `.utoc` (UE5 IoStore) hint [U]. Config path: `%LOCALAPPDATA%\<Project>\Saved\Config\Windows|WindowsNoEditor|WinGDK` [V from OptiScaler wiki].
- **Unity**: `UnityPlayer.dll` (+ `<Game>_Data\`, `globalgamemanagers`, `MonoBleedingEdge\` = Mono, `GameAssembly.dll` = IL2CPP). Graphics API at runtime; read `Player.log` [V one-click]. Feeder proven on Unity D3D11 (Fell & Sell) [V].
- **RE Engine**: `re_chunk_000.pak` next to exe: **must load praydog REFramework first** (monolithic `dinput8.dll`) or ReShade crashes the game [V one-click].
- **Others** [U]: CryEngine (`Bin64\CryRenderD3D11.dll`/`CrySystem.dll`), id Tech (`vulkan` + `.resources`), Source (`tier0.dll`, `engine.dll`), Godot (`*.pck`), Frostbite (no clear marker), Creation Engine (`Fallout4.exe`, `Data\*.ba2`), REDengine (`bin\x64`, `archive\pc`), Anvil/Dunia (`.forge`).
- Xbox/MS Store installs (`MicrosoftGame.config`) are protected: folder unwritable, exe unreadable; Xbox launch stub is `gamelaunchhelper.exe` [V one-click].

### 6.4 Anti-cheat markers (injection / ReShade / DLL proxy = ban or kick risk)
Present in the app already: EAC, BattlEye, EA Javelin (per scoper). Extend with this union (from one-click README and common knowledge):
| Anti-cheat | Files/folders [V = in one-click README; U = my knowledge] |
|---|---|
| Easy Anti-Cheat | `EasyAntiCheat\` folder, `EasyAntiCheat_Setup.exe`/`EasyAntiCheat_EOS_Setup.exe`, `EasyAntiCheat_Launcher.exe` [V name only] |
| BattlEye | `BattlEye\` folder, `BEService.exe`, `BEService_x64.exe`, `BEClient_x64.dll`, `BEClient.dll` [V name only] |
| GameGuard | `npggNT.des`, `GameMon.des`, `GameGuard.des`, `npgl.erl` [V name only] |
| EA Javelin | `EAAntiCheat.GameServiceLauncher.exe`, `EAAntiCheat.Installer.exe`, `Javelin` [V name only] |
| Tencent ACE | `AntiCheatExpert` folder [V]; Arknights: Endfield crashed at start with it (#113) |
| Riot Vanguard | `vgc.exe`/`vgk.sys` (system level) [U]; League/Valorant refused by exe name |
| Blizzard Overwatch | no marker files; blocks unsigned DLLs; refused by exe name [V] |
| Roblox Hyperion | refused by exe name [V] |
| Others [U] | XIGNCODE3 (`xigncode` folder, `x3.xem`), nProtect, FACEIT, VAC (Valve; no marker; ReShade is generally tolerated but multi-player use is at own risk), Denuvo Anti-Cheat (no marker), Ricochet (CoD) |
Policy from the tools: one-click refuses install when markers are found; Feeder/Nexus AIO say "single player only" because multiplayer builds block depth-buffer access and trigger anti-cheat [V/S]. EAC can sometimes be disabled via launch option (OptiScaler list: "7 Days To Die: make sure EAC is disabled") [V].

---------------------------------------------------------------------------

## 7. Method inventory (what the app can offer per game)

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

---------------------------------------------------------------------------

## 8. Proposed decision table: game signals -> methods (ranked)

Pre-checks that gate everything (stop with an explanation if true): anti-cheat marker present -> **no injection methods** (no ReShade add-ons, no OptiScaler, no DLL swaps; show "online game, skipped"; a driver-profile frame limiter is still safe); Microsoft Store protected install -> unsupported; non-NVIDIA GPU -> NR methods (B,C,D,F,J) impossible, only E (+spoof/OptiPatcher), G, H n/a. GPU tier: RTX 50 = NR full; RTX 40 = NR moderate (+patched DLL for FP8); RTX 20/30 = NR heavy (patched DLL needed); GTX = no NR/DLSS. Flag always: "NR is the leaked DLSS 5 build; community DLLs".

Legend for methods = section 7 letters. "SR" = upscaling gain. Items in [] are optional extras. Ranking aims at best result per effort/risk; the app should show the top 1-3 and a reason.

| # | Game signals (local + data) | Ranked methods | Why / notes |
|---|---|---|---|
| 1 | `nvngx_dlss.dll` present, **DX12**, 64-bit, UE or other, no anti-cheat | 1. **A** (update DLSS DLLs, keep SR/FG/RR) 2. **B** (RenoDX DLSS / renodx-dlss5 / DFC NR on top) 3. **F** (OptiScaler DLSS-NR fork) 4. **E** only if user wants FSR/XeSS or FG tweaks | Cheapest and safest first. NR add-ons hook the game's DLSS. Cyberpunk-style titles that churn NGX features: prefer F |
| 2 | `nvngx_dlss.dll` present, **DX11**, 64-bit | 1. **A** 2. **B** with renodx-dlss5 8.x (bridges DX11 itself) or NIGos `dlss5-dx11-bridge` + DFC 3. **F** | DX11+DLSS needs the bridge unless 8.x; never bridge + Feeder |
| 3 | `nvngx_dlss.dll` present, **Vulkan** | 1. **A** 2. **F** (OptiScaler covers Vulkan with native DLSS; ReShade engine cannot, because ReShade reaches Vulkan through a registered layer, not a proxy DLL) 3. **D** only if no native DLSS | one-click: "a Vulkan game with DLSS goes to OptiScaler" |
| 4 | `nvngx_dlssg.dll` / `sl.dlss_g.dll` present (native DLSS-G) | A (+update dlssg) -> **H** to unlock MFG on RTX 20/30/40 -> **E** with FG Input "DLSSG via SL" (SL2+ only) or Nukem for FSR3-FG output (non-NVIDIA) | SL1 games (Witcher 3 DX11, Dying Light 2, Plague Tale Requiem, Returnal) must use Nukem/FSR3 route |
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
| 18 | OptiScaler already present (`OptiScaler.ini`) or any `renodx-dlss5` / DFC / Alex's Toolkit / DX11 bridge | enforce "one neural consumer": refuse to add a second; stock OptiScaler blocks Feeder consumers; only DLSS-NR forks cooperate | DFC silently does nothing if conflict |
| 19 | Non-NVIDIA GPU (AMD/Intel) | **E** only (FSR/XeSS inputs; spoof DXGI or OptiPatcher if game has DLSS-only input); **H/J/B/C/D/F** unavailable | OptiScaler FSR4 officially RDNA3/4 |
| 20 | Anti-cheat marker or protected Store install | **none** (show reason); optional frame limiter via driver profile | matches existing `GameInspector` behaviour |

Frame-limiter column to add to every row: NVIDIA driver `0x10835002` (NVIDIA GPU) > RTSS SDK profile > UE `t.MaxFPS` (UE only) > in-game setting. If FG is active (OptiFG, FSR-FG, MFG, Smooth Motion), recommend cap = refresh/ (FG factor) and vsync via driver; with OptiFG avoid RTSS or enable Detours hooking.

---------------------------------------------------------------------------

## 9. Risks and open questions for the app

1. **Licensing/legal**: NR depends on a *leaked* NVIDIA build (`nvngx_dlssnr.dll` + community-modified variants). Keep the existing hard rule (user supplies; never bundle/host). Feeder zip, OptiScaler forks, MFG unlocks are GPL/MIT and fine to fetch; DFC and renodx-dlss5 stay import-only.
2. **PCGW**: anonymous Cargo API is closed; `idlookup` + `action=raw` still open at 60 req/min. CC BY-NC-SA. Plan: lazy per-game lookup, cache for days, attribute, treat as hint.
3. **Volatility**: Feeder 1.17 -> 1.19 in 3 weeks; DFC 1.4 -> 3.0 in 7 weeks; driver 616.64+ breaks some consumer versions. Store the *tested* versions pairs (as `docs/addons/` already does) and gate with a compatibility matrix (Feeder README table is a good seed).
4. **Fake downloads**: copycat repos are common. Pin exact repo owner/name; verify SHA-256; never follow redirect links from READMEs.
5. **Things I could not verify**: exact DFC API support beyond Feeder README; DFC 3.0 requirements (Neural Upstream needs the game to have an upscaler; with Feeder there is only DLAA unless the RazielGenshin fork adds SR); GitHub `digest` availability for Feeder assets; RTSS `.cfg` syntax; Special K per-game profile folder naming; UE `t.MaxFPS` behaviour per title; individual `libxess*`/`ffx_*`/`sl.*` DLL names; the 5-table PCGW join query; the format of `OptiPatcher/GameSupport.md`.
6. **Unreachable pages**: tweaktown (403), godisageek (403), Nexus pages needed Exa fetch; DLSS5-Swapper repo `rakanki911/DLSS5-Swapper` returned 404 (taken down or renamed); only mirrors exist.

## Source index
- Feeder: https://github.com/jlrouzies-fr/DLSS5-Feeder (README, LICENSE, `src/feed_dfc.h`, `docs/FEEDBACK-DFC.md`, Releases v1.19.0)
- One-click: https://github.com/faisalkindi/DLSS5oneclick (README = best public description of detection rules)
- Nexus AIO: https://www.nexusmods.com/site/mods/2251 ; Bayonetta: https://www.nexusmods.com/bayonetta/mods/114
- CoH guide using DFC: https://forums.homecomingservers.com/topic/65517-city-of-slop-preliminary-guide-to-get-dlss-5-running-for-coh/
- DSOGaming: https://www.dsogaming.com/?p=196501 ; ComputerBase: https://www.computerbase.de/news/gaming/selbst-fuer-directx-9-mod-bringt-dlss-5-in-alte-pc-spiele.99153/
- OptiScaler: https://github.com/optiscaler/OptiScaler (README, Spoofing.md, wiki raw `https://raw.githubusercontent.com/wiki/optiscaler/OptiScaler/{Compatibility-List,OptiFG,Frame-Generation-Options,Unreal-Engine-Tweaks,Hudfix-incompatible,FSR4-Compatibility-List}.md`), OptiPatcher https://github.com/optiscaler/OptiPatcher
- NR forks: https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass , https://github.com/Dagherbou/OptiScaler_DLSSNR
- NIGos bridge: https://github.com/NIGos/dlss5-dx11-bridge ; AIO add-on: https://github.com/kibblerz/DLSS5-Reshade-AIO
- PCGW API: https://www.pcgamingwiki.com/wiki/PCGamingWiki:API (fetched via `action=parse`)
- RenoDX: https://github.com/clshortfuse/renodx , `https://github.com/clshortfuse/renodx/releases/download/snapshot/games-index.json`, wiki `https://raw.githubusercontent.com/wiki/clshortfuse/renodx/Mods.md`
- Luma: https://github.com/Filoppi/Luma-Framework (wiki raw `.../wiki/Filoppi/Luma-Framework/Home.md`)
- NVIDIA list: https://www.nvidia.com/en-us/geforce/news/nvidia-rtx-games-engines-apps/ ; DLSS 5 announcement https://www.nvidia.com/en-eu/geforce/news/death-stranding-2-crimson-desert-dlss-4-multi-frame-gen
- NVPI settings: https://github.com/Orbmu2k/nvidiaProfileInspector (`nvidiaProfileInspector/CustomSettingNames.xml`, `Reference.xml`)
- RTSS: https://github.com/xanderfrangos/rtss-cli (`rtss-core.cpp` shows RTSSHooks API usage)
- Special K: https://github.com/SpecialKO/SpecialK (`src/config.cpp`)
- DLSS Swapper (community): https://getdlss5.com/docs
- ReShade limiter forum: https://reshade.me/forum/general-discussion/4600-fps-limiter-for-vulkan-opengl-directx-9-12
