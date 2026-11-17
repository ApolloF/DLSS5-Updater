# HSF (preview)

Capt-Hunter's "universal frame generation": a closed-source ReShade add-on that feeds NVIDIA DLSS Frame Generation into games that
have none (DirectX 9, 11 and 12). This file is built **only** from the package's README.txt, its file layout and its licence. No
binary of it was opened, dumped, disassembled or run, and none will be.

## Licence (summary of HSF Private Licence 1.0)

- Closed source, access by the owner's invitation only, personal to the person invited.
- With a build you may install it and use it on your own computers for your own play and testing.
- You may not share, publish, upload or send a build (or the source) to anyone, including mirrors and reuploads, and may not sell it.
- The NVIDIA Streamline and DLSS Frame Generation files in the package keep their own licences (the package's `licenses\` folder).
- The owner says never to use it in a game with online anti-cheat.

So the app never downloads, mirrors or bundles HSF, and the catalog has no entry for it. It is **import only**: the user imports the
release they were given and the app copies the files byte for byte into that user's own games, which is the use the licence allows.
No update check (there is no public channel to watch).

## Checked builds

| Release | Checked | Notes |
|---|---|---|
| 0.9.4 (preview) | 2026-11 | layout and README only |

## Package layout (0.9.4)

```
hsf.addon64, hsf.addon32
HSF_streamline\  hsf-dlssg-helper64.exe, nvngx_dlssg.dll, sl.common.dll, sl.dlss_g.dll, sl.interposer.dll, sl.pcl.dll, sl.reflex.dll
reshade-shaders\Shaders\HSF.fx
README.txt, LICENSE.txt, licenses\
```

## What the README says (facts the app relies on)

1. ReShade 6.8 with full add-on support in the game folder.
2. `hsf.addon64` for a 64-bit game, `hsf.addon32` for a 32-bit game, copied into the game folder.
3. The `HSF_streamline` folder into the game folder; 32-bit games need it too.
4. The `reshade-shaders` folder into the game folder (it adds `HSF.fx`).
5. DirectX 9/11 games, and DirectX 12 games without DLSS, need a flow source the user gets: iMMERSE LaunchPad, LumeniteFX Kernel or GITC Uplift.
6. No `HSF.ini` is needed. DirectX 12 games restart once when HSF's menu asks.
7. RTX 40 or newer; RTX 20/30 through MFG-Unlocker, installed by the user.
8. Never use it in online games with anti-cheat. (Display mode: no exclusive fullscreen.)

## What the app does

| Part | Handling |
|---|---|
| Source | Import only: a release `.zip` or the extracted folder (Settings -> Local components -> HSF: Import... / Folder...). A folder holding the release, or `hsf.addon64` itself, also works. |
| Library | `components\hsf\<id>\pkg\` with `hsf.addon64`, `hsf.addon32`, `HSF_streamline\` and `reshade-shaders\` (when present) copied byte for byte, plus a `.complete` record. README.txt, LICENSE.txt and `licenses\` are not kept. |
| Layout check | `hsf.addon64` and `HSF_streamline\sl.interposer.dll` must be there; anything else is refused with the reason and leaves nothing behind. `hsf.addon32` and `reshade-shaders` are kept when present. |
| Build id | SHA-256 of `hsf.addon64` (importing it again replaces that build). |
| Label | The dotted version in the zip's or folder's name (`HSF-0.9.4.zip` -> "0.9.4"), else "imported". |
| Order, default, picks | Newest first by label; installs always use the newest import. No default or per-game pick, no build picker. |
| Cleanup | Imports are never removed automatically; only by hand in Settings. |
| Install | In ReShade add-on mode. `Installer.InstallComponentAsync` with a `ComponentPlan`: the add-on matching the game's bitness (`hsf.addon32` for 32-bit; `hsf.addon64` for 64-bit and for a game whose bitness is unknown, which the game page says) into ReShade's add-on folder (`AddonPath` in ReShade.ini, else beside the exe), `HSF_streamline\` and `reshade-shaders\` beside the exe. ReShade is installed first when the game has none (not beside OptiScaler) and must be 6.8 or newer. Games with anti-cheat are refused. |
| Record | `InstallManifest.Components` entry `hsf` with the build id, version label, every file with its SHA-256, backups of anything replaced and `bits` in the extras. |
| Remove | `Installer.RemoveComponentAsync` undoes exactly the record and puts originals back. |
| Config | None (no `HSF.ini`). |
| Advisor | "HSF frame generation (preview)", advice only, shown only once a release is imported and only for a game with no frame generation of its own. Its needs list the flow source (GITC Uplift suggested), MFG Unlock on RTX 20/30 and borderless or windowed mode. The existing anti-cheat caution applies. Never picked for you. |

The app does not install a flow source or MFG Unlock for HSF; GITC Uplift and the MFG Unlock add-on are installed from their own rows.
