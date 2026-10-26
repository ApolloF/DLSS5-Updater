# RenoDX DLSS (ShortFuse) — `renodx-dlss.addon64`

## Checked builds

| Build (FileVersion) | Build stamp | Size | Checked | Notes |
|---|---|---|---|---|
| 0.2026.0928.0306 | 2026-09-28T02:05:38 | 2,915,840 | 2026-10-18 | Tested working with Lecram 0928. No `Preset` key. |
| (mirror 26.1003.2350) | 2026-10-03T23:50:12 | | 2026-10-18 (strings only) | Same settings as 0928. |
| 0.2026.1016.1704 | 2026-10-16T13:04:22 | 3,333,632 | 2026-10-18 | New `Preset` key, new strength defaults, much more fence/Present hooking code. A Werewulf crash occurred with this newer SF build; it was not attributed to the add-ons. |

## Where it comes from

- No public source. `clshortfuse/renodx` holds only the shared `src/utils/dlss/*` helpers and the unrelated
  `dlssfix` add-on (`renodx-dlssfix.addon64`); nightlies don't contain `renodx-dlss.addon64`.
- Official channel: RenoDX Discord, DLSS 5 thread. Third-party mirror: `github.com/RankFTW/rhi-repo` releases,
  tags `renodx-dlss-SF-<YY.MMDD.HHMM>` (older ones `renodx-dlss-SF-0.55`), one zip per release holding `renodx-dlss.addon64`;
  the releases publish no hashes and the repository has no licence.
- The catalog (`renodxDlss` section, [catalog.md](../catalog.md)) lists the newest ten of those releases with the SHA-256 the
  catalog generator computed, each with a mirror copy. The app downloads and installs the newest build automatically (mirror
  first, the author's URL as the fallback), refuses a hash mismatch, and imports the add-on from it (`docs/design.md` section 3).
- Framework is MIT (RenoDX).

## ReShade.ini layout

Section `[RENODX-DLSS]` for globals, `[RENODX-DLSS-preset1]` for the rest (see README step 3). The app's split is
`Core/RenoDxIni.cs`. All keys are prefixed `DirectNeuralRendering` unless shown otherwise.

| Key | Section | Type | UI label | Values (index = value) | Default | Range | Fact |
|---|---|---|---|---|---|---|---|
| `OptionsMode` (no prefix) | global | enum | Options Mode | DLSS-NR, DLSS-G, DLSS-SR/DLAA | 0 | | D |
| `DLSSQualityMode` (no prefix) | global | enum | DLSS Mode | Game/default, Performance, Balanced, Quality, Ultra Performance, Ultra Quality, DLAA | 0 | | D |
| `DLSSAutoExposure` (no prefix) | global | enum | DLSS Internal Automatic Exposure | Game/default, Off, On | 0 | | D |
| `DLSSPreset{DLAA,UltraQuality,Quality,Balanced,Performance,UltraPerformance}` (no prefix) | global | enum | | Game/default, J, K, L, M (list may be longer) | 0 | | D/I |
| `HookStage` | global | enum | Hook Method | Off, Render, Upscaled, Present | **0 (Off)** | | D |
| `ReuseDlssUpscalingResources` | global | enum | Use DLSS Upscaling Resources | Never, Optional, Required | 1 | | D |
| `UiCorrectionMode` | global | enum | UI Correction | Auto, Off, On | 0 | | D |
| `Encoding` | global | enum | Encoding | Auto, Linear BT.709, sRGB, HDR10, scRGB, scRGB-nl | 0 | | D |
| `DiffuseWhiteNits` | global | float | Diffuse White (nits) | | 100 | 48..500 | D |
| `DiffuseWhiteOverride` | global | bool | (hidden) | | 0 | | D; meaning I: 1 = user set Diffuse White, 0 = derived from Encoding |
| `Intensity` | preset1 | float | Overall Intensity | | 1.0 | 0..1 | D |
| `Style` | preset1 | enum | Model | Model A, B, C | 0 | | D |
| `Preset` | preset1 | enum | Preset (Advanced) | Default, Preset A, Preset B | 0 | | D, **1016+ only** |
| `LocalStructureStrength` | preset1 | float | Structure Intensity | | 0.7 (0928: 1.0) | 0..1 | D |
| `LocalToneStrength` | preset1 | float | Tone Intensity (0928: Local Tone Intensity) | | 0.3 (0928: 1.0) | 0..1 | D |
| `AutoMask` | preset1 | bool | Character Mask | Off, On | 1 | | D |
| `SkinStructureStrength` | preset1 | float | Character Intensity (0928: Skin Structure Strength) | | 0.5 (0928: 1.0) | 0..1 | D |
| `ProcessingScale` | preset1 | float | Resolution Scale | % | 100 | 0..200 (0 = minimum) | D |
| `FilterDomain` | preset1 | enum | Scaling Domain | scRGB-nl, Signed PQ, Linear | 0 | | D |
| `DownsampleFilter` | preset1 | enum | Downsample Filter | Area, Bilinear, Nearest | 0 | | D |
| `UpsampleFilter` | preset1 | enum | Upsample Filter | Bilinear, Bicubic, Nearest | 0 | | D |
| `ScaleReconstruction` | preset1 | enum | Scale Reconstruction | RGB residual, Processed image | 0 | | D |
| `PassCount` | preset1 | int | Pass Count | | 1 | 1..10 | D |
| `SignalEncoding` | preset1 | enum | NR Signal Encoding | sRGB, PQ SDR, Linear SDR | 0 | | D |
| `BlockHudless` | preset1 | bool | Block Hudless | Off, On | 1 | | D; 0928 tooltip: diagnostic, NULL HUDLess tags to Streamline |
| `Exposure` | preset1 | float | Exposure | x | 1.0 | 0..100 (I) | D |
| `ExposureMode` | preset1 | enum | Use Exposure Value | Off, On, Inverse | 1 | | D |

Setting struct (1016, for the next diff): global flag at `+0x338`, type byte at `+0x38`
(0 float slider, 1 enum/int, 2 bool, 3 button, 8 status text).

### Runtime behaviour (probe, both builds)

- The table above was confirmed with `tools/addon-probe`: keys read in that order, labels and selection index
  checked by seeding ini values. Values are plain integer indices; out-of-range values clamp (`Style=5` → 2,
  `Exposure=150` → 100); -1 or 0.5 on a segmented key reads as 0.
- Only `-preset1` is read; no key stores the active preset slot.
- On every load it writes `[RENODX-DLSS] DLSSQualityMode=0` and `DLSSAutoExposure=0` before reading, so those two are
  session-only and must not be written by the app.
- Also reads `[ADDON] LoadFromDllMain` and `[INSTALL] HookStreamline`. Registers with add-on API 18, ImGui table 19250.
- The DLSS-G page has only the "Enable Streamline v2 Early Load" button (`DLSSGEarlyLoadConfiguration`).
- `DiffuseWhiteOverride` is read but has no widget.

### Tooltips (C)

- Hook Method: Render = native D3D12 input before DLSS SR/DLAA/RR; Upscaled = its output after reconstruction;
  Present = Streamline swapchain buffer before presentation.
- Use DLSS Upscaling Resources: Optional reuses motion vectors/depth from DLSS SR/DLAA/RR when available, else dummy
  inputs; Required skips Present NR when unavailable.
- UI Correction: Auto and Off disable DLSSNR.UICorrection; On enables it.
- Resolution Scale: percentage of the native DLSS input/output extent; 0% = minimum supported extent.
- Encoding: Auto treats native DLSS input/output as linear BT.709 RGB; others override.
- NR Signal Encoding: encoding supplied to NR after the HDR-to-SDR bridge.
- Diffuse White: reference white for the signal bridge; Encoding Auto selects it automatically.
- Exposure: multiplies NR exposure independently of the game's exposure texture.
- DLSS overrides apply at the next DLSS feature creation (game restart may be needed). "Enable Streamline v2 Early
  Load" adds the add-on to `[ADDON] LoadFromDllMain`.

## Coexistence

- SF has no awareness of other NR add-ons (no `renodx-dlss5` strings). It pins itself until process exit and hooks
  NGX `Parameter::Set` and Streamline with MinHook.
- Running it next to Lecram v10/v11 depends on Lecram's `ForeignNr=observe`; Lecram v12's default `ForeignNr=auto` runs beside it. The user ran SF 0928 + Lecram 0928 together;
  a Werewulf crash with SF 1016 was not attributed to the add-ons. Record test results per exact pair rather than inferring
  compatibility from the SF version.

## App support

DLSS5 Updater downloads and installs the newest catalog build automatically (hash-checked, see above; Settings can turn the
download off) and also imports the add-on. A build it fetched is a library build like an import (`AddonBuild.Downloaded`, with `CatalogSha` the zip's hash so
the next check knows it is there), becomes the default when it is newer than the current default, and may go in the automatic
cleanup, which never deletes imports. It installs it per game in
ReShade-only mode with ReShade 6+, a game with native DLSS and the NR runtime `nvngx_dlssnr.dll`. It adds the add-on to
`[ADDON] LoadFromDllMain`, replaces OptiScaler-NR and excludes Deep Fried Chicken; Lecram's add-on may stay next to it.

- **Library.** Every imported build is kept: `components\renodx-dlss\<sha256>\renodx-dlss.addon64` plus a `version.json` (id =
  SHA-256, file name, file version, detected label, the user's own label, import date). Only a file whose version resource names
  it `renodx-dlss.addon64` is accepted. Importing the same file again changes nothing, an import never deletes another build, and
  Remove deletes the selected one. The first run after the library arrived moves the old `components\renodx-dlss.addon64` into it
  and makes it the default.
- **Label.** SF builds publish a file version, which is the detected label (for example `0.2026.928.306`). The user can give any
  build their own name in Settings (for example `SF 0928`); lists show that name, else `<file version> (<import date>)`.
- **Selection.** Settings picks the default build; a game can pick its own ("Default (...)" follows Settings). The install
  records the build's id next to its tag, and automatic reinstalls keep the build that is installed.
- **Options.** Each option's first choice, "Add-on default", writes nothing, so the add-on's own default (table above) applies.
  A switch (Off/On) in the app writes nothing for Off, which only fits keys whose default is off. `AutoMask` defaults to 1, so
  Character mask is a list instead: Add-on default (on), On writes `1`, Off writes `0`.
- **With Lecram.** The app writes `ForeignNr=observe` for Lecram v10/v11 builds only; v12 runs beside SF by default. The tested
  pair is `renodx-dlss` 0.2026.928.x with Lecram 0928 (see [lecram-dlss5.md](lecram-dlss5.md)). The
  Install tab shows the exact selected pair's recorded status (Works, Crashed or Untested), and offers buttons to mark it working
  or crashed. Untested is neutral information; only Crashed warns, with its recorded date. Neither blocks installation.
  Results persist by both build ids by build ids.
