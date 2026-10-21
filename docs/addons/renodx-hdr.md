# RenoDX HDR mods per game (`renodx-hdr`)

HDR for games that have no HDR of their own, through RenoDX add-ons for ReShade. This is a different component from
[ShortFuse's DLSS add-on](renodx-dlss-sf.md) (`renodx-dlss`): another id, another library, and both can be installed in one game.
Component id `renodx-hdr` (`ComponentRegistry.RenoDxHdr`), recorded the generic way (`docs/design.md` section 7).

## Component model

| Part | Here |
|---|---|
| Source | Download only, from the catalog ([catalog.md](../catalog.md)): the mirror copy when the licence allows one, else the author's own URL with the catalog's hash. No hash = no install. No import. |
| Library | `components\renodx-hdr\<sha256>\<file>` + `version.json` (`HdrBuild`: id, file, arch, mod id and name, catalog version, first seen, url, licence). |
| Build id / label | SHA-256 of the add-on file. The label is the catalog's version, the date the build was first seen plus its short hash (`2026-10-21-48fa2f3`), because the authors' snapshot builds have none. |
| Order | Newest first by the catalog's first-seen date. The library groups by mod, so the cleanup keeps the newest builds of each mod (default 2). |
| Default | "Newest": the build the catalog lists now for the game's pick. The app never pins. |
| Per-game pick | The mod itself (see "Which mod a game gets"). The Unreal Extended switch is stored per game in `GameOverride.Components["renodx-hdr"].Options["ue-extended"]`. A per-game *build* pin (an older catalog build) is not offered in the UI yet; see Known gaps. |
| Cleanup | In `BuildCleanup` as `LibraryKind.RenoDxHdr`. Protected: builds installed in any game (also for games on unplugged drives, through `install-refs.json`), per-game and default picks. Downloaded builds may go in the automatic cleanup. |
| Install | `InstalledComponent` with the add-on file and its hash, the `reshade.ini` and `Engine.ini` keys with their previous value or absent, the Engine.ini attribute change, and extras (`signature`, `mod`, `kind`, `method`, `peakNits`, `engineIni`). |
| Config | The `[renodx]` keys, Engine.ini and the peak brightness, below. There is no `TestedVersion`: the add-ons are rolling snapshots, and the catalog's per-game method data is what is checked. |
| Update check | A build with another SHA-256 in the catalog, another mod, another method, or a changed peak brightness. Reads the cached catalog; offline nothing is requested and nothing throws. |
| Docs and tests | This page; `tests/DLSSUpdater.Tests/Hdr*.cs`. |

## Which mod a game gets

`HdrPicker` (pure, `Core/HdrPick.cs`) goes through these in order and stops at the first that applies:

1. **Anti-cheat detected:** nothing. No add-on is injected into such games; the page says so.
2. **Catalog blocklist** (a flagged entry, or a `blocklist` item naming the game or the entry): nothing, with the catalog's reason.
3. **Named mod** by `GameMatcher`: overrides, then Steam AppID, then the normalised name (see catalog.md). Its file by bitness
   (`.addon64` for 64-bit, `.addon32` only when the catalog has one; unknown bitness takes x64 first). A named entry without
   a download (Nexus or Discord only) gives nothing and links its page; it does not fall back to an engine add-on. An entry that
   upstream points at the generic Unreal or Unity add-on follows the engine path below. `compat: in-progress` is shown as a note.
4. **Unreal 4 and 5, no named mod:** Unreal Extended by default; the user's switch selects the plain generic Unreal add-on
   instead. A catalog `ueExtendedOptOut` makes the plain add-on the default for that game (the user can still switch Unreal
   Extended on, with the catalog's warning shown). Both Unreal add-ons are 64-bit only: a 32-bit Unreal game gets nothing.
5. **Unity, no named mod:** the generic Unity add-on in the game's bitness (`.addon32` only when the catalog has one).
6. **Unreal Engine 3 and older, RE Engine, unknown engines:** nothing, with the reason.

The engine, bitness and Unreal version come from the game scan (`GameInfo.Tech`). The wiki's per-engine tables
(`engineGames`) add the Unreal Extended method and a note per game; a game is matched by its normalised name, exactly or as
"contains" (six letters or more, with the sequel guard of `GameMatcher`).

## What an install does

1. The build is downloaded into the library (mirror first, then the author) and checked against the catalog's size and SHA-256.
2. ReShade must be in the game. If it isn't, it is installed with the installer's own code in ReShade + add-ons mode as the
   proxy for the game's API (`dxgi.dll` for DX10 to 12 and unknown, `d3d9.dll`, `opengl32.dll`; the game's saved proxy name wins).
   A Vulkan-only game, or a game with OptiScaler but no ReShade, is not installed into; the page says what to do.
   ReShade 6 or newer is required.
3. The add-on goes into the folder of `[ADDON] AddonPath` in `ReShade.ini` when that is set (first entry, relative to the folder
   ReShade sits in, a trailing wildcard dropped), else next to ReShade. A path outside the game falls back to the ReShade folder.
   A file already there is backed up and put back on remove. Other RenoDX add-ons in the folder are warned about; `renodx-dlss*` is not.
4. The keys below are written; every file change, key and attribute is recorded before it is made.
5. Remove undoes exactly that, in reverse: attributes, keys (previous value back, or the key removed; a section the app added
   goes with its last key; a file the app created goes), the add-on, the original file. Anything the user changed since stays
   and is reported.

## `ReShade.ini` `[renodx]` (facts)

Source for the add-on side: `src/games/ue-extended/addon.cpp` and `src/utils/settings.hpp` of
[marat569/renodx](https://github.com/marat569/renodx) (MIT, read 2026-10-21), which is the code behind `renodx-ue-extended.addon64`.
The other facts are in `docs/research/rhi-2026-10.md` section 3.1 (what the ecosystem writes, not code).

| Key | Meaning (C = from the add-on source) | App writes |
|---|---|---|
| `Set_Path` | C: 0 "Off: fix native or Engine.ini HDR", 1 "On: upgrade SDR to HDR"; default 1; global | Unreal Extended: `0` for the native and ini methods, `1` for upgrade. No wiki entry: UE4 and unknown Unreal version `1`, UE5 `0`. |
| `Upgrade_<FORMAT>` | C: 0 Off, 1 Output size, 2 Output ratio, 3 Any size; default 0; global | Empty placeholder; the one or two formats the wiki entry names get their mode (`1`/`2`/`3`). |
| `Upgrade_CopyDestinations` | C: 0 Off, 1 On; default 0 | `0` when the wiki entry says to switch it off. |
| `Upgrade_SwapChainCompatibility`, `Upgrade_UseSCRGB` | C: exist, global | Empty placeholder. |
| `ForceBorderless` | C: 0/1, default 1 | UE Extended `1`, generic: empty placeholder. |
| `PreventFullscreen`, `DumpLUTShaders`, `FxUpgradeRender` | Not read from source. These are the values the ecosystem uses (research section 3.1). | UE Extended: `1`, `0`, `1`. Generic: empty placeholders (no `FxUpgradeRender`). |
| `SettingsMode` | C: 0 Simple, 1 Intermediate, 2 Advanced; the upgrade settings are only visible from 1 or 2 | UE Extended: `2`. Generic: placeholder. |
| Unity-only placeholders | `Blit_Copy_Hack, DumpUberShaders, Force_Pipeline_Cloning, Scaling_Offset, Swapchain_Encoding, Tonemap_Offset, Use_Resource_Cloning, Use_Swapchain_Proxy`: from the research notes, not verified in the Unity add-on's source | Empty placeholders |

**Missing keys only.** A key counts as missing when the file doesn't have it or has it empty. A value the user set is never
overwritten. A key we wrote that the user changed afterwards is released: an update leaves it, a remove leaves it. Generic Unreal
and Unity get only the empty placeholders, so the keys show up in the file for the user to edit. Named mods get no `[renodx]`
keys at all (their config is their own).

Empty values: the add-on's `ReadFromConfig` falls back to the setting's default when ReShade's `get_config_value` returns false.
Whether ReShade returns false for a key with an empty value was not checked in ReShade's source; the ecosystem writes the same
placeholders, so it is assumed.

## `Engine.ini` for Unreal Extended

Only for the **ini** method (the wiki says "Engine.ini", or no wiki entry and Unreal 5). Not for native, upgrade, the
generic add-ons, UE4 without a wiki entry, or Unity.

- **Where** (`EngineIniLocator`): the catalog's per-game path (a folder or an `.ini`, relative to `%LOCALAPPDATA%`; only inside
  AppData, Documents or the game); else the first existing `%LocalAppData%\<Project>\Saved\Config\{WinGDK, Windows,
  WindowsNoEditor}` (also tried with the game folder's name); then `Documents\My Games\<Game>\Saved\Config\...`; then
  `<Project>\Saved\Config\...` inside the game and its sibling projects; if none exists, `%LocalAppData%\<Project>\Saved\Config\Windows`
  (`WinGDK` for a Game Pass layout) is created. `<Project>` is the folder above `Binaries`.
- **Keys** (research section 3.2): `[SystemSettings] r.AllowHDR=1, r.HDR.EnableHDROutput=1, r.HDR.Display.OutputDevice=3,
  r.HDR.Display.ColorGamut=2, r.HDR.UI.CompositeMode=1` and `[/Script/Engine.RendererSettings] r.LUT.UpdateEveryFrame=1`.
  A game whose wiki entry names its own keys (`engineIni` in the catalog, for example Black Myth: Wukong's single
  `r.HDR.EnableHDROutput=1`) gets those instead. Only missing keys are appended; existing lines, duplicates and comments are kept.
- **Read-only** is set on the file afterwards so the engine doesn't rewrite it; the attribute change is recorded and undone. A file that was
  read-only already stays read-only.
- **Exact restore**: line endings, a missing final newline and a UTF-8 byte order mark are kept through the write and the undo, so a remove gives
  back the original bytes (tested).

## Peak brightness

One global setting (Settings, nits, 80 to 10000, empty = off). When set it is written as `ToneMapPeakNits` into `[renodx-preset1]`,
`[renodx-preset2]` and `[renodx-preset3]` (C: the add-on's presets are the sections `renodx-preset1..3`, and `ToneMapPeakNits` is a
per-preset setting with default 203). It replaces what the presets hold (it is the user's own choice in this app), the previous
values come back on remove or when the setting is cleared, and changing it makes installed games show "settings changed" as an update.
Whether every named mod uses `ToneMapPeakNits` (it is RenoDX's common key) was not checked per mod.

## Unverified or undecided

- Empty placeholder values (above).
- `PreventFullscreen`, `DumpLUTShaders`, `FxUpgradeRender` values and the Unity placeholders (above).
- "Upgrade Path: Off" in the wiki notes is taken as the native method (`Set_Path=0`, no Engine.ini).
- Wiki notes are free text; the generator turns the ones it recognises into `method`, `upgrades`, `copyDestinations` and `engineIni`.
  A note it doesn't recognise leaves `method` empty, and the UE4-upgrade / UE5-ini defaults apply.

## Checked builds (catalog, 2026-10-21)

| Add-on | Source (licence) | Catalog version |
|---|---|---|
| `renodx-ue-extended.addon64` | marat569/renodx (MIT), mirrored | 2026-10-21-48fa2f3 |
| `renodx-unrealengine.addon64` | clshortfuse/renodx (MIT), mirrored | 2026-10-21-3250814 |
| `renodx-unityengine.addon64` / `.addon32` | NotVoosh/renodx-unity (MIT), mirrored | 2026-10-21-a284b29 / 2026-10-21-ec1bf09 |
| Named mods | each author's own URL; mirrored when its repository's licence allows | per file in `catalog.json` |

The mirror's release notes carry each mirrored file's licence and copyright notice (catalog repo README).

## Known gaps

- No per-game pin of an older catalog build in the UI; the core already tells the cleanup about picks (`GameOverride.BuildPick`).
- Vulkan-only games need ReShade's Vulkan layer (workstream D) and are not installed into.
- Games installed with OptiScaler but without ReShade are refused until ReShade is switched on in the main install.
- Unreal project folders and Engine.ini locations beyond the order above (custom launchers, Xbox `WindowsApps` paths that can't be read) fall back to the new-file path.
