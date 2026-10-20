# Release bot

A scheduled task on claude-dev that keeps DLSS5 Updater current with the mods it supports. Every 6 hours it checks
the upstream channels the app uses and the copyparty drop folder, and keeps one bot PR open for anything new.

**Status: the task `DLSS5UpdaterReleaseBot` is disabled, and stays off until "Full isolation" below is set up.**
Until then, run the bot by hand with `-DryRun`.

## What it watches
| Key | Channel | Path when it changes |
|---|---|---|
| `optiscaler-nr`, `optiscaler-f5` | GitHub releases of the two OptiScaler builds in `Core/OptiSources.cs` (with the asset the app installs) | auto if proven config-neutral, else review |
| `gitc-uplift`, `mfg-unlock`, `dlss`, `streamline` | GitHub releases of the repos in `Core/ComponentStore.cs` | review (Claude) |
| `reshade` | the add-on download link on reshade.me | review (Claude) |
| `lecram` | Lecram's public MediaFire channel (`Core/MediaFireClient.cs`), names + hashes of the DLSS5-NR files | review (Claude) |
| `lecram-notes` | Lecram's members-only post on Buy Me a Coffee (`v11 Patch N` sections), read with the bot's own browser profile | review (Claude) |
| drop folder | https://copyparty.home.apollof.nl/dlss5updater-drop/ (any file the owner drops) | review (Claude) |

GitHub calls send `If-None-Match`, so unchanged sources cost one 304 each and don't count against the rate limit.
A source that fails (offline, rate limit) is skipped and checked again next run.

`lecram-notes` needs a Buy Me a Coffee login in a browser profile only the bot uses,
`%LocalAppData%\dlss5updater-releasebotrowser-profile` (never the owner's normal Chrome profile). Run
`pwsh $env:LOCALAPPDATA\dlss5updater-releasebotinelease-bot.ps1 -LoginBuyMeACoffee` once: it opens Chrome (or
Edge) with that profile on the post; log in, check the post shows, close the window. Each run then reads the post
with headless Chrome (`--headless=new --dump-dom`, 90 s timeout) and looks for new `vN Patch N` sections. Logged out,
a changed page or a failed fetch is a log line and one ntfy note asking to log in again (until it works again),
never a failure or a parked change. The new sections go only into Claude's facts file on the review path, marked as
paid content to paraphrase; the bot never puts their text in the repo, PRs, notices or release notes, or in its state.

## Two paths
**Auto path (no Claude).** Only when every new item is an OptiScaler build, no drop is pending and no bot PR is open.
The bot proves the bump config-neutral by itself: the new tag is a plain `vX.Y.Z` in the same minor line, not a
prerelease, has exactly one matching asset, and its `OptiScaler.ini` (in the repo at the tag and inside the release
zip) and the zip's file list are identical to the tested build after normalising BOM, line ends and trailing
whitespace. It reads the zip's directory and the ini with HTTP Range requests instead of downloading 180 MB. Then it
writes the bump itself: the `TestedTag` literal in `OptiSources.cs`, the version in csproj/installer/README, and
`docs/releases/v<version>.md` from a template. Before it pushes, the diff has to pass the general checks and an
allowlist: only those five paths, every changed line a pin or version line that differs only in the version token,
and the notes byte-equal to the template. The PR, notice and release text are templates with bot-known facts only.
It opens the PR, `gh pr merge --auto --squash --match-head-commit <sha it pushed>`, and saves `pending` straight away.
Once GitHub merges it (CI `build-test` is required by the ruleset), the bot checks that the squash commit's diff has
the same `git patch-id` as the diff it verified and that the PR head is still its own commit, and tags `v<version>`
with the message `release-bot prerelease v<version>`. `release.yml` publishes such tags as a **prerelease**, which the
app's self-update ignores. After 24 h the bot promotes it to the regular (latest) release, unless the owner deleted,
drafted, promoted or edited it (name, text, assets) in the meantime.

**Review path (Claude).** Everything else. The bot makes a worktree from `origin/main` (or from the open bot PR's
branch, merged with main), sets the patch version and starts `docs/releases/v<version>.md` itself, then clones that
into a sandbox. Headless Claude (Opus, [prompt.md](prompt.md), `--max-turns 200`, `--max-budget-usd 15`, 60 min) works
in the sandbox and can't commit. The bot compares the sandbox with its worktree file by file (it never runs git in
Claude's folder, whose `.git/config` Claude could have written), copies the changed files over, commits them with
`git add -- <paths>`, and checks the diff:
- `git diff --raw/--numstat -z --no-renames --no-ext-diff --no-textconv` with `core.quotePath=false`, hooks,
  fsmonitor and attribute files off. Any numstat `-` (binary), link, submodule, executable bit or file over 1 MB fails.
- Off limits: `.github/`, `tools/`, `.claude/`, `AGENTS.md`, `CLAUDE.md`, `.gitattributes`, `.gitignore`,
  `.editorconfig`, `Directory.Build.*`, `NuGet.config`, `global.json`, `*.props`, `*.targets`, scripts, and any
  `.csproj`/`.iss` change other than the app's version line. Allowed: `.cs`, `.xaml`, `.md`, `.txt`, `.ini`, `.cfg`
  under `src/`, `tests/`, `docs/`, `.json` under `tests/` and `docs/`, and `README.md`.
- Version consistent in csproj, installer, README and docs/releases. gitleaks over the diff and the PR text.

The PR gets the `needs-review` label and `gh pr merge --disable-auto`; Claude's summary only appears in it, quoted
and marked as unreviewed bot output. Claude's `config_changed` is only a veto for the wording (anything but a real
JSON `false` reads as a config change); it never makes anything auto. The bot does not build or test Claude's work
(that would run Claude's code as the owner): CI does. Merge a review PR yourself and tag it by hand
(AGENTS.md, Release flow); the bot never tags it.

**Sticky review.** A bot PR that ever needed review keeps the label and its `needsReview` state and never auto-merges,
also when a later run adds to it. So does any open `release-bot/*` PR whose head isn't the commit the bot pushed,
or that this bot state didn't open, and an auto PR still open after 2 h (CI red or stuck). Bot PRs are only those
with `isCrossRepository == false`, opened by the bot's gh account (today the owner's), on a `release-bot/` branch: a fork PR with that
name is ignored. New changes wait while the bot's own auto PR is in flight. Branches of bot PRs closed without merge
are deleted (only if the branch still points at the PR head).

Failures: a change that fails three times is parked until its version moves again; a drop that fails three times is
moved to `failed/` on the share (also before it is downloaded again). Failures of the machine (gh logged out, no
network, gitleaks or the Claude token missing, Claude API errors) are logged and notified but not counted.

Notices go to ntfy `claude-work`, are bot templates with sanitised names, and pass gitleaks first.

## Claude's reduced environment
- The environment is cleared. Claude gets PATH, SystemRoot and the other Windows basics, USERPROFILE, TEMP/TMP in
  the sandbox, the bot's own APPDATA/LOCALAPPDATA, `CLAUDE_CONFIG_DIR` and NuGet package cache under `claude-home\`
  (so a planted package never reaches the owner's builds), a sandbox git config (no system config, no credential
  helper), and `CLAUDE_CODE_OAUTH_TOKEN` from
  `DLSS5_BOT_CLAUDE_TOKEN`. No `DLSS5_DROP_PW`, `DLSS5_BOT_NTFY_TOKEN`, `GH_TOKEN`, `SSH_AUTH_SOCK` or anything else.
- `--setting-sources project` plus `--settings {"disableAllHooks":true}` and the empty `CLAUDE_CONFIG_DIR`: the owner's
  hooks, plugins, skills and global CLAUDE.md don't load. `--strict-mcp-config`, no MCP servers. The bot rejects any
  `.claude/` change, so project settings can't be planted for a later run.
- `--tools Read,Edit,Write,Glob,Grep,Bash,WebFetch`, Bash limited to `dotnet build/test/restore`, `strings`,
  `unzip -l` and `sha256sum`. No git at all (`git diff/log --output` writes any file, `git grep -O` starts a
  program, and the clone's `.git/config` is Claude-writable), no interpreters (python, py, node, pwsh, cmd, bash,
  perl), no gh/curl. WebFetch only github.com, raw.githubusercontent.com, api.github.com.
- Files come back by name only: the bot rejects links, `.git*` names, names ending in a dot or space and 8.3 short
  names (Windows would write them somewhere else, for example over the worktree's `.git` file), and checks that its
  worktree's `.git` file is unchanged before it runs `git add`.

This is not a sandbox. Edit plus `dotnet build`/`dotnet test` is code execution (an MSBuild target or a test can run
anything), as the Windows user the bot runs as. That user still has the owner's files: `~\.ssh`, the gh token in
Windows Credential Manager, the homelab admin key, the bot's Buy Me a Coffee browser profile. That's why the schedule
needs full isolation first.

## Full isolation (required before enabling the schedule)
1. Create a dedicated local Windows user (for example `dlss5bot`), a standard user, not an administrator: no gh
   login, no SSH keys, no env secrets, no access to the owner's profile (check the ACLs of `C:\Users\<owner>` and the
   repos), and no network shares mounted.
2. Give it .NET 8, git (for read-only use) and the Claude CLI, and Claude auth through a scoped token:
   `claude setup-token` as that user (or as the owner, then store the token for the bot as `DLSS5_BOT_CLAUDE_TOKEN`).
3. Write `%LocalAppData%\dlss5updater-releasebot\isolation.ps1` (owner side). The bot calls it as
   `& isolation.ps1 -User <name> -Exe <claude.exe> -ArgumentList <argv> -WorkingDirectory <sandbox\repo>
   -Environment <hashtable> -Stdin <prompt> -TimeoutMinutes <n>` and expects `@{ ExitCode = <int>; Stdout = <string> }`.
   It should copy the sandbox folder (repo clone, `in\`, `out\`) to a folder only that user and the owner can write,
   start Claude there as that user (for example a scheduled task with that user's stored credentials, or
   `Start-Process -Credential` with a secret from Credential Manager), wait, and copy the sandbox back. The bot (owner
   context) then only reads files from it, as now: it never runs git or anything else from Claude's folder, and only
   commits the checked files into its own clean worktree of the bare mirror.
4. Point `Get-ClaudeEnv` paths (TEMP, APPDATA, `CLAUDE_CONFIG_DIR`, NUGET_PACKAGES) at that user's profile in the hook.
   It must not be able to read the bot's browser profile (`browser-profile\`, the owner's Buy Me a Coffee session).
   That user must not be able to write anything the owner side runs or trusts: `%LocalAppData%\dlss5updater-releasebot`
   (`bin\` is what the task runs, `nohooks\` is the bot git's hooks folder, `state.json` holds the auto/review
   marks, `mirror.git` is what gets pushed), the repo checkouts, and the owner's NuGet cache.
5. Dry-run it: `pwsh ...\release-bot.ps1 -DryRun -RunAs dlss5bot -Simulate 'optiscaler-nr=v0.9.51>v0.9.70'`.
6. Enable: `pwsh tools/release-bot/install.ps1 -RunAs dlss5bot -Enable`.

## Files and secrets
- Installed copy: `%LocalAppData%\dlss5updater-releasebot\bin` (`release-bot.ps1`, `ReleaseBot.psm1`, `prompt.md`;
  the task runs this, not the repo). Re-run `install.ps1` after changing them. It registers the task disabled unless
  you pass `-Enable`.
- State: `...\state.json` (last seen value per source, ETags, processed and seen drops, failure counts, bot PRs with
  mode / head / patch-id / needs-review, the pending auto release, prereleases waiting for promotion, cached proof
  hashes per OptiScaler release asset).
- Logs: `...\logs\release-bot-<date>.log` and `claude-<run>.json`, deleted after 30 days.
- `...\mirror.git`: bare clone only the bot touches; every run makes its worktrees from it and removes them.
  `...\claude-home`: Claude's config, AppData and NuGet folders. `...\browser-profile`: the bot's Buy Me a Coffee
  login (`-LoginBuyMeACoffee`). `...\repo` from the first version of the bot can be deleted.
- User env vars (never in the repo or logs): `DLSS5_DROP_PW` (copyparty account `dlss5drop`, only sees the drop
  folder), `DLSS5_BOT_NTFY_TOKEN` (ntfy user `alerter`, token label `dlss5updater-releasebot`) and
  `DLSS5_BOT_CLAUDE_TOKEN` (`claude setup-token`). gh uses its own login, only in the bot's own process.

## Run, test, pause
```powershell
Invoke-Pester tools/release-bot                    # unit tests of the decisions (ReleaseBot.psm1), Pester 5+

pwsh tools/release-bot/install.ps1                 # install, task registered DISABLED
pwsh tools/release-bot/install.ps1 -Remove         # remove the task (state, logs and mirror stay in %LocalAppData%)

# by hand; -DryRun never pushes, opens PRs, labels, merges, tags, edits releases, notifies or moves drops
$bot = "$env:LOCALAPPDATA\dlss5updater-releasebot\bin\release-bot.ps1"
pwsh $bot -DryRun -SkipClaude                      # plumbing only, Claude reports "nothing to change"
pwsh $bot -DryRun -StateFile $env:TEMP\s.json -Simulate 'optiscaler-nr=v0.9.33>v0.9.34'               # auto path
pwsh $bot -DryRun -StateFile $env:TEMP\s.json -Simulate 'optiscaler-nr=v0.9.51>v0.9.70' -FakeClaude   # review path
```
A dry run saves state only to a `-StateFile` given on the command line, never to the real one, so simulations can
seed a baseline first (the first run with a new state file only records what is out there). `-Simulate
source=old>new` pretends the app pins `old` and upstream released `new`; `-FakeClaude` stands in for Claude by bumping
the pin in the sandbox. A dry run without `-SkipClaude`/`-FakeClaude` runs the real Claude in the reduced environment.

To have the bot catch up on a version that came out before it was installed, set that source's `seen` in
`state.json` to the version the app was tested with.
