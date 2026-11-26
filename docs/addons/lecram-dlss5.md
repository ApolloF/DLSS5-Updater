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
| v12.9.9 (regular channel) | `dlss5-nr.addon64` (sha256 `d8561030015e…`) | **12.9.9.3000** (product `v12.9.9 (12.9.9)`, OriginalFilename `dlss5-nr.addon64`, internal name `nr-addon`) | `v12.9.9` | 2026-11-17 (static only, probe not run) | Extracted from `DLSS5-NR-Setup-v12.9.9.exe` (`package.json` `version` `v12.9.9`, `kind` `v11`). Settings schema still 12. Adds the per-pass hook point and resolution, frame buffering, NR's own DLSS upscaler and preset, halo removal, and Input-lab keys; retires `NRLookDetail`, `NRLookDetailRadius` and `NRLookHaloBrightSide`; the `NRSkinStructure` default moves from 0 to 1. First build that also ships a 32-bit add-on and NR's host (see "Host and 32-bit"). |
| v13.4.0 (Early Access, dev channel) | `dlss5-nr.addon64` (sha256 `f3ecd69e137c…`) | **13.4.0.3000** (product `v13.4 (13.4.0)`, OriginalFilename `dlss5-nr.addon64`, internal name `nr-addon`) | `v13.4.0` | 2026-11-17 (static only, probe not run) | The same file in `DLSS5-NR-Dev-Setup-v13.4.0.exe` (`package.json` adds `"channel": "dev"`), in the hand-install zip `DLSS5 NR Add-on v13.4.0.zip` and in that zip's `dlss5-nr-host` folder. Everything of v12.9.9 plus NR's own frame generation (`NRFrameGen*`), HDR and experimental keys. Settings schema still 12. |
| v14.0.0 Dev3 (dev channel) | `dlss5-nr.addon64` (sha256 `56c5303448fb…`) | **14.0.0** (FileVersion `14.0.0`, product `v14.0.0 Dev3 (14.0.0)`, OriginalFilename `dlss5-nr.addon64`, internal name `nr-addon`) | `v14.0.0` | 2026-11-18 (static only, probe not run) | Extracted from `DLSS5-NR-Dev-Setup-v14.0.0.exe` (`package.json` `version` `v14.0.0`, `kind` `v11`, `channel` `dev`). Retires `NRHdrScreen`; adds VSync for frame generation, DLSS mode, SDR to HDR, Safe mode and more (see "v14.0.0"). The setup also ships NR's own Vulkan layer. The dev number (Dev3) is no part of the label, so a later v14.0.0 dev build gets the same label. |
| v15.2.0 Dev11 (dev channel) | `dlss5-nr.addon64` (sha256 `d42d6c25855e…`) | **15.2.0.3000** as the app reads it (FileVersion string `15.2.0`, product `v15.2.0 Dev (15.2.0)`, OriginalFilename `dlss5-nr.addon64`, internal name `nr-addon`) | `v15.2.0` | 2026-11-26 (static + probe) | The same file in the hand-install zip `DLSS5 NR Add-on v15.2.0.zip` (root and `dlss5-nr-host`) the dev folder lists; build line `NR-HOST v1 event=build version=v15.2.0 patch="v15.2.0 Dev" commit=8ada33738d built=2026-11-25T19:45Z`, the dev changelog names it "v15.2.0 Dev11". 19.8 MB. Adds the frame generator (NVIDIA DLSS / Intel XeSS gate / NVIDIA VFG), "In game" frame generation, optical flow input size and a new-model lab; `NRInputCurve` and `NRPresentShoulder` change default (see "v15.2.0"). Layout, ReShade requirement and file names unchanged (see "Host and 32-bit"). |
| v15.2.0 (an earlier dev build) | `dlss5-nr.addon64` (sha256 `72d85a89fe21…`) | same resource strings | `v15.2.0` | 2026-11-26 (probe, bare run only) | `commit=5e83712d05 built=2026-11-25T14:03Z`, five and a half hours before Dev11; a bare run writes the same keys. Same label as Dev11: the app can't tell them apart except by hash and date. Not read beyond that. |

The author's own names (from the beta.18 installer's `sources.json`): `v11.0.0-beta.5`..`beta.11` = "v11 Patch 3",
`beta.12`..`beta.18` = "v11 Patch 4" (C). The beta.29 installer's `sources.json` adds beta.22 = "v11 Patch 5", beta.24 = "Patch 6",
beta.26 = "Patch 7", beta.29 = "Patch 8" (C). The beta.48 installer's `sources.json` adds beta.40 = "v11 Patch 9", beta.45 = "v11 Patch 10" and
beta.48 = "v11 Patch 11" (C). The v12.9.9 installer's `sources.json` names `v12.5.0`..`v12.6.1`, `v12.9.0`..`v12.9.9` by their own number and
`v13.0.0-alpha.1`..`alpha.15` "v13 Alpha N"; the v13.4.0 Early Access installer's adds `v13.0.0-alpha.60`..`alpha.78` and `v13.0.0-rc.1` (all
"v13 Alpha") and `v13.1.0`..`v13.4.0` = "v13.1".."v13.4" (C); the v14.0.0 dev installer's adds `v14.0.0` = "v14.0.0 Dev3" (C). v15.2.0 is on both channels (see "v15.2.0 packages"): the regular folder lists `DLSS5-NR-Setup-v15.2.0.exe` (its changelog is headed "v15.2.0 Dev2"), the dev folder
the zip and `DLSS5-NR-Dev-Setup-v15.2.0.exe` (changelog "v15.2.0 Dev11"), so the label can't tell a regular from an Early Access build; only the product
string (`v15.2.0 Dev (15.2.0)`) and the changelog heading say which Dev it is. None of the builds between v12.0.0 and v12.9.9, between v12.9.9 and v13.4.0,
between v13.4.0 and v14.0.0 Dev3 (v13.5, v14.0.0 Dev1 and Dev2) or between v14.0.0 Dev3 and v15.2.0 (v15.0, v15.1, v15.2.0 Dev1..Dev10 but the one hash above) was checked.

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
`v11.9.0` / `v12.0.0` (C, test `Label_ReadsTheV11_9AndV12Layouts`); their version resource agrees. v12.9.9 carries `v12.9.9` seven times
(glued to "NR on", in its `build=` and `patch=` log fields, in the panel and menu-key texts, bare) and v13.4.0 carries `v13.4.0` twice (glued to
"NR on", bare); neither has a higher `vX.Y.Z`, and v13's `v13.4 (13.4.0)` description has two parts and is no label, so the labels are `v12.9.9`
and `v13.4.0` (C on the real files, test `Label_ReadsTheV12_9AndV13_4Layouts`). Both have a version resource with OriginalFilename
`dlss5-nr.addon64`, so they are the v11 generation.

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

### v12.9.9 and v13.4.0 packages (static only, never run)

- **`DLSS5-NR-Setup-v12.9.9.exe`** (regular channel): the same appended ZIP. Root entries `dlss5-nr.addon64`, `dlss5-nr.addon32`,
  `nr-host64.exe`, `package.json` (`version` `v12.9.9`, `kind` `v11`), `sources.json`, `README.txt`, `RELEASE-NOTES.md`, `setup-gui.ps1`,
  `THIRD-PARTY-NOTICES.txt`, `reshade\ReShade64.dll` + `reshade\ReShade32.dll` (the author's fixed ReShade 6.8.0 build, file version 6.8.0.1,
  "6.8.0 UNOFFICIAL"; its hashes are `fixed_sha256` / `fixed32_sha256` in `sources.json`) and `studio\` (Offline Studio with a v10 add-on). One
  root `dlss5-nr.addon64` (C).
- **`DLSS5-NR-Dev-Setup-v13.4.0.exe`** (Early Access): the same layout; `package.json` adds `"channel": "dev"`. Per its release notes it "updates
  itself to each new dev build, with its own settings" (`%LOCALAPPDATA%\DLSS5-NR\dev\updates.json`); to go back, the regular setup installs a
  regular release in that game (C). Its `sources.json` also pins NVIDIA files for NR's frame generation and DLSS (`dlss_store` 310.9.16,
  `streamline` 2.12.0 with five `bin/x64` plugins, `dlssg` 310.7.0) and MFG Unlock 1.9.0 (C).
- **`DLSS5 NR Add-on v13.4.0.zip`** (hand install, from the author's page): `dlss5-nr.addon64`, `dlss5-nr.addon32`, `INSTALL.txt`,
  `RELEASE-NOTES.md`, `THIRD-PARTY-NOTICES.txt` at the root and `dlss5-nr-host\` with `dlss5-nr.addon64` (the same file as the root one,
  sha256 `f3ecd69e137c…` both), `nr-host64.exe` and `ReShade64.dll` (the fixed 6.8.0.1 build). `INSTALL.txt`: copy everything into the game's ReShade folder, the host folder
  as it is; ReShade loads the add-on that matches the game's bitness and leaves the other (C). v12.9.9's release notes name the same layout
  for `DLSS5 NR Add-on v12.9.9.zip` (D, not opened).
- The release page (pasted by the owner, 2026-11-17) offers v12.9.9 as the regular release, v13.4.0 as Early Access through the dev setup,
  and v10.8 as the stable legacy build (D).
- **`DLSS5-NR-Dev-Setup-v14.0.0.exe`** (v14.0.0 Dev3, the owner's copy of 2026-11-18, sha256 `44c3234d4491…`): the same appended ZIP as v13.4.0
  plus one root entry, `dlss5-nr-vklayer64.dll` (10,752 bytes, FileVersion 14.0.0, "DLSS5 NR Vulkan layer: gives a game's Vulkan instance to
  the ReShade it loaded as a proxy", exports `vkNegotiateLoaderLayerInterfaceVersion`). The setup registers it per Windows user under
  `HKCU\SOFTWARE\Khronos\Vulkan\ImplicitLayers` with a `dlss5-nr-vklayer64.json` manifest (setup strings, C): the release notes' "Setup puts NR's
  build of ReShade with add-ons over" ReShade's standard build in its Vulkan layer, which loads no add-on. The app doesn't take or install it:
  the app registers its own ReShade build as the Vulkan layer (`docs/addons/reshade.md`). `dlss5-nr.addon32` `c12c7015a5e0…`, `nr-host64.exe`
  `aafad48439c6…` (both 14.0.0), `reshade\ReShade64.dll` unchanged (`af3c41736f8d…`); `sources.json` adds older fixed-ReShade hashes, the name
  `v14.0.0` = "v14.0.0 Dev3" and a `logs` url; `updates_dev` is unchanged (`uh4x7j5f7fj2y`). README.txt and `setup-gui.ps1` are as in v13.4.0 (C).
- **Session logs.** From v14.0 Dev2 the dev setup sends each game session's log, anonymized, to the author's Cloudflare Worker after the game
  closes ("Sent after each game session, anonymized"; a switch on the setup's Keys page and Storage tab turns it off). The upload URL is only in
  the setup exe and `sources.json` (`logs.url`), not in `dlss5-nr.addon64`, `dlss5-nr.addon32` or `nr-host64.exe` (C, string search). A game
  the app installs Lecram into, without the author's setup, sends nothing.

### v15.2.0 packages (2026-11-26)

- **`DLSS5 NR Add-on v15.2.0.zip`** (dev folder, 17,996,571 bytes, sha256 `665ecafc7517…`, downloaded from the author's public MediaFire link, nothing run): `INSTALL.txt`,
  `RELEASE-NOTES.md` (one line: "DLSS5 NR v15.2.0 Dev."), `THIRD-PARTY-NOTICES.txt`, `dlss5-nr.addon64` (`d42d6c25855e…`), `dlss5-nr.addon32`
  (`1455bddf7fba…`) and `dlss5-nr-host\` with `dlss5-nr.addon64` (the root file again), `nr-host64.exe` (`8881be26cf4c…`) and `ReShade64.dll`
  (`bb6c6757e9be…`, FileVersion 6.8.0.19, product "6.8.0 UNOFFICIAL": the author's fixed build again, a newer one than v13.4/v14's 6.8.0.1). Layout identical to v13.4.0's zip.
  What the app keeps with the build (`dlss5-nr.addon32` and the host folder) is exactly these files.
- **`INSTALL.txt`** is rewritten but asks for the same things: "Needs ReShade 6.8 or newer with add-on support in the game"; copy everything beside the game's
  ReShade, the host folder as it is; host for Direct3D 9, Direct3D 10, OpenGL, every 32-bit game and 64-bit Direct3D 11 games with in-game MSAA. New in it: the
  runtime search (`nvngx_dlssnr.dll` beside the exe or in `%LOCALAPPDATA%\DLSS5-NR\runtime`, and "NR loads the one whose kernels fit your card before one that does
  not"), NVIDIA's files for the features used (`nvngx_dlss.dll` 310.9.1 or newer, from the driver's store or `runtime\dlss\310.9.16\`; for frame generation the five
  Streamline **2.14.16** plugins `sl.interposer.dll`, `sl.common.dll`, `sl.dlss_g.dll`, `sl.reflex.dll`, `sl.pcl.dll` and `nvngx_dlssg.dll` 310.7.0 or newer in
  `runtime\streamline\2.14.16\`; v13.4 and v14 named 2.12.0), **`NRNvidiaPath=<folder>`** in a game's ReShade.ini for another folder with that layout, an anti-cheat
  warning, and "No Feeder, Lumenite or depth add-on is needed". The app brings none of NVIDIA's files (help text says so).
- **Both channels carry v15.2.0.** Listings of 2026-11-25 (cached by the app): the regular folder `r1xp6x0i043wx` lists `DLSS5-NR-Setup-v15.2.0.exe`
  (20,273,892 bytes, 2026-11-22), `changelog-v15.2.0.txt` (13,600 bytes, headed "v15.2.0 Dev2 (15.2.0)", "New since v12.9"), `licenses.txt` and `mods.json`; the dev
  folder `uh4x7j5f7fj2y` lists `DLSS5-NR-Dev-Setup-v15.2.0.exe` (20,844,224 bytes), the zip above, `changelog-v15.2.0.txt` (3,181 bytes, "v15.2.0 Dev11 (15.2.0)",
  published 2026-11-25), `features.json` (65 bytes) and `mods.json`. The setups were not opened. The v12 folder's changelog (C): "New since v12.9" is NR's own DLSS and
  DLAA in games without DLSS, frame generation in more games with VSync and NVIDIA's Dynamic Multi Frame Generation, motion and depth read at the game's TAA pass and
  from the engine in Unity games, SDR to HDR, and setup keeping NR up to date; also Desktop mode and Console mode (experimental, started from setup: the host's description now
  says "relayed games and desktop mode"), "Present runs on DLSS frames or Always on, chosen in the Hook point menu" (`NRPresentGuides`), "NR and frame generation each run
  Always on or In game" (`NRFrameGenRealInputs`), "HDR highlights default to Extended", setup installs OptiScaler 0.9.5, and setup names ReShade's official 6.8.0.2155 "where you or
  another mod put it, since it can hang games with frame generation" and replaces it with NR's fixed build on a Yes; a ReShade older than 6.7 beside NR "cannot load NR".
  Setup no longer replaces ReShade's standard Vulkan layer. The app installs neither; its own ReShade is the one in the game (`docs/addons/reshade.md`), so an official 6.8.0.2155 in a
  game the app set up for Lecram with frame generation on is the case the author warns about (open: whether the app should say so).
- **Early Access or released?** Neither the label nor the file says. Both folders carry v15.2.0 and the product string says "Dev" in every build; the app has no
  Early Access flag, so v15.2.0 is handled like every build: by its label, newest date first among equal labels (three Dev builds of 2026-11-24, 11-25 and 11-26 went through the library).

### Host and 32-bit

From v12.9 the add-on runs NR in a 64-bit host process for the games it can't serve in-process. What the app does with it is under "App
support"; the facts (C unless marked):

- **Which games.** Direct3D 9, Direct3D 10, OpenGL and every 32-bit game (Direct3D 9, 10, 11, OpenGL), and 64-bit Direct3D 11 games with
  in-game MSAA, run NR through the host; D3D12, Vulkan and other D3D11 games run it in the game. Not supported by the author yet: 32-bit
  Vulkan and 32-bit Direct3D 12 (README). The setup adds Direct3D 9/10, OpenGL and 32-bit games only through Browse (release page, D).
- **Files.** In the game's ReShade folder: `dlss5-nr.addon32` for a 32-bit game (version resource 12.9.9.3000 / 13.4.0.3000,
  OriginalFilename `dlss5-nr.addon32`, internal name `relay32`, "each frame goes to NR in the 64-bit host beside it") or `dlss5-nr.addon64`
  for a 64-bit one, plus the folder `dlss5-nr-host` beside them with `nr-host64.exe` (internal name `nr-host`, "DLSS5 NR host for relayed
  games"), its own `dlss5-nr.addon64` and `ReShade64.dll`. The setup exe holds `nr-host64.exe` and `reshade\ReShade64.dll` at its own
  paths and no `dlss5-nr-host` folder; how it assembles the folder in a game was not checked. Only the hand-install zip has the folder ready.
- **The same files in every package (2026-11-17, sha256).** v13.4.0: the zip's `dlss5-nr-host\dlss5-nr.addon64` is its root add-on
  (`f3ecd69e137c…`), its `dlss5-nr-host\nr-host64.exe` is the dev setup's root `nr-host64.exe` (`3d4ef7c1b8b0…`) and its
  `dlss5-nr-host\ReShade64.dll` is the setup's `reshade\ReShade64.dll` (`af3c41736f8d…`, the same in the v12.9.9 setup). v12.9.9's setup:
  `nr-host64.exe` `e41a2205091b…`, `dlss5-nr.addon32` `f8e01ad43d5c…`; v13.4.0: `dlss5-nr.addon32` `acb8fc8ccc80…` (zip and setup alike).
  A host folder assembled from a setup (its exe, `reshade\ReShade64.dll`, the root add-on) is therefore byte for byte the zip's. Every
  file of the set has a version resource naming it (OriginalFilename `dlss5-nr.addon32`, `nr-host64.exe`, `ReShade64.dll`,
  `dlss5-nr.addon64`; read with FileVersionInfo, nothing run).
- **ReShade.** INSTALL.txt: "Needs ReShade 6.8 or newer with add-on support in the game". In a 32-bit game that is the 32-bit ReShade;
  the host brings its own 64-bit one.
- **Settings.** A game NR reaches through its host has a second ReShade.ini in `dlss5-nr-host`: NR's shortcuts go to the host's ini, the
  panel key to both (README). Which other `[RenoDX.DLSS5]` keys the host reads from its own ini has not been checked; the later PR has to
  find out before writing options there. Host keys seen in v12.9.9: `NRRelay`, `NRRelayFlipEx`, `NRRelayFullscreenMode`,
  `NRResumeTakeover`, and in v13.4.0 also `NRRelayTwins`, `NRRelayVelocity`; none has a catalog entry (undocumented). `NRFrameBuffering`
  acts on relayed games too.
- **Logs.** The host writes its own `dlss5-nr-host\ReShade.log` (release page).
- **v15.2.0 Dev11 (2026-11-26).** Same layout and names: `dlss5-nr.addon32` (version resource OriginalFilename `dlss5-nr.addon32`, internal name `relay32`, "DLSS5 NR for 32-bit
  games (v15.2.0 Dev (15.2.0)): each frame goes to NR in the 64-bit host beside it"), `dlss5-nr-host\nr-host64.exe` (internal name `nr-host`, now "DLSS5 NR host for relayed games and
  desktop mode") with its own `dlss5-nr.addon64` (byte for byte the root add-on) and `ReShade64.dll` (6.8.0.19). The files the app keeps with the build are the ones it kept for v13.4.0
  (`ComponentStore.Lecram32File`, `LecramHostDir`). INSTALL.txt's ReShade requirement is unchanged (6.8), so `LecramRoute.HostReShade` stays. The 32-bit add-on logs
  `NR-RELAY v1 event=addon32 version=v15.2.0 ... commit=8ada33738d built=2026-11-25T19:50Z`; a bare 64-bit run without the host folder logs
  `NR-RELAY v1 event=no_host relay=off why=dlss5-nr-host_missing` and reads `NRRelayVelocity` and `NRMotionSource` first (probe).
- **Not checked** (open): which `[RenoDX.DLSS5]` keys a relayed game takes from the game's ReShade.ini and which from the host's own;
  how NR tells a 64-bit Direct3D 11 game with in-game MSAA; what the host keys above do.

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
- **v12.9.9 and v13.4.0** (static only; the probe was not run, the binaries were never executed). Method: `strings` (ASCII and UTF-16) of
  each `dlss5-nr.addon64`; the `nr-settings` catalog (`crates\nr-settings\src\catalog.rs`) holds one description per key in index order and
  the key names in reverse order right before it, so new keys pair with new descriptions one to one; v12.0.0's catalog was diffed against
  v12.9.9's and v12.9.9's against v13.4.0's. Panel option lists (`As the pass before|Before Upscaling|After Upscaling|Present`,
  `2x|3x|4x|5x|6x`, ...) and the panel's one-line hover help come from the same strings; the release notes are inside each setup (C).
  - **Schema.** Both still carry the settings-schema text of v12.0.0 (schema 12: `NROpticalFlow` on by default; schema 11: `NRHookPoint`
    Auto); no newer schema (C).
  - **Keys the app writes.** Every key the app writes is still in both catalogs with an unchanged entry, or still read through the API
    (`NeuralUplift`, `EnableHooks`, `DX11Source`, `ForeignNr` with `yield`/`observe` overrides, `NRFeedMode` via `nr_core_feed_mode`,
    `NRGpuCrashWitness` with the `NR-DRED v1` lines, `NRPresentFgPath` still `retired=1`). None is newly retired, so no retire entry was
    added (C for presence and catalog text; whether each value still acts was not probed). One default moved: `NRSkinStructure` (panel
    "Skin", 0..100 = runtime 0..1) defaults to 1, NVIDIA's stock face treatment, from v12.9.9 ("owner, 2026-10-25; 0 from 2026-10-14"; v12.0.0
    still said 0); a stored negative value reads as the default (C).
  - **New in v12.9.9** (catalog, C): `NRPass2HookPoint`..`NRPass4HookPoint` (0 same as the pass before, 1 Before Upscaling, 2 After
    Upscaling, 3 Present; "a point before the previous pass's reads as the previous pass's"; D3D12, Vulkan, D3D11),
    `NRPass2ResolutionScale`..`NRPass4ResolutionScale` (0 as pass 1, else 0.33..1 of the output, under 0.33 reads as 0.33; a pass at Before
    Upscaling stays native), `NRFrameBuffering` (0 Automatic default, 1 One frame, 2 Same frame; D3D11 at Present and relayed games),
    `NRUpscaler` (0 the game's FSR 3.1 or XeSS, default; 1 NR's own DLSS SR / DLAA with the game's exact inputs), `NRUpscalerPreset` (0 NVIDIA's
    default per mode, 1 J, 2 K, 3 L, 4 M), `NRLookHaloRemoval` (0..1, default 0; "acts with Fine-tune off too"), `NRLookTexture` and
    `NRLookClarity` (0..4, panel 0..2, default 1; they replace `NRLookDetail`), `NRMotionSource`, `NRMotionConvention`, `NRMotionBottomUp`,
    `NRDepthSource`, `NRReader` (the Input lab) and `NRGridPad` (native-size padding, off by default). Retired with a migration: `NRLookDetail`
    (loads once as Texture and Clarity), `NRLookDetailRadius`, `NRLookHaloBrightSide` (replaced by Halo removal); `NRLookHalo` is now "Dark
    halos", 0..1 default 0. New API-only keys without a catalog entry: `NRD3D11PresentAfterAddons`, `NRRelay`, `NRRelayFlipEx`,
    `NRRelayFullscreenMode`, `NRResumeTakeover` (C names, meanings undocumented).
  - **New in v13.4.0** (catalog, C): `NRFrameGen` (0 Off, default "until the owner's field sitting": NR hosts nothing and loads no NVIDIA file;
    1 On: DLSS Frame Generation hosted by NR through Streamline after NR's pass, NR on every real frame and never on a generated one; 2 Dynamic:
    NVIDIA's dynamic multiplier where supported, else On; the panel help says Dynamic "is not available in this version of NR, which uses the
    multiplier below"; read at the game's start), `NRFrameGenMultiplier` (0..4 = 2x..6x, clamped to what the GPU reports, logged
    `multiplier_clamped`; not used under Dynamic; no default named), `NRFrameGenUi` (0 Steady: NR's HUD-less image and UI alpha, DLSS-G puts the
    real UI back; 1 Interpolated: the UI is generated with the picture; read at start; no default named), `NRHdrScreen` (0 HDR10, 1 scRGB, how
    FG's picture reaches a Windows HDR screen in a game with an HDR10 chain; read at start; no default named), `NRGameBrightness` (the game's own
    brightness in nits at Present on an HDR chain, 48..1000, below 48 a RenoDX mod's value or 203), `NRInputCurve` (0 Off, 1 Per channel, 2 Same
    hue; what NR reads at After Upscaling; no default named), `NRPresentShoulder` (HDR roll-off modes 0..7, default 6) with its migration marker
    `NRPresentShoulderAdopted`, and the experimental `NRExpCamera` (off), `NRExpTaaInputs` (on by default from 2026-11-09), `NRPresentCallGuides`
    (off) and `NRQueueHeal` (0 Off, 1 Auto default, 2 On); `NRExperimental=1` turns the experimental inputs on. FG needs an RTX 40 or newer, the
    Streamline 2.12.0 plugins and `nvngx_dlssg.dll` 310.7.0+ in `%LOCALAPPDATA%\DLSS5-NR\runtime\streamline\2.12.0\` (or `NRNvidiaPath=<folder>`
    with the same layout), unless the game ships its own Streamline (`INSTALL.txt`, C). The game's own frame generation, another tool's and
    NVIDIA Smooth Motion keep NR's off (C). New API-only keys without a catalog entry: `NRNvidiaPath` (documented in `INSTALL.txt`),
    `NRFrameGenPreset` (logged with `NR-HUDLESS v1 preset profile=`, so a HUD-less profile, not a DLSS-G preset), `NRFrameGenNoTear`,
    `NRFrameGenReflexAsked`, `NRFgChainBound`, `NRFgCrashGuard`, `NRQueueOrder`, `NRCameraTranslation`, `NRMotionOwn`, `NRTaaMerged`,
    `NRUiAlphaTold`, `NRRtssUnder`, `NRVkIcdSeed`, `NRVulkanLineage`, `NRPresentShoulderSkin`, `NRRelayTwins`, `NRRelayVelocity` (C names,
    meanings undocumented).
  - **v14.0.0 Dev3** (catalog of `crates\nr-settings\src\catalog.rs`, C; the same method, diffed against v13.4.0). Schema text unchanged.
    Every key the app writes is still in the catalog with its entry, except **`NRHdrScreen`, retired 2026-11-17** ("keeps its base-key slot, a
    non-default value is logged once and ignored; never stored, written or shown"; an HDR game's frames under NR's frame generation now always
    reach the display on DLSS-G's own HDR10 chain). `NRFrameGen`'s entry is unchanged in the catalog (2 Dynamic: "NVIDIA's dynamic multiplier
    where the GPU and driver support it, else On"), and v13.4's panel text "not available in this version" is gone; the release notes call it
    "NVIDIA's Dynamic Multi Frame Generation" (C). `NRInputCurve` (Match game tones) defaults to 2 Same hue since 2026-11-15, with the
    migration marker `NRInputCurveAdopted` (a file that stored v13.3/v13.4's 0 adopts 2 once). New catalog keys:
    `NRFrameGenVSync` (0 Off: tearing allowed, "as v14.0"; 1 On: SyncInterval 1 on every route NR hosts while DLSS-G generates, where DLSS-G
    reports VSync support; never with Dynamic, the panel turns the other off; a session that falls short at 1 goes back to 0; Safe mode turns it
    off; applies at the next present; no default named), `NRUpscalerMode` (DLSS mode while `NRUpscaler=1`: 0 the game's choice / 1 DLAA / 2 Ultra
    Quality / 3 Quality / 4 Balanced / 5 Performance / 6 Ultra Performance; where NR's DLSS replaces FSR 3.1 or XeSS NR answers the game's
    render-size query with DLSS's size for the mode; the panel lists only the modes NVIDIA's DLSS reports, and DLSS 310 has no Ultra Quality),
    `NRSafeMode` (off by default; 1 turns off the camera finder, the TAA pass input, extended DLSS, call-time guides, frame pairing repair, engine
    inputs, Game mode and the overlay key, each key's own value kept; read at the game's start), `NRSdrHdr` (SDR to HDR: 0 Off default, 1
    Balanced, 2 Bright; read at the game's start, `NRSdrHdrHost` keeps the presenter for the next start) with its popup's `NRSdrHdrWhite` (game
    brightness, below 80 = Windows' SDR content brightness, default 0), `NRSdrHdrPeak` (below 400 = the display's peak, default 0),
    `NRSdrHdrLight` (Highlights from: 1 Game default, 0 Picture), `NRSdrHdrColour` (0 Richer default, 1 Kept), `NRSdrHdrUi` (UI brightness,
    203 nits default), `NROpticalFlowLevel` (0 Fast, 1 Balanced default, 2 Best), `NROpticalFlowGrid` (0 Coarse, 1 Standard default, 2 Fine),
    `NRCallDepth` and `NRCallMotion` (Input lab: 0 Auto default, 1 As stated), `NRExpTaaDeferred` (on where NR's DLSS is the upscaler),
    `NREngineInputs` (1 Auto default, ini only; Unity engine inputs, never with an anti-cheat), `NRTaaGameLook` (Game mode, off),
    `NRReShadeMenuKey`, `NRFrameGenKey`, `NRAllKey` (unbound by default) and the key state `NRKeysWere` (not a setting). New API-only keys
    without a catalog entry: `NRFgStartPlain`, `NRFrameGenHost`, `NRFrameGenMotionFit`, `NRFrameGenMotionFitWait`, `NRFrameGenReflexBridge`,
    `NRJitterDrive`, `NRMipBias`, `NRMotionFill`, `NROptimizerLoad`, `NROptimizerFgTier`, `NROptimizerSrTier`, `NRSdrHdrHost`, `NRUpscalerSaved`,
    `NRRelayTwinsIdle` (C names, meanings undocumented). Gone from the binary: `NRFrameGenNoTear`, `NRFgCrashGuard`, `NRPresentShoulderSkin`
    (API-only keys of v13.4, never offered).
  - **v15.2.0 Dev11** (the same catalog method, C, diffed against what this file records for v14.0.0 Dev3; v14.0.0's own binary is not at hand, so "new" means not in the notes above;
    plus a probe: `tools/addon-probe/host` rebuilt from ReShade's current headers, the add-on loaded in the fake host in a scratch folder, no game, device or driver
    profile touched). Schema text unchanged (12). **Probe (C):** a bare run writes only `UiWelcomed=0`, `ConfigVersion=12`, `NRScreenshotKey=0`, `NRShotKey=0` and
    `[OVERLAY] TutorialProgress=4`, logs `NR-SETTINGS ... NRHookPoint=0 NRPasses=1 NRResolutionScale=1 NRStyle=0 NRIntensity=1 NRVramBuffering=0`, and reads (API) `NRRelayVelocity`,
    `NRMotionSource`, `NRSafeMode`, `NRQueueHeal`, `NRExperimental`, `NRExpCamera`, `NRExpTaaInputs`, `NRExpTaaDeferred`, `NREngineInputs`, `NRPresentCallGuides`, `NRResumeTakeover`,
    `NRPresentFgPath`, `NRQueueOrder`, `NROptimizerFgTier`/`SrTier`, `NRVramBudgetGuard`/`Margin`. A file the app writes (`ConfigVersion=11`, `NRHookPoint=2`, every v14 and v15 key)
    loads as `file_version=11 migrated=ConfigVersion=12`: `NRHookPoint=2` is kept (`NR-HOOK chosen=present`), `NRFrameGen=1`, `NRFrameGenMultiplier=2`, `NRFrameGenUi=1`,
    `NRFrameGenVSync=1`, `NRUpscaler=1`, `NRUpscalerMode=4`, `NRSdrHdr=1`, `NRSafeMode=1`, `NRFrameGenEngine=2`, `NRFrameGenRealInputs=1`, `NRPresentGuides=1`, `NRFrameBuffering=2`,
    `NRLookHaloRemoval=0.5` all show in `event=nondefault`, and `NRHdrScreen=1` is logged `retired settings ignored`. Out of range (`NRFrameGen=3`, `NRFrameGenVSync=5`, `NRUpscalerMode=9`,
    `NRSdrHdr=7`, `NRSafeMode=3`, `NRFrameGenRealInputs=2`) reads as the default, not logged as nondefault; `NRFrameGenEngine=1` is kept (the Intel gate is checked at runtime).
    The add-on writes the API key `NRFrameGenHost=1` once frame generation is on. Not probed: what any value does in a game.
    **Every key the app writes is still in the catalog with its entry, or read through the API, and none is newly retired.** `NRFrameGenVSync` and the v14 keys read as in v14.0.0.
    **Changed defaults** (none of them a key the app offers): `NRInputCurve` (Match game tones) is **0 Off** again since 2026-11-18 (v14.0.0 Dev3: 2 Same hue; values 0 Off / 1 Per channel
    / 2 Same hue; marker `NRInputCurveOffAdopted`: every file NR wrote turns it off once), `NRPresentShoulder` (HDR highlights) is **2 Extended** since 2026-11-20 (was 6 Auto from v13 to
    v15.0; marker `NRPresentShoulderExtendedAdopted`: a stored 6 adopts 2 once; values 0..7, the panel lists eight). **New keys with a catalog entry:**
    `NRFrameGenEngine` (which frame generation NR runs, read at the game's start: **0 NVIDIA DLSS Frame Generation, default**; 1 Intel XeSS frame generation, only where the ini holds
    `NRXeFg`, "the owner's installs"; **2 NVIDIA Video Frame Generation (VFG)**, on RTX 40 and 50 cards where its files are in NR's store, "setup fetches them from NVIDIA"; a choice that cannot run
    gives way to DLSS-G, else frame generation is off, log and panel say why (`generator= why=`); 0 under Safe mode; the panel lists "NVIDIA DLSS|Intel XeSS|NVIDIA VFG" and its help names only
    DLSS and VFG), `NRXeFg` (the gate: absent in a player's ini, "nothing of XeSS exists, in the panel, the log or setup"; a file of before 2026-11-22 with `NRXeFg=1` and no `NRFrameGenEngine` adopts
    engine 1 once), `NRFrameGenRealInputs` (where frame generation runs: **0 Always on, default**, DLSS-G is given every frame and a frame without the game's camera gets NR's stand-in; 1 In game,
    generated frames only from frames whose camera and motion are the game's own, the rest go with no tags so the game's own frame shows and the panel says why; Safe mode turns it to 0;
    applies at the next frame), `NROpticalFlowHalf` (optical flow input size, in the popup on the motion row: 0 Full size default, 1 Half size for a picture 2560 or more wide; "no game has
    measured it"; a change remakes the flow at the next present) and the model lab's new-model set (`NRModelLabMode` 0 Simple / 1 Advanced remembered per game, `NRModelLiveWeights` ini-only
    live weights, `NRModelPresetAdopted`, the per-block `NRModelNewBlock0..30Strength` / `PositionBias` / `Temperature`, `NRModelBlock0..70*` and about thirty `NRModelNew*` axes beside the v11
    ones; "Old, the 71-block model" and "New, the 31-block model"). Migration markers: `NRInputsAdopted` (I: by its place in the key list; the catalog's marker for the inputs NR turns on by its own evidence says a file that stored the old 0 of
    `NRExpCamera`, `NRExpTaaInputs` or `NRPresentCallGuides` adopts the new on-by-default once). Defaults the catalog now states for the inputs: `NRExpCamera`, `NRExpTaaInputs`,
    `NRExpTaaDeferred` and `NRPresentCallGuides` on, `NREngineInputs` 1 Auto (0 Off, ini only, Safe mode reads 0), `NRQueueHeal` 1 Auto (0 Off, 2 On). API-only keys without a catalog
    entry that the probe saw read (not in this file's earlier lists; C names, meanings undocumented): `NRPresentScrgbAbsolute`, `NRPresentToe`, `NRMeterAttackStops`,
    `NRMeterReleaseStops`, `NRSourceEncodingOverride`, `NRVramBudgetGuard`, `NRVramBudgetMargin` (the log line says budget guard on, margin 512 MB by default).
  - **Offered by the app** (version-gated in `LecramIni`, v12.9.9 keys from `v12.9.9`, v13 keys from `v13.4.0`, v14 keys from `v14.0.0`, v15 keys from `v15.2.0`): the pass hook points and
    resolutions, `NRFrameBuffering`, `NRUpscaler`, `NRUpscalerPreset`, `NRLookHaloRemoval`, `NRFrameGen`, `NRFrameGenMultiplier`,
    `NRFrameGenUi`, `NRHdrScreen` (v13.4 only: retired from `v14.0.0`, so the app hides it there and removes a value it wrote), and from v14
    `NRFrameGenVSync`, `NRUpscalerMode`, `NRSdrHdr` and `NRSafeMode`; from v15.2.0 `NRFrameGenEngine` (offered 0 and 2; 1 is the owner-gated Intel XeSS) and
    `NRFrameGenRealInputs` (0/1). **Not offered:** v15's `NROpticalFlowHalf`, `NRXeFg`, the new-model lab keys and the migration markers (`NRInputsAdopted`, `NRInputCurveOffAdopted`,
    `NRPresentShoulderExtendedAdopted`); v14's SDR to HDR popup values (the panel's sliders),
    `NROpticalFlowLevel` / `NROpticalFlowGrid` (they act beside `NROpticalFlow`, which isn't offered), `NRCallDepth` / `NRCallMotion` (Input lab),
    `NRExpTaaDeferred`, `NREngineInputs` and `NRTaaGameLook` (experimental, ini only or "off until the owner has tried it"), the new hotkeys
    (the app offers none of NR's keys) and the migration markers; also `NRLookTexture` / `NRLookClarity` (they act only with Fine-tune, `NRLookMode`, on, and the
    app offers none of the Fine-tune set); the Input lab (`NRMotionSource`, `NRMotionConvention`, `NRMotionBottomUp`, `NRDepthSource`,
    `NRReader`: overrides for when NR misreads a game's motion or depth, tuned per game in the panel); `NRGridPad` (a quality detail whose value
    format beyond "off by default" isn't stated); `NRGameBrightness`, `NRInputCurve` and `NRPresentShoulder` (HDR measurement and roll-off with
    many modes; `NRInputCurve` has no default named; the panel sets the shoulder from its HDR icon); the experimental keys and `NRExperimental`
    (the author ships them off or judged per session); `NRFrameGenPreset` and every other API-only key above (undocumented values); the host
    keys (host not supported yet). `NRFrameGenMultiplier`, `NRFrameGenUi`, `NRHdrScreen` and the pass hook point have no default in the
    texts, so their Add-on default choice names none.

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
| `NRSkinStructure` | float | -1, -1..1 (-1 = follow stock) | 0, 0..1 (negative reads 0); v12.9.9+: 1 (negative reads the default) | 0 protects faces, 1 NVIDIA's stock face treatment (panel 0..100) | D/C |
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
| `NRPass2HookPoint`..`NRPass4HookPoint` | int | — | v12.9.9+, no default named | 0 same as the pass before, 1 Before Upscaling, 2 After Upscaling, 3 Present (numbered unlike `NRHookPoint`); an earlier point than the pass before's runs at that pass's | C |
| `NRPass2ResolutionScale`..`NRPass4ResolutionScale` | float | — | v12.9.9+, 0 or 0.33..1 | 0 as pass 1, else the fraction of the output (under 0.33 reads 0.33); a pass at Before Upscaling stays native | C |
| `NRFrameBuffering` | int | — | v12.9.9+, 0 | 0 Automatic, 1 One frame, 2 Same frame; D3D11 at Present and relayed games | C |
| `NRUpscaler` | int | — | v12.9.9+, 0 | 0 the game's FSR 3.1 / XeSS, 1 NR's own DLSS SR (DLAA at native size) | C |
| `NRUpscalerPreset` | int | — | v12.9.9+, 0 | 0 NVIDIA's per mode (K DLAA/Quality/Balanced, M Performance, L Ultra Performance), 1 J, 2 K, 3 L, 4 M | C |
| `NRLookHaloRemoval` | float | — | v12.9.9+, 0, 0..1 | Takes off NR's bright rims along silhouettes; acts with Fine-tune off | C |
| `NRFrameGen` | int | — | v13.4.0+, 0 | 0 Off, 1 On (DLSS-G hosted by NR after NR's pass), 2 Dynamic (v13.4: the fixed multiplier); read at the game's start | C |
| `NRFrameGenMultiplier` | int | — | v13.4.0+, no default named, 0..4 | 2x..6x, clamped to the GPU's limit; not used under Dynamic | C |
| `NRFrameGenUi` | int | — | v13.4.0+, no default named | 0 Steady (real UI recomposed), 1 Interpolated; read at start | C |
| `NRHdrScreen` | int | — | v13.4.0, no default named; v14.0.0: retired (logged once, ignored) | 0 HDR10, 1 scRGB (FG on a Windows HDR screen, HDR10 games); read at start | C |
| `NRFrameGenVSync` | int | — | v14.0.0+, no default named | 0 Off (tearing allowed), 1 On (SyncInterval 1 while DLSS-G generates, where supported); never with Dynamic | C |
| `NRUpscalerMode` | int | — | v14.0.0+, 0 | 0 the game's choice, 1 DLAA, 2 Ultra Quality, 3 Quality, 4 Balanced, 5 Performance, 6 Ultra Performance (with `NRUpscaler=1`) | C |
| `NRSdrHdr` | int | — | v14.0.0+, 0 | 0 Off, 1 Balanced, 2 Bright (SDR game on a Windows HDR display); read at start | C |
| `NRSafeMode` | int | — | v14.0.0+, 0 | 1 turns off every addition beyond NR's core, each key's value kept; read at start | C |
| `NRFrameGenEngine` | int | — | v15.2.0+, 0 | 0 NVIDIA DLSS Frame Generation, 1 Intel XeSS (only with the owner's `NRXeFg` gate), 2 NVIDIA VFG (RTX 40/50, files in NR's store); read at start, falls back to DLSS-G; 3 and up not probed | C / probe (1 and 2 kept, no gate check at load) |
| `NRFrameGenRealInputs` | int | — | v15.2.0+, 0 | 0 Always on (DLSS-G is given every frame), 1 In game (only frames with the game's own camera and motion); 2 reads as 0 | C / probe |
| `NROpticalFlowHalf` | int | — | v15.2.0+, 0 | 0 Full size, 1 Half size (optical flow input size for pictures 2560 or more wide); not offered | C |
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
- **Early Access folder.** v13.4.0's Early Access setup (`sources.json`, static read) keeps `updates.mediafire_folder` `r1xp6x0i043wx` (the
  regular releases) and adds `updates_dev`: folder `uh4x7j5f7fj2y`, pattern `DLSS5-NR-Dev-Setup-*.exe`, "the newest dev builds, never the
  regular folder ... (made 11-07, archive inside)" (C). The owner's rule is "always take the newest": the app (`MediaFireClient.DevFolder`) lists
  the dev folder on every check after the regular folders and takes the newest upload of the two (the regular pick, by the generation rule
  above, against the dev folder's newest, by the upload rule below). The settings keep remembering the regular folder's key only; a move inside
  the dev folder is followed for that check. When either listing comes from the cache, the check counts as offline and keeps the library. Going
  back from an Early Access build to a regular one is the user's choice (pick the build); the app never switches games.
- **Channel move.** A `channel.json` in the root (fields `notice`, `moved_to`) moves the channel to a new folder key. The app
  follows it once per check, remembers the new key in its settings (checked before the built-in folders) and refuses a loop or a second move.
- **Candidates.** `DLSS5-NR-Setup-v*.exe`, `DLSS5-NR-Dev-Setup-v*.exe`, `DLSS5-NR-*.zip`, `DLSS5-NR-Dev-*.zip`, `dlss5-nr.addon64`,
  `dlss5-nr-v*.addon64`, `dlss5-nr-dev-v*.addon64` (the dev zip and add-on names are guesses modelled on the regular ones; only the dev setup
  pattern is documented). The hand-install zip's name (`DLSS5 NR Add-on v13.4.0.zip`, with spaces) is not a channel candidate; it is offered on
  the author's page, not known in a folder. The version comes from
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
  memory and extracts the root `dlss5-nr.addon64` and, when the package has them (v12.9.9 and newer), `dlss5-nr.addon32` and NR's host
  (see "App support", Import); the exe is deleted afterwards. The Early Access setup has the same layout (C).
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
- **Live state (2026-11-26).** The listings the app cached on 2026-11-25 (above, "v15.2.0 packages") and the live MediaFire page of the dev zip (its sha256 matches the listing)
  were read; the setups were not downloaded. Three v15.2.0 Dev builds went through the app's library (2026-11-24, 11-25 and 11-26 fetches, same label), and the automatic cleanup deleted the
  oldest. Open: whether the regular folder's v15.2.0 Dev2 and the dev folder's Dev11 are the same file under another name (the regular setup was not opened).
- **Live state (2026-11-18): not checked.** The v14.0.0 Dev3 setup is the owner's copy; MediaFire was not called.
- **Live state (2026-11-17): not checked.** The v12.9.9 and v13.4.0 packages were checked from copies the owner provided; MediaFire was not
  called. Which files the regular and the dev folder list now (setup exes only, or also zips or raw add-ons, and whether either has a
  `channel.json`) is open.
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

DLSS5 Updater downloads and installs the add-on automatically from the catalog mirror (`neural.lecram`; the file itself decides v10 or v11) or imports it (the private build can also fetch v11-generation builds, regular and Early Access, from the author's channel, see "Update source") and installs it per game in ReShade-only mode: 64-bit Direct3D 11, Direct3D 12 and Vulkan games with any build, and
Direct3D 9, Direct3D 10, OpenGL and 32-bit games with a build that carries NR's host (see Host below). Prerequisites: ReShade 6+ (6.8+
with a host build, from INSTALL.txt), the NR runtime `nvngx_dlssnr.dll`, and up to v11.9 a game with native DLSS. v12.0.0 and newer also
run in games without it (`LecramRoute.FirstWithoutDlssBuild`: v12.0.0's release notes say "with or without DLSS", and its README says
NR then uses the game's motion vectors where it finds them, else optical flow). It is not added to `[ADDON] LoadFromDllMain`, since nothing here shows it needs early load.

- **Import.** The add-on file itself (`renodx-dlss5.addon64` or `dlss5-nr.addon64`), or one of the author's packages: a setup exe
  (`DLSS5-NR-Setup-v*.exe`, `DLSS5-NR-Dev-Setup-v*.exe`) or the hand-install zip (`DLSS5 NR Add-on v*.zip`). From a package the app takes
  the root `dlss5-nr.addon64` (the same appended-ZIP reader as the channel, `MediaFireClient.ExtractPackage`; the exe is never run) and
  imports it like the file. When the package has NR's host it also takes the host set: `dlss5-nr.addon32` and the `dlss5-nr-host` folder,
  from the zip's folder, or assembled from a setup's own paths (`nr-host64.exe`, `reshade\ReShade64.dll` and the root add-on: the same
  files byte for byte, see "Host and 32-bit"). Only these known entry names are read, each into a fixed name (no entry path reaches the
  disk), each at most 128 MB. Every file of the set must be a PE file whose version resource, when it has one, names it as that file, and
  the host's add-on must be the v11 generation. The 32-bit add-on or the host exe marks a package with a host; it then needs the whole
  set, else it is refused ("only in part"). A setup that has only the author's ReShade and no host exe imports without a host. A package
  without a root add-on is refused. The private build's channel downloads go the same way.
- **Host set in the library.** A build that came from a package with the host keeps the set in its own folder, in the game layout
  (`dlss5-nr.addon32`, `dlss5-nr-host\nr-host64.exe`, `dlss5-nr-host\ReShade64.dll`, `dlss5-nr-host\dlss5-nr.addon64`). The build id
  stays the SHA-256 of the 64-bit add-on: it decides the generation, label and options, the set comes from the same release, and the same
  build from the setup, the zip or the bare file is one build. A package import of a build already there replaces its set as a whole (as
  Deep Fried Chicken's imports do); a bare add-on import keeps it. A folder with only part of the set refuses to install ("Import its setup
  exe or add-on zip again"). Settings → Local components says per v11-generation build "with NR's host and 32-bit add-on" or "64-bit
  add-on only (no NR host)".
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
  NR injectors (`renodx-dlssfix`, Deep Fried Chicken from an older version) are moved aside too. Uninstall restores everything. ShortFuse's add-on is left alone.
- **Host.** With a build that carries the host, the app places the whole set beside the game's ReShade, as INSTALL.txt asks, whatever
  the game: `dlss5-nr.addon64`, `dlss5-nr.addon32` and the `dlss5-nr-host` folder. ReShade loads only the add-on that matches the game's
  bitness and NR starts its host only for the games it relays, so a 64-bit Direct3D 11 game with in-game MSAA (a game setting the app
  can't see) is covered without detecting it. Every file is recorded as placed (record-level `Files`, shown under the Lecram component);
  a `dlss5-nr-host` folder that wasn't there is recorded in `Dirs`, so it leaves whole, with the `ReShade.ini` and `ReShade.log` NR's host
  writes in it. Files of a hand install already there are backed up and restored. Switching to a build without the host, a run without
  Lecram, and Uninstall take the set out. A 32-bit game gets ReShade32 as its proxy (`d3d9.dll`, `opengl32.dll`, `dxgi.dll`), as for
  every ReShade-only install. The install refuses, with the reason: a game that needs the host (32-bit, Direct3D 9, Direct3D 10, OpenGL)
  with a build without it; 32-bit Vulkan and 32-bit Direct3D 12 (not supported by the author yet); a game without native DLSS with a build
  older than v12.0.0 (`LecramRoute.Refusal`). The app writes nothing into the host's own ReShade.ini; NR's shortcuts, which go there, are
  not app options. `nr-host64.exe` is never taken for the game's exe (`GameInspector`). The anti-cheat confirmation is the same as for
  any install.
- **Advisor.** In a game without DLSS, the method panel lists "Neural rendering with Lecram DLSS5 add-on" before the GITC Uplift card when
  a Lecram build is in the library (docs/advisor.md).
- **Keys written.** Only the options set in Settings, only to `[RenoDX.DLSS5]`, only while the add-on is installed. Options that
  exist in one generation only are hidden for the other (v10 only: `NRAutoMask`; v11 only: `NeuralUplift`, `NRPreset`,
  `NRPass2Preset`..`NRPass4Preset`, `NRVramBuffering`, `NRGpuCrashWitness`, `NRPresentFgPath` and every v12.9.9 / v13.4.0 key below); `NRHookPoint`, `NRStyle`, `NRLocalStructure`, `NRSkinStructure` and `NRPresentGuides` offer
  per-generation values. Within v11 the options only offer values that mean the same in beta.6 through beta.48
  (`NRPresentGuides` 1/2, not 0), so there is no per-build choice variant; `NRFeedMode` (1/2) is offered for both generations and
  beta.6 ignores it. Keys only later v11 builds read are tied to the first checked build that has them (`LecramIni`): GPU crash
  reports (Off 0, On 1) from beta.22, the pass presets (offered 0..2, Old and New of 310.9.16) from beta.29, Present with frame
  generation (0, or 1 as a test) from beta.48; from v12.9.9 the pass hook points (0..3) and resolutions (0, 0.33..1), frame buffering
  (0..2), the upscaler (0/1) and its DLSS preset (0..4) and halo removal (0..1); from v13.4.0 NR's frame generation (0..2), its multiplier
  (0..4 = 2x..6x), the UI in generated frames (0/1) and frame generation on HDR screens (0/1), so a v12 build never gets the frame
  generation keys; from v14.0.0 VSync for frame generation (0/1), the DLSS mode (0..6), SDR to HDR (0..2) and Safe mode (0/1); from v15.2.0 the frame generator (0 NVIDIA DLSS, 2 NVIDIA VFG) and where frame generation runs (0 Always on, 1 In game). Keys a later
  build retired are tied to that build the same way (Present with frame generation is retired from v12.0.0, frame generation on HDR screens
  from v14.0.0; nothing else the app writes is retired in v12.9.9, v13.4.0, v14.0.0 or v15.2.0). For an older build (by its detected label), or one that retired the key, the option is hidden
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