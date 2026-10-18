# DLSS-NR & OptiScaler-NR Sources Research Report (October 2026)

**Target Project:** DLSS Updater ([ApolloF/dlssupdater](https://github.com/ApolloF/dlssupdater))  
**Date:** 2026-10-17  
**Scope:** Investigation of upstream sources for DLSS-NR / OptiScaler-NR, RenoDX add-on integrations, ShortFuse compatibility binaries, DLSS 5 build variations, and update architecture recommendations.  
**Constraint Reminder:** *No application code modified; no binary DLLs/EXEs/archives bundled or committed.*

---

## Executive Summary

The upstream repository previously targeted by DLSS Updater for OptiScaler-NR, [`wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass`](https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass), is abandoned as of late September 2026 (79 unresolved issues, no commits since 2026-09-29). The community has coalesced around two primary maintained successors:
1. [`ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass`](https://github.com/ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass) — A direct continuation maintaining wilsjo2's pre-SR multipass pipeline while adding unlocked Multi-Frame Generation (MFG) for Turing/Ampere GPUs (`sm_86`) and preserving exact release asset naming (`OptiScaler-NR-v*.zip`).
2. [`janblade/OptiScaler-F5-DLSSNR-Multipass`](https://github.com/janblade/OptiScaler-F5-DLSSNR-Multipass) — The most active ecosystem fork (v0.1.32 released 2026-10-17), introducing synthetic motion vector/depth estimation ("Optical F5Low") for titles without native DLSS and cross-API frame generation.

Concurrently, Carlos Lopez (clshortfuse / ShortFuse) maintains the official **RenoDX** ecosystem ([`clshortfuse/renodx`](https://github.com/clshortfuse/renodx)), offering a pure ReShade 6+ add-on (`renodx-dlssfix.addon64` / `renodx-dlss.addon64`) that hooks DLSS and Streamline directly without proxying system graphics DLLs. For hardware compatibility, ShortFuse announced patched DLSS-NR version `310.9.16.0` (`compat-fp16-310.9.16-cuda13.4.3`) on 2026-10-16, supported by the open-source reconstruction tool [`clshortfuse/openNR`](https://github.com/clshortfuse/openNR) to translate Blackwell FP8 device code into FP16 for RTX 20/30/40 GPUs.

---

## 1. Forks and Successors of `wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass`

The original repository [`wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass`](https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass) pioneered two crucial features:
- **Pre-SR (Pre-Super Resolution) Execution:** Running neural rendering before the super-resolution upscaler reduces the rendered pixel footprint by ~50% compared to native resolution, drastically mitigating the performance overhead of generative neural rendering.
- **Configurable Multipass Pipeline:** Permitting 1 to 3 passes with independent style, tone, and detail weighting.

However, wilsjo2 ceased development on 2026-09-29 following prerelease `v0.8.91`, leaving 79 unresolved issues (notably scaling flicker when `WorkingScale` is below 100%, and unhandled Streamline edge cases).

### Active Forks Analysis

1. **[`ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass`](https://github.com/ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass)**:
   - **Status:** Actively maintained. Pushed 2026-10-17; 82 stars, 5 open issues.
   - **Key Changes:** Retains wilsjo2's guarded Pre-SR pipeline and 1–3 pass multipass configuration. Integrates sdli1995's Ampere/Turing Multi-Frame Generation unlock (`dlssg_sm86`) controlled via `[DLSSG] AmpereMfgUnlock=true`, bundled with required Streamline runtime components.
   - **Release Assets:** Publishes `OptiScaler-NR-v*.zip` (e.g., `OptiScaler-NR-v0.9.51.zip`) on stable releases, matching DLSS Updater's current asset regex `^OptiScaler-NR-v[\d.]+\.zip$` perfectly. Nightlies publish as `OptiScaler_v*.7z`.
   - **Licence:** GPL-3.0.

2. **[`janblade/OptiScaler-F5-DLSSNR-Multipass`](https://github.com/janblade/OptiScaler-F5-DLSSNR-Multipass)**:
   - **Status:** Highest community activity. Pushed 2026-10-17; 125 stars, 18 open issues; 30 releases (latest `v0.1.32` published 2026-10-17).
   - **Key Changes:** Extends DLSS Neural Rendering into games lacking native DLSS by generating synthetic depth and motion vectors through its proprietary "Optical F5Low" pass; supports hybrid multi-API frame generation (DLSS FG, XeFG, FSR FG) across DirectX 11, DirectX 12, and Vulkan.
   - **Release Assets:** `OptiScaler-DLSSNR-F5-*.zip` (e.g., `OptiScaler-DLSSNR-F5-0.1.32.zip`), plus optional `Optional-Vendor-Neutral-DLSS5-Forwarder.zip`.
   - **Licence:** GPL-3.0.

3. **[`jlrouzies-fr/OptiScaler-DLSSNR-PreSR-Multipass`](https://github.com/jlrouzies-fr/OptiScaler-DLSSNR-PreSR-Multipass)**:
   - **Status:** Targeted maintenance. Pushed 2026-10-03; 7 stars, 0 open issues.
   - **Key Changes:** Released `v0.8.92` specifically addressing the pre-SR sub-100% resolution bug, ensuring depth and motion buffers properly match scaled color buffers when `WorkingScale < 100%`.
   - **Licence:** GPL-3.0.

4. **[`Dagherbou/OptiScaler_DLSSNR`](https://github.com/Dagherbou/OptiScaler_DLSSNR)**:
   - **Status:** Integrated in community installers such as [DLSS5oneclick](https://github.com/faisalkindi/DLSS5oneclick).
   - **Key Changes:** Merges RenoDX color composition under MIT with OptiScaler; targets unmodified driver DLSS-NR model (RTX 50 default).
   - **Licence:** GPL-3.0 / MIT.

### Fork Comparison Table

| Repository | Status | Last Push | Stars / Issues | Release Assets Pattern | Licence | Primary Differentiator |
|---|---|---|---|---|---|---|
| [`wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass`](https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass) | **Abandoned** | 2026-09-29 | 825 / 79 | `OptiScaler-NR-v*.zip` | GPL-3.0 | Original Pre-SR multipass proof-of-concept. |
| [`ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass`](https://github.com/ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass) | **Active** | 2026-10-17 | 82 / 5 | `OptiScaler-NR-v*.zip` | GPL-3.0 | 1:1 drop-in replacement for wilsjo2; adds Turing/Ampere MFG unlock (`AmpereMfgUnlock`). |
| [`janblade/OptiScaler-F5-DLSSNR-Multipass`](https://github.com/janblade/OptiScaler-F5-DLSSNR-Multipass) | **Active** | 2026-10-17 | 125 / 18 | `OptiScaler-DLSSNR-F5-*.zip` | GPL-3.0 | Optical F5Low synthetic motion/depth for non-DLSS titles; cross-API FG (DX11/12/VK). |
| [`jlrouzies-fr/OptiScaler-DLSSNR-PreSR-Multipass`](https://github.com/jlrouzies-fr/OptiScaler-DLSSNR-PreSR-Multipass) | Low activity | 2026-10-03 | 7 / 0 | `OptiScaler-NR-v*.zip` | GPL-3.0 | Fixes sub-100% `WorkingScale` jitter/flicker. |
| [`Dagherbou/OptiScaler_DLSSNR`](https://github.com/Dagherbou/OptiScaler_DLSSNR) | Moderate | 2026-09-28 | 45 / 3 | `OptiScaler-*.zip` | GPL-3.0 / MIT | RenoDX color grading integrated into OptiScaler pipeline. |

---

## 2. The RenoDX DLSS Add-on (`clshortfuse / ShortFuse`)

Carlos Lopez ([clshortfuse](https://github.com/clshortfuse)), creator of RenoDX, maintains the official DirectX renovation framework. Within [`clshortfuse/renodx`](https://github.com/clshortfuse/renodx), the add-on source is located under [`src/addons/dlssfix`](https://github.com/clshortfuse/renodx/tree/main/src/addons/dlssfix).

### Publishing Location, Assets, and Cadence
- **Repository:** [`clshortfuse/renodx`](https://github.com/clshortfuse/renodx) (4,522 stars, 108 open issues).
- **Distribution Releases:** Nightly automated CI releases:
  - Rolling snapshot: [`https://github.com/clshortfuse/renodx/releases/tag/snapshot`](https://github.com/clshortfuse/renodx/releases/tag/snapshot)
  - Dated nightlies: `https://github.com/clshortfuse/renodx/releases/tag/nightly-YYYYMMDD` (e.g., `nightly-20261017`).
- **Asset Names:**
  - `renodx-dlssfix.addon64` (64-bit ReShade add-on, ~1.7 MB)
  - `renodx-dlssfix.addon32` (32-bit ReShade add-on, ~1.4 MB)
  - `renodx-dlssfix.pdb` (debug symbol database)
- **Downstream Aggregations:** Curated releases are also distributed under tags `renodx-dlss-SF-*` and `renodx-dlss5-*` in [`RankFTW/rhi-repo`](https://github.com/RankFTW/rhi-repo/releases) and [`yumlevi/renodx-dlss-installer`](https://github.com/yumlevi/renodx-dlss-installer).
- **Licence:** MIT License (SPDX: `MIT`).
- **Update Cadence:** Daily nightly builds generated automatically upon commits to the `main` branch.

### Architecture: RenoDX Add-on vs. OptiScaler-NR
- **OptiScaler-NR Approach:** Operates as a dynamic link library proxy (`dxgi.dll`, `d3d12.dll`, etc.) that hooks the graphics API entry points, intercepts upscaling requests, and substitutes third-party upscalers or custom render paths.
- **RenoDX Add-on Approach:** Operates as an in-process **ReShade add-on** (`.addon64`). It leaves the game's system DLL bindings intact and uses IAT and virtual table hooks directly against `nvngx_dlss.dll` and `sl.interposer.dll`. It intercepts native DLSS pipeline stages to invoke `nvngx_dlssnr.dll`.

### Installation Requirements & Configuration
1. **ReShade Requirement:** ReShade 6.x or newer compiled with full add-on support enabled (`ReShade64.dll`).
2. **Files in Game Directory:**
   - `renodx-dlssfix.addon64` (placed beside the game executable or in ReShade's add-on search path).
   - `nvngx_dlssnr.dll` (user-supplied DLSS-NR model runtime).
   - Up-to-date `nvngx_dlss.dll` and Streamline binaries (`sl.interposer.dll`, `sl.common.dll`).
3. **ReShade Configuration (`ReShade.ini`):**
   - **Early Hook Pinning:** The add-on checks `ADDON.LoadFromDllMain` to ensure hooks bind before the game loads Streamline:
     ```ini
     [ADDON]
     LoadFromDllMain=renodx-dlssfix.addon64
     ```
   - **Performance Settings (`[RenoDX.DLSS5]`):**
     - `NRHookPoint=1`: Runs DLSS-NR on the **Render** stage (pre-upscale), saving ~50% processing time compared to `NRHookPoint=0` (Upscaled).
     - `NRPasses=1`: Restricts execution to a single neural pass to avoid compounding frame times.
     - `NRDetailStability=2`: Set to *Always* to prevent temporal foliage/hair jitter during pre-SR execution.

### RenoDX Add-on Specification Table

| Attribute | Specification |
|---|---|
| **Repository** | [`clshortfuse/renodx`](https://github.com/clshortfuse/renodx) |
| **Release Tag / URL** | `snapshot` or `nightly-YYYYMMDD` via [GitHub Releases](https://github.com/clshortfuse/renodx/releases) |
| **Asset Filenames** | `renodx-dlssfix.addon64`, `renodx-dlssfix.addon32` |
| **Licence** | MIT License |
| **Injection Mechanism** | ReShade 6+ `.addon64` utilizing IAT/vtable hooks on `sl.interposer.dll` & `nvngx_dlss.dll` |
| **Target Pipeline** | Direct hooking of native DLSS games; configurable Render (Pre-SR) vs. Upscaled (Post-SR) |
| **Configuration Keys** | `[ADDON] LoadFromDllMain`, `[RenoDX.DLSS5] NRHookPoint=1`, `NRPasses=1`, `NRDetailStability=2` |
| **Update Cadence** | Continuous nightly automation |

---

## 3. ShortFuse's Patched DLSS-NR for RTX 20/30/40

On 2026-10-16, ShortFuse announced **"Patched DLSS-NR for RTX20, RTX30, and RTX40"** on the official RenoDX Discord:
- **Profile / Tag:** `compat-fp16-310.9.16-cuda13.4.3`
- **Base Version:** NVIDIA DLSS-NR `310.9.16.0`
- **Archive Name:** `nvngx_dlssnr_compat-fp16-310.9.16-cuda13.4.3.rar`

### Online Availability & Toolchain
- **Source-Only Toolchain on GitHub:** ShortFuse published [`clshortfuse/openNR`](https://github.com/clshortfuse/openNR) (MIT License, pushed 2026-10-16, 36 stars).
  - OpenNR is a source-only C++20 toolchain that reconstructs pinned DLSS-NR compatibility DLLs from user-supplied official NVIDIA binaries.
  - Profile recipe: [`profiles/compat-fp16-310.9.16.json`](https://github.com/clshortfuse/openNR/blob/main/profiles/compat-fp16-310.9.16.json).
- **Binary Distribution Status:** *Unverified on GitHub releases.* Consistent with NVIDIA's EULA restrictions, neither `clshortfuse/openNR` nor `clshortfuse/renodx` publishes pre-compiled NVIDIA binary weights or patched DLLs as GitHub release assets. The pre-patched `.rar` archive is distributed strictly through community channels (RenoDX Discord) or user-compiled via OpenNR.

### Technical Mechanism: FP16 Translation
Official NVIDIA DLSS-NR binaries (`nvngx_dlssnr.dll`) target Blackwell (`sm_100` / `sm_100a`, RTX 50 series) and rely on hardware-native **FP8 / NVFP4 tensor cores** and CUDA 13.4 compute capabilities. When executed on pre-Blackwell GPUs (Turing `sm_75`, Ampere `sm_86`, Ada Lovelace `sm_89`), the driver faults with illegal instruction exceptions.

OpenNR / ShortFuse's patch extracts device-code fatbinaries, disassembles PTX 9.4 code, translates matrix multiply-accumulate (MMA) operations to **FP16 half-precision**, and reassembles device fatbinaries using the CUDA 13.4.3 toolkit (`ptxas` / `fatbinary` version 13.4.92). This allows RTX 20, 30, and 40 series cards to run the neural model over standard FP16 tensor pipelines.

### Verification Hashes and Profiles

| Profile / Variant | Base Version | Input File Size | Input SHA-256 (NVIDIA Official) | Output SHA-256 (ShortFuse Compat) | CUDA / PTX Version |
|---|---|---|---|---|---|
| **Legacy `compat-fp16`** | 310.8.0.0 | 165,840,496 B | `e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e` | `e67dee209320cdafe0e93e45675d7aa34323a53acc57a72b2e40a181581c989a` | CUDA 13.3 / PTX 9.3 |
| **Current `compat-fp16-310.9.16`** | 310.9.16.0 | 266,424,944 B | `2bc19bc09a7b89dd44f73e2ac0108fcc586e2154bdd0830f242f4f040e28789f` | `cdfa6c1ec784867182164c7836f324f087b5c9f374d0c9fe46f54999feabf3ed` | CUDA 13.4.3 / PTX 9.4 |

### Import Workflow for DLSS Updater
In compliance with project hard rules (*"nvngx_dlssnr.dll must never be committed, bundled, uploaded or redistributed"*), the app requires user-supplied imports:
1. **User Import:** The user extracts `nvngx_dlssnr.dll` from `nvngx_dlssnr_compat-fp16-310.9.16-cuda13.4.3.rar` and drops it into DLSS Updater (either beside `DLSSUpdater.exe` or via **Settings → Local components**).
2. **Version Detection via PE Header:**
   - Inspect PE resource: `FileVersionInfo.GetVersionInfo(path)`.
   - Verify `OriginalFilename == "nvngx_dlssnr.dll"`.
   - Read `FileVersion` / `ProductVersion`: returns `310.9.16.0` (or `310.8.0.0`).
3. **Variant Identification via SHA-256:**
   - Compute SHA-256 of the file and cross-reference with an expanded `ComponentStore.KnownDlssNr`:
     - `e16bcf...` → `"NVIDIA original · RTX 50 (310.8.0)"`
     - `e67dee...` → `"ShortFuse compat · RTX 20–50 (310.8.0)"`
     - `2bc19b...` → `"NVIDIA original · RTX 50 (310.9.16)"`
     - `cdfa6c...` → `"ShortFuse compat · RTX 20–50 (310.9.16)"`
     - Other → `"Unknown / Unverified build"`
4. **Multi-Version Library Storage:**
   - Store imported files in `%LocalAppData%\DLSSUpdater\components\dlssnr\<tag>\nvngx_dlssnr.dll`, allowing users to retain both 310.8 and 310.9.16 builds and toggle between them.

---

## 4. DLSS 5 / NR Builds: Version Differences & Per-Game Swapping

Two distinct major milestone builds of DLSS 5 / Neural Rendering (`nvngx_dlssnr.dll`) are circulating in the PC modding ecosystem:

### Build Characteristics

1. **DLSS-NR 310.8.0.0 (September 2026 - Initial Leak):**
   - **Size:** ~158 MB (165,840,496 bytes).
   - **Origin:** Early developer build leak (discovered in early *NBA 2K27* binaries).
   - **Hardware Support:** Native RTX 50; RTX 20/30/40 via legacy `compat-fp16` (CUDA 13.3).
   - **Performance & Stability:** Heavy computational penalty (often reducing frame rates by 40–50%); erratic VRAM buffer spikes; noticeable temporal instability (edge shimmering, moiré on wire meshes and fine foliage); sub-100% resolution scaling causes severe ghosting and jitter.

2. **DLSS-NR 310.9.16.0 (October 2026 - Streamline 2.14.16 Release):**
   - **Size:** ~254 MB (266,424,944 bytes) — enlarged due to expanded parameter count and refined weights.
   - **Origin:** Streamline SDK 2.14.16 / NGX runtime release.
   - **Hardware Support:** Native RTX 50; RTX 20/30/40 via `compat-fp16-310.9.16-cuda13.4.3` (CUDA 13.4.3, PTX 9.4).
   - **Performance & Stability:** Substantial generation speedup (30–40% faster inference times); significantly smoother frame pacing; reduced VRAM allocation volatility; improved motion vector alignment minimizing smearing and haloing.

### Why Users Need Per-Game Version Swapping
Users cannot rely on a single universal DLSS-NR binary across all titles for several practical reasons:
- **Engine Initialization Crashes:** Certain DirectX 11 titles (or older Unreal Engine 4 titles using legacy proxy hooks) crash upon encountering the 254 MB memory layout or new Streamline 2.14 entry points of 310.9.16, but initialize stably under 310.8.0.
- **Visual Art Direction & Mod Conflicts:** Generative lighting enhancements in 310.9.16 feature altered contrast and skin/material tone-mapping curves. In titles heavily modified with custom HDR shaders (e.g., Cyberpunk 2077 with RenoDX), 310.9.16 can occasionally over-saturate specular highlights, leading users to prefer 310.8.0.
- **Hardware-Specific VRAM Constraints:** On lower-VRAM cards (e.g., 6 GB RTX 2060 or 8 GB RTX 3070), the larger working buffers of 310.9.16 can push total allocation past local VRAM thresholds in memory-heavy titles, inducing system paging stutters where 310.8.0 remained within VRAM limits.
- **Proxy Hook Compatibility:** Certain mod wrappers (e.g., older builds of OptiScaler-NR or specific game-specific ReShade add-ons) have hardcoded buffer expectations that malfunction under 310.9.16.

### DLSS 5 / NR Version Comparison Table

| Attribute | DLSS-NR 310.8.0.0 (Old) | DLSS-NR 310.9.16.0 (New) |
|---|---|---|
| **Release Timing** | September 2026 (Initial leak) | October 2026 (Streamline 2.14.16) |
| **Binary Size** | 165,840,496 bytes (~158 MB) | 266,424,944 bytes (~254 MB) |
| **Official GPU Target** | RTX 50 series (Blackwell, FP8/NVFP4) | RTX 50 series (Blackwell, FP8/NVFP4) |
| **Community GPU Target** | RTX 20, 30, 40 series via `compat-fp16` (CUDA 13.3) | RTX 20, 30, 40 series via `compat-fp16-310.9.16` (CUDA 13.4.3) |
| **Inference Efficiency** | High latency; 40–50% FPS penalty | Optimized inference; 30–40% faster than 310.8 |
| **Temporal Stability** | Prone to shimmering and foliage jitter | Markedly reduced shimmering and cleaner motion vectors |
| **VRAM Footprint** | Moderate baseline, volatile spikes | Higher baseline model footprint, but smoother memory allocation |
| **Primary Swap Reason** | Legacy engine compatibility; avoids crashes in older wrappers | Maximizing frame rates and temporal image quality in modern games |

---

## 5. Architectural Recommendations for DLSS Updater

To transition cleanly away from abandoned dependencies while enhancing stability and modularity, DLSS Updater should adopt a tiered multi-source integration model:

### Recommended Source Tiers

1. **OptiScaler-NR Integration (Primary): [`ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass`](https://github.com/ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass)**
   - **Role:** Immediate drop-in successor to `wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass`.
   - **Rationale:** Preserves exact release archive naming (`OptiScaler-NR-v*.zip`) and directory structure; resolves open bugs; adds optional Turing/Ampere MFG unlocking (`[DLSSG] AmpereMfgUnlock=true`).
   - **Update Check:** GitHub Releases API with ETag caching on `ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass`.

2. **OptiScaler-NR Integration (Alternative / Advanced): [`janblade/OptiScaler-F5-DLSSNR-Multipass`](https://github.com/janblade/OptiScaler-F5-DLSSNR-Multipass)**
   - **Role:** Advanced option for titles without native DLSS.
   - **Rationale:** Highest community momentum; provides Optical F5Low motion/depth generation and multi-API frame generation.
   - **Update Check:** GitHub Releases API with ETag caching on `janblade/OptiScaler-F5-DLSSNR-Multipass`. Asset regex: `^OptiScaler-DLSSNR-F5-[\d.]+\.zip$`.

3. **ReShade Add-on Integration (Primary for Native DLSS Titles): [`clshortfuse/renodx`](https://github.com/clshortfuse/renodx)**
   - **Role:** Non-proxy alternative for native DLSS games.
   - **Rationale:** Direct ReShade add-on injection eliminates potential proxy DLL conflicts (`dxgi.dll`); utilizes pre-SR render hooks (`NRHookPoint=1`) to halve compute costs.
   - **Update Check:** GitHub Releases API querying release `snapshot` or newest `nightly-*` tag on `clshortfuse/renodx` with ETag caching. Asset: `renodx-dlssfix.addon64`.

4. **DLSS-NR Model Management: Local Multi-Version Store**
   - **Role:** Local component storage and per-game version binding.
   - **Rationale:** Never downloads or bundles proprietary binaries. Automatically reads PE version resources (`310.8.0.0` vs `310.9.16.0`) and verifies SHA-256 against known original/compat hashes, enabling users to pin 310.8 or 310.9.16 per game.

### Source Integration Specification Matrix

| Component Source | Upstream Repository / Origin | Asset Pattern / Filename | Update Mechanism | Target Files Copied | Required INI Adjustments | GPU / Version Constraints |
|---|---|---|---|---|---|---|
| **OptiScaler-NR (Primary)** | [`ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass`](https://github.com/ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass) | `^OptiScaler-NR-v[\d.]+\.zip$` | GitHub Releases API (ETag) | `OptiScaler.dll` (as chosen proxy, e.g. `dxgi.dll`), `OptiScaler/` folder | `[DlssNr] Enabled=true`, `[DLSSG] AmpereMfgUnlock=true` (optional) | RTX 20–50; needs user-imported `nvngx_dlssnr.dll` |
| **OptiScaler-NR (Alternative)** | [`janblade/OptiScaler-F5-DLSSNR-Multipass`](https://github.com/janblade/OptiScaler-F5-DLSSNR-Multipass) | `^OptiScaler-DLSSNR-F5-[\d.]+\.zip$` | GitHub Releases API (ETag) | `OptiScaler.dll` (as proxy), `OptiScaler/` folder | `[DlssNr] Enabled=true`, `[F5Low] Enabled=true` | Supports games without native DLSS; DX11, DX12, Vulkan |
| **RenoDX DLSS Add-on** | [`clshortfuse/renodx`](https://github.com/clshortfuse/renodx) | `^renodx-dlssfix\.addon64$` | GitHub Releases API (ETag on `snapshot`/nightly) | `renodx-dlssfix.addon64` next to game exe | `[ADDON] LoadFromDllMain=renodx-dlssfix.addon64`, `[RenoDX.DLSS5] NRHookPoint=1`, `NRPasses=1` | Requires ReShade 6+ with add-ons; game must have native DLSS/Streamline |
| **DLSS-NR 310.9.16 (Compat/Stock)** | User-supplied (`nvngx_dlssnr_compat-fp16-310.9.16-cuda13.4.3.rar` or official) | `nvngx_dlssnr.dll` | User Import Only (No remote check) | `nvngx_dlssnr.dll` into game root | None (consumed by OptiScaler or RenoDX) | Original: RTX 50 only; ShortFuse compat: RTX 20/30/40/50 |
| **DLSS-NR 310.8.0 (Compat/Stock)** | User-supplied (`nvngx_dlssnr.dll` 310.8.0.0 or official) | `nvngx_dlssnr.dll` | User Import Only (No remote check) | `nvngx_dlssnr.dll` into game root | None (consumed by OptiScaler or RenoDX) | Original: RTX 50 only; ShortFuse compat: RTX 20/30/40/50 |

---

## Verification Summary

- **Verified:** Abandonment of `wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass` (last commit 2026-09-29).
- **Verified:** Active status, release cadence, and asset conventions of `ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass` and `janblade/OptiScaler-F5-DLSSNR-Multipass`.
- **Verified:** Location, source structure (`src/addons/dlssfix`), nightly release assets, and configuration options of `clshortfuse/renodx`.
- **Verified:** OpenNR profile details, required CUDA 13.4.3 toolchain versions, and reference SHA-256 hashes for both 310.8 and 310.9.16 in `clshortfuse/openNR`.
- **Verified:** PE file version metadata (`FileVersionInfo`) and OriginalFilename parsing behavior for DLSS-NR detection.
- **Unverified:** Direct public GitHub URL for the pre-built `nvngx_dlssnr_compat-fp16-310.9.16-cuda13.4.3.rar` archive (distributed via RenoDX Discord to respect NVIDIA licensing).
