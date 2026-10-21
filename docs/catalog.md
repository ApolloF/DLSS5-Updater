# The catalog

The app's list of community mod files lives in a separate public repo,
[ApolloF/DLSS5-Updater-catalog](https://github.com/ApolloF/DLSS5-Updater-catalog). A GitHub Action there rebuilds
`catalog.json` every six hours from the authors' own lists, hashes every binary, and re-hosts the ones whose licence
allows it. Its README documents the format (and `schema/catalog.schema.json` is the JSON Schema); this page is the app side.

## Why

- Upstream "snapshot" builds have no versions. The catalog names a build by the date it was first seen plus its hash and
  keeps the hashes of older builds, so "update available" is a hash change.
- Every download is verified: **no hash in the catalog = no install**.
- Games come with Steam AppIDs, so matching doesn't depend on store titles.

## Code (`src/DLSSUpdater/Core`)

| File | Role |
|---|---|
| `CatalogModel.cs` | The slice of catalog.json the app reads (`CatalogDocument` → `Sections.Renodx` → `Mods`, `Generic`, `EngineGames`, `Overrides`; `Sections.RenodxDlss` → `Builds`). Unknown properties and sections are ignored. Registered in `JsonCtx`. |
| `CatalogState.cs` | The catalog the app holds: the last good copy at startup, refreshed with every update check; a failed refresh keeps what it has. Holds the `HdrPicker`. |
| `CatalogClient.cs` | `LoadAsync` fetches catalog.json through `GitHubClient.GetRepoFileAsync` (ETag cache, 304s are free) and keeps the last good copy in `AppPaths.CatalogFile`. Offline, rate-limited, damaged or newer-than-us (`schema` > 1) answers fall back to that copy; it never throws except on cancellation. `LoadCached` reads the copy with no request. |
| `CatalogDownloader.cs` | `DownloadAsync(file, dest, ...)` downloads from `MirrorUrl`, else `Url` (and from `Url` when the mirror copy fails), into a staging file, checks size and SHA-256, and only then moves it to `dest`. A mismatch returns a one-line `Refusal` and leaves nothing behind. |
| `OptiCompatIndex.cs` | The `optiscalerCompat` section (OptiScaler's wiki compatibility list and its HUDfix-incompatible list) looked up by game title with `GameMatcher.MatchName`. Feeds the method advisor (`docs/advisor.md`); an older catalog without the section just leaves it empty. |
| `GameMatcher.cs` | `new GameMatcher(catalog.Sections.Renodx).Match(game)`: overrides first, then Steam AppID, then the normalised name (exact; game name contains entry name, longest wins; entry starts with game name). Anything that is "name + only a sequel number" (digits, roman numerals up to 39) is not a match. Only `main` and `engine` entries take part; a matched entry can be `Blocked`, so check `Mod.Blocked` and show `BlockReason`. |

`GameInfo.SteamAppId` comes from the Steam appmanifest (`Launchers.SteamLibrary`); other launchers and manual folders have none.

## Using it from a component (workstream E and later)

```csharp
var load = await catalogClient.LoadAsync(ct);          // never throws
if (load.Document?.Sections.Renodx is { } renodx)
{
    var match = new GameMatcher(renodx).Match(game);
    var file = match.Mod?.Files.FirstOrDefault(f => f.Arch == "x64" && f.IsInstallable);
    if (file is not null)
    {
        var result = await downloader.DownloadAsync(file, libraryPath, progress, ct);
        // result.Ok, result.Path, or result.Refusal (one line for the log and the UI)
    }
}
```

`EngineGames` entries for Unreal Extended carry what the generator read out of the wiki notes: `method` (`native`, `ini`, `upgrade`), `upgrades`
(`format` + `mode` `output-size`/`output-ratio`/`any-size`), `copyDestinations` and `engineIni` keys; `HdrPick.cs` turns them into the `[renodx]`
and Engine.ini work (`docs/addons/renodx-hdr.md`). `Sections.RenodxDlss.Builds` lists ShortFuse's DLSS add-on releases, newest first, each
`File` a zip with its SHA-256 (pinned, no mirror): `ComponentStore.CheckRenoDxCatalogAsync` downloads the newest, takes `renodx-dlss.addon64` out
and imports it (`docs/design.md` section 3, the exception).

A build's identity for the library is `file.Sha256`; `file.Version` is its label; `file.History` lists earlier builds.
`renodx.Overrides` offers `OptsOutOfUeExtended(appId, name)` and `EngineIniPath(appId, name)` for the Unreal Extended path.
