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
| Source | `Download` (official public source, verified), `Import` (user-supplied), or both. Import-only when the licence or a paywall requires it (section 3). |
| Library | Every build the app has is kept in that component's library under `components\<component>\<build id>\`, with a `version.json` (id, label, detected version, source = downloaded/imported, date, user label). One build = one folder holding the whole set of files. No single-file "current import" slots. |
| Build id | SHA-256 of the build's main file (downloads may use the release tag when the release publishes digests). Duplicate imports dedupe by id. |
| Order | Newest first by the component's version (tag, version resource or detected label; one `BuildOrder` implementation). Ties: newer date first. |
| Default | Settings hold `<Component>Id`; **null means "Newest"** and follows the newest build in the library automatically. Picking a build pins it; picking "Newest" unpins. The app never pins on its own. |
| Per-game pick | `GameOverride.<Component>Id`, same semantics (null = follow the default). The game page shows the same picker for every component with more than one build: "Default (<label>)", then the builds. |
| Cleanup | Every library is in `BuildCleanup` with a keep count (default 2). Protected: the default, per-game picks, builds installed in a game, the runtime a game can switch back to. Automatic cleanup removes downloaded builds only; imported builds go only by hand. The UI shows why extra builds stay. |
| Install | One `Installer.Place<Component>` that places the whole build set; the manifest records the component's build id, tag and its file list (`<Component>Files`), before each copy. Removal = exactly those files, originals restored. |
| Config | If the app writes the mod's config: a `<Component>Config` with `TestedVersion` (+ schema when the mod has one), a compat report, merge rules shared with the ini merger, options defined once with help topics. Options a build doesn't know are hidden and not written. |
| Update check | Downloads: through `GitHubClient.GetApiAsync` (ETag cache) or the component's documented channel; offline falls back to cached data and never throws at the user. Import-only: no check in the app; the release bot watches the author's public channel if one exists. |
| Docs and tests | `docs/addons/<component>.md` (checked builds table, package layout, config diff per version, licence) and one test file per component: import/download, install/uninstall, config writing, compat detection. |

## 3. Sources and licences

- Public, openly licensed release (GitHub releases, official site): download, verify against the published digest.
  No digest = no install.
- Licence forbids redistribution, or builds are paid: **import-only** in the public build. The app accepts the files
  the user got from the author (the release folder, the main file, or a .zip of the release) and copies them byte for
  byte. No probes or reverse engineering where the licence forbids it (see `docs/addons/README.md`).
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

## 7. Known gaps (2026-10-20)

Measured against this guideline on main (dd469ed). Each is fixed when its component is next touched, or in its own PR.

- **Deep Fried Chicken** has a single import slot, no library, no default/per-game pick, no cleanup. (Planned: DFC library.)
- **Defaults pin themselves**: RenoDX via `EnsureAddonDefaults`/`AdoptImportedAddon`; the NR runtime has its own
  legacy-first adoption rule (`NrLibrary`). Lecram moves to "Newest" with the build-switch change.
- **Cache-only components**: DLSS, Streamline, MFG Unlock and ReShade live in the download cache or as a single
  imported file in the components root, not in libraries; MFG Unlock and ReShade imports replace each other.
- **Two library implementations** scan builds the same way (`BuildLibrary` for downloads, `AddonLibrary`/`NrLibrary`
  for imports). Target: one library type with a source flag.
- **Verification differs**: GitHub digests, Uplift's `SHA256SUMS.txt` (skipped when absent), the MediaFire listing hash.
  Target: one verifier per source kind, and no install without a digest.
- **Compat checks differ** per component (`OptiCompat`, `UpliftConfig`, `DfcConfig`, `LecramIni`, ...). Target: one
  `ComponentCompat` shape (tested version, schema, missing keys, summary) that each config class fills.
- **Cleanup reasons** are not shown yet. (Planned: cleanup reason line.)
