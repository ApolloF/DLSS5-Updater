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

- 4.x also runs on games **without** their own DLSS (DFC's own or an external ReShade motion provider's vectors).
  The app still installs DFC only into games with native DLSS; opening that up needs a decision about
  `nvngx_dlss.dll` for those games (open item).
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

## Options the app writes

`arm`, `enabled`, `passes`, `neural_work_percent`, `native_vulkan_enabled`, `preserve_native_tone_color`,
`clean_fry_enabled`, `color_strength` (from earlier docs), and from 4.x `output_hud_enabled`,
`output_highlights_enabled`, `output_deband_enabled` (on/off only; the comment above is all the documentation, so the
strengths and HUD regions are left as they are in the game's cfg). Everything else is tuned in the overlay; see the
comment above `SettingsOptions.Dfc`.
