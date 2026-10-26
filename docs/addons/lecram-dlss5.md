# DLSS5 Neural Rendering (Lecram) — `renodx-dlss5.addon64` / `dlss5-nr.addon64`

## Checked builds

| Build | File | Version resource | Detected build label | Checked | Notes |
|---|---|---|---|---|---|
| 0927.1512 | `renodx-dlss5.addon64` | 0.2026.0927.1512 | `v7.5.0-rc2` | 2026-10-18 (strings) | Older build; only its label was read. |
| 0928 | `renodx-dlss5.addon64` | 0.2026.0928.2324 | `v8.5.0-rc10-model-sdr1` | 2026-10-18 (static) | D3D12 only. Intensity 0..2, `NRPreset` 0..3, `NRHookPoint` default 0. Overlay: Preset Default/#1/#2/#3, Style Default/Natural/Cinematic. The build the SF 0928 pair was tested with. |
| v10.7 (stable) | `renodx-dlss5.addon64` | 0.2026.1013.0940 | `v10.7.0` (also holds `v10.1.0-rc3` in tooltip texts) | 2026-10-18 (static + probe) | Adds D3D11 and Vulkan. Ships with `Offline Studio for Neural Rendering.exe`. |
| v11.1 patch 3 (experimental) | `dlss5-nr.addon64` | **none** | `v11.0.0-beta.6` | 2026-10-18 (static + probe) | Ground-up rewrite, settings layer in Rust (`nr-settings`). Identify by strings, tag by hash. |
| v11 Patch 4 | `dlss5-nr.addon64` (sha256 `79af73dc1e77…`) | **none** | `v11.0.0-beta.18` | 2026-10-18 (static + probe) | Intermediate. Same key set and catalog as beta.6 except the `NRHookPoint` default (After Upscaling) and the `NRPresentGuides` numbering; `NRFeedMode` read again. Ships inside the author's installer `DLSS5-NR-Setup-v11.0.0-beta.18.exe` (static only, never run). |
| v11 beta.22 (Patch 5) | `dlss5-nr.addon64` (sha256 `94e41518596d…`) | **none** | `v11.0.0-beta.22` | 2026-10-18 (static + probe) | Settings catalog and probed behaviour identical to beta.18; adds the API keys `NRMaskBlendMode` and `NRGpuCrashWitness`, and stays active next to a `renodx-dlss5.addon64` that ReShade has disabled. |
| v11 Patch 8 / beta.29 | `dlss5-nr.addon64` (sha256 `f7ac2d030790…`) | **none** | `v11.0.0-beta.29` | 2026-10-19 (static + probe) | Catalog, classification and all 38 migration/value probes identical to beta.22. Adds `NRPass2Preset`..`NRPass4Preset` (mixed models), a default-on D3D12 crash watcher behind `NRGpuCrashWitness`, and writes `[OVERLAY] TutorialProgress=4`. Same file standalone and inside `DLSS5-NR-Setup-v11.0.0-beta.29.exe`. beta.24 (Patch 6) and beta.26 (Patch 7) were not checked. |
| v11 Patch 10 / beta.45 | `dlss5-nr.addon64` (sha256 `c50905c160de…`) | **none** | `v11.0.0-beta.45` | 2026-10-20 (static + probe) | Same key set, API reads and writes as beta.29. Catalog identical apart from one behaviour note (`NRLocalTone` no longer "resets history"). The panel renames Render preset to "Model version" (1 Old, 2 New). Same file standalone (channel `archive` folder) and inside `DLSS5-NR-Setup-v11.0.0-beta.45.exe`. |
| v11 Patch 11 / beta.48 (**supported**) | `dlss5-nr.addon64` (sha256 `30f3154e7bee…`) | **none** | `v11.0.0-beta.48` | 2026-10-20 (static + probe) | Catalog as beta.45; classification (`classify111.py`) identical to beta.29. Adds the API key `NRPresentFgPath` (test option, Present with frame generation). Same file standalone (channel `addon` folder) and inside `DLSS5-NR-Setup-v11.0.0-beta.48.exe`. beta.40 (Patch 9) is not on the public channel and was not checked. |
| v11.9 | `dlss5-nr.addon64` (sha256 `173954453862…`) | **11.9.0.3000** (product `v11.9 (11.9.0)`, OriginalFilename `dlss5-nr.addon64`, internal name `nr-addon`) | `v11.9.0` | 2026-10-26 (static only, probe not run) | The MediaFire channel's current build. With v12.0.0 the first builds with a PE version resource; the generation comes from its OriginalFilename. The app writes the same keys as for beta.48, `NRPresentFgPath` included. |
| v12.0.0 | `dlss5-nr.addon64` (sha256 `124cd5a76b71…`) | **12.0.0.3000** (product `v12 (12.0.0)`, OriginalFilename `dlss5-nr.addon64`, internal name `nr-addon`) | `v12.0.0` | 2026-10-26 (static only, probe not run) | Same file name and section as v11, so the app treats it as the v11 generation. `NRPresentFgPath` retired (still read, logged `retired=1`). `ForeignNr` gains `auto`, the new default. New `NROpticalFlow` (settings schema 12, on by default; not offered). Also ships inside `DLSS5-NR-Setup-v12.0.0.exe` (not opened). |

The author's own names (from the beta.18 installer's `sources.json`): `v11.0.0-beta.5`..`beta.11` = "v11 Patch 3",
`beta.12`..`beta.18` = "v11 Patch 4" (C). The beta.29 installer's `sources.json` adds beta.22 = "v11 Patch 5", beta.24 = "Patch 6",
beta.26 = "Patch 7", beta.29 = "Patch 8" (C). The beta.48 installer's `sources.json` adds beta.40 = "v11 Patch 9", beta.45 = "v11 Patch 10" and
beta.48 = "v11 Patch 11" (C).

The detected build label is the app's own name for a build (the add-ons publish none): the highest `vX.Y.Z[-pre]` ASCII string in
the binary. Every build also carries the RenoDX framework strings `v6.1.0` / `v6.1.2`, which lose by being lower. The v10.7
binary has `v10.7.0` once (next to its overlay name) and `v10.1.0-rc3` twice in localised tooltips, so the highest string is
`v10.7.0`; a user can name any build themselves in Settings (for example `v10.1`). beta.18 and beta.22 carry
`v11.0.0-beta.NN` twice (add-on description and a bare string), `v11.0.0-beta.NNNR` once (glued to "NR on") and `v11.0.0-H`;
the tie at 11.0.0 goes to the most frequent, `v11.0.0-beta.NN` (C, test `Label_ReadsTheV11Beta18AndBeta22Layouts`). beta.29 has the
same layout; the app's import reads `v11.0.0-beta.29` from the real file and extracts the same add-on from its setup exe (C). beta.45 and
beta.48 drop the `dlss5-nr11.0.0-beta.NN` description string: `v11.0.0-beta.NN` three times (glued to "NR on", in the menu-key tooltip,
bare) and `v11.0.0-H` once, so the label is still `v11.0.0-beta.NN` (C, test `Label_ReadsTheV11Beta18AndBeta22Layouts`). v11 has no
framework `v6.1.x` strings. v11.9.0 and v12.0.0 carry `vX.Y.0` twice (bare and glued to "NR") and nothing higher, so the label is
`v11.9.0` / `v12.0.0` (C, test `Label_ReadsTheV11_9AndV12Layouts`); their version resource agrees.

The DLSS SDK's version string (`v310.8.0`) is never a build label: some older v10 builds (for example the one released as
`renodx-dlss5-7.0.0-rc8`, and one with file version 0.2026.828.517) carry no label of their own, only that string, which earlier
versions of the app took for their version and ranked above every real build. Any `v` string with a three-digit major is skipped
(`IsSdkLabel`); such a build falls back to its file version, which ranks below every labelled build. A library entry stored with
the SDK string is relabelled from its file when the library loads. A build from the catalog is named by the catalog's version
(the catalog takes it from the release tag, `renodx-dlss5-7.0.0-rc8` → `v7.0.0-rc8`), and library builds the catalog lists are
relabelled to it on each check; a catalog version that is itself the SDK string changes nothing.

Licence: no restriction found; THIRD-PARTY-NOTICES lists MIT RenoDX, ReShade, ImGui, Detours, nlohmann/json, gtl,
NVIDIA DLSS/Streamline headers. Downloaded and installed automatically from the catalog mirror (`neural.lecram`, public build too); imports stay possible.

## Files and placement

- One add-on file per game. v11 goes inert when `renodx-dlss5.addon64` is also loaded
  (`NR-COMPAT v1 coinstalled=1 other=renodx-dlss5.addon64 inert=1`, UI: "Remove renodx-dlss5.addon64: two NR add-ons
  are loaded"). v10 warns when two copies run. Probe (C): the check is the file's presence next to the add-on, not a
  loaded module (an empty `renodx-dlss5.addon64` is enough, beta.18 and beta.22). beta.22 also reads `[ADDON] DisabledAddons`:
  when it lists v10's add-on name `DLSS 5 Neural Rendering` it stays active
  (`coinstalled=0 other=renodx-dlss5.addon64 file=present disabled_in_reshade=1`); the file name in that list does not count.
- `nvngx_dlssnr.dll` (NVIDIA-signed): v10 next to the add-on or the game exe; v11 also `%LOCALAPPDATA%\DLSS5-NR\runtime`
  or `NRRuntimePath` in the ini. NVIDIA's own build only has RTX 50 kernels; RTX 20/30/40 need a compatibility build.
- v10.7 Offline Studio exe sits next to the add-on and needs `nvngx_dlssnr.dll` in its folder too.
- Crash log `RenoDX-DLSS5-crash.log` next to the exe.
- beta.18 and beta.22 search the runtime as beta.6 (`searched=addon,exe,shared ini=unset`, C; help text "tested with 310.8
  and 310.9.16", D). They expect nothing else from the installer: a bare probe run starts, reports `runtime_missing` and
  carries on (C).

### The author's installer (Patch 4 / beta.18, static only, never run)

`DLSS5-NR-Setup-v11.0.0-beta.18.exe`: version resource "DLSS5 NR Setup" 11.0.0-beta.18, internal name `nr-setup` (Rust). A
zip is appended to the exe (7-Zip lists it): `dlss5-nr.addon64` (same hash as the standalone beta.18 file), `package.json`
(`kind: v11`), `sources.json`, `README.txt`, `RELEASE-NOTES.md`, `setup-gui.ps1` (hand-over from the older PowerShell setup),
`studio\Offline Studio for Neural Rendering.exe` + `studio\renodx-dlss5.addon64` (Offline Studio 10.7.4 with a v10 add-on
build) and notices (C). From README, release notes and strings (C/D):

- Data in `%LOCALAPPDATA%\DLSS5-NR` (`packages`, `downloads`, `setup` + Start-menu entry, `studio`, `runtime`,
  `updates.json`). The Studio goes to `%LOCALAPPDATA%\DLSS5-NR\studio`, outside the game folders, so its
  `renodx-dlss5.addon64` never sits next to a game's `dlss5-nr.addon64`.
- Does not ship `nvngx_dlssnr.dll`: it copies one found in another game into the shared `%LOCALAPPDATA%\DLSS5-NR\runtime`
  (expected 310.8.0.0, sha256 `e16bcf15…`) and can "Reclaim" per-game copies.
- Downloads ReShade 6.8.0 (add-on build), optionally OptiScaler 0.9.4 and `nvngx_dlss.dll` 310.9.1, all hash-pinned.
- Writes `[INPUT] KeyOverlay` and `[RenoDX.DLSS5] NRToggleKey/NRScreenshotKey/NRShotKey`, edits `[ADDON] LoadFromDllMain`
  (drops v10's name when it replaces v10) and may write OptiScaler.ini (`LoadReshade`, ...). No hook-point or other NR key
  strings found (D).
- Finds `dlss5-nr.addon64` / `renodx-dlss5.addon64` "on every drive"; "Update all" updates every game behind, hand-made
  installs included, and `-Action adopt` takes one over (C). A game this app manages would be updated by it too. No
  ShortFuse (`renodx-dlss.addon64`) or DLSS5 Updater handling found (D).

## ReShade.ini

Section `[RenoDX.DLSS5]` in every build. Both keep a `ConfigVersion` and back up the ini before migrating
(`ReShade.ini.v<N>-backup`). v11 named presets live in `%LOCALAPPDATA%\RenoDX\DLSS5\Presets`.

Runtime (probe, `tools/addon-probe`):

- **v10.7** uses the ReShade config API only. On first load it writes its whole default config, 151 keys with
  `ConfigVersion=10`. Bad or out-of-range values are logged as
  `ReShade.ini [RenoDX.DLSS5] KEY=VAL (using X): this value could not be used as written`. Overlay name
  "DLSS 5 Neural Rendering"; widget labels: `NRIntensity` "Strength" 0..1, `NRStyle` Default/Natural/Cinematic,
  `NRHookPoint` Upscaled/Render/"Present (alpha)", Resolution Full/Quality/Balanced/Performance/Match game + 33..100%,
  `NRCodecMode` "HDR mode", toggles Character mask / Independent skin / UI correction / Keep the HUD.
  Log on load: `EnableHooks=2: NGX hooks only, Streamline modules left unpatched`.
- **v11.1 p3** reports itself as `v11.0.0-beta.6` (add-on name "DLSS5 NR"). Through the API it reads only
  `ConfigVersion`, `EnableHooks`, `NeuralUplift`, `ForeignNr`, `NRRuntimePath` (and `[INPUT] KeyOverlay`); every other
  setting is parsed from `ReShade.ini` on disk next to the exe, which it also writes directly (`ConfigVersion=11`,
  `NRScreenshotKey=0`, `NRShotKey=0`). Its log line `NR-SETTINGS event=effective ... NRHookPoint=3 NRPasses=1
  NRResolutionScale=1 NRStyle=0 NRIntensity=1 NRVramBuffering=0 nondefault=N` shows the values in use, and
  `NR-SETTINGS retired settings ignored` lists retired keys. Probe-confirmed ranges: `NRHookPoint` 0..3 (invalid → 3),
  `NRPasses` 1..4, `NRVramBuffering` 0..2, `NRIntensity` clamps to 1, `NeuralUplift=0` disables NR. Its UI is drawn
  with d2d1/dwrite, so labels come only from the binary strings and the user's screenshots.
  Runtime search log: `NR-RUNTIME v2 ready=0 reason=runtime_missing searched=addon,exe,shared ini=unset`.
- **v11 beta.18** reports `v11.0.0-beta.18` (`NR-CORE v1 event=start`, same add-on name). Through the API it reads the beta.6
  set plus `NRFeedMode` (C). A bare run writes the same three keys and logs `NR-HOOK v1 chosen=after_upscaling` /
  `NRHookPoint=0` (C). The retired / out-of-range / recognised classification (`classify111.py`, values 99999, -99999, 0.5
  over the 258 candidate keys) is identical to beta.6 (C); the `nr-settings` catalog differs only in the `NRHookPoint` and
  `NRPresentGuides` entries (D). `NRFeedMode` is still logged as `retired settings ignored` by the settings layer, but the
  core reads it through the API (`nr_core_feed_mode`); the release notes document 1 = keep NR's meter, 2 = force the game's
  exposure (C).
- **v11 beta.22** reports `v11.0.0-beta.22`. Catalog and classification identical to beta.18 (D/C), and every migration and
  value probe below gives the same result (C). New API reads (C): `NRGpuCrashWitness` (read before anything else; any non-zero
  value logs `NR-DRED v1 event=enabled breadcrumbs=1 contexts=1 page_faults=1 source=ini`, i.e. D3D12 DRED crash breadcrumbs, a
  diagnostic) and `NRMaskBlendMode` (any integer is passed on and logged as `NR-COMPAT v1 mask_blend_mode=N
  source=NRMaskBlendMode`, text is ignored; the binary sends it as the NGX parameter `DLSSNR.MaskBlendMode`, D). Neither key is
  in the settings catalog; their value meanings are undocumented, so the app offers neither.
- **v11 beta.29** reports `v11.0.0-beta.29`. Catalog (apart from the new keys), classification (`classify111.py`) and every
  migration and value probe of beta.22 (38 seeded inis) give the same result (C). New:
  - `NRPass2Preset`..`NRPass4Preset`, catalog (C): "0 Same as pass 1 (default: the model pass 1 runs, Render preset's choice or
    the model lab's Preset 1), 1-3 the DLL's preset id for this pass alone (310.9.16: 1 honest_tench, the model the lab edits; 2
    the small FP8 model). Sent at this pass's feature create only while the DLL ships several models, else 0. A change replaces
    the live NR features." Probe (C): 1, 2, 3 are kept (logged as non-default); 4, -1, 1.5 and text read as 0. Accepted whatever
    `NRPasses` and `NRPass<N>FollowPass1` say (the settings layer does not tie them). `NRPreset` also accepts 0..3 (4 reads as 0).
  - `NRGpuCrashWitness` (C, release notes + probe): absent = crash watcher on, DRED off (`NR-DRED v1 event=start watch=1 dred=0
    source=off`); `0` = all off (`watch=0`); any non-zero = watcher and DRED on from launch (`event=enabled ... source=ini`).
    After a D3D12 freeze or device removal it writes `dlss5-nr-crash-<time>.txt` beside ReShade.log, names it on Home at the next
    launch and turns DRED on for the next launches "until it catches a crash or another build is installed" (C).
  - Reads `[OVERLAY] TutorialProgress` and writes `4` when it is absent or 0 (probe: 1, 3, 4 kept, C).
  - 007 First Light (`007firstlight.exe` in the binary): After and Before Upscaling run as Auto (Present) there (release notes, C).
- **v11 beta.45** (Patch 10) reports `v11.0.0-beta.45`. Bare run: same API reads and writes as beta.29 (C). Catalog: every entry as
  beta.29 except `NRLocalTone`, whose note "model steering resets history" became "model steering" (C; the changelog says changing
  Strength, Style, Local tone, Structure or Skin no longer resets NR's image with a flicker). The panel calls the `NRPreset` control
  "Model version" ("Old: the first model. New: the newer, faster model and NVIDIA's default", C) and lists the pass presets as
  `As pass 1|Old|New|Preset 3`, i.e. 1 = Old (honest_tench, the model the lab edits), 2 = New (the small FP8 model) (C). The catalog
  text of `NRPreset` / `NRPass<N>Preset` is unchanged; 0 is still the DLL's shipping default. The changelog's other items (letterbox
  handling at Present, VRAM buffering behaviour, fixes) change no key.
- **v11 beta.48** (Patch 11) reports `v11.0.0-beta.48`. Catalog as beta.45; the retired / out-of-range / recognised classification
  over 298 candidate keys (values 99999, -99999, 0.5) is identical to beta.29 (C). `NRPreset` and `NRPass2Preset` keep 1..3 and read
  4 as 0 (C). The v10 migration is unchanged (a `ConfigVersion=10` file gets `NRHookPoint=0`, `NRModelCarryoverReset=1`,
  `ConfigVersion=11`, C). New:
  - `NRPresentFgPath`, read through the API right after `NRGpuCrashWitness` (not in the settings catalog, so never logged as retired
    or non-default). Logged at start as `NR-PRESENT v1 fg_path=<classic|contract> NRPresentFgPath=<n>` (C). Probe (C): absent, 0, 2,
    99999, -1, `true`, `on` give `classic`; 1 and 1.5 give `contract` (read as an integer; only 1 switches). The add-on never writes
    the key; a seeded value survives the v10 migration as written. Release notes (C): a test option, off by default: 1 runs NR once
    per real frame at frame generation's call and never again on the final frame, for players who see ROUTE-02 blinks at Present
    with frame generation (`ROUTE-02` is the diagnostic "Real-frame schedule has a missing delivered job", C). beta.29 and beta.45
    do not read it (C).
  - The retired settings keys `NRPresentPreFg` / `NRPresentPreFgContract` stay retired (C); the new path is the API key only.
- **v11.9.0 and v12.0.0** (static strings only; the probe was not run on either). Both have a version resource (11.9.0.3000 and
  12.0.0.3000, description "v12 (12.0.0): ReShade's menu key ... opens the DLSS 5 NR menu", C). In v12.0.0 (C):
  - `NRPresentFgPath` is retired: the start line reads `NR-PRESENT v1 fg_path=classic NRPresentFgPath=<n> retired=1`, so the
    value no longer switches the path. The app hides the option for v12.0.0 and newer and removes a value it wrote for v11.
  - `ForeignNr` takes `auto` (the new default), `yield` or `observe`; auto keeps running beside ShortFuse's `renodx-dlss.addon64`
    (`NR-COMPAT v1 other_nr=renodx-dlss.addon64 ...`, `beside`). The app writes no `observe` for v12.
  - `NROpticalFlow` is new (settings schema 12: "NROpticalFlow on by default; an older file that stores it off adopts on once";
    `NR-OFA v1 available=... enabled=...` log lines). Not offered: what it does in a game needs a probe, and the app writes
    `ConfigVersion=11` when it seeds a hook point.

### Main keys

| Key | Type | v10.7 default / range | v11.1 default / range | Values | Fact |
|---|---|---|---|---|---|
| `EnableHooks` | int | 2 | absent = auto (may escalate) | 0 all hooks off, 1 NGX + Streamline, 2 NGX only | C |
| `DX11Source` | text | auto | auto | auto, native, foreign, off. Present in D3D11 needs native | C |
| `ForeignNr` | text | yield | yield; v12.0.0: auto | yield = stand down when another NR producer (NGX feature 18) evaluates in the process; observe = keep running; auto (v12) = keep running beside ShortFuse's add-on by itself | C/D |
| `NRHookPoint` | int | 2 (Present), 0..2 | beta.6: 3 (Auto); beta.18+: 0 (After Upscaling) | 0 Upscaled ("After Upscaling"), 1 Render ("Before Upscaling"), 2 Present, 3 Auto (v11: Present with Upscaled fallback). Invalid → the build's default | C/D |
| `NRIntensity` | float | 1.0, 0..1 | 1.0, 0..1 (higher clamps) | 0928: 0..2 | D/C |
| `NRStyle` | int | 0, 0..2 | 0, 0..2 | 0928 overlay Default/Natural/Cinematic; v11 overlay Model A/B/C | D/C |
| `NRLocalTone` | float | 1.0, 0..2 | 1.0, 0..2 | | D/C |
| `NRLocalStructure` | float | 1.0, 0..2 | 1.0, 0..1 | | D/C |
| `NRSkinStructure` | float | -1, -1..1 (-1 = follow stock) | 0, 0..1 (negative reads 0) | | D/C |
| `NRSkinIndependent` | bool | 1 | retired (always on) | | D/C |
| `NRAutoMask` | bool | 0 | retired (always on) | Character mask | D/C |
| `NRUICorrection` | bool | 0 | 0 | | D |
| `NRPasses` | int | 1, 1..4 | 1, 1..4 (+ `NRPass2..4*` per-pass keys) | | D/C |
| `NRPreset` | int | not read | 0, 0..3 | 0 = DLL's shipping default; sent as `DLSSNR.Hint.Render.Preset`; recreates NR; shown only when the DLL has several models. DLL 310.9.16 (beta.29 catalog): 1 honest_tench (the model the lab edits), 2 small FP8 model. beta.45+ panel: "Model version", 1 Old, 2 New | C |
| `NRPass2Preset`..`NRPass4Preset` | int | — | beta.29+: 0, 0..3 | 0 same as pass 1, else the DLL's preset id for that pass (as `NRPreset`) | C |
| `NRResolutionScale` | float | 1.0, 0.33..1 | 0.33..1 | | D/C |
| `NRFollowInputRes` | int | 0 | dev | | D/C |
| `NRVramBuffering` | int | — | 0 | 0 Full, 1 Reduced, 2 Minimal | C |
| `NRMaxWorksets` | int | 4, 1..8 | retired | | D |
| `NRFeedMode` | int | 0, 0..2 | beta.6: retired; beta.18+: read through the API (settings layer still logs it retired) | Auto, v1 frame meter, v2 game exposure. beta.18+: 1 keeps NR's meter, 2 forces the game's exposure (D3D12 at After Upscaling; Vulkan keeps the meter) | C |
| `NRCodecMode` | int | 2, 0..5 | retired | 2 Auto, 0 Classic, 1 Anchored, 3 Display, 4 PsychoVisual, 5 Source preserving | C |
| `NRPresentGuides` | int | 0 | 0, 0..2 (3 and text read as 0) | v10: 0 Optional, 1 Required, 2 Never. beta.6: 0 DLSS frames (1 read as 0), 2 Always on. beta.18+: 0 Always on (stock), 1 DLSS frames, 2 Always on. Only 1 and 2 mean the same in every v11 build | C |
| `NRMaskBlendMode` | int | — | beta.22+, absent = not sent | Passed to NGX `DLSSNR.MaskBlendMode`; meanings undocumented | C/D |
| `NRPresentFgPath` | int | — | beta.48 to v11.9, absent = 0; v12.0.0: retired (read, logged `retired=1`) | 0 the usual Present path with frame generation (`classic`), 1 NR once per real frame at frame generation's call (`contract`, test option). Other values read as 0 | C |
| `NROpticalFlow` | int | — | v12.0.0+, on by default (schema 12) | Optical flow (`NR-OFA`); not probed, not offered by the app | C |
| `NRGpuCrashWitness` | int | — | beta.22+ | beta.22: absent/0 off, non-zero DRED breadcrumbs. beta.29: absent = crash watcher on (DRED after a crash), 0 all off, non-zero watcher + DRED from launch | C |
| `NRToggleKey` / `NRScreenshotKey` / `NRShotKey` | VK | 117 F6 / 116 F5 / 118 F7 | unbound when absent | | D/C |

v10.7 also has Present/HDR keys (`NRPresentEncoding` Auto/SDR/HDR10/scRGB, `NRPresentWhiteSource` Auto/Windows/Game,
`NRSourceEncoding` Auto/SDR-relative/Linear/PQ, `NRDiffuseWhiteNits` 203 (80..1000), `NRPaperWhiteScale` 2.5375, ...),
normalisation (`NRNormGovernor` 0 Off/1 Slew/2 Stable, ...), Look (`NRLook*`), model-lab (`NRModel*`, -1024..1024),
screenshot and diagnostic keys. v11 retires almost all Present/HDR/normalisation keys (logged once, ignored, never
written back) and adds layers (`NRLayer*`, Exposure -5..5 stops, Temperature 2000..12000 default 6504, others -1..1,
`NRLayer*Stage` 0 After NR / 1 Before NR), `NeuralUplift`, `NRModelLabEnabled`, `NRUiRenderer`, `UiPanelSize`, shot
options and `NRRuntimePath`. Full tables were dumped during analysis; re-run the probe for exact lists.

### Migrations that change values (C)

- v10.7: `NRNormGovernor=1` → 2; `NRHookPoint=0` → 2 (Present); `NRPQCalibration` pinned 2.5375; `NRDisableRelease=1`.
- v11: older files adopt `NRHookPoint=3` once; `NRPreUpscale=1` → Render if `NRHookPoint` absent; `NRPresentGuides`
  1 → 0, 2 → 2; negative `NRSkinStructure` → 0; `NRLocalStructure` > 1 → 1; v10 model axes reset once
  (`NRModelCarryoverReset`).
- Probed on beta.6, beta.18 and beta.22 (C): a file with `ConfigVersion` ≤ 10 gets `NRHookPoint` overwritten once with the
  build's default (beta.6 3, beta.18/22 0), **also an explicit value** (1, 2 or 3 seeded with `ConfigVersion=10`), plus
  `NRModelCarryoverReset=1` and `ConfigVersion=11`, and all 31 `NRModel*` axes go back to their defaults (`*Offset` and
  `*Direction` 0, the rest 1; log `event=migrate model_reset=1 axes=31`); other values (`NRStyle`, `NRIntensity`, `NRPass*`,
  ...) are kept. A file without `ConfigVersion` counts as v10 when it has a `NeuralUplift` key (v10.7 writes one into
  every ini) and is migrated the same way; without `NeuralUplift` it keeps an explicit `NRHookPoint` (only
  `ConfigVersion=11` is added). A `ConfigVersion=11` file keeps its value ("Patch 3 setups keep theirs"); an absent key there
  means 0 from beta.18 on (3 in beta.6). `NRPreUpscale=1` selects Render only in a `ConfigVersion=11` file without
  `NRHookPoint`; in a v10 file the hook-point reset wins. beta.18+ keep a v10 file's `NRPresentGuides` value as written.

App behaviour: when installing a Lecram v11 build with an `NRHookPoint` override into a game whose `ReShade.ini` has a v10 section (`ConfigVersion` ≤ 10, or absent with `NeuralUplift`), the app applies v11's migration itself (`ConfigVersion=11`, `NRModelCarryoverReset=1`, and the 31 model axes reset to defaults) before merging our profile. v11 then sees an already-migrated section on first load and keeps our chosen `NRHookPoint`. When no hook point is overridden in the app, the file is left alone for v11's own migration to set its default. Fresh config clears the old section before merging the profile, so it needs no pre-migration. If that profile sets both `NRHookPoint` and `NeuralUplift`, the app also writes `ConfigVersion=11` to keep v11 from treating the new section as v10 and resetting the chosen hook point. Without a `NeuralUplift` override, no version marker is needed.

## Other mods it reads

- `[ADDON] LoadFromDllMain`/`AddonPath` (diagnostic only); RenoDX mods' `renodx-preset1` tone-map keys (read-only).
- v11 reads the game's `OptiScaler.ini` (upscaler, FG, `PluginsLoadReshade`, `EnableDlssInputs`, ...) to route hooks.
- v10 reads DFC feeder NGX parameters (`DFC.Feeder.*`); v11 has no DFC strings.
- No check for ShortFuse's add-on by name, and no SF version check (still none in beta.18 and beta.22, D). beta.22 reads
  `ForeignNr=observe` and lists it as a recognised non-default value (`NR-SETTINGS v1 event=nondefault ... ForeignNr=observe`,
  C); its catalog text for the key is unchanged, so the SF + Lecram route through `observe` still applies. The behaviour next
  to a real foreign NR producer can't be probed without a game.

## Update source

Since the builds went behind the author's Buy Me a Coffee, the public DLSS5 Updater no longer contacts this channel: Lecram's add-on is imported by the user. Only the owner's private build
(`-p:LecramChannel=true`, `tools/build-private.ps1`, define `LECRAM_CHANNEL`, `BuildFlags.LecramChannel`) still checks the same public MediaFire folder as the author's installer (DLSS5-NR-Setup). Method: static analysis of
`DLSS5-NR-Setup-v11.0.0-beta.22.exe` (never run, no network used for the analysis); the live listing was then checked once.

- **Listing** (public, no login): `GET https://www.mediafire.com/api/1.5/folder/get_content.php?folder_key=<key>&content_type=files&chunk=<n>&response_format=json`.
  Keys (`MediaFireClient.Folders`, newest generation first): `r1xp6x0i043wx` (folder `v12`) and `wrwi1qkpgy0qu` (folder `v11`). `response.folder_content.files[]` carries `filename`, `hash` (SHA-256), `size`, `created_utc`,
  `links.normal_download`, `quickkey`; `more_chunks` is `yes`/`no` (the installer stops after 10 chunks, so does the app).
- **One folder per major.** From v12 the author gives each major its own folder, and no older folder points to it: v12's
  `sources.json` (inside `DLSS5-NR-Setup-v12.0.0.exe`, static read) has `updates.mediafire_folder` `r1xp6x0i043wx` with the note
  "v12's own folder, so v11 setups (wrwi1qkpgy0qu) never see v12", and its release notes say v11.9's Check for updates does not see
  v12 (C). The app checks the folders newest generation first and takes the first one with a build, so a late v11 fix never wins
  over v12. A new major's folder has to be added to `Folders` with a release; the folder of the installer each new major ships
  (`updates.mediafire_folder` in its `sources.json`) is the key.
- **Channel move.** A `channel.json` in the root (fields `notice`, `moved_to`) moves the channel to a new folder key. The app
  follows it once per check, remembers the new key in its settings (checked before the built-in folders) and refuses a loop or a second move.
- **Candidates.** `DLSS5-NR-Setup-v*.exe`, `DLSS5-NR-*.zip`, `dlss5-nr.addon64`, `dlss5-nr-v*.addon64`. The version comes from
  the file name (`vN.N.N` with an optional `-alpha.N`/`-beta.N`/`-rc.N`); a major below 11, or a missing hash or `created_utc`, skips
  the file. The newest upload wins, not the highest version. The installer prefers EXE over ZIP over raw add-on on a tie; the app only
  needs the add-on, so it prefers raw over ZIP over EXE.
- **Download.** `links.normal_download` is a landing page; the first `href="https://download...` in it (HTML entities decoded) is the
  file. The downloaded file's SHA-256 must equal the listing's, else it is refused. Nothing is ever executed. MediaFire can be slow
  (v12.0.0's 10 MB setup took 137 s at ~74 KB/s on 2026-10-26), so the check runs as its own run ("Checking Lecram") after the startup
  or update check, with download progress and Cancel; a download that gets no data for 30 s is given up and the next check starts over.
- **Setup exe.** A Rust PE with a normal DEFLATE ZIP appended after the PE sections (central directory offsets count from the ZIP's
  start; the app also accepts offsets counted from the file start). Entries include `dlss5-nr.addon64` and `package.json`
  (schema 1, `version` `v11.0.0-beta.22`, `kind` `v11`). The app finds the end-of-central-directory record, reads the ZIP in
  memory and extracts only the root `dlss5-nr.addon64`; the exe is deleted afterwards.
- **Live state (2026-10-18):** the folder lists `DLSS5-NR-Setup-v11.0.0-beta.24.exe` (8,981,402 bytes) and `licenses.txt`.
- **Live state (2026-10-20):** the root lists `DLSS5-NR-Setup-v11.0.0-beta.48.exe` (9,116,821 bytes), `changelog-v11.0.0-beta.48.txt` and
  `licenses.txt`. Two subfolders appeared: `addon` (the raw beta.48 `dlss5-nr.addon64`) and `archive` (older setups beta.18..beta.45,
  their changelogs and raw add-ons as `dlss5-nr(N).addon64`). The app reads only the root (as the installer does), so it takes the
  beta.48 setup exe and extracts the add-on; its hash equals the raw file's (C). Patch 9 (beta.40) is in neither folder.
- **Live state (2026-10-26):** the `v11` root ends at `DLSS5-NR-Setup-v11.9.0.exe` (no `channel.json`). The `v12` folder
  (`r1xp6x0i043wx`, created 2026-10-24) lists `DLSS5-NR-Setup-v12.0.0.exe` (10,107,204 bytes, sha256 `b63590ec239c…`),
  `changelog-v12.0.0.txt`, `licenses.txt` and an `addon` subfolder. The setup holds one root `dlss5-nr.addon64` (sha256 `124cd5a76b71…`,
  the checked v12.0.0 build) with the three strings the app recognises v11+ by (C). Release 3.1.0 only read the `v11` folder, so it
  stayed at v11.9.0.
- **In the app (private build only).** The listing is cached under `%LocalAppData%\DLSS5Updater\cache` (ETag/Last-Modified sent when present). One
  listing per folder until one has a build, requests in sequence. The add-on goes through the normal import (labels, generation, dedupe by SHA-256) and
  never writes the default (see "Default build"). Games are never switched. Offline, a broken listing or an
  exe without ZIP is logged once and the library stays as it is. In the private build Settings → Local components has "Check for new builds
  automatically" (default on) and "Check now"; the public build hides both and makes no request, even when forced. The stored setting is never reset. A new build may have options beyond the last checked one; fetching it does not
  mark its configuration as checked.

### Default build

The default Lecram build (`AppSettings.LecramId`) is unset by default, shown as "Newest (<label>)" in Settings: it follows the newest build in
the library, so an import or download of a newer build needs no settings write and an older import changes nothing. Picking a concrete build
pins it, and it stays pinned when newer builds arrive (unlike ShortFuse's add-on, which has no "Newest"). A pinned build that is removed goes back to Newest. Settings from
before this reset the pin once (`LecramNewestMigrated`), since pins written by the old automatic logic can't be told from the user's picks.
Per-game build picks are untouched.

## App support

DLSS5 Updater downloads and installs the add-on automatically from the catalog mirror (`neural.lecram`; the file itself decides v10 or v11) or imports it (the private build can also fetch v11 builds from the author's channel, see "Update source") and installs it per game in ReShade-only mode, with the same
prerequisites as ShortFuse's add-on: ReShade 6+, a game with native DLSS and the NR runtime `nvngx_dlssnr.dll`. It excludes
OptiScaler-NR and Deep Fried Chicken. It is not added to `[ADDON] LoadFromDllMain`, since nothing here shows it needs early load.

- **Library.** Every imported build is kept: `components\lecram-dlss5\<sha256>\<file name>` plus a `version.json` (id = SHA-256,
  file name, file version, generation V10/V11, detected build label, the user's own label, import date). Importing the same file
  again changes nothing, an import never deletes another build, and Remove deletes the selected one. The first run after the
  library arrived moves the old single files (`components\renodx-dlss5.addon64`, `components\dlss5-nr.addon64`) into it and
  makes them the default. Settings picks the default build; a game can pick its own ("Default (...)" follows Settings).
  The install records the build's id next to its tag, and automatic reinstalls keep the build that is installed.
- **Generations.** v10 is identified by the version resource's OriginalFilename. v11 has no version resource,
  so it is identified as a PE file containing the strings `RenoDX.DLSS5`, `NR-SETTINGS` and `NR-COMPAT` (checked: the v11 binary
  has no literal `[RenoDX.DLSS5]` and v10.7 has neither log string). Each build knows its generation, which decides the file name
  it is installed under. See "Checked builds" for how a build label is detected (any generation); the install tag is
  `{label}-{sha256[..12]}`.
- **Placement.** The chosen build's file is placed under its generation's name. The other generation's file is removed if the app
  placed it, or moved to the backup folder if it was already in the game (v11 goes inert next to `renodx-dlss5.addon64`). Other
  NR injectors (`renodx-dlssfix`, Deep Fried Chicken) are moved aside too. Uninstall restores everything. ShortFuse's add-on is left alone.
- **Keys written.** Only the options set in Settings, only to `[RenoDX.DLSS5]`, only while the add-on is installed. Options that
  exist in one generation only are hidden for the other (v10 only: `NRAutoMask`; v11 only: `NeuralUplift`, `NRPreset`,
  `NRPass2Preset`..`NRPass4Preset`, `NRVramBuffering`, `NRGpuCrashWitness`, `NRPresentFgPath`); `NRHookPoint`, `NRStyle`, `NRLocalStructure`, `NRSkinStructure` and `NRPresentGuides` offer
  per-generation values. Within v11 the options only offer values that mean the same in beta.6 through beta.48
  (`NRPresentGuides` 1/2, not 0), so there is no per-build choice variant; `NRFeedMode` (1/2) is offered for both generations and
  beta.6 ignores it. Keys only later v11 builds read are tied to the first checked build that has them (`LecramIni`): GPU crash
  reports (Off 0, On 1) from beta.22, the pass presets (offered 0..2, Old and New of 310.9.16) from beta.29, Present with frame
  generation (0, or 1 as a test) from beta.48. Keys a later build retired are tied to that build the same way (Present with frame
  generation is retired from v12.0.0). For an older build (by its detected label), or one that retired the key, the option is hidden
  and the key is not written; a key the app wrote is removed when the game moves to a build that doesn't read it, unless you changed
  it since. A build
  without a readable label gets every v11 key. Because v11 resets `NRHookPoint` once in a v10-era ini, a hook point set here has to be applied again
  after the game's first start with v11 if that game ran v10 before. The
  Settings page follows the default build's generation; a game's options follow the generation of the build that game uses.
  Keys the app wrote are removed again when the add-on is dropped, unless you changed them since.
- **ShortFuse next to Lecram.** With both installed and a v10 or v11 build, the app writes `ForeignNr=observe` (otherwise Lecram
  yields), also over the `ForeignNr=yield` the add-on writes itself on first run, unless the profile sets `ForeignNr` explicitly.
  v12.0.0 and newer default to `auto`, which already runs beside it, so they get no key. Lecram alone never gets it, and an
  `observe` the app wrote is removed when ShortFuse's add-on goes or the game moves to v12. The user's own value always wins.
- **Pair status.** ShortFuse's `renodx-dlss` 0.2026.0928.x with Lecram 0928 (v8.5.0-rc10) was tested working. A crash in
  Werewulf with a newer SF build was not attributed to the add-ons. Results are recorded by both build ids in settings.json,
  by build ids: Works shows no message, Untested shows neutral information with known working pairs, and
  Crashed shows a dated warning. The installer logs Untested as information and Crashed as a warning; neither blocks installs.
  Use "Mark as working" / "Mark as crashed" on the Install tab to persist a result for the exact selected
  builds (including resolved defaults). Old saved data defaults to Untested; the seeded 0928 pair is Works. New SF versions
  do not imply a crash. Changing the selected builds does not affect results recorded for other build pairs.