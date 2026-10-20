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
`%LocalAppData%\dlss5updater-releasebot\browser-profile` (never the owner's normal Chrome profile). Run
`pwsh $env:LOCALAPPDATA\dlss5updater-releasebot\bin\release-bot.ps1 -LoginBuyMeACoffee` once: it opens Chrome (or
Edge) with that profile on the post; log in, check the post shows, close the window. Each run then reads the post
with headless Chrome (`--headless=new --dump-dom`, 90 s timeout) and looks for new `vN Patch N` sections. Logged out,
a changed page or a failed fetch is a log line and one ntfy note asking to log in again (until it works again),
never a failure or a parked change. The new sections go only into Claude's facts file on the review path, marked as
paid content to paraphrase; the bot never puts their text in the repo, PRs, notices or release notes, or in its state.
Only the post (`<article>`) is read, and parsing stops for good at the first page region (comments, footer, sidebar,
the post's end) or line that looks like the comment box, so members' comments never reach Claude and can't start a
section of their own. Inside the post a section ends at the next patch heading, a rule, or a heading of the same or a
higher level than its own (an `<h4>` inside an `<h3>` patch section stays in it). A new section of another major than
`$LecramNotesMajor` (release-bot.ps1, 11) or more than 5 patches past the last one seen is refused: a log line and
one ntfy note, no Claude run, and "seen" stays. Before publishing, the bot checks Claude's diff and summary
for any run of 8 words from the notes: a quoting diff is refused, a quoting summary is left out of the PR.

## Two paths
**Auto path (no Claude).** Only when every new item is an OptiScaler build, no drop is pending, no bot PR is open,
and `origin/main` is exactly the last release: `gh release view v<main version>` shows a published full release (not
a draft, not a prerelease, so a bot prerelease still in its 24 h window or one the owner holds back blocks the next
bump), the tag `v<main version>` is fetched fresh from origin (the mirror's local copy is deleted first, and a failed
fetch blocks), and `git diff v<main version> origin/main` is empty outside `docs/`. An auto release ships all of main,
so any unreleased work on main sends the change to review. A gh error other than "not found" stops the run (machine
failure). A tag push that fails is deleted from the mirror again.
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
the same `git patch-id` as the diff it verified, that the PR head is still its own commit and that the squash commit's
parent is still the released state (same check against `<sha>^1`), and tags `v<version>`
with the message `release-bot prerelease v<version>`. `release.yml` publishes such tags as a **prerelease**, which the
app's self-update ignores. After 24 h the bot promotes it to the regular (latest) release, unless the owner deleted,
drafted, promoted or edited it (name, text, assets) in the meantime. The first time it sees the release, the text must
equal the notes template it wrote and the name the tag, so an edit made before that first look counts too.

**Review path (Claude).** Everything else. The bot makes a worktree from `origin/main` (or from the open bot PR's
branch, merged with main), sets the patch version and starts `docs/releases/v<version>.md` itself, then clones that
into a sandbox. Headless Claude (Opus, [prompt.md](prompt.md), `--max-turns 200`, `--max-budget-usd 15`, 60 min) works
in the sandbox and can't commit. The bot compares the sandbox with its worktree file by file (it never runs git in
Claude's folder, whose `.git/config` Claude could have written), copies the changed files over, commits them with
`git add -- <paths>`, and checks the diff:
- `git diff --raw/--numstat -z --no-renames --no-ext-diff --no-textconv` with `core.quotePath=false`,
  fsmonitor and attribute files off. Any numstat `-` (binary), link, submodule, executable bit or file over 1 MB fails.
- The owner's global git hooks run on the bot's commits and pushes (gitleaks, AI-trailer strip, `.prepush` gate). So
  they never run repo content, the bot refuses to make a worktree, commit or push when the tree has a `.prepush` or
  the mirror has a repo hook, and it checks its own commit messages for AI trailers before every push. This fails
  closed on purpose: once a `.prepush` is committed on `origin/main`, every bot worktree has it and every bot commit
  and push is refused (each run fails with "not running git hooks over repo content") until it is removed from main.
- Off limits: `.github/`, `tools/`, `.claude/`, `AGENTS.md`, `CLAUDE.md`, `.gitattributes`, `.gitignore`,
  `.editorconfig`, `Directory.Build.*`, `NuGet.config`, `global.json`, `*.props`, `*.targets`, scripts, and any
  `.csproj`/`.iss` change other than the app's version line. Allowed: `.cs`, `.xaml`, `.md`, `.txt`, `.ini`, `.cfg`
  under `src/`, `tests/`, `docs/`, `.json` under `tests/` and `docs/`, and `README.md`.
- Version consistent in csproj, installer, README and docs/releases. gitleaks over the diff and the PR text.

The PR gets the `needs-review` label and `gh pr merge --disable-auto`; the bot then reads `autoMergeRequest` back, and
if auto-merge is still on it turns the PR into a draft (GitHub never merges a draft) and sends a priority notice. Claude's summary only appears in it, quoted
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
- `--restricted` (ignores user, project and local settings files, confines the file tools to the sandbox given with `--add-dir`) plus `--settings {"disableAllHooks":true}`, `--permission-prompts none` (anything that would need a person is denied, writes to settings, git and tool config among them) and the empty `CLAUDE_CONFIG_DIR`: the owner's
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
needs full isolation first, and the bot refuses to start the real Claude without `-RunAs <user>` (also in a dry run)
unless you pass `-UnsafeRunClaudeAsOwner`. `install.ps1 -Enable` without `-RunAs` throws.

## Full isolation (required before enabling the schedule)
1. Create a dedicated local Windows user (for example `dlss5bot`), a standard user, not an administrator: no gh
   login, no SSH keys, no env secrets, no access to the owner's profile (check the ACLs of `C:\Users\<owner>` and the
   repos), and no network shares mounted.
2. Give it .NET 8, git (for read-only use) and the Claude CLI, and Claude auth through a scoped token:
   `claude setup-token` as that user (or as the owner, then store the token for the bot as `DLSS5_BOT_CLAUDE_TOKEN`).
3. Write `%LocalAppData%\dlss5updater-releasebot\isolation.ps1` (owner side). The bot calls it as
   `& isolation.ps1 -User <name> -Exe <claude.exe> -ArgumentList <argv> -WorkingDirectory <sandbox\repo>
   -Environment <hashtable> -Stdin <prompt> -TimeoutMinutes <n> -Sandbox <sandbox>` and expects
   `@{ ExitCode = <int>; Stdout = <string>; Sandbox = <the isolated copy> }`. It should:
   - copy the `-Sandbox` folder (repo clone, `in\`, `out\`) to a fresh folder inside the isolation root (below) that
     only that user and the owner can write, and replace the `-Sandbox` path with that folder in `-ArgumentList`,
     `-Environment` values, `-Stdin` and `-WorkingDirectory`;
   - start Claude there as that user (for example a scheduled task with that user's stored credentials, or
     `Start-Process -Credential` with a secret from Credential Manager) and wait;
   - before it returns, try to stop every process still running as that user (`taskkill /F /FI "USERNAME eq <user>"`;
     dotnet build servers linger). Still recommended, but the bot doesn't rely on it: a task running with a Limited
     token likely can't see or kill another user's processes;
   - **not** copy anything back itself, and return the folder as `Sandbox`.
   The isolation root is a folder of its own for these sandboxes (for example `C:\dlss5bot-iso`), outside
   `%LocalAppData%\dlss5updater-releasebot` and the owner's profile; pass it as `-IsolationRoot` (install.ps1 hands it
   to the task). The bot copies the sandbox back itself (`Copy-SandboxBack` in `ReleaseBot.psm1`) and refuses a
   sandbox whose resolved path isn't inside the isolation root, or is inside the bot's own folder. The isolated user
   could plant a junction or symlink to the owner's files (`~\Notes`, `~\.claude`, `~\.ssh`) for a plain owner-side
   `Copy-Item -Recurse` to follow into a public PR, and a process of theirs that is still running could swap a folder
   for a junction after any check by path. So the bot first walks the tree without following links and aborts on any
   link (the folder itself included), then opens every file by handle without following a link, asks Windows for the
   handle's real path (`GetFinalPathNameByHandle`), requires it inside the sandbox, refuses a file with more than one
   hard link, and copies the bytes from that same handle. Any violation, more than 20,000 files or more than 200 MB
   aborts and removes everything copied so far. Build output (`bin`, `obj`, ...) is left behind. Then, as before, it
   only reads files from the copy: it never runs git or anything else from Claude's folder, rejects links again in the
   tree compare, and only commits the checked files into its own clean worktree of the bare mirror.
4. Point `Get-ClaudeEnv` paths (TEMP, APPDATA, `CLAUDE_CONFIG_DIR`, NUGET_PACKAGES) at that user's profile in the hook.
   It must not be able to read the bot's browser profile (`browser-profile\`, the owner's Buy Me a Coffee session).
   That user must not be able to write anything the owner side runs or trusts: `%LocalAppData%\dlss5updater-releasebot`
   (`bin\` is what the task runs, `mirror.git\hooks` must stay free of repo hooks, `state.json` holds the auto/review
   marks, `mirror.git` is what gets pushed), the repo checkouts, and the owner's NuGet cache.
5. Re-run `pwsh tools/release-bot/install.ps1` (still without `-Enable`) so the installed `bin\` has the current
   `release-bot.ps1` and `ReleaseBot.psm1` (an older install has no `ReleaseBot.psm1` at all), then dry-run the
   installed copy: `pwsh $env:LOCALAPPDATA\dlss5updater-releasebot\bin\release-bot.ps1 -DryRun -RunAs dlss5bot
   -IsolationRoot C:\dlss5bot-iso -StateFile $env:TEMP\s.json -Simulate 'optiscaler-nr=v0.9.51>v0.9.70'`.
6. Limit what a hijacked run can do with the network: give the bot a low-value Claude token (its own account or a
   spending-capped one, revoked if it leaks), and/or block that user's outbound traffic except api.anthropic.com and
   GitHub (Windows Firewall rule per user, or a proxy). WebFetch to GitHub is allowed, so data could still leave
   that way; the user must simply not have anything worth sending.
7. Enable: `pwsh tools/release-bot/install.ps1 -RunAs dlss5bot -IsolationRoot C:\dlss5bot-iso -Enable` (`-Enable`
   without `-RunAs` or `-IsolationRoot` throws).

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
  login (`-LoginBuyMeACoffee`). `...\nohooks`: the empty hooks folder in Claude's sandbox git config.
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
the pin in the sandbox. A dry run without `-SkipClaude`/`-FakeClaude` runs the real Claude in the reduced environment,
so it needs `-RunAs` (or `-UnsafeRunClaudeAsOwner`) like a real run.

Later improvement: the auto-path proof compares against the old tag's ini and zip file list, fetched live (and cached
by asset id). Storing the tested build's ini and entry-list hashes in the repo next to `TestedTag` would pin the
comparison to what was really tested, even if upstream re-tags or edits the old release.

To have the bot catch up on a version that came out before it was installed, set that source's `seen` in
`state.json` to the version the app was tested with.
