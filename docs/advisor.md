# Method advisor

The game page's **Works in this game** panel answers "which ways of improving this game does the app know, and which fit
here". `MethodAdvisor.Advise(AdvisorInput)` (`Core/MethodAdvisor.cs`) is pure: the caller reads the game, the GPU, the catalog
and the library and passes them in. It implements the decision table of `docs/research/methods-2026-10.md` section 8 (20
rows); `MethodAdvice.Rows` reports which rows applied, and `tests/DLSSUpdater.Tests/MethodAdvisorTests.cs` has a test per row.

## Input

| Input | From |
|---|---|
| Graphics APIs, bitness, engine, upscaler DLL and plugin signals, anti-cheat | `GameInfo` / `GameTech` (the detection scan) |
| Native DLSS | `Installer.HasNativeDlss`: DLLs our own install placed don't count |
| GPU vendor and RTX generation | `GpuDetector` (display adapter class in the registry, read once at startup); tests inject a `GpuInfo`. An unreadable GPU never hides a method. |
| OptiScaler compatibility list, HUDfix-incompatible list | the catalog's `optiscalerCompat` section (`docs/catalog.md`), matched by title with `GameMatcher.MatchName` |
| RenoDX game list | the catalog's `renodx` section, `GameMatcher` |
| Imported builds, DFC version | the component library |
| Routes already in the game | `NrAddons.PresentIn` (record and files) |
| Store install | `MethodAdvisor.IsProtectedStoreInstall` (under `WindowsApps`, or `MicrosoftGame.config` with an unreadable exe) |

## Output

An ordered list of `MethodOption`: title, `Availability` (available / needs setup / not possible / coming), a one-line reason,
the component ids it would install (and the install mode), what it needs, and an `Unverified` flag (also said in the reason)
where the research marks something unverified: Smooth Motion's RTX 40/50 requirement, the DX11 bridge requirement, the DX10
route, an Unreal DLSS plugin standing in for the DLL, an assumed graphics API. What cannot be done comes last with its reason.
Anti-cheat and protected Store installs return no methods and a `Blocked` line (row 20); a non-NVIDIA or GTX GPU leaves only
OptiScaler (row 19).

Methods the app does not install yet (Luma, third-party upscaler mods, the DX11 bridge, the all-in-one add-on, the RenoDX HDR
mod) are listed as **coming**, so the answer is complete without promising an install.

## The button

"Select" turns on the components of a method on the game page (and the install mode it needs). It never installs; the user
presses Install. Smooth Motion has no components: it is a driver setting on the game's NVIDIA tab.

## One neural consumer

`NrAddons` holds the rule (`Refusal`, `FeederRefusal`, `PresentIn`): DFC, GITC Uplift, ShortFuse, Lecram, the DX11 bridge and
OptiScaler's NR all detour the game's NGX calls, so a game gets one (ShortFuse + Lecram only with the Settings override, as
before). `Installer` checks it before anything is fetched (`EnsureOneNeuralConsumer`): a DX11 bridge in the folder blocks any
route, and DLSS5-Feeder needs ReShade + add-ons mode and Deep Fried Chicken alone. Routes the app places itself replace each
other as before.
