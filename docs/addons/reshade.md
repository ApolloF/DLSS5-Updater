# ReShade (crosire)

Open source (BSD-3-Clause, https://github.com/crosire/reshade). The app downloads it, keeps every build in a library and
installs it as the loader for the add-ons or as the proxy dll. Checked 2026-10-21; **C** = read from the upstream source
or a real build, **I** = inference.

## Component model (docs/design.md section 2)

| Part | ReShade |
|---|---|
| Source | Download (reshade.me, crosire's CI artifacts) and import. |
| Library | `components\reshade\<id>\` with `ReShade64.dll` and/or `ReShade32.dll` and `version.json` (`ReShadeBuild`: version, channel, hashes, verification, origin, commit, date, user label). Id = SHA-256 of `ReShade64.dll` (of `ReShade32.dll` for a 32-bit-only import). |
| Channels | Stable (newest add-on build), Stable without add-ons, Nightly (CI), Legacy (6.0.0+), Custom (import). |
| Default | `AppSettings.ReShadeId`: null = newest Stable add-on build; `channel:<name>` follows a channel; an id pins one build. Per game: `GameOverride.ReShadeId`, same meaning. |
| Cleanup | `LibraryKind.ReShade`; protected: the default, per-game picks, builds installed in a game, the newest build of each moving channel. Imports are never deleted automatically. |
| Install | Still the pre-3.0 record fields (`ReShade`, `ReShadeSha`, `Proxy`, `ReShadeIni`) plus `ReShadeId` (library id) and `ReShadeVulkan`; `LegacyComponents` shows them as the `reshade` component. |
| Config | `ReShade.ini` from the user's template (`ReShadeIniTemplate`) merged under the app's ReShade overrides. |
| Update check | reshade.me front page (`ParseSitePage`), GitHub API (workflow runs and artifacts) through the ETag cache; offline uses the library. |

## Sources and verification

| Channel | Where | Verification |
|---|---|---|
| Stable | `https://reshade.me/downloads/ReShade_Setup_<v>_Addon.exe` (version from the front page link; fallback: newest crosire/reshade tag) | None published. **C**: the add-on DLLs are not Authenticode-signed (checked on 6.8.0). SHA-256 recorded at download, checked again at every install. |
| Stable without add-ons | `.../ReShade_Setup_<v>.exe` | The plain DLLs and both setups carry a signature from a self-issued certificate (`CN=ReShade`, thumbprint 589690208A5E52FB96980C4A6698F50ACD47C49F, valid until 2040, same on 6.5.1, 6.7.3, 6.8.0) with no trusted root. Not used as trust (anyone can read the certificate); same rule as Stable. |
| Legacy | same URL for a version from the crosire/reshade tags, 6.0.0 and up (6.5.1 and 6.7.3 were downloaded to check) | As Stable; the DLL's version must match the one asked for. |
| Nightly | Newest successful `push` run of workflow `build.yml` on `main` (API `.../actions/workflows/build.yml/runs?branch=main&event=push&status=success&per_page=1`), its artifacts `ReShade (64-bit)` and `ReShade (32-bit)` from `https://nightly.link/crosire/reshade/workflows/build/main/<name>.zip` | **C**: the artifacts API publishes `digest: sha256:...` and it equals the SHA-256 of the nightly.link zip (checked 2026-10-21). The app requires the match. Without a digest the build is kept but labelled "Unverified source". |
| Custom | Import of a DLL, a folder, a setup exe or a zip | None; every DLL must be a PE file of the right bitness with ReShade in its version information. |

nightly.link needs the artifact name **percent-encoded including the brackets**: `ReShade%20%2864-bit%29.zip` downloads,
`ReShade (64-bit).zip` is a 404. .NET keeps `%28`/`%29` on the wire (checked with `HttpClient`).

The setup exe is a PE stub with a zip appended; `ReShade64.dll`, `ReShade32.dll`, `ReShade64.json`, `ReShade32.json`,
`ReShade64_XR.json` and `ReShade32_XR.json` are the entries. Only the two DLLs are taken, from the archive's top level.

### Checked builds

| Version | Channel | Notes |
|---|---|---|
| 6.8.0 (64: 6.8.0.2155 add-on / 6.8.0.2158 plain) | Stable, Stable without add-ons | Version resource: ProductName `ReShade`, CompanyName `crosire`, OriginalFilename `ReShade64.dll` / `ReShade32.dll`. |
| 6.7.3, 6.5.1 | Legacy | Setups download from the same URL pattern. |
| main @ 7bf9de8 (2026-09-24) | Nightly | Digests of both artifacts match. |

## DLL name (`ReShadeNaming`)

ReShade decides what it is from its own file name (**C**, `source/dll_main.cpp`: `dxgi`, `d3d*`, `opengl32`, `dinput*`, `.asi`).

| Primary API (`GameTech.PrimaryApi`) | File |
|---|---|
| DX12, DX11, DX10 | `dxgi.dll` |
| DX9 | `d3d9.dll` |
| DX8 | `d3d8.dll` |
| OpenGL | `opengl32.dll` |
| Vulkan | the implicit layer, no dll in the game |
| not detected | `dxgi.dll` |

32-bit games get `ReShade32.dll`'s content under the same name. When OptiScaler loads ReShade (OptiScaler mode), the name is
always `ReShade64.dll` (**C**, OptiScaler.ini: "Loads Reshade64.dll from game's exe folder", `LoadReshade`) and the 64-bit DLL.
The game's own file-name choice (Install location) overrides the table. `d3d8.dll` is a ReShade proxy name too (**I**: ReShade
converts Direct3D 8 itself; its setup still warns about a wrapper).

## Foreign dlls (`ForeignDll`)

Checked before a proxy name is written; the original is backed up by the install record and restored on uninstall; one warning
line is shown and the install can be cancelled.

| Mod | Signs |
|---|---|
| ReShade | **C** version resource ProductName `ReShade`, OriginalFilename `ReShade64.dll`/`ReShade32.dll`; strings `reshade.me` and `crosire`. |
| OptiScaler | OriginalFilename `OptiScaler.dll` (as `Installer.IsOptiScaler`); strings `OptiScaler.ini` + `OptiScaler`. |
| DXVK | **C** (3.1.1, every dll of x32/x64) ProductName `DXVK`, CompanyName `DXVK`; the string `DXVK_CONFIG_FILE` and `dxvk.conf`. ReShade's setup uses the same ProductName test. |
| Special K | **C** (SK_26_10_10 `SpecialK64.dll`/`SpecialK32.dll`) ProductName `Special K`, CompanyName `The Special☆K Group`, InternalName `SpecialK64.dll`; exports such as `?SK_ImGui_ControlPanel@@YA_NXZ`, `?SK_Render_GetSwapChain@@YAPEAUIUnknown@@XZ`; strings `SK_ImGui_`, `Special K`, `SpecialK64.dll`. Installed as `dxgi.dll` etc. the version resource stays. |
| ENB | **I**, no real binary could be fetched (enbdev.com serves no direct download). ENB's documentation puts `enblocal.ini` / `enbseries.ini` beside its wrapper (`d3d9.dll`, `d3d11.dll`), so those files mark it; also a version resource starting `ENB` or company `Boris Vorontsov`, or the strings `enbseries` / `enblocal.ini`. Tests use synthetic files. |
| other | "unknown": backed up and warned the same way. |

## Vulkan implicit layer (`VulkanLayer`, `VulkanLayerCommand`)

Facts from crosire/reshade `setup/MainWindow.xaml.cs`, `setup/ReShade64.json` and `source/dll_main.cpp` (read 2026-10-21):

- Files in `%ProgramData%\ReShade` (`C:\ProgramData\ReShade`): `ReShade64.dll`, `ReShade32.dll`, `ReShade64.json`, `ReShade32.json`. The folder gets read access for ALL APPLICATION PACKAGES (`S-1-15-2-1`) so Store games can load it.
- Registration: DWORD value 0, name = full path of the manifest, under `HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers` (64-bit) and `HKLM\SOFTWARE\Wow6432Node\Khronos\Vulkan\ImplicitLayers` (32-bit).
- Manifest: layer `VK_LAYER_reshade`, type `GLOBAL`, `library_path` `.\ReShade64.dll`, `api_version` `1.3.268`, `implementation_version` `1`, device extension `VK_EXT_tooling_info`, `disable_environment` `DISABLE_VK_LAYER_reshade_1=1`. `VulkanLayer.Manifest` generates the same text.
- Loaded into every Vulkan app, but when not loaded as a proxy ReShade starts only if a `ReShade.ini` exists next to the target exe (`dll_main.cpp`: "only actually load when a configuration file exists for the target executable"). So the per-game step is the game's `ReShade.ini`; ReShade's `ReShadeApps.ini` is only the setup's own bookkeeping. `ReShadeIniTemplate.VulkanExtras` adds the depth preprocessor definitions the setup writes.
- Elevation: `DLSS5Updater.exe --vulkan-layer install <build folder> <sha64|-> <sha32|-> <version>` / `--vulkan-layer remove`, started with `runas` by Settings → ReShade → Vulkan layer. It installs only DLLs that pass `ReShadeSource.Check` and whose copy in ProgramData hashes to the given values, reports by exit code, and never touches a layer without the app's marker file (`DLSS5Updater.layer.json`), such as one from ReShade's setup. The registry and files sit behind `IVulkanLayerHost`; tests use a fake.

## ReShade.ini template (`ReShadeIniTemplate`)

`%LocalAppData%\DLSS5Updater\reshade-template.ini`, written on first use, edited with the "Edit template" button. Default keys: `[ADDON] DisabledAddons=Generic Depth,Effect Runtime Sync`, `[GENERAL] EffectSearchPaths / TextureSearchPaths` (`.\reshade-shaders\...\**`), `[SCREENSHOT] SavePath=.\reshade-screenshots`, `[INPUT] KeyOverlay=36,0,0,0` and `KeyScreenshot=44,0,0,0`, `[OVERLAY] TutorialProgress=4`, `VariableListUseTabs=1`. Merged through `ConfigProfile.Merge` under the app's ReShade overrides: a value the game's ini already has is kept (unless "apply"), what is written is recorded in `InstallManifest.ReShadeIni`, and uninstall restores the original ini or removes the one the install created. Sections `RENODX*`, `RenoDX.DLSS5`, `Uplift` and `DFC` are never seeded. Used for installs started from the game page; "Update all" does not add template keys to games that already have ReShade.

## Migration from 2.x

On startup `MigrateReShade` moves a `components\ReShade64.dll` into the library as a Custom build, and complete downloads in `cache\reshade\<tag>` as Stable builds (64-bit only; the 32-bit DLL is fetched from the same version the first time a 32-bit game needs it, and must leave the 64-bit DLL byte-identical). A user who had imported a file keeps it as the default (`ReShadeId` is set once), as before. Install records without `ReShadeId` keep working (the hash is the build id). Nothing in a game changes until the user installs or updates.
