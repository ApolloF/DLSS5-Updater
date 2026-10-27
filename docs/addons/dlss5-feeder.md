# DLSS5-Feeder

ReShade add-on by Jean-Laurent Rouzies (`jlrouzies-fr/DLSS5-Feeder`). It makes the DLSS calls a game without DLSS never
makes: it takes the backbuffer, ReShade's depth (Generic Depth add-on) and estimated motion vectors from a ReShade shader,
and hands them to NVIDIA's DLSS as a DLAA frame, so a neural consumer (Deep Fried Chicken here) has something to run on.
DLAA only: no upscaling, no performance gain, a few ms per frame at 4K. Facts below are from its README and release notes; nothing was reverse engineered.

## Component model (docs/design.md section 2)

| Part | Feeder |
|---|---|
| Source | `Download` and `Import`. Releases of exactly `jlrouzies-fr/DLSS5-Feeder` (copycat repositories exist, the owner and name are pinned in `FeederLibrary.Repo`). Asset `DLSS5-Feeder-<version>.zip`. |
| Licence | MIT for `src/`, `shaders/`, `host/` (GitHub reports NOASSERTION because of an extra notice line). Third-party parts keep their own licences and none is bundled in the zip. Downloading and copying into the user's game is what the licence allows. |
| Verification | The SHA-256 GitHub publishes in the asset's `digest` field (all 4 releases checked 2026-10-21 had one). A release without a digest is not offered: no digest = no install. **Import** (Settings → ReShade add-ons → DLSS5-Feeder builds: Import… for the release .zip or `dlss5-feed.addon64`, Folder… for the extracted release): the four package files are copied byte for byte as a build whose id is the SHA-256 of the 64-bit add-on, labelled with the version from `READ-ME-FIRST.txt` ("imported" in the list); no digest check, the user chose the files. Imports are never removed by the automatic cleanup. The catalog repo needs no `feeder` section for that reason; add one (SHA-256 plus a mirror) the day GitHub stops publishing digests. |
| Library | `LibraryKind.Feeder`: `components\dlss5-feeder\<tag>\pkg\` plus the `.complete` record. Only the four files below are kept. Newest first by `BuildOrder`. |
| Default / per-game | `Components["dlss5-feeder"].Build` in settings and `GameOverride.BuildPick("dlss5-feeder")`; the picker shows **"Auto (<build>)"** first (the newest build, or the pinned default) and then every build; a pick wins, and "Reset to auto" (Settings and the game page) goes back. A removed build's pins go back to Auto, never to another build. The author tags betas (`v1.18.0-beta.2`) without GitHub's prerelease flag, so a beta is told by its tag and follows Settings' "include pre-releases" like the other downloads. |
| Cleanup | In `BuildCleanup` like every library; the default, per-game picks and builds installed in a game are protected. |
| Install record | Generic `InstalledComponent` (`dlss5-feeder`, build id = tag, version label, files with SHA-256, backups). `Extras`: `bits` (`64`/`32`) and, for 32-bit, `manual`. |
| Config | None, so there is nothing to override per game.  `dlss5-feed.cfg` is created by the add-on itself with working defaults and re-read while the game runs; the README documents `host_window`, `preset`, `stall_log_ms` and others, but the app writes no key. |
| Update check | Through `GitHubClient.GetReleasesAsync` (ETag cache); offline it uses the cached release list, then the library. |

## What the app places

| Game | Files (relative to the install folder) |
|---|---|
| 64-bit | `dlss5-feed.addon64`, `reshade-shaders\Shaders\DLSS5_Feed.fx` |
| 32-bit | `dlss5-feed.addon32`, `reshade-shaders\Shaders\DLSS5_Feed.fx`, `host64\dlss5-feed-host64.exe` |

Taken from the zip by exact name; a zip missing one of them is refused whole. Not taken: `dlss5-feed-helper.addon64` (64-bit
helper mode, only for games whose own process cannot run NGX), the Vulkan layers (`layer-x64`, `layer-x86`: machine-wide
registration needs administrator rights), `READ-ME-FIRST.txt` and `Verify-DLSS5Feeder.ps1`.

## What it needs and the app does not do

- **One neural consumer, Deep Fried Chicken** (imported, 4.x or newer for games without DLSS). The planner
  (`NrAddons.FeederRefusal`, checked in `Installer`) refuses Feeder without it and next to stock OptiScaler, the DX11 bridge,
  ShortFuse's RenoDX add-on, Lecram or GITC Uplift. Feeder's README also names `renodx-dlss5` and an OptiScaler DLSS-NR fork as
  consumers; the app only pairs it with DFC, the one it can install in a game without DLSS.
- **ReShade 6.8+ with add-on support**, `nvngx_dlssnr.dll` (from the NR library: downloaded from the catalog mirror or imported) and an
  `nvngx_dlss.dll` (the app places NVIDIA's, as for DFC without DLSS).
- **A motion-vector shader** in `reshade-shaders\Shaders`, picked with `DLSS5_MV_PROVIDER` in the overlay. LumeniteFX Kernel is
  recommended; its AGNYA licence ("all rights reserved unless explicitly stated") stops the app from shipping it. The advisor
  treats `lumenite_*.fx` in the shader folder as "present".
- **The right depth buffer** in Generic Depth, MSAA/SSAA off, single-player only.
- Routes with manual steps: Direct3D 9 (dgVoodoo2, freeware that cannot be redistributed; the README has the settings),
  Vulkan (ReShade's Vulkan layer, one administrator prompt, plus `ReShadeApps.ini`), 32-bit (the `host64\` folder needs a 64-bit
  ReShade and DFC's 64-bit set placed by hand). The advisor lists each as a "Needs" line.

## Checked builds

| Release | Published | Zip (bytes) | Checked | Notes |
|---|---|---|---|---|
| v1.19.0 | 2026-10-11 | 503554 | 2026-10-21 | Layout: `dlss5-feed.addon64`, `dlss5-feed.addon32`, `dlss5-feed-helper.addon64`, `host64\dlss5-feed-host64.exe`, `reshade-shaders\Shaders\DLSS5_Feed.fx`, `layer-x64\`, `layer-x86\`, `READ-ME-FIRST.txt`, `Verify-DLSS5Feeder.ps1`. README: "NOT RUN IN A GAME: the two 1.19.0 fixes". Line 1 of `dlss5-feed.log` must say `dlss5-feed 1.19.0 commit 3c09eab`. |
| v1.18.0-beta.2 | 2026-10-06 | 503310 | 2026-10-21 (listing only) | Same asset name pattern; beta. |
| v1.18.0-beta.1 | 2026-09-29 | 492288 | 2026-10-21 (listing only) | Beta. |
| v1.17.0 | 2026-09-27 | 390753 | 2026-10-21 (listing only) | No helper add-on yet. |

Volatility: four releases in four weeks; the driver/consumer matrix in its README changes with them (driver 616.64+ breaks
`renodx-dlss5` 4.6/4.7, DFC is fine). Re-check this table, the zip layout and `FeederLibrary.PackageFiles` for every new release.

## Tests

`tests/DLSSUpdater.Tests/FeederTests.cs` (release choice, digest check, library, 64-bit and 32-bit install and uninstall, update),
`DfcTests.cs` (install in the same run as DFC, removal by a run without it, refusals), `NrConsumerTests.cs` (the rule).
