# DLSS Updater

Windows app that keeps an OptiScaler-NR + ReShade + DLSS setup current across all your games.

It pulls the latest releases of:

- [OptiScaler-DLSSNR-PreSR-Multipass](https://github.com/ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass/releases) (standard package, prereleases included)
- [GITC Uplift](https://github.com/ghostinthecamera/GITC-Uplift/releases) (MIT ReShade DLSS-NR add-on, 64-bit; exclusive alternative to OptiScaler-NR, ShortFuse, Lecram and DFC)
- [MFGAdaUnlock-RenoDx](https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases) (`renodx-mfgunlock.addon64`)
- [NVIDIA DLSS](https://github.com/NVIDIA/DLSS) runtime DLLs: `nvngx_dlss.dll` (SR), `nvngx_dlssd.dll` (RR), `nvngx_dlssg.dll` (FG)
- [NVIDIA Streamline](https://github.com/NVIDIA-RTX/Streamline) (optional, signed `sl.*.dll` set for Dynamic MFG)
- [ReShade](https://reshade.me) with full add-on support (`ReShade64.dll` taken from the official setup)

and installs them next to each game's executable, together with your own `nvngx_dlssnr.dll`. Uplift puts that runtime in `GITC-Uplift\nvngx_dlssnr.dll` so NVIDIA DLSS cannot load a game-folder copy first.

> **Single-player games only.** Replacing or injecting DLLs can trigger anti-cheat in online games. DLSS Updater flags games with known anti-cheat (EAC, BattlEye, GameGuard, …) and asks before touching them, but no check is complete. For online games, use the DLSS override in the NVIDIA app instead.
>
> Not affiliated with or endorsed by NVIDIA. NVIDIA and DLSS are trademarks of NVIDIA Corporation, used here only to describe compatibility. Everything is downloaded from the official sources listed above; nothing from NVIDIA is bundled.

## Download

Each [release](https://github.com/ApolloF/dlssupdater/releases/latest) has two builds of the same app:

- **`DLSSUpdater-Setup-<version>.exe`**: installer. Installs for your user without admin rights (or for all users), adds a Start menu entry and an optional desktop icon, and has an uninstaller. Run a newer setup to update.
- **`DLSSUpdater.exe`**: portable. A single self-contained exe; put it anywhere and run it.

Both keep settings and downloads in `%LocalAppData%\DLSSUpdater`, so you can switch between them. Uninstalling keeps that folder and also removes a stale Seaglass add-on link left by older versions if it pointed at the installed copy.

## What an install does

1. Finds the real game exe (Unreal `*-Win64-Shipping.exe`, Unity players, etc.; you can pick another folder).
2. Copies `OptiScaler.dll` as the chosen proxy (`dxgi.dll` by default) plus its `OptiScaler\` backend folder.
3. Builds `OptiScaler.ini` from the release ini plus your settings and patches `ReShade.ini` (keybinds, MFG Unlock options). Out of the box nothing is changed from what the upstream projects ship, except `LoadReshade=true` when ReShade is installed and `[DlssNr] Enabled=true` when the DLSSNR runtime is installed (both default to off upstream).
4. Places `ReShade64.dll`, `renodx-mfgunlock.addon64` and `nvngx_dlssnr.dll` beside it.
5. Replaces every `nvngx_dlss*.dll` in the game with the latest NVIDIA build, or with a pinned version (globally or per game, also downgrading). Games that ship no DLSS have this off by default; OptiScaler still gets an `nvngx_dlss.dll` so it can offer DLSS.
6. Optionally replaces all `sl.*.dll` the game ships with one matching Streamline release (never mixed, never adds plugins).
7. Moves anything it replaces into `.dlssupdater\backup` next to the exe. **Uninstall** and **Restore DLSS** put the originals back.

Existing configs (Settings → General): **Keep game settings** (default: values the app wrote follow your settings, anything the game had or you changed in-game stays), **Apply app settings** (your settings overwrite the same keys everywhere) or **Fresh config** (OptiScaler.ini rebuilt from the release; old one backed up).

Settings also pre-configures DLSSNR (passes, model resolution, style, strengths) and its HDR tone mapping (curve, white point, paper white, highlight protection, max brightening) plus OptiScaler's HDR output. Every option has an ⓘ tooltip and a guide entry on the About page that explains each dropdown choice.

Settings has key-capture fields for the OptiScaler hotkeys (menu, FPS overlay, frame generation, DLSSNR) and ReShade (overlay, effects, screenshot, reload).

**Install mode**: OptiScaler-NR (default, the main purpose) or **ReShade + add-ons**, which installs ReShade itself as the proxy (`dxgi.dll`, `d3d12.dll`, `d3d11.dll`, `d3d9.dll`, `dinput8.dll` or `opengl32.dll`) with MFG Unlock and the DLSS / Streamline updates, without OptiScaler or DLSSNR. Set the default in Settings and override it per game; switching removes the other mode's files.

**Presets**: save the whole OptiScaler / ReShade configuration (options, keybinds, overrides) under a name, load it, or assign it to single games. The built-in **Recommended (ApolloF)** preset holds the tuned setup (DLSS upscaler, preset override, overlay on **Del**, DLSSNR strengths and HDR tone mapping, ReShade tutorial skipped). **Reset to defaults** goes back to the upstream defaults.

**Config check**: before installing a new OptiScaler-NR release the app compares its OptiScaler.ini with every key it manages and warns when keys disappeared or the release is a newer major line than tested.

**NVIDIA profile** (per game only; the global driver profile is read, never written): DLSS SR / RR / FG preset overrides, DLSS render resolution, multi frame generation count, Smooth Motion, RTX HDR, RTX Dynamic Vibrance, FPS limiter, VSync and power mode, written straight to the driver profile via NvAPI like NVIDIA Profile Inspector. Nothing is loaded until the tab is opened. The scan detects which DLSS features a game ships (SR, RR, FG, Reflex, from `nvngx_*` and Streamline `sl.*` files); the tab lists them, moves presets for missing features into a collapsed group, and suggests Smooth Motion for games without FG. The game list shows RR / FG / Reflex badges.

**What's new**: when a component has a newer release than the last one you looked at, its version in the header gets a green dot. Click any version for a short summary of that release (headline and first points of its notes) and a link to the full release notes; closing it clears the dot. Nothing is flagged on the first check, only releases that arrive later.

**Update all** re-applies everything to each managed game after a new release or after a game patch reverts the DLLs.

Games are discovered from Steam, Epic, GOG, EA, Ubisoft and `XboxGames` folders. Standalone games can be added one by one or as a library folder whose subfolders are games.

Games with anti-cheat (EAC, BattlEye, GameGuard, …) are flagged and need an extra confirmation before anything is injected. DLSS-only swaps work without it.

## First run

`nvngx_dlssnr.dll` can't be redistributed. Put it next to `DLSSUpdater.exe` on first launch, or import it under **Settings → Local components**. It's stored in `%LocalAppData%\DLSSUpdater\components`.

ReShade is downloaded from reshade.me on the first install that needs it (add-on build, cached per version). To use your own build, import `ReShade64.dll` the same way; **Use download** switches back.

## Seaglass add-on

Seaglass add-on support lives on the [`feature/seaglass-addon`](https://github.com/ApolloF/dlssupdater/tree/feature/seaglass-addon) branch until Seaglass ships add-on support. Releases of DLSS Updater no longer include it.

## Support

DLSS Updater is free. If it's useful to you, [sponsor it on GitHub](https://github.com/sponsors/ApolloF) or leave a one-off tip on [Ko-fi](https://ko-fi.com/apollof).

## Build

```
dotnet test
dotnet publish src/DLSSUpdater -c Release -o publish
```

Produces a single self-contained `publish\DLSSUpdater.exe` (.NET 8, WPF), the portable build. The installer wraps it with [Inno Setup](https://jrsoftware.org/isinfo.php):

```
iscc /DAppVersion=1.10.0 installer\DLSSUpdater.iss
```

Tagging `v*` (matching `<Version>` in the csproj) builds both on GitHub Actions and attaches them to a release.

`DLSSU_INTEGRATION=1` enables a test that downloads the real releases (including ReShade from reshade.me) and installs them into a temporary folder.

## License

DLSS Updater is released under the [MIT License](LICENSE). The third-party files it downloads and installs (OptiScaler, MFG Unlock, NVIDIA DLSS and Streamline, ReShade) and your own `nvngx_dlssnr.dll` keep their own licences.

## DLSS 5 / NR library and maintained sources

Import each `nvngx_dlssnr.dll` under **Settings → Local components**. Every build is kept side by side in `%LocalAppData%\DLSSUpdater\components\dlssnr`, including builds with the same version but different contents. Version numbers come from the DLL version resource; SHA-256 identifies the build. Existing single-file imports are retained automatically. NR is never downloaded or distributed by this app.

Choose the global NR default in Settings, or choose a version on a game and press **Switch NR**. **Switch back** restores the previous game file; **Switch default back** restores the previous default. Changes to the default apply on the next install or update for games following it. Full uninstall still restores the game's original files. Downloaded DLSS SDK versions are also retained and available in the DLSS version picker, including offline.

OptiScaler-NR now comes from [ShyVortex's maintained continuation](https://github.com/ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass). Existing installs, cached releases and settings remain usable. The [janblade F5 fork](https://github.com/janblade/OptiScaler-F5-DLSSNR-Multipass) is an advanced alternative described in the research report; it is not an automatic source in this release.

The optional **RenoDX DLSS add-on** is ShortFuse's `renodx-dlss.addon64` (neural rendering for ReShade). It isn't published on any official GitHub, so it is import-only: import it under **Settings → Local components** (row "ShortFuse RenoDX DLSS"); only a file that names itself `renodx-dlss.addon64` is accepted. Every import is kept as its own build in `%LocalAppData%\DLSSUpdater\components\renodx-dlss` (importing the same file again changes nothing, and **Remove** deletes just the selected build): pick the default build in Settings, a build per game on its Game config tab, and give a build your own name with **Save name**. It works in ReShade-only mode (it replaces OptiScaler's NR, so the two never run together), with ReShade 6+ (full add-on support), an imported NR runtime and a game with native DLSS. The installer keeps other early-loaded add-ons, adds the add-on to `LoadFromDllMain`, moves older `renodx-dlssfix` add-ons (and Lecram's files, unless you install Lecram's add-on too) out of the way (restored on uninstall), and writes the options you pick in Settings to `[RENODX-DLSS]` in `ReShade.ini`. With OptiScaler-NR v0.9+, don't combine its built-in MFG unlock (`AdaMfgUnlock` / `AmpereMfgUnlock`) with the MFG Unlock add-on; the installer refuses that.

The optional **Lecram DLSS5 add-on** (`renodx-dlss5.addon64` for v10 and older, `dlss5-nr.addon64` for v11 and newer) can also be imported by hand under **Settings → Local components** (row "Lecram DLSS5 Neural Rendering"); v10 is recognised by its version resource, v11 (which has none) by strings in the file. Lecram's builds carry no public version name, so each one is labelled by the highest `vX.Y.Z` string inside it (for example `v8.5.0-rc10-model-sdr1` for the 0928 build) and you can name it yourself. All imported builds are kept side by side in `%LocalAppData%\DLSSUpdater\components\lecram-dlss5`, both generations included, with a default in Settings and an optional pick per game. It has the same requirements as the RenoDX add-on, can run next to it (the app then sets `ForeignNr=observe` in `[RenoDX.DLSS5]`, and warns when the ShortFuse build a game uses is newer than the tested 0.2026.0928), and replaces OptiScaler-NR and Deep Fried Chicken. Its options appear in the ReShade add-ons tab; the ones the build's generation doesn't have are hidden, per game too. v11 builds are also fetched automatically from the author's public MediaFire channel (the one his installer uses) on every update check: the Setup exe is opened as an archive, never run, its add-on is checked against the listed SHA-256 and added to the library (the newest becomes the default, games are not switched). Turn this off or use **Check now** in the same row. The author's own installer (DLSS5 NR Setup, from v11 Patch 4) finds the add-on in every game and its Update all replaces it there, so manage a game with either that installer or this app, not both.

The optional **Deep Fried Chicken** add-on is import-only as well (personal-use licence). Under **Settings → Local components** (row "Deep Fried Chicken") pick `deep-fried-chicken.addon64` from the extracted package's `64-bit` folder: the NGX bridge, the default cfg and, from 4.x, `deep-fried-chicken-present-support.dll`, the `deep-fried-chicken-neural` folder and `reshade-shaders\Shaders\DFC_OutputState.addonfx` come along, and a new import replaces the old set as a whole. Installs (ReShade-only mode, ReShade 6+, imported NR runtime) place that whole set beside the game exe, add `.\reshade-shaders\Shaders\**` to ReShade's `EffectSearchPaths` when nothing covers that folder, and replace RenoDX, Lecram and OptiScaler-NR; uninstall or switching away takes every file out again. DFC 2.x needs a game with native DLSS. DFC 4.x also installs into games without their own DLSS: it then runs NR on its own (or an external ReShade provider's) motion vectors, without DLSS upscaling, and the app adds `nvngx_dlss.dll` from NVIDIA's DLSS release beside the exe, plus `nvngx_dlssg.dll` when you tick the game's DLSS-G frame generation toggle (frame generation itself is set in DFC's overlay; it needs borderless or windowed mode, and DLSS-G an RTX 40/50 GPU). DFC's 32-bit route is not supported. Details: [docs/addons/deep-fried-chicken.md](docs/addons/deep-fried-chicken.md).

Single `renodx-dlss.addon64`, `renodx-dlss5.addon64` and `dlss5-nr.addon64` files from earlier versions move into the libraries on first start and become the defaults.

[ShortFuse's openNR](https://github.com/clshortfuse/openNR) describes the FP16 compatibility builds for RTX 20/30/40, including **compat-fp16-310.9.16-cuda13.4.3**. Download or build the file yourself, extract `nvngx_dlssnr.dll`, and import it. Known stock/compat hashes for 310.8.0 and 310.9.16 are labelled; other builds are marked unverified. A matching version alone does not establish hardware compatibility.
