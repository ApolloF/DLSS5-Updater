# GITC Uplift

ReShade add-on by ghostinthecamera that runs DLSS-NR (`nvngx_dlssnr.dll`, user-supplied) on the game image.
Open source (MIT): https://github.com/ghostinthecamera/GITC-Uplift. Facts below come from the source at commit 31374b9
(v1.4.2, 2026-10-18); file:line references are into that tree. Re-check them for every new release.

## Release package

- Asset `GITC-Uplift-<version>.zip` plus `SHA256SUMS.txt` on GitHub releases (tag `v<version>`).
- Files: `gitc-uplift.addon64`, `gitc-uplift.addon32`, `gitc-uplift-helper64.exe`, `Uplift.fx`, `UpliftMask.fx`.
- Install (README): the add-on files and a `GITC-Uplift\` folder go into ReShade's add-on folder (game folder unless
  `[ADDON] AddonPath` says otherwise); the `.fx` files go into ReShade's shader folder. The README says to delete old
  `uplift.addon64` / `uplift.addon32` / `uplift-helper64.exe` when updating; the code has no rename migration.
- Needs the ReShade build with full add-on support. Vulkan and OpenGL need ReShade 6.8+.
- Version: `CMakeLists.txt:2` (`project(uplift VERSION 1.4.2)`), logged at load. Add-on name "GITC Uplift".
- State folder `%LOCALAPPDATA%\Uplift` (or env `UPLIFT_STATE_DIR`).

## Runtime (nvngx_dlssnr.dll) lookup

`environment.cpp:22-41`: if `SnippetPath` is set, only that path (relative = game folder). Otherwise
`<addon dir>\GITC-Uplift\`, then `<addon dir>\`, then the game folder. A copy mapped from the game folder is blocked
(`addon.cpp:1764-1773`; on RTX 50 NVIDIA's DLSS loads that copy first). So the app puts the dll in `GITC-Uplift\`.
README: NR 310.8 tested for older RTX cards; 310.9.16 experimental, kernels for RTX 50 only; network preset B needs 310.9.16.

## Helper process

`gitc-uplift-helper64.exe` next to the add-on runs NR for: 32-bit games (`gitc-uplift.addon32`, always), D3D9 games in
the 64-bit add-on, Vulkan with `VulkanNr=2`, 32-bit D3D12. 32-bit games still use the 64-bit `nvngx_dlssnr.dll`.

## Conflicts

- Refuses to run while a module named `renodx-dlss.addon64`, `renodx-dlss.addon`, `renodx-dlss5.addon64` or
  `renodx-dlss5.addon` is loaded (`environment.cpp:73-82`); `renodx-dlssfix` does not count. That covers ShortFuse's add-on
  and Lecram's v10 file; Lecram v11 (`dlss5-nr.addon64`) is caught at run time instead (env marker
  `RENODX_DLSS5_NR_RUNTIME_LOADED`, live foreign NGX NR features, or the dll already mapped), only when `ForeignNr=0` (Yield).
- No OptiScaler code. OptiScaler's own NR (if any) would count as a foreign NR producer.

## ReShade.ini `[Uplift]`

Read/write through ReShade's config API (`reshade_config_store.cpp`). Bools `1`/`0` (also `true`/`false` on read); choices
are the enum index; out-of-range or unreadable values keep the default with a warning; floats clamp after overlay edits.
`ConfigVersion` = 3 (written on save). Migration: `< 3` and `InputExposure=Game` → Auto. `Enabled` is re-read every second.

Main keys (default; range; values). H = hidden in the overlay.

| Key | Default | Range / values |
|---|---|---|
| Enabled | 0 | bool; overlay "Enable" |
| EnableKey | 0 | VK code 0-255, 0 = none |
| Intensity | 1.0 | 0-10 (slider 0-1) |
| Style | 0 | 0 Style A, 1 B, 2 C |
| Preset | 1 | 0 Runtime default, 1 A: Legacy, 2 B: Small FP8 (needs NR 310.9.16) |
| PassCount | 1 | 1-10 |
| ResolutionMode | 0 | 0 Full, 1 Quality 67%, 2 Balanced 58%, 3 Performance 50%, 4 Match game, 5 Custom |
| ResolutionScale | 100 | 25-100 (Custom %) |
| Source | 0 | NR stage: 0 Auto, 1 After DLSS, 2 Present |
| PreUpscale | 0 | bool; before upscaling (experimental) |
| ResolutionAfterDlss / ResolutionPresent | 0 | as ResolutionMode |
| ResolutionScaleAfterDlss / ResolutionScalePresent | 100 | 25-100 |
| KeepFacesAfterDlss / KeepFacesPresent | 0 | bool |
| LocalStructure / LocalTone | 1.0 | 0-1 |
| AutoMask | 1 | bool; Character mask |
| SkinStructure | 1.0 | 0-1, -1 = follow Structure |
| KeepFaces | 0 | bool (one extra NR run per frame) |
| FaceProtection | 0 | 0-1 |
| LightingScale | 2.5 | 0.5-10 (% of height) |
| Mask | 0 | 0 Auto (UPLIFT_MASK), 1 Off |
| MaskBlendMode | 1 | 0-3; NR 310.9.16 only; value meanings undocumented |
| UICorrection | 0 | 0 Auto, 1 Off, 2 On |
| MotionVectors | 0 | 0 Auto, 1 DLSS, 2 Launchpad, 3 None, 4 Lumenite |
| Upsampling | 1 | 0 Classic, 1 Edge-aware |
| PresentWithFrameGen | 0 | bool |
| Encoding | 0 | 0 Auto, 1 Linear BT.709, 2 sRGB, 3 BT.2100 PQ, 4 scRGB, 5 scRGB-nl |
| DiffuseWhiteNits | 0 | 0 = auto, else 48-500 |
| TransferStrength / ColorStrength | 1.0 | 0-1 |
| InputExposure | 0 | 0 Auto, 1 Game, 2 Metered, 3 Manual |
| AutoExposureMode | 0 | 0 Blend, 1 Switch (only with InputExposure Auto) |
| AutoExposureBlend | 0.8 | 0-1 (game exposure 0 … Uplift meter 1) |
| ExposureAdapt | 1 | 0 Off, 1 Smooth |
| AdaptBrighterStops / AdaptDarkerStops | 2 / 0.7 | 0.1-20 stops/s |
| LinearUnitNits | 0 | 0 = auto, else 1-10000 |
| SourcePrimaries | 0 | 0 Auto, 1 BT.709, 2 BT.2020, 3 AP1 |
| NeuralTransfer | 0 | 0 Bounded ratio, 1 Consistent |
| ChromaClamp | 0 | 0-3 stops, 0 = off |
| NearBlackGuard | 0 | bool |
| GraceSeconds | 5 | 0-60 |
| BudgetMarginMB | 0 | 0-65536 MiB, 0 = auto (512) |
| VramCheck | 0 | 0 Careful, 1 Relaxed, 2 Off |
| AutoResume / AutoRetry | 1 | bool |
| ShowNvIndicator | 0 | bool, from next load |
| LogLevel | 2 | 0 Error, 1 Warning, 2 Info, 3 Debug |
| SnippetPath | empty | runtime path, from next game start |
| NgxHooks | 0 | 0 Auto, 1 Off (safe mode: Present only), from next start |
| ForeignNr | 0 | 0 Yield, 1 Observe |
| VulkanNr | 0 | 0 Native, 1 Direct3D 12, 2 Helper |
| UseD3D9Ex | 0 | bool, from next start; exclusive fullscreen becomes borderless |
| ChainedHistory | 1 | bool (diagnostic) |
| StateRestore | 0 | 0 Full, 1 Minimal |
| MotionScaleX / MotionScaleY | 1 | -4..4 |

Look ("Shape result") keys, all shown: `ShapeResult` 0, `EditStrength` 1 (0-2), `Brighten`/`Darken`/`ResultColor`/
`HueShift`/`Shadows`/`Midtones`/`Highlights`/`LargeScaleTone`/`FineDetail` 1 (0-2), `MaxBrighten`/`MaxDarken` 0 (0-3 stops),
`MaxColorChange` 0 (0-0.5), `DetailRadius` 2 (0.25-10), `HaloSuppression` 0 (0-1), `Stabilize` 0 (Off/Static/Motion),
`StabilizeMs` 250 (16-2000), `StabilizeDetail` 0.

Face mask (advanced): `ShowFaceMask` 0, `FaceMaskThreshold` 1.5 (0-10), `FaceMaskFull` 6 (1-30), `FaceMaskStrength` 2
(0.5-4), `FaceMaskSoftness` 0.5 (0.1-2), `FaceEdgeFalloff` 0.15 (0.05-1), `FaceLightingDensity` 0.3 (0.05-1),
`FaceFillSkin` 1, `FaceFillRadius` 3 (0.5-10), `FaceFillTolerance` 0.04 (0.005-0.2), `FaceSpeckSize` 2 (0-3 px, 0 off).

Per pass 2..10: `Pass<N>FollowPass1` 1, `Intensity`, `Style`, `LocalTone`, `LocalStructure`, `SkinStructure`, `AutoMask`,
`TransferStrength`, `ColorStrength`, `Stage` (0 Same as previous, 1 Before upscaling, 2 After DLSS, 3 Present), `Preset`
(0 inherit, 1 Legacy, 2 Small FP8). Pass rows are not saved while `FollowPass1=1`.

Hidden / no effect: `DLSSQualityMode`, `DLSSPreset*`, `DLSSAutoExposure`, `PedestalRemoval`, `DepthDirection`,
`GlobalTone`, `Performance`, `AdjustVulkanDevices`, `AdjustVulkanDevicesForNgx`, `VulkanNativeNr`,
`DiagnosticRemoveDevice`, `DlssPlacementBlocked` (bookkeeping latch), `ConfigVersion`. The app does not write these.

## App

DLSS5 Updater installs the x64 add-on, helper and both effects, writes `[Uplift] Enabled=1`, and places the user-supplied
runtime in `GITC-Uplift\nvngx_dlssnr.dll`. It never downloads the runtime. ReShade + add-ons mode is required; other NR
routes are displaced and restored on uninstall. Releases newer than tested v1.4.2 warn in the log.

The app exposes the main stage, resolution, strength, face protection, exposure, compatibility and diagnostics options.
The look/shape keys, advanced face-mask keys, per-pass keys, HDR detail keys and hidden keys remain in the in-game
GITC Uplift tab. `SnippetPath` is not exposed: the app uses Uplift's default runtime lookup folder. Numeric dropdowns
provide common values; other values within the documented ranges can be entered in Advanced.
