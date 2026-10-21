# RHI parity plan (draft for sign-off)

Goal: DLSS5 Updater becomes a full replacement for RHI: game-specific HDR mods installed and kept up to date
automatically, ReShade fully managed (API-correct DLL, channels, add-ons, shader packs), frame limiters, and a
per-game "what works here" answer for upscaling / frame gen / neural rendering. Done = each workstream below merged
into `feature/rhi-parity` with tests, checked in the running app, then one merge to main as **3.0.0**.

Research (facts only, used clean-room): `docs/research/rhi-2026-10.md`, `docs/research/methods-2026-10.md`.

## Ground rules for every worker
- **Clean room.** RHI is GPL-3, we are MIT: never copy RHI code or its JSON/data (rhi-repo has no licence at all).
  Use upstream sources (RenoDX `games-index.json` + wiki, Luma wiki, crosire `Addons.ini`, OptiScaler wiki raw md,
  PCGamingWiki `idlookup`/`action=raw`). Facts (ini keys, registry paths, URLs, DRS ids) are fine.
- `docs/design.md` component model applies to every new component (library, Newest default, per-game pick,
  cleanup, install record, config/compat, docs/addons page, tests). AGENTS.md hard rules: never ship/host/download
  `nvngx_dlssnr.dll`; tests never write NVIDIA profiles, launch games or touch real game folders.
- Everything downloaded is verified by SHA-256 (published digest or our catalog). No digest = no install.
- Anti-cheat games: no injection of any kind; the UI says why. Driver-profile frame limit stays allowed.

## Our mirror = the catalog repo
New public repo `ApolloF/DLSS5-Updater-catalog`, GitHub Action on a 6 h schedule (runs on GitHub, not claude-dev):
- Pulls upstream lists (RenoDX `games-index.json` + wiki Mods table, generic UE/Unity/UE-Extended, Luma wiki +
  releases, Addons.ini, our shader-pack list, DLSS5-Feeder releases, OptiScaler compat list).
- Downloads every binary, records SHA-256, size, upstream URL, first-seen date as version. Re-hosts it as a release
  asset in the catalog repo when the source licence permits (MIT/BSD/Apache, e.g. clshortfuse/renodx); otherwise
  the entry points upstream and pins the hash (app refuses a mismatch until the Action refreshes it).
- Publishes `catalog.json` (+ per-game overrides table we maintain by hand: name fixes, blocklist, UE-Extended
  opt-outs, Engine.ini paths). App reads it through the ETag cache and keeps the last good copy offline.
- Result: real versions + change history for "snapshot" builds that upstream never versions.

Decisions (user, 2026-10-21): re-host when the licence allows, otherwise hash-pin upstream. ShortFuse's
`renodx-dlss` add-on is auto-pulled through the mirror in the public build too (design.md §3 gets an exception
entry for it). `nvngx_dlssnr.dll` hosting: open, see "Open questions". Vulkan layer may ask for elevation, on that
action only. NVIDIA driver extras are in scope (workstream K).

## Workstreams (branch → model, depends on)
Wave 1 (parallel):
- **A Detection** `feature/rhi-detect` → Sonnet. PE import + delay-load parser (DX8-12, Vulkan, OpenGL, multi-API),
  bitness, engine (UE/UE3/Unity/RE Engine) + UE version, Unity `boot.config`, `D3D12Core.dll`, upscaler DLL
  signals (DLSS/DLSS-G/Streamline/FSR/XeSS), Battle.net + Rockstar launchers, cached per exe write time.
  Accept: unit tests on synthetic PE files for every API/bitness case; scan of a temp game tree.
- **B Component records** `feature/rhi-component-record` → **Opus** (lead-implementer). Replace per-component
  fields in `InstallManifest`/`KnownInstalls`/`GameOverride` with one generic installed-component list (id, build,
  files, backups, ini edits) + migration of existing manifests. Why Opus: it migrates records of files already in
  users' games; a mistake orphans or deletes game files. Accept: migration tests from every old manifest shape,
  round-trip, uninstall restores originals.
- **C Catalog** `feature/rhi-catalog` → Sonnet. The catalog repo + Action (above) and app-side `CatalogClient`
  (fetch, cache, hash verify, name normalisation, AppID-first matching, override table). Accept: Action run green
  on GitHub producing catalog.json; app tests with a fixture catalog.

Wave 2:
- **E RenoDX HDR mods** `feature/rhi-renodx` (top priority) → Sonnet. Needs A, B, C. Per-game named mod by
  AppID/name; generic UE/Unity (32/64) fallback; UE-Extended with `[renodx]` keys and Engine.ini HDR keys (path
  resolution, read-only flag, removal); peak nits into presets; update = catalog hash change; Update All.
- **D ReShade managed** `feature/rhi-reshade` → Sonnet. Needs A, B. ReShade as a library with channels
  Stable/Nightly/Legacy/Custom/No-addons; DLL name by detected API (dxgi/d3d9/d3d8/opengl32) with collision rules
  (OptiScaler `LoadReshade`); foreign-DLL detection (ReShade/OptiScaler/DXVK/Special K/ENB signatures, back up +
  warn); `reshade.ini` template seeding; Vulkan implicit layer (one UAC prompt, HKLM `Khronos\Vulkan\ImplicitLayers`).

Wave 3:
- **F Add-ons + shader packs + presets** `feature/rhi-addons-shaders` → Sonnet. Needs D. Addons.ini catalog,
  shader packs (Lilium, PumboAutoHDR, RenoFX, crosire, ...), per-game selection, preset import.
- **G Frame limiters** `feature/rhi-framelimit` → Sonnet. Needs D. NVIDIA driver FPS cap via `NvDrs` (0x10835002,
  mocked in tests), ReLimiter / Display Commander add-ons (mutually exclusive, driver cap off when used).
- **H Method advisor** `feature/rhi-methods` → Sonnet. Needs A, C. Pure `MethodAdvisor` from the 20-row decision
  table (signals → ranked methods + one-line reason); DLSS5-Feeder as a downloadable component (repo pinned
  exactly); one-neural-consumer enforcement (DFC/Feeder/renodx-dlss5/bridge/OptiScaler); game page "Available here".

Wave 4:
- **I Luma** `feature/rhi-luma` → Sonnet. **J DXVK + Lilium** `feature/rhi-dxvk` → Sonnet.
- **K NVIDIA extras** `feature/rhi-nvidia` → Sonnet. Needs B. Per-game driver profile settings through `NvDrs`:
  DLSS SR/RR/FG presets + "latest DLL", render scale, MFG mode/factor, Smooth Motion, RTX HDR, VSync/low latency.
  DRS ids in `docs/research/rhi-2026-10.md` §9.2, cross-checked against nvidiaProfileInspector's
  `CustomSettingNames.xml`. Tests use a fake DRS layer; nothing writes a real profile.

Final: integration pass + docs + release notes (me, Haiku checker for the check command), one Opus reviewer pass
on the whole branch before main (risk: game files, elevation, downloads). UI checked by you on the PC.

Concurrency: max 3 workers at once (5 h limit). Each worker: Orca worktree off `feature/rhi-parity`, PR into it,
CI `build-test` green, I review the diff and run the check before merging.

## Out of scope for 3.0 (later, if wanted)
DLDSR, digital vibrance, DLSS DLL version mirror, Nexus, REFramework, ultrawide fixes, HDR auto-toggle, game
launching, tray/background updater.

## Open questions
- `nvngx_dlssnr.dll` in the mirror (AGENTS.md hard rule today says never). Undecided; the catalog is built so a
  private-only source can be added later without touching the public catalog.
