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
| v0.1.37, v0.1.37.2, **v0.1.38** | `v0.1.37-Optiscaler-F5.zip`, `v0.1.37.2-Optiscaler-F5.zip`, `v0.1.38-Optiscaler-F5.zip` | yes (tested tag: v0.1.38) | new asset naming; tags can have four parts |

Other assets of a release stay unmatched on purpose: `Optional-Vendor-Neutral-DLSS5-Forwarder.zip` (see design.md, known gaps),
`Optional-F5Low-Update-1.1.0.184.zip` (an optional update of the F5Low plugin) and `OptiScaler-DLSSNR-Troubleshooting.md`.

## Zip layout (v0.1.38)

Same root as before: `OptiScaler.dll`, `OptiScaler.ini`, `nvngx.dll_dlssnr.dll` (the forwarder the app places beside it) and the
`OptiScaler\` folder, which `Installer.InstallOpti` mirrors as a whole. New since 0.1.36: `OptiScaler\plugins\f5low.dll` (the
F5Low frame generation) and an updated `OptiScaler\libxess_fg.dll`. Both are installed, recorded in the install record and
removed on uninstall like the rest of the folder (`InstallerTests`).

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

None of these exist in OptiScaler-NR, so they are in the default source's `UnsupportedKeys` and hidden there. Defaults and
ranges come from the 0.1.38 ini: `MultiViewScreens` 0 (a sum: 1, 2, 4, 8, ... per screen, 256 the combined view, +512 all screens
as one picture), `SecondaryFeatures` 0 (stand aside; 1 own model; 2 share the main model), `DepthFade` false, far 0.5 / 0.85 /
0.5, near 0.0 / 0.1 / 1.0, sky 1.0, all distances and strengths 0 to 1.

## Not exposed

- `NativeBlend` and `NativeBlendWas*`: owned by the OptiScaler menu, which stores the previous values of the setting it
  changes in the `Was` keys; writing them from here would fight the menu.
- `NativeDepth*` (depth finder, alignment, scale, offset, linearize and so on): debug and per-game depth alignment, done in the
  game with the menu's debug views.

## Compat check

`OptiCompat` warns "newer than tested" for a new major or minor version, and in a 0.x series (F5, OptiScaler-NR) also for a
higher patch number or fourth part, because those are real releases there.
