# Release bot

A scheduled task on claude-dev that keeps DLSS5 Updater current with the mods it supports. Every 6 hours it checks
the upstream channels the app uses and the copyparty drop folder. When something is new it lets headless Claude Code
(Opus) update the app in a fresh worktree, checks the work itself, and opens or updates one bot PR.

## What it watches
| Key | Channel |
|---|---|
| `optiscaler-nr`, `optiscaler-f5` | GitHub releases of the two OptiScaler builds in `Core/OptiSources.cs` (with the asset the app installs) |
| `gitc-uplift`, `mfg-unlock`, `dlss`, `streamline` | GitHub releases of the repos in `Core/ComponentStore.cs` |
| `reshade` | the add-on download link on reshade.me |
| `lecram` | Lecram's public MediaFire channel (`Core/MediaFireClient.cs`), names + hashes of the DLSS5-NR files |
| drop folder | https://copyparty.home.apollof.nl/dlss5updater-drop/ (any file the owner drops: `.addon64`, zips, a zipped DFC folder) |

GitHub calls send `If-None-Match`, so unchanged sources cost one 304 each and don't count against the rate limit.
A source that fails (offline, rate limit) is skipped and checked again next run.

## What happens when something is new
1. Dropped files are downloaded into `%LocalAppData%\dlss5updater-releasebot\work\<run>` (outside the repo), with size,
   sha256, version resource and zip entries collected for Claude.
2. A worktree is made from `origin/main` (or from the open bot PR's branch, merged with main, so there is only ever one
   bot PR). Claude gets [prompt.md](prompt.md) plus a facts file, and runs with Opus, `--max-turns 200`,
   `--max-budget-usd 15`, a 60-minute timeout and a short tool allowlist (no push, no gh, no curl).
3. Claude decides: nothing to change (no commits), or it updates pins, options, help, tests, `docs/addons`, bumps the
   patch version in the three places and writes `docs/releases/v<version>.md`, then commits.
4. The bot checks the result itself: version consistent everywhere, no binaries or big files in the diff,
   `dotnet build -warnaserror` and `dotnet test` pass. Then it pushes and opens or updates the PR.
5. Config-neutral (Claude says so, no options/UI/help/settings files touched, at most 60 changed lines in `src` and
   `tests`): `gh pr merge --auto --squash`, and once GitHub merges it the bot tags `v<version>` on the merge commit so
   `release.yml` publishes. If CI takes longer than 20 minutes, the next run tags it. Everything else waits for a
   "ship it".
6. Drop files are moved to `processed/` on the share (copyparty deletes everything after 30 days).
7. An ntfy push goes to `claude-work` with the PR link, also for "nothing to change" and for failures.

A change that fails three times is parked until its version moves again (one notice per failure).

## Files and secrets
- Installed copy: `%LocalAppData%\dlss5updater-releasebot\bin` (the task runs this, not the repo). Re-run `install.ps1`
  after changing the script or prompt.
- State: `...\state.json` (last seen value per source, ETags, processed drop hashes, failure counts, a pending release).
- Logs: `...\logs\release-bot-<date>.log` and `claude-<run>.json`, deleted after 30 days.
- Clone used for worktrees: `...\repo`. Worktrees and work folders are removed at the end of every run.
- User env vars (never in the repo or logs): `DLSS5_DROP_PW` (copyparty account `dlss5drop`, only sees the drop
  folder) and `DLSS5_BOT_NTFY_TOKEN` (ntfy user `alerter`, token label `dlss5updater-releasebot`). gh and claude use
  their own logins.

## Run, pause, remove
```powershell
pwsh tools/release-bot/install.ps1                 # install and schedule every 6 h (task DLSS5UpdaterReleaseBot)
pwsh tools/release-bot/install.ps1 -DryRun         # same task, but the bot runs with -DryRun
Start-ScheduledTask DLSS5UpdaterReleaseBot         # run it now
Disable-ScheduledTask DLSS5UpdaterReleaseBot       # pause (Enable-ScheduledTask to resume)
pwsh tools/release-bot/install.ps1 -Remove         # remove the task (state, logs and clone stay in %LocalAppData%)

# by hand
pwsh $env:LOCALAPPDATA\dlss5updater-releasebot\bin\release-bot.ps1 -DryRun
pwsh ...\release-bot.ps1 -DryRun -StateFile <copy of state.json>   # simulate: set a source's "seen" back first
pwsh ...\release-bot.ps1 -DryRun -SkipClaude                       # plumbing only, no Claude
```
`-DryRun` does everything (checks, downloads, Claude, build, tests) except push, PR, merge, tag, notify, moving drop
files and saving state.

The first run only records what is out there now. To have the bot catch up on a version that came out before it was
installed, set that source's `seen` in `state.json` to the version the app was tested with.
