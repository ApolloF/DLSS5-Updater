# ReShade add-ons, shader packs and presets

What the app places next to ReShade in a game, besides ReShade itself (`docs/addons/reshade.md`). The lists come from the
catalog repository (`addons` from crosire's `Addons.ini`, `shaderPacks` from a list we keep in that repository); the user can
import their own. Checked 2026-10-21.

## Component model (docs/design.md section 2)

| Part | Add-ons / shader packs / preset |
|---|---|
| Source | Catalog download (mirror first, else the author's URL, SHA-256 verified before anything is unpacked) or the user's own import. |
| Library | Verified downloads are unpacked once into `cache\reshade-extras\<sha256>\` (safe to delete). Imports live in `components\reshade-extras\imported\{addons,packs}\<name>\` byte for byte and are never cleaned up automatically; presets in `reshade-presets\<name>.ini` under the data folder. |
| Default | Add-ons: none. Shader packs: the catalog's `default` packs (HDR essentials: Lilium, PumboAutoHDR, RenoFX) until the user ticks something; the app-wide choice is `AppSettings.Components["reshade-shaders"].Options["packs"]`. Presets: none. |
| Per game | Own list under `GameOverride.Components[...]` (same option keys, plus `bits` for a manual 32/64 choice). A game with no key follows the app-wide choice (Auto); an empty value means none; Reset to auto removes the key. |
| Cleanup | The cache can go; imports stay until the user removes them. |
| Install | Three generic components (`reshade-addons`, `reshade-shaders`, `reshade-preset`), each a plan of the whole wanted set (`Installer.InstallComponentAsync`); removal undoes exactly what the record lists. `Extras` keeps `want:<id>` (hash token of what the catalog offers), `placed:<id>` and `bits`/`folder`/`preset`+`sha` for update checks. |
| Config | Only ReShade.ini keys: `[GENERAL] EffectSearchPaths` / `TextureSearchPaths` (shader folder added to the existing list, or to the template's value when the key is missing) and `PresetPath`. A value the user set for those keys in the per-game (or global) ReShade overrides wins and is not written. |
| Update check | `ReShadeExtras.ComponentsToUpdate` compares the record with the selection and the catalog hashes; only components the game already has are listed; Update all runs them after the main install. |
| Docs, tests | This page; `ReShadeAddonTests`, `ShaderPackTests`, `ReShadePresetTests`, `ExtrasLibraryTests`, `ReShadeLayoutTests`, `ExtrasPicksTests`, `ZipStageTests`, `ReShadeExtrasViewModelTests`. |

## Rules

- **Nothing in games with anti-cheat**, and nothing without ReShade in the game (`manifest.ReShade` or a `ReShade.ini`).
- **Add-on folder**: `[ADDON] AddonPath` of the game's ReShade.ini (one path, relative to the ini), else the install folder; a path
  outside the game is refused. 64-bit games get `.addon`/`.addon64`, 32-bit games `.addon32`; the bitness is the exe's unless
  the game has a manual choice.
- **Effects of an add-on** go below the shader folder; the search path edit is added when missing.
- **Shader folder**: `<install folder>\reshade-shaders` with the marker `.dlss5updater-shaders.json` (placed through the plan, so
  tracked). A folder that exists without our marker and holds files we didn't place is the user's: it is not touched; packs go to
  `reshade-shaders-managed` and both are on the search paths. The UI says so.
- **Pack files**: Shaders and Textures keep their layout; hidden and executable files are never placed; the first pack in catalog
  order wins a shared file (identical copies are fine, a different one is reported); files another component owns are skipped.
- **Add-on conflicts**: an add-on with a file another add-on or component owns is left out whole, with the reason.
- **Managed elsewhere**: RenoDX, Display Commander, ReLimiter, Deep Fried Chicken, Lecram, Uplift and DLSS5 Feeder are listed
  (`managedBy`) but never installed from here.
- Zips are unpacked by `ZipStage` (entries stay below the folder, size and count caps).

## Presets

Valid = a root-level `Techniques=` line with at least one `Name@File.fx`. Import copies the file into the library; applying copies
it to `<install folder>\reshade-presets\<name>.ini` and sets `PresetPath=.\reshade-presets\<name>.ini`. The shaders a preset needs
are its `File.fx` names minus the `.fx` files found in the game's search folders; packs that hold them are suggested greedily
(`ReShadePresets.SuggestPacks`), and the game page can tick and install them.
