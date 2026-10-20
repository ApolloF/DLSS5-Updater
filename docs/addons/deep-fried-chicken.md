# Deep Fried Chicken (DFC)

Closed-source ReShade add-on that runs DLSS 5 neural rendering. Its licence allows personal use only and forbids
reverse engineering, redistribution and modification. So this file is built **only** from what the author ships as
documentation: INSTALL.txt / README.txt, the cfg files (keys, defaults, comments), file names and version resources.
No disassembly, no string dumps of its binaries and no probe runs. The app never downloads DFC; the user imports their
own copy, and the files are copied into games byte for byte. Copying the user's own files into their games is the
install the licence allows; it forbids copying, rehosting or repacking the Software for others.

## Checked builds

| Release (INSTALL.txt / README.txt) | Add-on version resource | config_schema | Checked | Notes |
|---|---|---|---|---|
| 2.0 | 2.0.0 | 13 | 2026-09 (files dated 2026-09-14) | add-on, NGX bridge and cfg only |
| 4.0.6 preview | 4.0.0-preview (numeric 4.0.0.0) | 13 | 2026-10-18 | adds the present-support dll, the neural folder, a ReShade effect and a 32-bit route |
| 5.0.0 essentials | 5.0.0 (numeric 5.0.0.0) | 13 | 2026-10-20 | same 64-bit file names as 4.0.6 plus THIRD-PARTY-NOTICES.txt in the neural folder; README.txt replaces INSTALL.txt; LICENSE-Deep-Fried-Chicken.md added |

`DfcConfig.TestedVersion` compares against the numeric version resource, so it is `5.0.0`. (For 4.0.6 it was
`4.0.0`: the preview's hotfix number was only in INSTALL.txt.) The bridge, present-support dll and the neural helper exe
have no version resource.

## 5.0.0 essentials package

Distributed as `Deep-Fried-Chicken-v5.0.0-essentials.7z` (encrypted with the password from the release post, headers
too). The app can't open .7z; the user extracts it and imports the folder.

```
README.txt, LICENSE-Deep-Fried-Chicken.md
64-bit\  deep-fried-chicken.addon64, deep-fried-chicken.cfg, deep-fried-chicken-nvngx.dll,
         deep-fried-chicken-present-support.dll,
         deep-fried-chicken-neural\  dfc-neural-helper64.exe, dfc-neural-motion.onnx, deep-fried-chicken-ort.dll,
                                     DirectML.dll, onnxruntime_providers_shared.dll, THIRD-PARTY-NOTICES.txt
         reshade-shaders\Shaders\DFC_OutputState.addonfx
32-bit\  deep-fried-chicken.addon32, deep-fried-chicken-bridge.cfg, reshade-shaders\Shaders\DFC_Universal_Feed.fx + ReShade.fxh,
         host64\  the same 64-bit add-on, cfg, bridge and neural folder, plus dfc-universal-host64.exe
```

Against 4.0.6 the add-on, bridge, present-support dll, neural helper exe and notices changed; the ONNX runtime,
DirectML, the motion model and the effect are byte-identical.

README.txt, 64-bit games: "Copy the CONTENTS of the 64-bit folder next to the game's exe (where ReShade's dxgi.dll /
d3d11.dll / opengl32.dll is). Keep the deep-fried-chicken-neural and reshade-shaders folders as folders." Then put your
own `nvngx_dlssnr.dll` and `nvngx_dlss.dll` next to the exe. It names 310.8.0 and 310.9.16 as working NR builds; NVIDIA's
own builds run NR only on RTX 50, RTX 20/30/40 need a community build. Updating: "Replace DFC's files with this
release's ... Keep your .cfg files to keep your settings. Remove other DLSS feeder or neural add-ons from the same
folder." The app does all three (whole set replaced, game cfg kept and merged, other NR add-ons displaced).

What's new in 5.0 per README.txt that touches the app: NR presets (Game, Preset A, Preset B; Preset B is the FP8 model
in 310.9.16+), Texture Boost, game DLSS tuning, frame-generation awareness, a status card, menu languages, a
"Faster DX9" startup option (D3D9Ex, off by default), and `deep-fried-chicken-hooks.log`. All are set in the overlay.
Removed since 4.0: DFC's own upscaler (Universal SR), scene jitter and the virtual display (as in 4.0.6).

### cfg diff: 4.0.6 preview → 5.0.0 (64-bit `deep-fried-chicken.cfg`)

`config_schema` stays 13 and no key was added or removed. One default changed, for all 30 passes:
`layer_N_nr_preset` 1 → 0. The cfg has no comment on these keys and README.txt names the choices only by label (Game,
Preset A, Preset B), not by number, so the value mapping is undocumented. The app doesn't write `layer_N_nr_preset`;
the preset is picked in the overlay. The 32-bit `host64\deep-fried-chicken.cfg` is identical to the 64-bit one.

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
exe. 5.0's README.txt spells it out: copy the 32-bit folder's contents next to the game exe, put a 64-bit ReShade
(with add-on support) into `host64` as `dxgi.dll`, and `nvngx_dlssnr.dll` / `nvngx_dlss.dll` into `host64` too. The
app only handles 64-bit ReShade and games, so this route is still left out. The import refuses the `host64` add-on
with a message pointing at the 64-bit folder, and an archive's `host64` copy is skipped when looking for the add-on.

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

- **Import** (`Core/DfcSource.cs`, Settings → Local components: **Folder…** or **Import…**): the extracted release
  folder (its `64-bit` subfolder is used), the 64-bit folder itself, the add-on in it, or a `.zip` of any of these
  (unpacked to a temp folder beside the import; the `host64` copy is skipped). `.7z`/`.rar` are refused with "extract
  it first". The add-on's OriginalFilename must be `deep-fried-chicken.addon64` and the bridge must sit beside it.
  - **4.x and newer: the whole 64-bit folder** is taken, as the package says ("copy the contents of the 64-bit
    folder"), subfolders included (`ComponentStore.IsDfcSetFile`). Left out: the cfg (kept as the default, see below),
    `*.log`, and at the folder root NVIDIA's `nvngx_*` files, `ReShade*` files and proxy dll names (the user may have
    dropped them there per step 3; the app places those itself). The present-support dll and the neural folder are
    required. A folder with more than 64 files or 1 GiB is refused as not DFC's folder (5.0: 11 files, 75 MB), and a
    `.zip` with more than 256 entries or 2 GiB unpacked is refused before unpacking.
  - **2.x**: only the add-on and the bridge (the folder may be Downloads).
  - The set is staged first, so a failed import leaves the library as it was.
- **Library** (`AddonKind.Dfc` in `Core/AddonLibrary.cs`, the same model as the Lecram and ShortFuse builds): every
  import is one build under `components\dfc\<id>\`, id = SHA-256 of its `deep-fried-chicken.addon64`, holding the whole
  imported set, its default cfg and `version.json` (id, version resource as label, import date, the user's own name).
  Importing the same add-on again is the same build: its set is replaced as a whole (no file of the older set stays),
  its name and import date stay. Builds are ordered by version resource, then import date. An import from before the
  library (the set directly in `components\dfc\`) moves into it once at startup (`MigrateDfc`), and old install records
  whose `DfcTag` is `<version>-<sha12>` are matched to that build by the hash prefix.
- **Default and per-game pick**: Settings → Local components lists the builds with "Newest (…)" first. `DfcId` null
  (the default) follows the newest build; picking a build pins it, picking Newest unpins it, the app never pins on its
  own, and a removed pinned build falls back to Newest. Each game can pick its own build on its page ("Default (…)",
  then the builds; `GameOverride.DfcId`). That build decides the set placed, the default cfg, which options are shown
  for the game, and whether games without their own DLSS are allowed.
- **Cleanup**: DFC is a library in the build cleanup (keep count 2 by default). The default, builds picked for a game
  and builds installed in one are protected, and the reason is shown. The automatic cleanup never deletes an import;
  only the manual Clean up or Remove in Settings does.
- **Install** (`Installer.PlaceDfc`): every file of the chosen build's set goes to the same relative path beside the game exe, each
  one recorded in the manifest (`Files`, and `DfcFiles` for which of them are DFC's; recorded before the copy), with
  anything already there backed up; the manifest also records the build (`DfcId`, `DfcTag`). Files an earlier set of
  ours had and the new one lacks (switching builds) are taken out. Records
  from before `DfcFiles` are matched by the old file list (`IsDfcPackagePath`), which covered every file 4.0.6 had. If ReShade.ini's `[GENERAL] EffectSearchPaths` doesn't cover `reshade-shaders\Shaders`
  (ReShade's default without the key is `.\`, not recursive), `.\reshade-shaders\Shaders\**` is appended; that
  entry stays when DFC goes (it is ReShade's standard folder).
- **cfg**: the chosen build's default is placed when the game has none; the app's options are merged in (only the keys
  set in Settings). Options whose key that cfg lacks are hidden: in Settings by the default build, on a game's page by
  the game's build.
- **Remove / switch** (RenoDX, Lecram or OptiScaler-NR): all DFC files of ours go, originals come back and empty
  folders (`deep-fried-chicken-neural`, `reshade-shaders\Shaders`, any other folder of the set) are deleted. Foreign copies of the add-on, bridge
  and present-support dll are moved to the backup; a foreign neural folder or effect file is left, since it does
  nothing without the add-on.

## Games without their own DLSS

- **Native DLSS** means the game ships DLSS Super Resolution itself (`nvngx_dlss.dll` or Streamline's `sl.dlss.dll`
  anywhere in the game); copies an install of ours added beside the exe (for DFC, or OptiScaler's "add missing DLSS")
  don't count (`Installer.HasNativeDlss`). The same rule now decides the native-DLSS requirement of ShortFuse and Lecram,
  so a DFC install never makes a game look like a DLSS game to them.
- **4.x build** (the game's chosen build decides): installs. Besides the whole 64-bit set and the user's
  `nvngx_dlssnr.dll` (as in every game), the app places `nvngx_dlss.dll` from NVIDIA's DLSS release (the DLSS component, the version picked for the game's DLSS files)
  beside the exe, because INSTALL.txt asks for it and DFC doesn't ship it. It is recorded in the manifest
  (`DfcRuntimes`), kept current on reinstall and update, and taken out when DFC goes (uninstall or switching route).
  Restore DLSS leaves it, since DFC needs it. A copy the app doesn't own is never replaced.
- **Frame generation**: the per-game toggle "Add nvngx_dlssg.dll for DFC's DLSS-G frame generation" places NVIDIA's
  `nvngx_dlssg.dll` from the same release the same way; off takes it out again. Frame generation itself is switched on,
  and DLSS-G or FSR 3.1 chosen, in the overlay's Rendering card. FSR 3.1's AMD dll is not provided by the app.
- **2.x build**: refused in such games ("Deep Fried Chicken 2.0.0 needs a game with native DLSS. Import Deep Fried
  Chicken 4.x, which also supports games without their own DLSS."). The game page shows the same text before install;
  picking a 4.x or newer build for the game lifts it.
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
