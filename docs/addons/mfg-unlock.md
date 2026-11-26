# MFG Unlock

ReShade add-on by mavismmg that unlocks multi frame generation (DLSS-G on RTX 40, Intel XeMFG on RTX 20/30).
Open source (MIT, https://github.com/mavismmg/MFGAdaUnlock-RenoDx). Facts below come from the release notes, `README.md`,
`docs/settings.md` and the source (`src/addons/mfgunlock/addon.cpp`, `addon_dlss5.inl`, `nr_runtime.hpp`) at tags 1.9.0, 1.11.0,
1.12.0 and 1.12.1; there is nothing to probe. Re-check them for every new release.

## Checked builds

Asset `renodx-mfgunlock.addon64` on GitHub releases (tag without `v`, `ComponentStore.MfgRepo`), downloaded with the digest GitHub
publishes. The app downloads only the newest release; imports of older builds keep working.

| Tag | Date | SHA-256 of the asset | Config diff against the previous row |
|---|---|---|---|
| 1.9.0 | 2026-11-07 | not recorded | last build checked before this table (README, 2026-10-18 and 2026-11-07) |
| 1.10.0, 1.10.1 | 2026-11-16, 11-17 | not checked | no change to a `[RenoDX.MFGUnlock]` key the app offers; new `[DLSSG]`/`[Diagnostics]` keys (`StreamlineLog`, `HudFix`, `XmxGenerator` ...) live in `MFGAdaUnlock.ini` |
| 1.11.0 | 2026-11-22 | `f8c806fe99fa200614be0c04f0a7908dd9e2de6d28ed67bb309bcd9fb66d703b` | performance overlay (`[Overlay]`, `MFGAdaUnlock.ini`), `[DLSSG] HostGameReflex`, `[Diagnostics] CrashDump` hang dump; `[RenoDX.MFGUnlock]` table unchanged |
| 1.12.0 | 2026-11-23 | `f7cc86a84c05436409a1b7a59499ffb9d8d5cec51ed6fe0e8bb4e55885e62ebf` | DLSS 5 neural rendering (`[DLSS5]`, `[DLSS5.Looks]`, `MFGAdaUnlock.ini`), `[Diagnostics] ForceDRED`; `[RenoDX.MFGUnlock]` table unchanged |
| 1.12.1 | 2026-11-26 | `fc5afd831c1fea5b6c0ecc7a47b0a14b37d78a0f0eecbbfba72459f14a061977` | `[DLSS5] Budget` and `Silhouettes`; `[RenoDX.MFGUnlock]` table unchanged |

`MfgConfig.TestedVersion` is 1.12.1. A newer build logs "newer than the tested" when it is installed.

## Two config files

| File | Section | Written by | Keys |
|---|---|---|---|
| the game's `ReShade.ini` | `[RenoDX.MFGUnlock]` | the app (Settings, game config) and the add-on's panel | below |
| `MFGAdaUnlock.ini` beside the game | `[Overlay]`, `[DLSSG]`, `[DLSS5]`, `[DLSS5.Looks]`, `[Diagnostics]`, `[Panel]`, `[XeFG]` ... | the add-on's own panel only | the add-on's panel |

The app never reads or writes `MFGAdaUnlock.ini` (see "Neural rendering" below for why that matters).

## `[RenoDX.MFGUnlock]` keys the app offers

Every key is in the documented table of 1.4.1 (the oldest README checked) and of 1.9.0, 1.11.0, 1.12.0 and 1.12.1, and is read by
`addon.cpp` of 1.12.1 with the same meaning, so no option needs a per-build gate: an import older than 1.4.1 is the only build
that could lack one, and the app does not claim support for those. Ranges (C = stated in docs/settings.md or release notes,
D = clamp read from `addon.cpp` 1.12.1):

| Key | Default | Values the app offers | Fact |
|---|---|---|---|
| `ForceMultiplier` | 0 | 2-6 | C; D: out of range becomes 0 |
| `MaxCount` | 4 | 3-5 | C; D: clamped to 2-5 |
| `DynamicMFG` | 0 | 1 | C; priority over `ForceMultiplier`. 1.8.5+: accepts 310.9.1 or newer with Streamline 2.14 or newer, the runtime's support bit decides; validated pairs stay 310.9.1 + 2.14.1 and 310.9.16 + 2.14.16 |
| `DynamicTargetFPS` | 0 (refresh) | 60-240 | C; D: 0-1000; ignored with VSync |
| `RuntimeSelectionMode` | 0 | 1, 2 | C; D: out of range becomes 0 |
| `HDRCompatibilityMode` | 0 (Native) | 1-3 | C; D: out of range becomes 2 |
| `ReflexModeOverride` | 0 | 1-3 | C |
| `ReflexSourceFpsCap` | 0 | 60-240 | C (the number is the output cap, despite the name) |
| `ReflexVrrHeadroomEnabled` | 0 | 1 | C |
| `ReflexVrrHeadroomBasisPoints` | 100 | 50-300 | C; invalid becomes 100 |
| `RaiseFrameCeiling` | 0 | 1 | C |
| `ForceFlipMeteringOff` | 0 | 1 | C |
| `DepthEdgeGuardLevel` | 0 | 1-4 | C; D: out of range becomes 0 |
| `BoundaryArtifactMitigationMode` | 1 | 0-2 | C |
| `[ADDON] LoadFromDllMain` | (ReShade) | `renodx-mfgunlock.addon64` | "Load MFG Unlock early"; ReShade's key, not the add-on's |

### Not exposed

`Enabled`, `TemporalFix`, `BlackwellFrameworkKernels`, `ExperimentalAdaptiveQuality`, `AdaptiveQualityProfile` and
`AdaptiveQualityV3*`, `ThinGeometry*`, `Rtx50QualityFixes` (research or restart-only quality controls that the add-on's panel
exposes), `DlssSuperResolutionPreset`, `DlssRayReconstructionPreset` (the NVIDIA profile tab has presets), `DynamicMaxMultiplier`
(1.8.5; the NVIDIA profile tab has the driver's own ceiling), `ReflexPacingMethod` (experimental), `DynamicReflexSourceCap`,
`FixedOutputFpsCap` (legacy migration keys). `AutoEarlyLoad` and `PreAdaAdmission` are in the 1.9.0 table and gone from the 1.11.0
one (the notes do not say why). None of them is written.

## 1.11.0 to 1.12.1: what is new and what the app does with it

| Feature | Keys (file) | Default | App |
|---|---|---|---|
| Performance overlay, Shift+F10 | `[Overlay] PerfOverlay`, `PerfOverlayLevel` (0 Minimal, 1 Standard, 2 Full), `PerfOverlayCorner`, `PerfOverlayScale` (50-200), `PerfOverlayOpacity`, `PerfOverlayKey` (121 = F10), `PerfOverlayModifier` (0 none, 1 Shift, 2 Ctrl, 3 Alt) (`MFGAdaUnlock.ini`) | off | not written; named in the `comp-mfg` help topic |
| Game's own Reflex while MFG Unlock hosts Streamline | `[DLSSG] HostGameReflex` (`MFGAdaUnlock.ini`) | 1 | not written; "leave unset unless asked" |
| Crash and hang records | `[Diagnostics] CrashDump`, `ForceDRED` (`MFGAdaUnlock.ini`) | 0 | not written |
| DLSS 5 neural rendering (NR) | `[DLSS5] Enabled`, `RuntimePath`, `Network`, `Style`, `Intensity`, `LocalTone`, `LocalStructure`, `Skin`, `ProtectHud`, `Scale`, `Passes`, `Budget` (1.12.1), `Silhouettes` (1.12.1), `HookPoint`, `Motion`, `TaaFinder`, `Stability`, `PaperWhite`, `ToggleKey`, `CompareKey`, `KeyModifier`, `VramMargin`, `Coexist` (`MFGAdaUnlock.ini`) | off | never written, never turned on; named in the `comp-mfg` help topic |

## Neural rendering and the one-consumer rule

MFG Unlock 1.12.0 can run NVIDIA's DLSS 5 NR itself. It does not ship or download `nvngx_dlssnr.dll`: the user puts one beside the
game's exe (or sets `[DLSS5] RuntimePath`). It is off by default (`[DLSS5] Enabled=0`) and needs a game restart to turn on.

The app's rule (docs/advisor.md row 18: one neural consumer per game; every NR route detours the game's NGX calls) therefore holds
by construction, and this is what makes it hold:

- The app writes only `[RenoDX.MFGUnlock]` keys into `ReShade.ini`, never `MFGAdaUnlock.ini`, so `[DLSS5] Enabled` stays at the
  add-on's default (off) in everything the app installs. Tests assert that no offered option, default profile or install writes it.
- `[DLSS5] Coexist` defaults to 0: MFG Unlock's NR stands aside when another DLSS 5 mod is loaded. The detection (1.12.1
  `nr_runtime.hpp`, `Foreign()`, I: read from source) is a loaded module whose file name contains `dlss5` and `.addon` (Lecram's
  `dlss5-nr.addon64`, `renodx-dlss5.addon64`), or an NGX feature 18 (NR) created by someone else. ShortFuse's `renodx-dlss.addon64`,
  `gitc-uplift.addon64` and OptiScaler's NR do not have `dlss5` in their name, so they are caught only through the NGX create
  call, if at all. Do not rely on it: a user who turns on the DLSS 5 tab in the add-on's panel next to one of them gets two
  consumers. The `comp-mfg` help topic says so.
- Not done yet: warning at install time when a game's `MFGAdaUnlock.ini` already has `[DLSS5] Enabled=1` while another NR
  route is installed. That check belongs in `Installer`'s conflict step (`NrAddons` has no MFG route; adding one touches every
  switch over `NrAddon`).

## Dynamic MFG pairs (MfgCompat)

`MfgCompat.Pairs` holds the validated DLSS-G + Streamline pairs of the version matrix in `docs/settings.md` 1.12.1:
310.9.0 + 2.12.x (fixed MFG only), 310.9.1 + 2.14.1 (Dynamic validated on D3D12), 310.9.16 + 2.14.16 (tested with XeMFG DLSS-G in
Cyberpunk 2077; in-game validation of the rest pending). The add-on itself accepts any 310.9.1+ with Streamline 2.14+ and lets the
runtime decide, so the app's "only validated with" message is advice, not a block.

## Release notes

`ReleaseNotes.Summarize` shows the first heading and the first four top-level bullets. The 1.11.0 to 1.12.1 notes start with an
intro paragraph, then `## Performance overlay` / `## DLSS 5 tab` / `## DLSS 5` with a bullet list: the summary is that heading plus
those bullets (`ReleaseNotesTests`, MFG notes).

## Licence

MIT (c) 2026 ImDreamt; redistribution is allowed, and the app downloads the asset from the author's GitHub release.
