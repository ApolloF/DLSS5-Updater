# Deep Fried Chicken (DFC)

Closed-source ReShade add-on that runs DLSS 5 neural rendering. Its licence allows personal use only and forbids
reverse engineering, redistribution and modification. So this file is built **only** from what the author ships as
documentation: INSTALL.txt, the cfg files (keys, defaults, comments), file names and version resources. No
disassembly, no string dumps of its binaries and no probe runs. The app never downloads DFC; the user imports their
own copy, and the files are copied into games byte for byte.

## Checked builds

| Release (INSTALL.txt) | Add-on version resource | config_schema | Checked | Notes |
|---|---|---|---|---|
| 2.0 | 2.0.0 | 13 | 2026-09 (files dated 2026-09-14) | add-on, NGX bridge and cfg only |
| 4.0.6 preview | 4.0.0-preview (numeric 4.0.0.0) | 13 | 2026-10-18 | adds the present-support dll, the neural folder, a ReShade effect and a 32-bit route |

`DfcConfig.TestedVersion` compares against the numeric version resource, so it is `4.0.0`: the hotfix number of the
preview (4.0.6) is only in INSTALL.txt. The bridge, present-support dll and the neural helper exe have no version resource.

## 4.0.6 preview package

```
INSTALL.txt
64-bit\  deep-fried-chicken.addon64, deep-fried-chicken.cfg, deep-fried-chicken-nvngx.dll,
         deep-fried-chicken-present-support.dll,
         deep-fried-chicken-neural\  dfc-neural-helper64.exe, dfc-neural-motion.onnx, deep-fried-chicken-ort.dll
                                     (ONNX Runtime 1.24), DirectML.dll (1.15.4), onnxruntime_providers_shared.dll
         reshade-shaders\Shaders\DFC_OutputState.addonfx
32-bit\  deep-fried-chicken.addon32, deep-fried-chicken-bridge.cfg, reshade-shaders\Shaders\DFC_Universal_Feed.fx + ReShade.fxh,
         host64\  the same 64-bit add-on, cfg, bridge and neural folder, plus dfc-universal-host64.exe
```

INSTALL.txt says: ReShade with full add-on support matching the game's bitness; copy the 64-bit folder contents next
to the game exe; keep `deep-fried-chicken-neural` whole (64-bit: beside the exe); add your own `nvngx_dlss.dll` /
`nvngx_dlssnr.dll`. Nothing in it is marked optional, so the app always installs the whole 64-bit set, the neural
folder included. INSTALL.txt does not say what each file does; the app makes no claims about it.

Other 4.0.6 notes from INSTALL.txt that matter here:

- 4.x also runs on games **without** their own DLSS: "Games without their own DLSS get NR with DFC's (or an external)
  motion; games with DLSS keep SR/Quality/Model tuning." External providers (Lumenite Kernel or QuantMotion, VORT,
  Launchpad, texMotionVectors effects such as DRME / qUINT / dh_uber_motion) are used when running, on DX10/11/12,
  Vulkan and OpenGL, not DX9. INSTALL.txt does not say which file makes DFC's own motion; the package's motion model
  (`dfc-neural-motion.onnx`) is in `deep-fried-chicken-neural`, which the app always installs. See "Games without
  their own DLSS" below.
- "NR (DLSS 5 neural rendering) works on top of the game's own anti-aliasing. Lower Resolution under Pass controls for
  speed."
- "Frame Gen (Rendering card) for games without their own DLSS: needs borderless or windowed mode; DLSS-G needs an
  RTX 40/50 GPU and NVIDIA's nvngx_dlssg.dll beside the game exe (DFC does not ship it); FSR 3.1 needs AMD's
  amd_fidelityfx_*_dx12.dll. It pauses while the ReShade menu is open."
- Removed in 4.x: DFC's own upscaler (SR), scene jitter and the virtual display. The cfg still has
  `universal_sr_enabled=0`; the app leaves it alone.
- Profiles in the overlay now save every setting except per-install hooking options.
- Logs to ask for: `deep-fried-chicken-hooks.log` and `deep-fried-chicken.log` in the game folder.
- Vulkan games and emulators need a ReShade.ini next to the exe, with DFC's files in that folder.

### 32-bit route: not supported

It needs a 32-bit ReShade (`ReShade32.dll`), the addon32, a bridge cfg and a 64-bit companion host folder with its own
exe. The app only handles 64-bit ReShade and games, so this route is left out. The import refuses the `host64`
add-on with a message pointing at the 64-bit folder.

## cfg diff: 2.0.0 → 4.0.6 preview (64-bit `deep-fried-chicken.cfg`)

`config_schema` stays 13. No key changed its default; all keys the app writes are still there.

- Added (all default off / neutral): `nr_interpolation_enabled=0`, `experimental_fg2x_enabled=0`,
  `universal_sr_enabled=0`, `motion_full_resolution_flow=0`, `fog_awareness_enabled=0`, `fog_awareness_strength=1.000`,
  `internal_motion_architecture=8`, `internal_guide_quality=2`.
- Added at the end, with comments:
  - "Optional output protection; disabled by default. Regions use normalized screen coordinates.":
    `output_hud_enabled=0`, `output_highlights_enabled=0`, `output_deband_enabled=0`, `output_highlight_strength=1.0`,
    `output_deband_strength=0.5`, `output_deband_threshold=0.006`, `output_hud_rect0=0.45,0.45,0.1,0.1`,
    `output_hud_rect1..3=0,0,0,0`.
  - "Optional independent output grading. Full neutral defaults are built in.": `grade_enabled=0`,
    `grade_basic_enabled=1`, `grade_strength=1.0`.
  - "Experimental synthesized D3D11 DLSS bias. Does not affect native vectors.": `reactive_protection_enabled=0`,
    `reactive_protection_strength=0.5`, `reactive_protection_threshold=0.65`, `flow_slope_aware_depth=0`.
  - "Optional DFC output comparison. Difference view measures changes, not quality.": `output_compare_enabled=0`,
    `output_difference_view=0`, `output_compare_position=0.5`, `output_difference_scale=8.0`.
- Removed: `texture_boost`, `texture_boost_strength`.

The 32-bit `deep-fried-chicken-bridge.cfg` (mode, work_resolution, cast_*, sr_*, diagnostic switches, ...) is not used.

## What the app does

- **Import** (`Core/DfcSource.cs`): the add-on, the folder holding it, or the package folder (its `64-bit` subfolder
  is used). Taken: the add-on (OriginalFilename must be `deep-fried-chicken.addon64`), the bridge (required), the cfg
  (optional default), `deep-fried-chicken-present-support.dll`, everything in `deep-fried-chicken-neural\` and
  `reshade-shaders\Shaders\DFC_*`. Nothing else in the folder is copied. For add-on version 4+ the present-support
  dll and the neural folder are required. The set is staged and replaces the previous import as a whole, so files
  of an older set never stay behind. A 2.0 import (flat add-on/bridge/cfg) keeps working without migration.
- **Install** (`Installer.PlaceDfc`): every file of the set goes to the same relative path beside the game exe, each
  one recorded in the manifest, with anything already there backed up. Files an earlier set of ours had and the new
  one lacks are taken out. If ReShade.ini's `[GENERAL] EffectSearchPaths` doesn't cover `reshade-shaders\Shaders`
  (ReShade's default without the key is `.\`, not recursive), `.\reshade-shaders\Shaders\**` is appended; that
  entry stays when DFC goes (it is ReShade's standard folder).
- **cfg**: the imported default is placed when the game has none; the app's options are merged in (only the keys
  set in Settings). Options whose key the imported cfg lacks are hidden.
- **Remove / switch** (RenoDX, Lecram or OptiScaler-NR): all DFC files of ours go, originals come back and empty
  folders (`deep-fried-chicken-neural`, `reshade-shaders\Shaders`) are deleted. Foreign copies of the add-on, bridge
  and present-support dll are moved to the backup; a foreign neural folder or effect file is left, since it does
  nothing without the add-on.

## Games without their own DLSS

- **Native DLSS** means the game ships DLSS Super Resolution itself (`nvngx_dlss.dll` or Streamline's `sl.dlss.dll`
  anywhere in the game); copies an install of ours added beside the exe (for DFC, or OptiScaler's "add missing DLSS")
  don't count (`Installer.HasNativeDlss`). The same rule now decides the native-DLSS requirement of ShortFuse and Lecram,
  so a DFC install never makes a game look like a DLSS game to them.
- **4.x import**: installs. Besides the whole 64-bit set and the user's `nvngx_dlssnr.dll` (as in every game), the app
  places `nvngx_dlss.dll` from NVIDIA's DLSS release (the DLSS component, the version picked for the game's DLSS files)
  beside the exe, because INSTALL.txt asks for it and DFC doesn't ship it. It is recorded in the manifest
  (`DfcRuntimes`), kept current on reinstall and update, and taken out when DFC goes (uninstall or switching route).
  Restore DLSS leaves it, since DFC needs it. A copy the app doesn't own is never replaced.
- **Frame generation**: the per-game toggle "Add nvngx_dlssg.dll for DFC's DLSS-G frame generation" places NVIDIA's
  `nvngx_dlssg.dll` from the same release the same way; off takes it out again. Frame generation itself is switched on,
  and DLSS-G or FSR 3.1 chosen, in the overlay's Rendering card. FSR 3.1's AMD dll is not provided by the app.
- **2.x import**: refused in such games ("Deep Fried Chicken 2.0.0 needs a game with native DLSS. Import Deep Fried
  Chicken 4.x, which also supports games without their own DLSS."). The game page shows the same text before install.
- In games with native DLSS nothing extra is placed: DFC uses the game's own DLSS.
- The game page shows a short hint for these games (no DLSS upscaling, DFC's or an external motion, frame generation
  needs borderless/windowed and RTX 40/50 for DLSS-G).

### Frame generation keys in the cfg: undocumented

The 4.0.6 `deep-fried-chicken.cfg` has three keys whose names touch frame generation or interpolation, all without a
comment, in the uncommented block at the top:

```
nr_interpolation_enabled=0
experimental_fg2x_enabled=0
frame_generation_coexistence=0
```

The only comments in the file head the five blocks at its end ("Live neural residual controls; neutral preserves the
normal result.", and the four quoted in the cfg diff above: output protection, output grading, the D3D11 DLSS bias and
the output comparison); none of them covers these keys. INSTALL.txt names only the
overlay's Rendering card. So no documented key turns DFC's frame generation on or picks DLSS-G vs FSR 3.1, and the app
writes none of them.

## Options the app writes

`arm`, `enabled`, `passes`, `neural_work_percent`, `native_vulkan_enabled`, `preserve_native_tone_color`,
`clean_fry_enabled`, `color_strength` (from earlier docs), and from 4.x `output_hud_enabled`,
`output_highlights_enabled`, `output_deband_enabled` (on/off only; the comment above is all the documentation, so the
strengths and HUD regions are left as they are in the game's cfg). Everything else is tuned in the overlay; see the
comment above `SettingsOptions.Dfc`.
