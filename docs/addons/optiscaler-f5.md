# OptiScaler F5 (janblade)

[janblade/OptiScaler-F5-DLSSNR-Multipass](https://github.com/janblade/OptiScaler-F5-DLSSNR-Multipass): a fork of OptiScaler with DLSS
neural rendering (NR). Open source, so there is nothing to probe: the facts below come from each release's `OptiScaler.ini`
(its comment blocks), release notes and zip layout. The app's keys, versions and asset rules live in `OptiSources.F5`
(`Core/OptiSources.cs`); the options in `SettingsOptions.Nr`.

## Checked builds

| Tag | Asset | Checked | Notes |
|---|---|---|---|
| v0.1.32 / v0.1.33 / v0.1.34 | `OptiScaler-DLSSNR-F5-0.1.32.zip`, `...-0.1.33-with-dlss-fg.zip`, `...-v0.1.34-with-dlss-fg.zip` | yes | 0.1.33: HDR output keys; 0.1.34: `[MFGUnlock]` keys |
| v0.1.35.1, v0.1.36 | `OptiScaler-DLSSNR-F5-v0.1.36-with-dlss-fg.zip` | ini diffed | F5Low becomes the default frame generation output in 0.1.36 |
| v0.1.37, v0.1.37.2, v0.1.38 | `v0.1.37-Optiscaler-F5.zip`, `v0.1.37.2-Optiscaler-F5.zip`, `v0.1.38-Optiscaler-F5.zip` | yes | new asset naming; tags can have four parts |
| **v0.1.39** (pre-release, `OptiScaler v0.1.39-pre1 (956bb719)`) | `v0.1.39-Optiscaler-F5.zip` | yes (tested tag: v0.1.39), 2026-11-26 | Vulkan F5Low, `[Memory]`, `[DLSSG] Preset`, model weights; ini and zip diffed against 0.1.38, see below |

Other assets of a release stay unmatched on purpose: `Optional-Vendor-Neutral-DLSS5-Forwarder.zip` (see design.md, known gaps),
`Optional-F5Low-Update-1.1.0.184.zip` (an optional update of the F5Low plugin) and `OptiScaler-DLSSNR-Troubleshooting.md`.

## Zip layout (v0.1.39)

Same root as before: `OptiScaler.dll`, `OptiScaler.ini`, `nvngx.dll_dlssnr.dll` (the forwarder the app places beside it) and the
`OptiScaler\` folder, which `Installer.InstallOpti` mirrors as a whole. New since 0.1.36: `OptiScaler\plugins\f5low.dll` (the
F5Low frame generation) and an updated `OptiScaler\libxess_fg.dll`. Both are installed, recorded in the install record and
removed on uninstall like the rest of the folder (`InstallerTests`).

0.1.39 against 0.1.38: the zip root is the same. In `OptiScaler\` the Vulkan FidelityFX set was renamed and split:
`amd_fidelityfx_vk.dll` is gone; `amd_fidelityfx_upscaler_vk.dll`, `amd_fidelityfx_loader_vk.dll` and
`amd_fidelityfx_framegeneration_vk.dll` are new. The app mirrors the folder as a whole, so nothing is special-cased
(`F5Build039_MirrorsTheRenamedVulkanFfxDlls_AndUninstallRemovesThem`). The release notes tell updaters to replace the proxy
dll, `nvngx.dll_dlssnr.dll` (the weights selector needs the new one) and `plugins\f5low.dll` (1.1.0.208); the app replaces the
whole set on update. The zip also has `docs\` (PLUGINS.md and others) and `TROUBLESHOOTING.md`.

## Frame generation

F5Low is the default frame generation output from 0.1.36 (`FGOutput=f5low`). The app writes no frame generation output keys
(`FGOutput`, `FGInput`) and keeps it that way. `[F5Low] FrameGenerator` (auto = `xefg_tensor`, `dlssg`, `intel`) and
`[F5Low] KeepGameDlssg` (auto = false) choose what F5Low runs; `dlssg` needs Streamline and `nvngx_dlssg.dll` in OptiScaler's
streamline folder (the zip ships them) and applies after a game restart.

## Keys by first tag

| Key | First tag | In the app |
|---|---|---|
| `[DlssNr] MultiViewScreens` | v0.1.35.1 | Several screens |
| `[F5Low] FrameGenerator`, `[F5Low] KeepGameDlssg` | v0.1.36 | Frame generator, Keep the game's DLSS-G |
| `[DlssNr] SecondaryFeatures` | v0.1.36 | Other DLSS features |
| `[DlssNr] DepthFade`, `DepthFadeFarStart/FarEnd/FarStrength`, `DepthFadeNearStart/NearEnd/NearStrength`, `DepthFadeSkyStrength` | v0.1.37 | Depth fade and its seven values |
| `[DLSSG] Preset` | v0.1.39 | DLSS-G preset (auto, A, B, C) |
| `[Memory] ProtectMemory` | v0.1.39 | Protect video memory (off, fg, all) |
| `[F5Low] VulkanLatencyGate`, `VulkanTearing`, `VulkanFlipMetering`, `VulkanPacer` | v0.1.39 | The four Vulkan options |
| `[XeSS] UnrealDepth` | v0.1.39 | XeSS depth in Unreal Engine |

Defaults of the 0.1.39 keys: `Preset` auto (the driver profile; A, B, C also read as 1, 2, 3; `nvngx_dlssg.dll` 310.9.16 or newer for C),
`ProtectMemory` fg (`off`, `fg`, `all`; true is all, false is off), `VulkanLatencyGate` true, `VulkanTearing` true,
`VulkanFlipMetering` false (experimental, RTX 50 only), `VulkanPacer` true (needs a newer f5low.dll), `UnrealDepth` true. Choices
and defaults are checked against `Config.cpp` of the v0.1.39 tag (`readString("DLSSG", "Preset")`, `gpumem::ParseProtectScope`).
`[DLSSG] Preset` and `[DlssNr] Preset` share a key name: look an option up by section as well.

There is one OptiScaler source now; F5's `VersionedKeys` say from which build each key exists. Defaults and
ranges come from the 0.1.38 ini: `MultiViewScreens` 0 (a sum: 1, 2, 4, 8, ... per screen, 256 the combined view, +512 all screens
as one picture), `SecondaryFeatures` 0 (stand aside; 1 own model; 2 share the main model), `DepthFade` false, far 0.5 / 0.85 /
0.5, near 0.0 / 0.1 / 1.0, sky 1.0, all distances and strengths 0 to 1.

## Config diff 0.1.38 to 0.1.39

Nothing the app writes was removed or renamed. Changed comments on keys the app manages: only `[Upscalers] Dx12Upscaler` (it can
now name a custom upscaler folder; the app doesn't write it any more, it only stays in the legacy key list so old installs are
cleaned up). That was the app's "1 setting(s) ... described differently than in v0.1.38" warning; it goes with the raised tested tag.
Other comments changed on keys the app doesn't expose: `Dx11Upscaler`, `VulkanUpscaler`, `[FrameGen] FGNvngxReplacement`
(custom frame generation), `[FSR] UpscalerIndex`, `[Libraries] FfxVkPath` (new: `FfxVkSRPath`, `FfxVkFGPath`),
`[fakenvapi] ForceReflex` (XeFG's low latency follows the game's Reflex setting), `[DlssNr] InputFlatten` (Vulkan note).

## Not exposed

- `NativeBlend` and `NativeBlendWas*`: owned by the OptiScaler menu, which stores the previous values of the setting it
  changes in the `Was` keys; writing them from here would fight the menu.
- `[DlssNr] WeightsSetA` / `WeightsSetB` (0.1.39): names of folders in `OptiScaler\nr_weights\`; free text that only makes sense with weight
  files the user made for the DLL, set in the OptiScaler menu (Neural Rendering page, Model weights card).
- `[Upscalers] RRUpscaler` / `RRAllowRegularUpscalers` (ray reconstruction provider), `[FSRFG] Provider`, `[Libraries] FfxVk*Path` and the
  `custom\` providers (plugin API): the app doesn't pick upscalers or providers.
- `[Log] LogGpuMemory`: a diagnostic.
- `NativeDepth*` (depth finder, alignment, scale, offset, linearize and so on): debug and per-game depth alignment, done in the
  game with the menu's debug views.

## Compat check

`OptiCompat` warns "newer than tested" for a new major or minor version, and in a 0.x series (F5) also for a
higher patch number or fourth part, because those are real releases there.

## How OptiScaler finds its files (0.1.39 source, same in 0.1.38 and upstream)

Facts from `OptiScaler/Util.cpp`, `dllmain.cpp` and `Config.cpp` of the v0.1.39 tag; they decide where the app must put files.

- **ReShade.** `LoadReshade=true` loads `Util::ReShadePath()`: `ReShade64.dll` (else `d3d12-ReShade64.dll`) in the folder of the
  **process exe** (`GetModuleFileNameW(nullptr)`), not the folder of OptiScaler's own dll. There is no ini key for another place.
  `Loading ReShade64.dll, result: 0` in the log means `LoadLibraryExW` returned null: the file is not there (or does not load). The
  0.1.39 Status page shows the Win32 error. This is unchanged since 0.1.38 and the same in upstream. It works for the app's layout only
  when the proxy dll sits in the exe's folder; a game whose exe lives elsewhere (Phantom Blade Zero: `platform\steam\PBZ.exe`, proxy in
  `ProjectPBZ\Binaries\Win64`) fails.
- **The `OptiScaler\` folder** (plugins, `streamline`, libxess, f5low.dll) is `[Libraries] OptiDllPath` (default `.\OptiScaler`), relative to
  the exe folder too; a missing folder falls back to the exe folder. The same game logged "f5low.dll is not in the plugins folder".
- **DLSS files.** `Util::FindFilePath(dir, name)`: the file directly in `dir`, else a breadth-first search of all of `dir` (it skips only
  `OptiScaler\custom` and `OptiScaler\streamline`; dot folders are searched), then Unreal Engine fallbacks (`Win64`/`WinGDK` parent, then
  `Engine\Plugins` a few levels up). `nvngx_dlss.dll` is searched in the OptiScaler folder, then the exe folder, unless
  `[Libraries] NvngxDlssPath` names the file (or its folder). `Config::CheckUpscalerFiles` searches the proxy's own folder once more
  for `nvngx_dlss.dll` as the signed stand-in (`nvngxReplacement`: spoofing and signature checks) and ignores `NvngxDlssPath`. The
  app's backups in `.dlssupdater\backup\` mirror the game's paths and keep the original file names, so this search finds them.
