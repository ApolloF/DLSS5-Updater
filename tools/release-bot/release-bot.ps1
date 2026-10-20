#Requires -Version 7.2
<#
.SYNOPSIS
  DLSS5 Updater release bot: watches the mods the app pins and the copyparty drop folder, lets headless Claude Code
  update the app in a fresh worktree, opens (or updates) one bot PR, and auto-releases config-neutral bumps.
.DESCRIPTION
  See README.md next to this file. Runs from the installed copy in %LocalAppData%\dlss5updater-releasebot\bin.
  -DryRun does everything except push, PR, merge, tag, notify, moving drop files and saving state.
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$SkipClaude,
    [string]$StateFile,
    [int]$ClaudeTimeoutMinutes = 60,
    [double]$ClaudeBudgetUsd = 15,
    [int]$ClaudeMaxTurns = 200,
    [int]$MergeWaitMinutes = 20
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$Repo = 'ApolloF/dlss5updater'
$Root = Join-Path $env:LOCALAPPDATA 'dlss5updater-releasebot'
$LogDir = Join-Path $Root 'logs'
$WorkRoot = Join-Path $Root 'work'
$Clone = Join-Path $Root 'repo'
if (-not $StateFile) { $StateFile = Join-Path $Root 'state.json' }
$PromptFile = Join-Path $PSScriptRoot 'prompt.md'
$DropUrl = 'https://copyparty.home.apollof.nl/dlss5updater-drop/'
$NtfyUrl = 'https://ntfy.apollof.nl/claude-work'
$BranchPrefix = 'release-bot/'
$ClaudeExe = Join-Path $env:USERPROFILE '.local\bin\claude.exe'
$RunId = Get-Date -Format 'yyyyMMdd-HHmmss'
$Work = Join-Path $WorkRoot $RunId
$Ua = 'dlss5updater-releasebot'

# The mods the app pins a tested version for. Asset: the release asset the app installs (null = any release).
$GhSources = @(
    @{ Id = 'optiscaler-nr'; Repo = 'ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass'; Asset = '^OptiScaler-NR-v[\d.]+\.zip$' }
    @{ Id = 'optiscaler-f5'; Repo = 'janblade/OptiScaler-F5-DLSSNR-Multipass'; Asset = '^OptiScaler-DLSSNR-F5-[\d.]+\.zip$' }
    @{ Id = 'gitc-uplift'; Repo = 'ghostinthecamera/GITC-Uplift'; Asset = $null }
    @{ Id = 'mfg-unlock'; Repo = 'mavismmg/MFGAdaUnlock-RenoDx'; Asset = '^renodx-mfgunlock\.addon64$' }
    @{ Id = 'dlss'; Repo = 'NVIDIA/DLSS'; Asset = $null }
    @{ Id = 'streamline'; Repo = 'NVIDIA-RTX/Streamline'; Asset = '^streamline-sdk-v[\d.]+\.zip$' }
)

New-Item -ItemType Directory -Force $Root, $LogDir, $WorkRoot | Out-Null
$LogFile = Join-Path $LogDir "release-bot-$(Get-Date -Format 'yyyyMMdd').log"

function Log([string]$msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [$RunId]$(if ($DryRun) { ' [dry]' }) $msg"
    Add-Content -Path $LogFile -Value $line -Encoding utf8
    Write-Host $line
}

function Write-Atomic([string]$path, [string]$text) {
    $tmp = "$path.tmp"
    [IO.File]::WriteAllText($tmp, $text, [Text.UTF8Encoding]::new($false))
    Move-Item -Force $tmp $path
}

function Invoke-Native([string]$exe, [string[]]$argv, [string]$cwd = $null, [switch]$Quiet) {
    $psi = [Diagnostics.ProcessStartInfo]::new($exe)
    foreach ($a in $argv) { $psi.ArgumentList.Add($a) }
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false
    if ($cwd) { $psi.WorkingDirectory = $cwd }
    $p = [Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEndAsync(); $err = $p.StandardError.ReadToEndAsync()
    $p.WaitForExit()
    $r = [pscustomobject]@{ Code = $p.ExitCode; Out = $out.Result.Trim(); Err = $err.Result.Trim() }
    if (-not $Quiet -and $r.Code -ne 0) { Log "  $exe $($argv -join ' ') -> exit $($r.Code): $($r.Err)" }
    $r
}

function Git([string[]]$argv, [string]$cwd = $Clone, [switch]$Check) {
    $r = Invoke-Native 'git' $argv $cwd
    if ($Check -and $r.Code -ne 0) { throw "git $($argv -join ' ') failed: $($r.Err)" }
    $r
}

function Gh([string[]]$argv, [string]$cwd = $Clone, [switch]$Check) {
    $r = Invoke-Native 'gh' $argv $cwd
    if ($Check -and $r.Code -ne 0) { throw "gh $($argv -join ' ') failed: $($r.Err)" }
    $r
}

function Get-Secret([string]$name) {
    # A shell started before the variable was set only has it in the User scope.
    [Environment]::GetEnvironmentVariable($name) ?? [Environment]::GetEnvironmentVariable($name, 'User')
}

function Send-Notice([string]$title, [string]$body, [string]$click = $null, [int]$priority = 3, [string]$tags = 'robot') {
    if ($DryRun) { Log "would notify: $title | $($body -replace "`n", ' / ')"; return }
    $token = Get-Secret 'DLSS5_BOT_NTFY_TOKEN'
    if (-not $token) { Log 'DLSS5_BOT_NTFY_TOKEN is not set; notice not sent'; return }
    $h = @{ Authorization = "Bearer $token"; Title = ($title -replace '[^\x20-\x7E]', ''); Priority = "$priority"; Tags = $tags }
    if ($click) { $h.Click = $click }
    try {
        Invoke-RestMethod -Method Post -Uri $NtfyUrl -Headers $h -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 20 | Out-Null
        Log "notified: $title"
    } catch { Log "notice failed: $($_.Exception.Message)" }
}

# ---------- state ----------

function Read-State {
    if (-not (Test-Path $StateFile)) { return $null }
    $s = Get-Content -Raw $StateFile | ConvertFrom-Json -AsHashtable
    foreach ($k in 'items', 'etags', 'failures', 'dropHashes') { if (-not $s[$k]) { $s[$k] = @{} } }
    $s
}

function Save-State($s) {
    if ($DryRun) { Log 'dry run: state not saved'; return }
    Write-Atomic $StateFile ($s | ConvertTo-Json -Depth 8)
}

# ---------- upstream checks (cheap, conditional where the server supports it) ----------

$script:GhToken = $null
function Get-Conditional([string]$url, $state, [hashtable]$headers = @{}) {
    $cached = $state.etags[$url]
    $h = @{ 'User-Agent' = $Ua } + $headers
    if ($cached -and $cached.etag) { $h['If-None-Match'] = $cached.etag }
    $resp = Invoke-WebRequest -Uri $url -Headers $h -SkipHttpErrorCheck -TimeoutSec 30
    $code = [int]$resp.StatusCode
    if ($code -eq 304 -and $cached) { return @{ Changed = $false; Value = $cached.value } }
    if ($code -eq 403 -or $code -eq 429) { throw "rate limited ($code) on $url" }
    if ($code -ne 200) { throw "HTTP $code on $url" }
    $etag = $resp.Headers['ETag'] | Select-Object -First 1
    @{ Changed = $true; Body = [string]$resp.Content; ETag = $etag }
}

function Check-GhRelease($src, $state) {
    if (-not $script:GhToken) {
        $t = Invoke-Native 'gh' @('auth', 'token') -Quiet
        $script:GhToken = if ($t.Code -eq 0) { $t.Out } else { '' }
    }
    $h = @{ Accept = 'application/vnd.github+json' }
    if ($script:GhToken) { $h.Authorization = "Bearer $($script:GhToken)" }
    $url = "https://api.github.com/repos/$($src.Repo)/releases?per_page=15"
    $r = Get-Conditional $url $state $h
    if (-not $r.Changed) { return $r.Value }
    $rels = @($r.Body | ConvertFrom-Json) | Where-Object { -not $_.draft }
    if ($src.Asset) { $rels = $rels | Where-Object { $_.assets | Where-Object name -Match $src.Asset } }
    $pick = ($rels | Where-Object { -not $_.prerelease } | Select-Object -First 1) ?? ($rels | Select-Object -First 1)
    $value = if ($pick) {
        @{ seen = $pick.tag_name; detail = "$($src.Repo) $($pick.tag_name)$(if ($pick.prerelease) { ' (prerelease)' }), published $($pick.published_at), $($pick.html_url)"
           assets = @($pick.assets | ForEach-Object { "$($_.name) ($($_.size) bytes$(if ($_.digest) { ", $($_.digest)" }))" }) }
    } else { @{ seen = '(none)'; detail = "$($src.Repo): no matching release"; assets = @() } }
    $state.etags[$url] = @{ etag = $r.ETag; value = $value }
    $value
}

function Check-ReShade($state) {
    $r = Get-Conditional 'https://reshade.me' $state
    if (-not $r.Changed) { return $r.Value }
    $versions = [regex]::Matches($r.Body, 'downloads/ReShade_Setup_(\d+(?:\.\d+)+)_Addon\.exe') | ForEach-Object { $_.Groups[1].Value }
    $latest = $versions | Sort-Object { [version]$_ } -Descending | Select-Object -First 1
    if (-not $latest) { throw 'reshade.me: no add-on download link found' }
    $value = @{ seen = $latest; detail = "ReShade $latest with add-on support (reshade.me)"; assets = @("ReShade_Setup_$($latest)_Addon.exe") }
    $state.etags['https://reshade.me'] = @{ etag = $r.ETag; value = $value }
    $value
}

function Check-Lecram($state) {
    # Same public channel as Core/MediaFireClient.cs (DLSS5-NR-Setup's update folder).
    $files = @()
    for ($chunk = 1; $chunk -le 10; $chunk++) {
        $url = "https://www.mediafire.com/api/1.5/folder/get_content.php?folder_key=wrwi1qkpgy0qu&content_type=files&chunk=$chunk&response_format=json"
        $resp = Invoke-WebRequest -Uri $url -Headers @{ 'User-Agent' = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) $Ua" } -SkipHttpErrorCheck -TimeoutSec 30
        if ([int]$resp.StatusCode -ne 200) { throw "MediaFire HTTP $($resp.StatusCode)" }
        $content = ($resp.Content | ConvertFrom-Json).response.folder_content
        $files += @($content.files)
        if ($content.more_chunks -ne 'yes') { break }
    }
    $relevant = $files | Where-Object { $_.filename -match '^(DLSS5-NR-.+\.(zip|addon64|exe)|dlss5-nr\.addon64|channel\.json)$' } |
        Sort-Object created_utc -Descending
    if (-not $relevant) { throw 'MediaFire listing has no DLSS5-NR files' }
    $newest = $relevant | Where-Object filename -NE 'channel.json' | Select-Object -First 1
    $set = ($relevant | ForEach-Object { "$($_.filename):$($_.hash.Substring(0, 12))" } | Sort-Object) -join ' '
    @{ seen = $set; detail = "Lecram MediaFire channel, newest $($newest.filename) (created $($newest.created_utc), sha256 $($newest.hash))"
       assets = @($relevant | ForEach-Object { "$($_.filename) sha256 $($_.hash) created $($_.created_utc) $($_.links.normal_download)" }) }
}

# ---------- drop folder ----------

function Get-DropPw {
    $pw = Get-Secret 'DLSS5_DROP_PW'
    if (-not $pw) { throw 'DLSS5_DROP_PW is not set' }
    $pw
}

function Get-DropFiles {
    $resp = Invoke-WebRequest -Uri "$($DropUrl)?ls" -Headers @{ PW = (Get-DropPw); 'User-Agent' = $Ua } -SkipHttpErrorCheck -TimeoutSec 30
    if ([int]$resp.StatusCode -ne 200) { throw "drop folder HTTP $($resp.StatusCode)" }
    @(($resp.Content | ConvertFrom-Json).files) | ForEach-Object { [uri]::UnescapeDataString($_.href) } | Where-Object { $_ -and $_ -notmatch '[\\/]' }
}

function Describe-DropFile([string]$path) {
    $fi = Get-Item -LiteralPath $path
    $lines = @("file: $($fi.Name)", "size: $($fi.Length) bytes", "sha256: $((Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLower())",
               "local copy (analysis only, never commit or copy into the repo): $path")
    $vi = [Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    if ($vi.FileVersion -or $vi.ProductVersion) {
        $lines += "version resource: FileVersion=$($vi.FileVersion) ProductVersion=$($vi.ProductVersion) OriginalFilename=$($vi.OriginalFilename) ProductName=$($vi.ProductName) CompanyName=$($vi.CompanyName)"
    }
    if ($fi.Extension -eq '.zip') {
        try {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $z = [IO.Compression.ZipFile]::OpenRead($path)
            try { $lines += 'zip entries:'; $lines += $z.Entries | Select-Object -First 200 | ForEach-Object { "  $($_.FullName) ($($_.Length) bytes)" } }
            finally { $z.Dispose() }
        } catch { $lines += "zip unreadable: $($_.Exception.Message)" }
    }
    $lines -join "`n"
}

function Move-DropFile([string]$name) {
    if ($DryRun) { Log "would move drop file $name to processed/"; return }
    $dest = "/dlss5updater-drop/processed/$RunId-$name"
    $resp = Invoke-WebRequest -Method Post -Uri "$DropUrl$([uri]::EscapeDataString($name))?move=$([uri]::EscapeDataString($dest))" `
        -Headers @{ PW = (Get-DropPw); 'User-Agent' = $Ua } -SkipHttpErrorCheck -TimeoutSec 30
    if ([int]$resp.StatusCode -ge 300) { Log "moving $name failed: HTTP $($resp.StatusCode)" } else { Log "moved $name to processed/" }
}

# ---------- repo helpers ----------

function Get-RepoVersion([string]$dir) {
    $m = Select-String -Path (Join-Path $dir 'src\DLSSUpdater\DLSSUpdater.csproj') -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    $m.Matches[0].Groups[1].Value
}

function Test-VersionConsistent([string]$dir, [string]$version) {
    $iss = Get-Content -Raw (Join-Path $dir 'installer\DLSSUpdater.iss')
    $readme = Get-Content -Raw (Join-Path $dir 'README.md')
    (Get-RepoVersion $dir) -eq $version -and $iss.Contains("/DAppVersion=$version") -and $readme.Contains("/DAppVersion=$version") -and
        (Test-Path (Join-Path $dir "docs\releases\v$version.md"))
}

function Next-Patch([string]$v) { $p = [version]$v; "$($p.Major).$($p.Minor).$($p.Build + 1)" }

function Remove-Worktree([string]$wt) {
    if (-not $wt -or -not (Test-Path $wt)) { return }
    Git @('worktree', 'remove', '--force', $wt) | Out-Null
    if (Test-Path $wt) { Remove-Item -Recurse -Force $wt -ErrorAction SilentlyContinue }
    Git @('worktree', 'prune') | Out-Null
}

function Invoke-Claude([string]$wt, [string]$prompt, [string]$outFile) {
    $allowed = @('Read', 'Edit', 'Write', 'Glob', 'Grep',
        'Bash(dotnet build:*)', 'Bash(dotnet test:*)', 'Bash(dotnet restore:*)',
        'Bash(git status:*)', 'Bash(git diff:*)', 'Bash(git add:*)', 'Bash(git commit:*)', 'Bash(git log:*)', 'Bash(git show:*)',
        'Bash(git rm:*)', 'Bash(git mv:*)', 'Bash(git grep:*)',
        'Bash(strings:*)', 'Bash(python:*)', 'Bash(py:*)', 'Bash(unzip -l:*)', 'Bash(sha256sum:*)', 'Bash(ls:*)', 'Bash(grep:*)',
        'WebFetch(domain:github.com)', 'WebFetch(domain:raw.githubusercontent.com)', 'WebFetch(domain:api.github.com)')
    $denied = @('Bash(git push:*)', 'Bash(gh:*)', 'Bash(curl:*)', 'Bash(git tag:*)', 'Bash(git reset:*)', 'Bash(git checkout:*)', 'WebSearch')
    $argv = @('-p', '--model', 'opus', '--output-format', 'json', '--max-turns', "$ClaudeMaxTurns",
        '--max-budget-usd', "$ClaudeBudgetUsd", '--permission-mode', 'acceptEdits', '--no-session-persistence',
        '--strict-mcp-config', '--add-dir', $Work, '--allowedTools') + $allowed + @('--disallowedTools') + $denied
    $psi = [Diagnostics.ProcessStartInfo]::new($ClaudeExe)
    foreach ($a in $argv) { $psi.ArgumentList.Add($a) }
    $psi.WorkingDirectory = $wt
    $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false
    $p = [Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEndAsync(); $err = $p.StandardError.ReadToEndAsync()
    $p.StandardInput.Write($prompt); $p.StandardInput.Close()
    if (-not $p.WaitForExit($ClaudeTimeoutMinutes * 60 * 1000)) {
        Log "claude timed out after $ClaudeTimeoutMinutes min; killing it"
        $p.Kill($true); $p.WaitForExit()
    }
    Write-Atomic $outFile $out.Result
    $meta = try { $out.Result | ConvertFrom-Json } catch { $null }
    Log "claude exit $($p.ExitCode), turns $($meta.num_turns), cost `$$([math]::Round([double]$meta.total_cost_usd, 2)), error $($meta.is_error)"
    $p.ExitCode
}

# ---------- release of a merged auto-merge PR ----------

function Complete-PendingRelease($state) {
    $pending = $state.pending
    if (-not $pending) { return }
    $v = Gh @('pr', 'view', "$($pending.pr)", '--repo', $Repo, '--json', 'state,mergeCommit,url') -cwd $Root
    if ($v.Code -ne 0) { Log "pending PR #$($pending.pr): cannot read it, trying next run"; return }
    $pr = $v.Out | ConvertFrom-Json
    if ($pr.state -eq 'CLOSED') { Log "pending PR #$($pending.pr) was closed without merge"; $state.Remove('pending'); return }
    if ($pr.state -ne 'MERGED') { Log "pending PR #$($pending.pr) not merged yet ($($pr.state))"; return }
    $tag = "v$($pending.version)"
    Git @('fetch', '--quiet', 'origin', '--tags') -Check | Out-Null
    $sha = $pr.mergeCommit.oid
    $csproj = Git @('show', "$($sha):src/DLSSUpdater/DLSSUpdater.csproj")
    if ($csproj.Out -notmatch "<Version>$([regex]::Escape($pending.version))</Version>") {
        Log "merge commit $sha does not carry version $($pending.version); not tagging"
        Send-Notice 'DLSS5 Updater bot: release not tagged' "PR #$($pending.pr) merged, but the csproj version at $sha is not $($pending.version). Tag it by hand." $pr.url 4 'warning'
        $state.Remove('pending'); return
    }
    if ((Git @('ls-remote', '--tags', 'origin', "refs/tags/$tag")).Out) { Log "$tag already exists on origin"; $state.Remove('pending'); return }
    if ($DryRun) { Log "would tag $tag on $sha and push it"; return }
    Git @('tag', '-a', $tag, $sha, '-m', $tag) -Check | Out-Null
    Git @('push', 'origin', "refs/tags/$tag") -Check | Out-Null
    Log "tagged $tag on $sha; release.yml publishes it"
    Send-Notice "DLSS5 Updater $tag released" "PR #$($pending.pr) merged and $tag tagged. release.yml builds and publishes it now.`n$($pending.summary)" "https://github.com/$Repo/releases/tag/$tag" 3 'rocket'
    $state.Remove('pending')
}

# ---------- main ----------

Get-ChildItem $LogDir -File | Where-Object LastWriteTime -LT (Get-Date).AddDays(-30) | Remove-Item -Force -ErrorAction SilentlyContinue

$lockPath = Join-Path $Root 'run.lock'
try { $lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
catch { Log 'another run holds the lock; exiting'; exit 0 }

$wt = $null
$exit = 0
try {
    Log "start (state $StateFile)"
    foreach ($tool in 'git', 'gh') { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool not on PATH" } }
    # Stale work folders from a killed run (older than a day) are removed; the share keeps nothing locally.
    Get-ChildItem $WorkRoot -Directory | Where-Object LastWriteTime -LT (Get-Date).AddDays(-1) | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    if (-not (Test-Path (Join-Path $Clone '.git'))) {
        Log "cloning $Repo"
        $c = Invoke-Native 'gh' @('repo', 'clone', $Repo, $Clone, '--', '--quiet')
        if ($c.Code -ne 0) { throw "clone failed: $($c.Err)" }
    }
    Git @('worktree', 'prune') | Out-Null

    $state = Read-State
    $seeding = $null -eq $state
    if ($seeding) { $state = @{ items = @{}; etags = @{}; failures = @{}; dropHashes = @{} } }

    Complete-PendingRelease $state

    # Upstream checks. A source that fails (offline, rate limit) is skipped and checked again next run.
    $changes = [Collections.Generic.List[hashtable]]::new()
    $checks = @($GhSources | ForEach-Object { $s = $_; @{ Key = $s.Id; Run = { Check-GhRelease $s $state }.GetNewClosure() } }) +
        @{ Key = 'reshade'; Run = { Check-ReShade $state } } + @{ Key = 'lecram'; Run = { Check-Lecram $state } }
    $current = @{}
    foreach ($c in $checks) {
        try {
            $v = & $c.Run
            $current[$c.Key] = $v
            $old = $state.items[$c.Key]
            if (-not $old) { Log "$($c.Key): baseline $($v.seen)" }
            elseif ($old.seen -ne $v.seen) { Log "$($c.Key): $($old.seen) -> $($v.seen)"; $changes.Add(@{ Key = $c.Key; Old = $old.seen; New = $v.seen; Detail = $v.detail; Assets = $v.assets }) }
            else { Log "$($c.Key): unchanged ($(($v.seen -split ' ')[0]))" }
        } catch { Log "$($c.Key): check skipped: $($_.Exception.Message)" }
    }

    # Drop folder: download everything new into the local work folder (outside the repo).
    $dropped = [Collections.Generic.List[hashtable]]::new()
    try {
        $names = @(Get-DropFiles)
        Log "drop folder: $($names.Count) file(s)"
        if ($names.Count) { New-Item -ItemType Directory -Force (Join-Path $Work 'drop') | Out-Null }
        foreach ($n in $names) {
            $local = Join-Path $Work "drop\$n"
            $resp = Invoke-WebRequest -Uri "$DropUrl$([uri]::EscapeDataString($n))" -Headers @{ PW = (Get-DropPw); 'User-Agent' = $Ua } -OutFile $local -SkipHttpErrorCheck -PassThru -TimeoutSec 600
            if ([int]$resp.StatusCode -ne 200) { Log "drop ${n}: download HTTP $($resp.StatusCode), retry next run"; continue }
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $local).Hash.ToLower()
            if ($state.dropHashes[$hash]) { Log "drop ${n}: already processed ($($state.dropHashes[$hash])), moving it away"; Move-DropFile $n; continue }
            $dropped.Add(@{ Name = $n; Path = $local; Hash = $hash; Facts = (Describe-DropFile $local) })
            Log "drop ${n}: new ($hash)"
        }
    } catch { Log "drop folder skipped: $($_.Exception.Message)" }

    if ($seeding) {
        foreach ($k in $current.Keys) { $state.items[$k] = @{ seen = $current[$k].seen; detail = $current[$k].detail; updated = (Get-Date).ToString('o') } }
        if ($dropped.Count) { Log 'first run: drop files stay for the next run' }
        Save-State $state
        Log "first run: baseline saved for $($current.Count) source(s); nothing processed"
        return
    }

    # Changes that already failed three times are parked until their version moves again.
    $todo = @($changes | Where-Object { [int]$state.failures["$($_.Key)|$($_.New)"] -lt 3 })
    $todoDrops = @($dropped | Where-Object { [int]$state.failures["drop|$($_.Hash)"] -lt 3 })
    if (-not $todo.Count -and -not $todoDrops.Count) {
        # A source added to the bot later gets its baseline here; parked changes keep their old value.
        foreach ($k in $current.Keys) { if (-not $state.items[$k]) { $state.items[$k] = @{ seen = $current[$k].seen; detail = $current[$k].detail; updated = (Get-Date).ToString('o') } } }
        Save-State $state
        Log 'nothing new'
        return
    }

    # ---- something new: one Claude run in a fresh worktree ----
    Git @('fetch', '--quiet', '--prune', 'origin') -Check | Out-Null
    $openPr = $null
    $list = Gh @('pr', 'list', '--repo', $Repo, '--state', 'open', '--json', 'number,headRefName,url,body,title') -Check
    $openPr = @($list.Out | ConvertFrom-Json) | Where-Object { $_.headRefName.StartsWith($BranchPrefix) } | Select-Object -First 1

    New-Item -ItemType Directory -Force $Work | Out-Null
    $wt = Join-Path $Work 'wt'
    $mainVersion = (Git @('show', 'origin/main:src/DLSSUpdater/DLSSUpdater.csproj') -Check).Out -replace '(?s).*<Version>([^<]+)</Version>.*', '$1'
    if ($openPr) {
        $branch = $openPr.headRefName
        Log "updating open bot PR #$($openPr.number) ($branch)"
        Git @('worktree', 'add', '--quiet', '-B', $branch, $wt, "origin/$branch") -Check | Out-Null
        $m = Git @('merge', '--no-edit', 'origin/main') $wt
        if ($m.Code -ne 0) { Git @('merge', '--abort') $wt | Out-Null; throw "bot PR #$($openPr.number) conflicts with main; resolve it by hand" }
        # The PR keeps its own bump; it only needs a new one when main has caught up with it.
        $prVersion = Get-RepoVersion $wt
        $version = if ([version]$prVersion -gt [version]$mainVersion) { $prVersion } else { Next-Patch $mainVersion }
    } else {
        $branch = "$BranchPrefix$RunId"
        Git @('worktree', 'add', '--quiet', '-b', $branch, $wt, 'origin/main') -Check | Out-Null
        $version = Next-Patch $mainVersion
    }
    $before = (Git @('rev-parse', 'HEAD') $wt -Check).Out
    Log "worktree $wt on $branch, target version $version"

    $facts = [Text.StringBuilder]::new()
    [void]$facts.AppendLine("# Release bot run $RunId`n")
    [void]$facts.AppendLine("Target version: $version (main is at $mainVersion).")
    [void]$facts.AppendLine($(if ($openPr) { "Mode: UPDATE the open bot PR #$($openPr.number) on branch $branch. Earlier bot commits are already on this branch; keep them, add to them, and extend the existing docs/releases/v$version.md instead of starting over." } else { "Mode: NEW bot PR on a fresh branch from origin/main." }))
    [void]$facts.AppendLine("`n## Upstream changes`n")
    if (-not $todo.Count) { [void]$facts.AppendLine('(none)') }
    foreach ($c in $todo) {
        [void]$facts.AppendLine("### $($c.Key): $($c.Old) -> $($c.New)`n$($c.Detail)`nAssets:")
        foreach ($a in $c.Assets) { [void]$facts.AppendLine("- $a") }
        [void]$facts.AppendLine()
    }
    [void]$facts.AppendLine("## Dropped builds (copyparty drop folder)`n")
    if (-not $todoDrops.Count) { [void]$facts.AppendLine('(none)') }
    foreach ($d in $todoDrops) { [void]$facts.AppendLine("### $($d.Name)`n$($d.Facts)`n") }
    $factsFile = Join-Path $Work 'facts.md'
    $resultFile = Join-Path $Work 'result.json'
    Write-Atomic $factsFile $facts.ToString()

    $prompt = (Get-Content -Raw $PromptFile) + "`n`n---`nFacts file: $factsFile`nResult file: $resultFile`nTarget version: $version`nWorktree (your working directory): $wt`n"
    if ($SkipClaude) {
        Log 'skip-claude: writing a no-change result instead of running Claude'
        Write-Atomic $resultFile (@{ status = 'nochange'; config_changed = $false; title = ''; summary = 'Claude skipped (test run).' } | ConvertTo-Json)
    } else {
        Log "running claude (opus, timeout $ClaudeTimeoutMinutes min, budget `$$ClaudeBudgetUsd, max turns $ClaudeMaxTurns)"
        Invoke-Claude $wt $prompt (Join-Path $LogDir "claude-$RunId.json") | Out-Null
    }
    if (-not (Test-Path $resultFile)) { throw 'Claude wrote no result file' }
    $result = Get-Content -Raw $resultFile | ConvertFrom-Json
    Log "claude result: $($result.status), config_changed=$($result.config_changed): $($result.summary)"

    $newCommits = [int](Git @('rev-list', '--count', "$before..HEAD") $wt -Check).Out
    $dirty = (Git @('status', '--porcelain') $wt).Out
    if ($dirty) { Log "worktree has uncommitted changes after Claude:`n$dirty" }

    $markDone = {
        foreach ($c in $todo) { $state.items[$c.Key] = @{ seen = $c.New; detail = $c.Detail; updated = (Get-Date).ToString('o') }; $state.failures.Remove("$($c.Key)|$($c.New)") }
        foreach ($d in $todoDrops) { $state.dropHashes[$d.Hash] = "$($d.Name) $RunId"; $state.failures.Remove("drop|$($d.Hash)"); Move-DropFile $d.Name }
    }
    $what = (@($todo | ForEach-Object { "$($_.Key) $($_.New -replace ' .*', '')" }) + @($todoDrops | ForEach-Object { "drop $($_.Name)" })) -join ', '

    if ($result.status -eq 'nochange' -and $newCommits -eq 0) {
        & $markDone
        Save-State $state
        Send-Notice 'DLSS5 Updater bot: nothing to change' "New: $what`n$($result.summary)" $null 2 'robot'
        return
    }
    if ($result.status -ne 'changed' -or $newCommits -eq 0 -or $dirty) { throw "Claude did not finish cleanly (status $($result.status), new commits $newCommits, dirty $([bool]$dirty))" }

    # Ground truth, not Claude's word: version in all three places, no binaries, real build and tests.
    if (-not (Test-VersionConsistent $wt $version)) { throw "version $version is not consistent in csproj, installer, README and docs/releases" }
    $changed = @((Git @('diff', '--name-only', 'origin/main...HEAD') $wt).Out -split "`n" | Where-Object { $_ })
    $bad = $changed | Where-Object { $_ -match '\.(dll|exe|addon64|addon32|zip|7z|bin)$' -or $_ -match 'nvngx' }
    if ($bad) { throw "binary files in the diff: $($bad -join ', ')" }
    $offLimits = $changed | Where-Object { $_ -match '^(\.github/|tools/release-bot/|AGENTS\.md$)' }
    if ($offLimits) { throw "Claude changed files it must not touch: $($offLimits -join ', ')" }
    $big = $changed | Where-Object { (Test-Path (Join-Path $wt $_)) -and (Get-Item (Join-Path $wt $_)).Length -gt 1MB }
    if ($big) { throw "files over 1 MB in the diff: $($big -join ', ')" }
    Log 'running dotnet build -warnaserror && dotnet test'
    $b = Invoke-Native 'dotnet' @('build', '-warnaserror', '--nologo', '-v', 'q') $wt
    if ($b.Code -ne 0) { throw "build failed: $(($b.Out -split "`n" | Select-Object -Last 15) -join ' | ')" }
    $t = Invoke-Native 'dotnet' @('test', '--nologo', '--no-build', '-v', 'q') $wt
    if ($t.Code -ne 0) { throw "tests failed: $(($t.Out -split "`n" | Select-Object -Last 15) -join ' | ')" }
    Log 'build and tests passed'

    # Auto-release only when Claude says config-neutral AND the diff agrees: no options, UI, help or settings touched.
    $numstat = (Git @('diff', '--numstat', 'origin/main...HEAD', '--', 'src', 'tests') $wt).Out -split "`n" | Where-Object { $_ }
    $codeLines = ($numstat | ForEach-Object { $p = $_ -split "`t"; [int]($p[0] -replace '-', '0') + [int]($p[1] -replace '-', '0') } | Measure-Object -Sum).Sum
    $uiTouched = $changed | Where-Object { $_ -match '(ViewModels/|Views/|\.xaml$|HelpTopics\.cs$|AppSettings\.cs$|GameConfig\.cs$|Ini\.cs$|Config\.cs$)' }
    $neutral = (-not $result.config_changed) -and (-not $uiTouched) -and $codeLines -le 60
    Log "classification: claude config_changed=$($result.config_changed), ui/config files touched=$(@($uiTouched).Count), src+tests lines=$codeLines -> $(if ($neutral) { 'neutral (auto-release)' } else { 'needs review' })"

    $title = if ($result.title) { $result.title } else { "Bot: $what" }
    $body = "$($result.summary)`n`nNew upstream/dropped: $what.`n`n$(if ($neutral) { 'Config-neutral (tested-version bumps only), checks green: auto-merging, then tagging v' + $version + '.' } else { 'Touches options or config, so it waits for a ship it.' })`n`nOpened by tools/release-bot (run $RunId)."
    $bodyFile = Join-Path $Work 'pr-body.md'
    Write-Atomic $bodyFile $body

    if ($DryRun) {
        Log "would push $branch, $(if ($openPr) { "update PR #$($openPr.number)" } else { 'open a PR' }): $title"
        if ($neutral) { Log "would enable auto-merge (squash) and tag v$version after merge" }
        Send-Notice "DLSS5 Updater bot: $title" $body $null 3 'robot'
        return
    }

    Git @('push', 'origin', "HEAD:refs/heads/$branch") $wt -Check | Out-Null
    if ($openPr) {
        Gh @('pr', 'edit', "$($openPr.number)", '--repo', $Repo, '--title', $title, '--body-file', $bodyFile) -Check | Out-Null
        $prNum = $openPr.number; $prUrl = $openPr.url
    } else {
        $created = Gh @('pr', 'create', '--repo', $Repo, '--base', 'main', '--head', $branch, '--title', $title, '--body-file', $bodyFile) -Check
        $prUrl = ($created.Out -split "`n" | Select-Object -Last 1).Trim()
        $prNum = [int]($prUrl -replace '.*/pull/(\d+).*', '$1')
    }
    Log "PR #$prNum $prUrl"

    if ($neutral) {
        Gh @('pr', 'merge', "$prNum", '--repo', $Repo, '--auto', '--squash') -Check | Out-Null
        $state.pending = @{ pr = $prNum; version = $version; summary = "$($result.summary)" }
        Log "auto-merge enabled; waiting up to $MergeWaitMinutes min for CI"
        $deadline = (Get-Date).AddMinutes($MergeWaitMinutes)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 60
            $s = (Gh @('pr', 'view', "$prNum", '--repo', $Repo, '--json', 'state')).Out | ConvertFrom-Json
            if ($s.state -ne 'OPEN') { break }
        }
        & $markDone
        Save-State $state
        Send-Notice "DLSS5 Updater bot: auto-merging $title" "$($result.summary)`nChecks green locally; tagging v$version once GitHub merges it." $prUrl 3 'robot'
        Complete-PendingRelease $state
    } else {
        & $markDone
        Send-Notice "DLSS5 Updater bot: PR needs your ship it" "$($result.summary)`nNew: $what" $prUrl 4 'eyes'
    }
    Save-State $state
}
catch {
    $exit = 1
    $msg = $_.Exception.Message
    Log "FAILED: $msg"
    if ($state -and -not $seeding) {
        foreach ($c in @($todo)) { if ($c) { $k = "$($c.Key)|$($c.New)"; $state.failures[$k] = [int]$state.failures[$k] + 1 } }
        foreach ($d in @($todoDrops)) { if ($d) { $k = "drop|$($d.Hash)"; $state.failures[$k] = [int]$state.failures[$k] + 1 } }
        try { Save-State $state } catch { Log "state not saved: $($_.Exception.Message)" }
    }
    Send-Notice 'DLSS5 Updater bot failed' "$msg`nLog: $LogFile" $null 4 'warning'
}
finally {
    Remove-Worktree $wt
    if ($wt -and (Test-Path $Clone)) { Git @('branch', '-D', $branch) | Out-Null }
    if (Test-Path $Work) { Remove-Item -Recurse -Force $Work -ErrorAction SilentlyContinue }
    $lock.Dispose()
    Log "end (exit $exit)"
}
exit $exit
