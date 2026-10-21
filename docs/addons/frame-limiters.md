# Frame limiters (`relimiter`, `display-commander`, `ue-frame-cap`, driver)

One frame limit per game, set by exactly one method. Facts below were checked on 2026-10-21 against the projects' own repositories
and Epic's engine files; nothing here comes from RHI.

## The four methods

| Method | Id | What it does | Where it is recorded |
|---|---|---|---|
| NVIDIA driver limiter | `driver` | Frame Rate Limiter V3 (`0x10835002`, 0 = off, 20 to 1000 in the driver) in the game's NVIDIA profile, through `NvidiaProfile.SetFrameRateLimit`. No file in the game, every graphics API, allowed with anti-cheat. | `NvidiaRecord` (previous value, `prev:10835002`) and the cap in the game's `frame-limit` pick (`driver` option) |
| ReLimiter | `relimiter` (`ComponentRegistry.ReLimiter`) | ReShade add-on for frame pacing on G-Sync / VRR displays. | Generic `InstalledComponent` |
| Display Commander | `display-commander` | ReShade add-on with an FPS limiter, window and display controls. | Generic `InstalledComponent` |
| Unreal `t.MaxFPS` | `ue-frame-cap` | One key in the game's Engine.ini. No file. | Generic `InstalledComponent` (config edit only) |

`FrameLimitService` brings a game to one method and takes the previous one away: add-on and Engine.ini cap through the installer's
component plan and `RemoveComponentAsync`, the driver value through `RestoreFrameRateLimit`. What can fail (download refused, no
32-bit build, anti-cheat, no Engine.ini) is checked before the previous limiter is removed. The active method is read back from the
install record (add-ons, Engine.ini) or the `driver` option (driver limiter), so a record edited by hand can't make two active.

**Driver limit and add-ons.** While ReLimiter or Display Commander is installed, a driver limit that would cap the game (a value of its
own in the game's profile, or a global limit) is set to 0 in the game's profile, because two limiters fight over the same frames
(ReLimiter's own troubleshooting says to check that "no other frame limiter is active (... NVIDIA driver limiter)"). Removing the
add-on puts the profile back to what it held before. Nothing is written when no limit would apply (the game gets no new profile).
The Unreal cap does not touch the driver.

## Component model (docs/design.md section 2)

| Part | ReLimiter / Display Commander | Unreal cap, driver |
|---|---|---|
| Source | Download from the catalog (`frameLimiters` section, `CatalogDownloader`, SHA-256 and size checked, no hash = no install) **and** import (below). | None: no files. |
| Library | `components\frame-limiters\<limiter>\<sha256>\<file>` + `version.json` (`LimiterBuild`). One build = one file, so the 64-bit and the 32-bit file are two builds. `LibraryKind.FrameLimiter`, both limiters in one library; the cleanup groups by limiter and bitness. | None. |
| Build id / label | SHA-256 of the file. Label: ReLimiter the release tag without `v` (`3.3.5`); Display Commander republishes the tag `latest`, so the label is the catalog's first-seen date plus short hash. Imports show the file's version resource, else `imported <hash>`. | The cap (`60 FPS`). |
| Default | Automatic ("Auto (3.3.5)"): the catalog's current build for the game's bitness, downloaded when the library lacks it; offline, the newest build in the library. A pin by build id is `GameOverride.BuildPick(<limiter>)` (game page "Build for this game") or `AppSettings.DefaultBuild`. A pin that left the library falls back to Auto with a note. There is no default picker in Settings: the build is chosen per game. | |
| Per-game pick | The method (`GameOverride.Components["frame-limit"].Options["method"]`, absent = Auto), the cap (`fps`), ReLimiter's OSD (`osd`: `on` / `off`, absent = leave the add-on's own), the user's extra ini lines (`ini`) and the build (`BuildPick`). The Settings default method and cap are the same options in `AppSettings.Components["frame-limit"]`. | Same. |
| Cleanup | In `BuildCleanup`; protected: default, per-game picks, builds installed in a game (also on unplugged drives, `install-refs.json`). Downloaded builds may go automatically, **imported builds only by hand** (Settings → Frame limit → Remove). | |
| Install | `InstalledComponent`: the add-on file + hash, the ini keys with their previous value or absent (file/section created flags), extras (`method`, `fps`, `osd`, `userIni`, `iniFile`). Switching and removal undo exactly that. | Config keys of Engine.ini (file created, section created, previous value). |
| Config | Below. | Below. |
| Update check | The add-on's build differs from what Auto (or the pin) resolves to now: "Update" on the game page and in Update all (re-applied without overwriting a key the user changed in the game's own panel). | None. |
| Docs and tests | This page; `tests/DLSSUpdater.Tests/FrameLimit*.cs`; catalog repo `FrameLimitersGeneratorTests`. | |

## Sources and licences (checked 2026-10-21)

| | ReLimiter | Display Commander |
|---|---|---|
| Repository | `RankFTW/ReLimiter` | `pmnoxx/display-commander` |
| Licence | **MIT** (GitHub licence API and LICENSE) | **None found.** The README says "distributed under the terms in LICENSE", but the repository has no LICENSE file, GitHub reports no licence. |
| Catalog | Mirrored (permissive licence), upstream URL kept. | **Pinned**: the author's URL plus SHA-256, never a mirror copy; the app refuses a changed file until the catalog Action refreshes the hash. |
| Release | Tagged releases (`v3.3.5`, ...) with `relimiter.addon64` and, from v3.3.1, `relimiter.addon32` (earlier notes said 64-bit only since 3.0; that changed with 3.3.1, see its CHANGELOG "32-bit (New)"). Releases carry GitHub digests. | The rolling release tag `latest` (rebuilt on every push to main) with `zzz_display_commander.addon64` / `.addon32` (plus `latest_debug` with PDBs, not used). |
| Files | `relimiter.addon64`, `relimiter.addon32` | `zzz_display_commander.addon64`, `zzz_display_commander.addon32` |

The Display Commander licence situation is a decision for the project owner (design.md section 3 allows auto-download only for
openly licensed releases and for the ShortFuse exception). It is handled like the ShortFuse add-on: the app downloads from the author's own
release URL, checked against the catalog's hash, and never redistributes a copy. If the author objects or adds a licence, it is a catalog edit.

## Import (design.md: every downloadable component can also be imported)

Settings → Frame limit → per limiter: **Import…** (one add-on file or a `.zip` of the release) and **Folder…** (a folder holding the
files, searched three levels down). Accepted names are the four above. Each file must be a PE file of the bitness its name says. It is
copied byte for byte into the library as an **import** (never removed by the automatic cleanup). A downloaded build with the same hash
becomes an import. A picked imported build beats the catalog's (Auto never picks an import while the catalog is reachable).

## Config the app writes (only documented keys)

| File | Section / key | Value | Source |
|---|---|---|---|
| `relimiter.ini` beside the add-on file (the ReShade `AddonPath` folder when set) | `[FrameLimiter] target_fps` | the cap, 30 to 1000 | ReLimiter USER_GUIDE, "INI Configuration Reference" (0 = off, 30 to 1000) |
| same | `[FrameLimiter] osd_enabled` | `true` / `false`, only when chosen in the app | same table (`osd_enabled`, bool). The toggle key `osd_toggle_key` exists but is not written. |
| `DisplayCommander.ini` beside the exe | `[DisplayCommander] fps_limit` | the cap | Display Commander's `default_settings.toml` template: "FPS limiter: fps_limit = target FPS (0 = unlimited ...). Requires fps_limiter_enabled = true", for keys "that match Display Commander config" and "when a game's DisplayCommander.ini does not contain a setting"; the same names in `main_tab_settings.cpp` (`DisplayCommander` section). |
| same | `[DisplayCommander] fps_limiter_enabled` | `true` | same template (`fps_limiter_enabled = true`) |
| the game's `Engine.ini` (`EngineIniLocator`, the catalog's `engineIniPaths` hint first) | `[SystemSettings] t.MaxFPS` | the cap | Epic's `Engine/Config/ConsoleVariables.ini` header: "Console variables also can be set in engine ini files (e.g. BaseEngine.ini, DefaultEngine.ini) in the [SystemSettings] section."; the cvar's help text: "Caps FPS to the given value. Set to <= 0 to be uncapped." (Epic's console variable reference documents `[ConsoleVariables]` of `DefaultEngine.ini` for projects; packaged games read the user's Engine.ini, where `[SystemSettings]` is what the engine applies, and what the HDR component already uses.) |

Anything else is **undocumented and not written**: ReLimiter's background cap, VSync mode, Display Commander's mode and background
keys, window settings, DisplayCommander's OSD. The game page's "Extra ini lines" lets the user add any key; they are written after the
app's own keys (the user's value wins), recorded with their previous values and put back on removal. Keys are written like all component
config: previous value recorded, a key that changed since stays on removal, a file the app created goes with its last key; a read-only
Engine.ini is written and stays read-only. An explicit **Apply** replaces a value the user set in the game's own panel; an **Update**
(the add-on build changed, Update all) leaves such a value alone.

## Recommendation hint

From `docs/research/methods-2026-10.md` section 8: with frame generation active (DLSS-G, FSR or XeSS frame generation files in the game)
the page shows "a cap of refresh rate ÷ frame generation factor" for the primary display's refresh rate (factor 2, higher factors
explained in the text). It is only shown; no value is applied.

## Anti-cheat and bitness

- Anti-cheat games: the add-ons are refused with the reason; the driver limiter and the Engine.ini cap stay available (nothing is injected).
- 32-bit games: ReLimiter 3.3.1+ and Display Commander have a 32-bit file; if the catalog has none (older ReLimiter releases) the add-on is
  refused with "has no 32-bit build". ReShade32 is installed first like for the HDR component.
- Both add-ons need ReShade 6 with add-on support; `ReShadeAddonHost` (shared with the HDR component) installs it when the game has
  none, and refuses beside OptiScaler without ReShade, or for Vulkan games without the layer.
