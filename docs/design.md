# Design guideline

How every component in DLSS5 Updater is handled. New code follows this; code that doesn't is listed under "Known gaps"
and is brought in line when it is touched. AGENTS.md has the commands and coding conventions; this file has the
product rules they serve. When a rule here and the code disagree, fix the code or change the rule in the same PR.

## 1. Principles

- **One model for every component.** A mod, runtime or add-on is a *component*. Components differ in where builds come
  from and what config they have, not in how they are stored, chosen, installed, cleaned up or shown.
- **Never surprise the game.** Every file placed in a game is recorded; every file replaced is backed up; uninstall
  and switching leave the game as it was. Nothing changes in a game without a user action (install, update, update all).
- **Respect the authors.** Download only from a source the author publishes openly for that purpose. If the licence
  forbids redistribution, or the builds are paid, the component is import-only in public builds.
- **Say why.** When the app keeps, refuses, hides or skips something, the UI says why in one line.
- **Facts, not guesses.** Options, labels and help text come from the mod's own docs, config comments and release
  notes for that build. Undocumented values are marked undocumented (AGENTS.md, Conventions).

## 2. The component model

Each component has exactly these parts. A part that doesn't apply is explicitly "none", not missing.

| Part | Rule |
|---|---|
| Source | `Download` (official public source, or the catalog's hash-pinned or mirrored copy, verified), `Import` (user-supplied), or both. Import-only when the licence or a paywall requires it (section 3; one documented exception). |
| Library | Every build the app has is kept in that component's library under `components\<component>\<build id>\`, with a `version.json` (id, label, detected version, source = downloaded/imported, date, user label). One build = one folder holding the whole set of files. No single-file "current import" slots. |
| Build id | SHA-256 of the build's main file (downloads may use the release tag when the release publishes digests). Duplicate imports dedupe by id. |
| Order | Newest first by the component's version (tag, version resource or detected label; one `BuildOrder` implementation). Ties: newer date first. |
| Default | Settings hold the build by component id (`AppSettings.DefaultBuild(id)`: `<Component>Id` for the components from before 3.0, `Components[id].Build` for the rest); **null means "Newest"** and follows the newest build in the library automatically. Picking a build pins it; picking "Newest" unpins. The app never pins on its own. |
| Per-game pick | `GameOverride.BuildPick(id)` (`<Component>Id` or `Components[id]`), same semantics (null = follow the default). The game page shows the same picker for every component with more than one build: "Default (<label>)", then the builds. |
| Cleanup | Every library is in `BuildCleanup` with a keep count (default 2). Protected: the default, per-game picks, builds installed in a game, the runtime a game can switch back to. Automatic cleanup removes downloaded builds only; imported builds go only by hand. The UI shows why extra builds stay. |
| Install | One generic record per component in the game's install record (`InstallManifest.Components`, an `InstalledComponent`): component id, build id, version label, the files placed with the SHA-256 of what was placed, backups (original -> backup), config keys set (file, section, key, value, the previous value or absent), file attributes changed, and a string map for extras. `Installer.InstallComponentAsync` takes a `ComponentPlan` (the whole wanted set) and records each step before it is taken. Removal undoes exactly what the record lists, in reverse order (attributes, keys, files), originals restored; a file, key or attribute that changed since we set it stays as it is and is reported as changed by the user or the game, never deleted silently. Placed files and backups are also kept in the record-level `Files`/`Backups` lists, so an older version still restores the originals. |
| Config | If the app writes the mod's config: a `<Component>Config` with `TestedVersion` (+ schema when the mod has one), a compat report, merge rules shared with the ini merger, options defined once with help topics. Options a build doesn't know are hidden and not written. |
| Update check | Downloads: through `GitHubClient.GetApiAsync` (ETag cache) or the component's documented channel; offline falls back to cached data and never throws at the user. Import-only: no check in the app; the release bot watches the author's public channel if one exists. |
| Docs and tests | `docs/addons/<component>.md` (checked builds table, package layout, config diff per version, licence) and one test file per component: import/download, install/uninstall, config writing, compat detection. |

## 3. Sources and licences

- Public, openly licensed release (GitHub releases, official site): download, verify against the published digest.
  No digest = no install.
- Licence forbids redistribution, or builds are paid: **import-only** in the public build. The app accepts the files
  the user got from the author (the release folder, the main file, or a .zip of the release) and copies them byte for
  byte. No probes or reverse engineering where the licence forbids it (see `docs/addons/README.md`).
- **Exception, ShortFuse's DLSS add-on (user decision 2026-10-21):** `renodx-dlss.addon64` has no licence we can find and no
  official channel, but it is auto-pulled in the public build too, through the catalog. The catalog only **pins** it (the
  author's release URL plus the SHA-256 of each build, never a mirror copy); the app downloads the zip, refuses a hash mismatch,
  and takes the add-on out of it like an import (version resource checked, kept in the library). It is the project owner's decision
  that this add-on should arrive without a manual import, in the public build too: its builds appear every few days and the only
  channel is a community release list. Imports stay possible, a Settings switch turns the download off, and if the author objects
  or a licence appears, it is a catalog edit, not an app release. It is the only component that is both unlicensed and
  auto-downloaded.
- Owner-only conveniences that a public build must not have (today: fetching Lecram's paid builds from his channel)
  sit behind a compile-time switch (`-p:<Name>=true`, default off, `BuildFlags.<Name>`), are compiled in both variants
  and tested in both. Public releases build with the switch off; `tools/build-private.ps1` builds the private
  variant; the release bot drops it on copyparty after each release (`tools/release-bot/README.md`).
- NVIDIA's `nvngx_dlssnr.dll` is never bundled, uploaded or downloaded (AGENTS.md, Hard rules).

## 4. Choosing builds

1. The game's own pick, if set and still in the library.
2. Else the default: the pinned build, or the newest build when the default is "Newest".
3. Else nothing: the component can't be installed, and the game page says what to import or download.

A pinned build that was removed falls back to "Newest" and the settings are cleared, never re-pinned to another id.

## 5. UI rules

- Every component row on the game page: installed version, latest/default (with picker when the library has more
  than one build), switch. Same columns, same picker control for all components.
- Settings → Local components: one row per import-capable component, with the same buttons ("Import…" for a file or
  .zip, "Folder…" for a release folder where the mod ships as a folder) and an info line (imported version, tested
  version, compat warning).
- Settings → Libraries: one row per library: builds, size, keep count, and the reason line for builds kept beyond it.
- Text that depends on a build switch has one variant per build; no text claims a feature the running build doesn't have.
- Theme tokens only (`Theme/Nvidia.xaml`); every option has a short explanation and a help topic.

## 6. Adding or updating a component

A new component, or a new version of one, ships in one PR with:

1. The component model filled in (section 2), every part either implemented or explicitly "none".
2. Research notes and the checked-builds row in `docs/addons/<component>.md`; the config diff against the last
   tested build; `TestedVersion` raised only after the options match.
3. Options, help topics and per-build variants for the new or changed config keys.
4. Tests for import/download, install, uninstall, switching, config writing and version/compat detection.
5. README table row, release notes entry, and the release bot's watch list (`tools/release-bot`) when there is an
   upstream channel to watch.
6. The check command (`dotnet build -warnaserror && dotnet test`, plus `-p:<Switch>=true` builds for each build
   switch) passing.

## 7. Registering a component

A component recorded the generic way needs one registration and no per-component fields:

1. One line in `ComponentRegistry`'s list: `Register(new("<id>", "<Name>") { Library = ..., Label = ..., OwnedFiles = [...] })`.
   The id is stable (letters, digits, `-`, `.`, `_`): install records, `install-refs.json` and settings keep it.
   `Library` (if the component has builds) makes the cleanup protect its default, per-game picks and installed builds,
   and `KnownInstalls` remember them for unplugged drives. `Label` puts it in the game list's mod label.
2. Install: build a `ComponentPlan` (source -> destination files, config keys, files to lock read-only, extras) and call
   `Installer.InstallComponentAsync`; inside the main install run use `ApplyPlan` with its context. Installing again
   with another plan updates in place: what the new plan lacks is undone, a placed file the user changed is kept in
   `.dlssupdater\previous` before it is replaced.
3. Remove: `Installer.RemoveComponentAsync`; the game's uninstall removes it with everything else.
4. Settings: `AppSettings.DefaultBuild/SetDefaultBuild(id)` and `GameOverride.BuildPick/SetBuildPick(id)`, with on/off
   and option values in the same `ComponentPick` (`PickOf(id)`). Null build = follow the default / Newest.

Paths: files go into the game or the install folder. Config keys and attributes may also change an `.ini`/`.cfg` in
%LocalAppData% or Documents (`InstallManifest.KnownFolderTags`), for example UE's Engine.ini; nothing else outside.

**Record versions.** Schema 1 is every record up to 2.2.x; schema 2 adds `Components`. The components that existed
before 3.0 (OptiScaler, ReShade, MFG Unlock, NR runtime, DLSS, Streamline, ShortFuse, Lecram, DFC, GITC Uplift) still
store their per-component fields, and `LegacyComponents` reads them into the same `InstalledComponent` view, so code
that works by id sees them too. Downgrading to 2.2.x: it reads a schema-2 record, ignores `Components` and drops it on
its next save, but still deletes the placed files and restores the backups (both are also in the record-level lists).
It does not undo config keys or attribute changes of generic components. Fields a newer version wrote are kept
(`JsonExtensionData`), so a record never loses data this version doesn't know.

## 8. Known gaps (2026-10-21)

Measured against this guideline on main (after #63, #66 and #67). Each is fixed when its component is next touched, or in its own PR.

- **Defaults pin themselves**: RenoDX via `EnsureAddonDefaults`/`AdoptImportedAddon`; the NR runtime has its own
  legacy-first adoption rule (`NrLibrary`). Lecram and DFC follow "Newest", but a default the user pinned still moves
  to a newer import (`AdoptImportedAddon`); by section 2 a pin should stay until the user changes it.
- **Cache-only components**: DLSS, Streamline and MFG Unlock live in the download cache or as a single imported file
  in the components root, not in libraries; MFG Unlock imports replace each other. (ReShade has a library since 3.0;
  its install fields are still the pre-3.0 ones, see `docs/addons/reshade.md`.)
- **Two library implementations** scan builds the same way (`BuildLibrary` for downloads, `AddonLibrary`/`NrLibrary`
  for imports). Target: one library type with a source flag.
- **Verification differs**: GitHub digests, Uplift's `SHA256SUMS.txt` (skipped when absent), the MediaFire listing hash.
  Target: one verifier per source kind, and no install without a digest.
- **Per-component install fields**: the components from before 3.0 still keep their own fields in the install record
  (`Opti`/`OptiTag`, `DfcFiles`, `DfcCfg`, `LecramFile`, ...) and their own install and removal code in `Installer`;
  `LegacyComponents` only reads them. Each moves to `InstalledComponent` when it is next reworked; the per-component
  fields keep being written for one major version so 2.2.x can still read the record.
- **Compat checks differ** per component (`OptiCompat`, `UpliftConfig`, `DfcConfig`, `LecramIni`, ...). Target: one
  `ComponentCompat` shape (tested version, schema, missing keys, summary) that each config class fills.
