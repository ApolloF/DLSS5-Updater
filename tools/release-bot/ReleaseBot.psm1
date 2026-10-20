# Decisions of the release bot that can be tested without network, git remotes or Claude (ReleaseBot.Tests.ps1).
# release-bot.ps1 does the I/O and calls these. Everything here fails closed: unknown input means "not allowed".

# ---------- versions ----------

function ConvertTo-BotVersion([string]$s) {
    # "v1.2.3", "1.2", "0.1.27.1" and "2.0.1-pre1" (suffix ignored) parse; anything else is $null.
    if ($s -notmatch '^\s*v?(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:[-+][0-9A-Za-z.\-+]*)?\s*$') { return $null }
    $build = if ($Matches[3]) { [int]$Matches[3] } else { 0 }
    if ($Matches[4]) { [version]::new([int]$Matches[1], [int]$Matches[2], $build, [int]$Matches[4]) }
    else { [version]::new([int]$Matches[1], [int]$Matches[2], $build) }
}

function Get-NextPatch([string]$v) {
    $p = ConvertTo-BotVersion $v
    if (-not $p) { throw "not a version: $v" }
    "$($p.Major).$($p.Minor).$($p.Build + 1)"
}

function Test-PlainTag([string]$tag) { $tag -cmatch '^v\d+(\.\d+){2,3}$' }

# ---------- text that leaves the machine ----------

function Format-SafeText([string]$s, [int]$max = 120) {
    # Upstream names (drop files, tags) end up in titles and notices: keep a plain subset and cap the length.
    $t = ($s -replace '[^A-Za-z0-9 ._,:()/+#=-]', '' -replace '\s+', ' ').Trim()
    if ($t.Length -gt $max) { $t = $t.Substring(0, $max) + '...' }
    $t
}

function Format-Unreviewed([string]$s, [int]$max = 2000) {
    # Claude's free text only goes into review PRs, quoted, without links or mentions that GitHub would act on.
    $t = "$s" -replace '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]', '' -replace '@', '(at)' -replace '://', ' : //' -replace '(?i)\bwww\.', 'www .' -replace '#(?=\d)', '# '
    if ($t.Length -gt $max) { $t = $t.Substring(0, $max) + ' [cut]' }
    (($t -split "`r?`n") | ForEach-Object { "> $_" }) -join "`n"
}

function Get-ClaudeVerdict($result) {
    # Claude's config_changed is only ever a veto: anything but a real JSON false counts as a config change.
    # Claude's output never auto-merges either way; this only picks the wording of the review PR.
    if ($null -ne $result -and $result.config_changed -is [bool] -and $result.config_changed -eq $false) { 'claims-neutral' } else { 'config-change' }
}

# ---------- gates ----------

function Get-ClaudeLaunchRefusal([string]$runAs, [bool]$unsafeAsOwner) {
    # Real Claude only runs as the isolated user, or as the owner when that was asked for by name. -DryRun doesn't
    # change this: a dry run starts the real Claude too.
    if ($runAs) { return $null }
    if ($unsafeAsOwner) { return $null }
    'real Claude needs -RunAs <isolated user> (README, Full isolation), or -UnsafeRunClaudeAsOwner to run it as you'
}

function Get-UnreleasedReason([string]$gitDir, [string]$version, [string]$ref, [string[]]$gitPrefix = @()) {
    # $null when $ref holds nothing beyond the release tag v<version> except docs/; otherwise why not. An auto
    # release must never ship work on main that nobody released yet.
    $tag = "refs/tags/v$version"
    # git.exe, not git: release-bot.ps1's own Git function would win the lookup.
    $sha = git.exe @gitPrefix --git-dir $gitDir rev-parse --verify --quiet "$tag^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $sha) { return "main is at $version but there is no tag v$version" }
    git.exe @gitPrefix --git-dir $gitDir diff --quiet --no-ext-diff --no-textconv $sha $ref -- . ':(exclude)docs/' 2>$null
    switch ($LASTEXITCODE) {
        0 { return $null }
        1 { return "main has unreleased changes since v$version" }
        default { return "cannot compare v$version with $ref" }
    }
}

function Get-ReleaseStateReason($rel, [string]$version) {
    # $rel: gh release view v<version> --json tagName,isDraft,isPrerelease, or $null when there is none. A tag alone
    # is not a release: a prerelease still in its 24 h window, a draft or a deleted release with the tag kept all
    # count as unreleased.
    if (-not $rel) { return "no published release v$version" }
    if ("$($rel.tagName)" -cne "v$version") { return "release v$version reads as $(Format-SafeText "$($rel.tagName)" 40)" }
    if ($rel.isDraft -isnot [bool] -or $rel.isDraft) { return "release v$version is a draft" }
    if ($rel.isPrerelease -isnot [bool] -or $rel.isPrerelease) { return "release v$version is still a prerelease" }
    $null
}

function Sync-ReleaseTag([string]$gitDir, [string]$version, [string[]]$gitPrefix = @()) {
    # The local tag is dropped first: a tag the mirror kept from its clone, an older fetch or a failed bot push must
    # never stand in for origin's. $null when origin's v<version> is now the local one; otherwise why not.
    $ref = "refs/tags/v$version"
    git.exe @gitPrefix --git-dir $gitDir update-ref -d $ref 2>$null | Out-Null
    git.exe @gitPrefix --git-dir $gitDir rev-parse --verify --quiet $ref 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { return "cannot drop the local tag v$version" }
    git.exe @gitPrefix --git-dir $gitDir fetch --quiet --no-tags origin "+${ref}:$ref" 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { return "cannot fetch tag v$version from origin (missing or unreachable)" }
    $null
}

function Get-ReleaseGateReason([string]$gitDir, [string]$version, [string]$ref, $rel, [string[]]$gitPrefix = @()) {
    # $null only when v<version> is a published full release and $ref holds nothing beyond its tag except docs/.
    if ($version -notmatch '^\d+\.\d+\.\d+$') { return "not a release version: $(Format-SafeText $version 40)" }
    $r = Get-ReleaseStateReason $rel $version
    if (-not $r) { $r = Sync-ReleaseTag $gitDir $version $gitPrefix }
    if (-not $r) { $r = Get-UnreleasedReason $gitDir $version $ref $gitPrefix }
    $r
}

function Find-PushBlockers([string]$worktree, [string]$gitDir) {
    # The owner's global pre-push hook runs the command in a repo's .prepush, and its hooks chain to the repo's own
    # hooks folder. Neither may run anything that came from the repo or a bot run.
    $b = [Collections.Generic.List[string]]::new()
    if ($worktree -and (Test-Path -LiteralPath (Join-Path $worktree '.prepush'))) { $b.Add('the tree has a .prepush file') }
    $hooks = Join-Path $gitDir 'hooks'
    if (Test-Path -LiteralPath $hooks) {
        foreach ($h in Get-ChildItem -LiteralPath $hooks -Force) { if ($h.Name -notlike '*.sample') { $b.Add("$($gitDir) has a repo hook: $($h.Name)") } }
    }
    @($b)
}

function Test-NoAiTrailer([string]$messages) {
    # The bot's commits carry no AI attribution (the owner's commit-msg hook strips it too; this doesn't rely on it).
    $messages -notmatch '(?im)^\s*co-authored-by:.*(claude|anthropic)|generated with \[?claude code'
}

# ---------- bot PRs ----------

function Select-BotPr($prs, [string]$botLogin, [string]$prefix) {
    # Only PRs from a branch of this repo, opened by the bot's account, count. A fork PR named release-bot/* is ignored.
    @($prs) | Where-Object {
        $_ -and $_.isCrossRepository -eq $false -and $_.author.login -eq $botLogin -and "$($_.headRefName)".StartsWith($prefix, [StringComparison]::Ordinal)
    } | Sort-Object number | Select-Object -First 1
}

function Test-NeedsReviewLabel($pr) { [bool](@($pr.labels) | Where-Object { $_.name -eq 'needs-review' }) }

function Test-AutoEligible($pr, $rec) {
    # A PR may auto-merge only while it is exactly what the bot pushed on the auto path and never needed review.
    [bool]($pr -and $rec -and $rec.mode -eq 'auto' -and $rec.needsReview -ne $true -and $pr.isCrossRepository -eq $false -and
        $rec.head -and $pr.headRefOid -eq $rec.head -and -not (Test-NeedsReviewLabel $pr))
}

function Test-AutoMergeOff($prView) {
    # $prView: gh pr view --json autoMergeRequest,isDraft. Off only when GitHub reports no auto-merge request.
    [bool]($prView -and $prView.PSObject.Properties['autoMergeRequest'] -and $null -eq $prView.autoMergeRequest)
}

function Get-PendingDecision($pending, $pr, $rec, [string]$mergedPatchId) {
    # What to do with the auto PR the bot is waiting on: wait, drop, tag, or refuse.
    if (-not $pending) { return 'none' }
    if (-not $pr) { return 'wait' }
    if ($pr.state -eq 'CLOSED') { return 'closed' }
    if ($pr.state -ne 'MERGED') { return 'wait' }
    if (-not (Test-AutoEligible $pr $rec)) { return 'refuse' }
    if ($rec.patchId -ne $pending.patchId -or -not $mergedPatchId -or $mergedPatchId -ne $pending.patchId) { return 'refuse' }
    'tag'
}

# ---------- prereleases ----------

function Get-ReleaseFingerprint($rel) {
    $assets = @($rel.assets | ForEach-Object { "$($_.name)|$($_.size)|$($_.digest)" } | Sort-Object) -join ';'
    $text = "$($rel.tagName)`n$($rel.name)`n$($rel.isPrerelease)`n$($rel.isDraft)`n$assets`n$($rel.body)"
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
}

function Get-NotesHash([string]$text) { Get-TextHash (ConvertTo-NormalIni $text) }

function Get-PromoteDecision($rel, $rec, [datetime]$now, [int]$hours = 24) {
    # $rel: gh release view JSON or $null when there is no release. $rec: what the bot recorded when it tagged,
    # including notesHash, the hash of the release notes template it wrote.
    # release.yml needs a while to create the release, so "not found" only means deleted after 12 hours.
    if (-not $rel) { return $(if (-not $rec.fingerprint -and ($now - [datetime]$rec.tagged).TotalHours -lt 12) { 'building' } else { 'gone' }) }
    if ($rel.isDraft) { return 'edited' }
    if (-not $rel.isPrerelease) { return 'already' }
    if (@($rel.assets).Count -lt 3) { return 'building' }
    $fp = Get-ReleaseFingerprint $rel
    if (-not $rec.fingerprint) {
        # The first look must already match what release.yml makes from the bot's tag, so an edit the owner made
        # before the bot ever saw the release also stops the promotion.
        if (-not $rec.notesHash -or (Get-NotesHash "$($rel.body)") -ne $rec.notesHash -or $rel.name -cne $rel.tagName) { return 'edited' }
        return 'record'
    }
    if ($rec.fingerprint -ne $fp) { return 'edited' }
    if (($now - [datetime]$rec.tagged).TotalHours -lt $hours) { return 'wait' }
    'promote'
}

# ---------- diffs ----------

function ConvertFrom-GitDiffZ([string]$raw, [string]$numstat) {
    # $raw: git diff --raw -z --no-renames; $numstat: git diff --numstat -z --no-renames (same range).
    $entries = [ordered]@{}
    $t = $raw -split "`0"
    for ($i = 0; $i -lt $t.Count; $i++) {
        if (-not $t[$i].StartsWith(':')) { continue }
        $m = $t[$i] -split ' '
        $path = $t[$i + 1]; $i++
        $entries[$path] = [pscustomobject]@{ Path = $path; OldMode = $m[0].TrimStart(':'); NewMode = $m[1]; NewBlob = $m[3]; Status = $m[4]; Added = $null; Deleted = $null; Binary = $true }
    }
    foreach ($rec in ($numstat -split "`0")) {
        if (-not $rec) { continue }
        $p = $rec -split "`t", 3
        if ($p.Count -ne 3) { continue }
        $e = $entries[$p[2]]
        if (-not $e) { continue }
        $e.Binary = $p[0] -eq '-' -or $p[1] -eq '-'
        if (-not $e.Binary) { $e.Added = [int]$p[0]; $e.Deleted = [int]$p[1] }
    }
    @($entries.Values)
}

function ConvertFrom-UnifiedZero([string]$patch) {
    # Removed and added lines of one file's `git diff -U0` output.
    $removed = [Collections.Generic.List[string]]::new(); $added = [Collections.Generic.List[string]]::new()
    $inHunk = $false
    foreach ($l in ($patch -split "`n")) {
        $l = $l.TrimEnd("`r")
        if ($l.StartsWith('@@')) { $inHunk = $true; continue }
        if (-not $inHunk) { continue }
        if ($l.StartsWith('-')) { $removed.Add($l.Substring(1)) }
        elseif ($l.StartsWith('+')) { $added.Add($l.Substring(1)) }
    }
    [pscustomobject]@{ Removed = @($removed); Added = @($added) }
}

$script:VersionLines = @{
    'src/DLSSUpdater/DLSSUpdater.csproj' = '^\s*<Version>\d+\.\d+\.\d+</Version>\s*$'
    'installer/DLSSUpdater.iss' = '^;\s+iscc /DAppVersion=\d+\.\d+\.\d+ installer\\DLSSUpdater\.iss\s*$'
    'README.md' = '^iscc /DAppVersion=\d+\.\d+\.\d+ installer\\DLSSUpdater\.iss\s*$'
}
$script:PinLine = '^\s+new Regex\(@"[^"]+", RegexOptions\.IgnoreCase\), "[a-z0-9-]*", "v\d+(?:\.\d+){2,3}"(?:, \[\], \[\]\);|,)\s*$'
$script:PinFile = 'src/DLSSUpdater/Core/OptiSources.cs'

function Test-LinePairs($hunk, [string]$pattern, [string]$token) {
    # Every removed line has an added twin that matches the pattern and differs only in the version token.
    if ($hunk.Removed.Count -ne $hunk.Added.Count -or $hunk.Removed.Count -eq 0) { return $false }
    for ($i = 0; $i -lt $hunk.Removed.Count; $i++) {
        $a = $hunk.Removed[$i]; $b = $hunk.Added[$i]
        if ($a -notmatch $pattern -or $b -notmatch $pattern) { return $false }
        if (($a -replace $token, 'X') -cne ($b -replace $token, 'X')) { return $false }
    }
    $true
}

function Test-DiffSafety($entries, $hunks, [hashtable]$sizes) {
    # Rules for every diff the bot publishes, whoever wrote it. Returns the violations (empty = fine).
    # $hunks: path -> ConvertFrom-UnifiedZero result; $sizes: path -> blob size in bytes.
    $v = [Collections.Generic.List[string]]::new()
    if (-not @($entries).Count) { $v.Add('empty diff') }
    foreach ($e in @($entries)) {
        $p = $e.Path
        if ($e.Binary) { $v.Add("${p}: binary or attribute-mangled (numstat -)") }
        if ($e.Status -notin 'A', 'M', 'D') { $v.Add("${p}: status $($e.Status)") }
        if ($e.Status -ne 'D' -and $e.NewMode -ne '100644') { $v.Add("${p}: mode $($e.NewMode)") }
        if ($p -match '(?i)nvngx') { $v.Add("${p}: nvngx") }
        if ($p -match '[\\:*?"<>|]' -or $p -match '(^|/)\.\.?(/|$)' -or $p -match '[\x00-\x1F]') { $v.Add("${p}: odd path") }
        if ($p -match '(?i)(^|/)(\.gitattributes|\.gitignore|\.gitmodules|\.editorconfig|Directory\.Build\.[^/]*|Directory\.Packages\.props|NuGet\.config|global\.json|AGENTS\.md|CLAUDE(\.local)?\.md)$' -or
            $p -match '(?i)^(\.github|\.claude|\.config|\.vscode|\.devcontainer|tools)/' -or $p -match '(?i)\.(props|targets|sln|ps1|psm1|cmd|bat|sh)$') {
            $v.Add("${p}: off-limits file")
            continue
        }
        if ($e.Status -ne 'D' -and $sizes -and $sizes[$p] -gt 1MB) { $v.Add("${p}: over 1 MB") }
        if ($script:VersionLines.ContainsKey($p) -and $p -ne 'README.md') {
            if ($e.Status -ne 'M' -or -not (Test-LinePairs $hunks[$p] $script:VersionLines[$p] '\d+\.\d+\.\d+')) { $v.Add("${p}: only the version line may change") }
            continue
        }
        if ($p -match '(?i)\.(csproj|iss)$') { $v.Add("${p}: only the app's version line may change"); continue }
        $ok = $p -eq 'README.md' -or
            ($p -match '^(src|tests|docs)/' -and $p -match '(?i)\.(cs|xaml|md|txt|ini|cfg)$') -or
            ($p -match '^(tests|docs)/' -and $p -match '(?i)\.json$')
        if (-not $ok) { $v.Add("${p}: not an allowed text file") }
    }
    @($v)
}

function Test-PinDiff($entries, $hunks, [string]$version, [string]$notesPath, [string]$notesExpected, [string]$notesActual) {
    # The extra allowlist of the auto path: the bot's own pin bump and nothing else. Returns the violations.
    $v = [Collections.Generic.List[string]]::new()
    $allowed = @($script:VersionLines.Keys) + $script:PinFile + $notesPath
    $seen = @{}
    foreach ($e in @($entries)) {
        $p = $e.Path; $seen[$p] = $true
        if ($p -notin $allowed) { $v.Add("${p}: not on the auto-path allowlist"); continue }
        if ($e.Binary) { $v.Add("${p}: binary"); continue }
        if ($p -eq $notesPath) {
            if ($e.Status -ne 'A') { $v.Add("${p}: release notes must be a new file") }
            elseif ($notesActual -cne $notesExpected) { $v.Add("${p}: release notes differ from the template") }
            continue
        }
        if ($e.Status -ne 'M') { $v.Add("${p}: status $($e.Status)"); continue }
        $ok = if ($p -eq $script:PinFile) { Test-LinePairs $hunks[$p] $script:PinLine '"v\d+(?:\.\d+){2,3}"' }
              else { Test-LinePairs $hunks[$p] $script:VersionLines[$p] '\d+\.\d+\.\d+' }
        if (-not $ok) { $v.Add("${p}: a changed line is not a pin or version line") }
    }
    foreach ($p in @($script:VersionLines.Keys) + $script:PinFile + $notesPath) { if (-not $seen[$p]) { $v.Add("${p}: missing from the diff") } }
    @($v)
}

# ---------- deterministic pin bump (OptiScaler builds) ----------

function Get-PinnedTag([string]$text, [string]$assetLiteral) {
    # The TestedTag on the one OptiSources.cs line that holds this build's asset regex.
    $lines = @(($text -split "`n") | Where-Object { $_.Contains("@`"$assetLiteral`"") })
    if ($lines.Count -ne 1) { return $null }
    $m = [regex]::Matches($lines[0], '"(v\d+(?:\.\d+){2,3})"')
    if ($m.Count -ne 1 -or $lines[0].TrimEnd("`r") -notmatch $script:PinLine) { return $null }
    $m[0].Groups[1].Value
}

function Update-PinText([string]$text, [string]$assetLiteral, [string]$oldTag, [string]$newTag) {
    if (-not (Test-PlainTag $newTag)) { throw "not a plain tag: $newTag" }
    if ((Get-PinnedTag $text $assetLiteral) -cne $oldTag) { throw "pin for $assetLiteral is not $oldTag" }
    $lines = $text -split "`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Contains("@`"$assetLiteral`"")) { $lines[$i] = $lines[$i].Replace("`"$oldTag`"", "`"$newTag`"") }
    }
    $lines -join "`n"
}

function Update-VersionText([string]$path, [string]$text, [string]$old, [string]$new) {
    # Exactly the lines Test-DiffSafety allows: <Version> in the csproj, the iscc example in the .iss and README.
    $pattern = $script:VersionLines[$path]
    if (-not $pattern) { throw "no version line in $path" }
    $n = 0
    $lines = $text -split "`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i].TrimEnd("`r")
        if ($l -match $pattern -and $l.Contains($old)) { $lines[$i] = $lines[$i].Replace($old, $new); $n++ }
    }
    if ($n -ne 1) { throw "$path has $n version line(s) with $old, expected 1" }
    $lines -join "`n"
}

function ConvertTo-NormalIni([string]$text) {
    # Byte-for-byte after normalising: no BOM, LF line ends, no trailing whitespace, no trailing blank lines.
    ((($text.TrimStart([char]0xFEFF) -replace "`r`n?", "`n") -split "`n" | ForEach-Object { $_.TrimEnd() }) -join "`n").TrimEnd("`n")
}

function Get-TextHash([string]$s) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($s))).ToLowerInvariant()
}

function ConvertTo-EntryList([string[]]$names, [string]$tag) {
    # Zip entry names, sorted, with the build's own version in folder names masked out.
    $ver = $tag.TrimStart('v')
    @($names | Where-Object { $_ -and -not $_.EndsWith('/') } | ForEach-Object {
        ($_ -replace '\\', '/').ToLowerInvariant().Replace("v$ver", '{v}').Replace($ver, '{v}')
    } | Sort-Object -Unique)
}

function Get-PinProof($old, $new) {
    # $old/$new: @{ Tag; RawIni; ZipIni; Entries } facts of the tested and the new build (hashes or text).
    # Returns the reasons it is NOT config-neutral; empty means proven.
    $r = [Collections.Generic.List[string]]::new()
    if (-not (Test-PlainTag $new.Tag)) { $r.Add("tag $($new.Tag) is not a plain vX.Y.Z tag") }
    $ov = ConvertTo-BotVersion $old.Tag; $nv = ConvertTo-BotVersion $new.Tag
    if (-not $ov -or -not $nv -or $nv -le $ov) { $r.Add("$($new.Tag) is not newer than $($old.Tag)") }
    elseif ($nv.Major -ne $ov.Major -or $nv.Minor -ne $ov.Minor) { $r.Add("$($new.Tag) is a new minor line") }
    if ($new.Prerelease -ne $false) { $r.Add('prerelease or unknown') }
    foreach ($k in 'RawIni', 'ZipIni', 'Entries') {
        if (-not $old[$k] -or -not $new[$k]) { $r.Add("$k unknown") }
        elseif ($old[$k] -ne $new[$k]) { $r.Add("$k differs from $($old.Tag)") }
    }
    @($r)
}

function New-PinNotes([string]$version, $bumps) {
    # $bumps: @{ Name; Old; New; Repo } per pin, all bot-known and validated. Template only, no upstream text.
    $lines = @("Tested-version update made by the release bot.", "")
    foreach ($b in $bumps) {
        $lines += "- $($b.Name) $($b.New) is now the tested build (was $($b.Old)). Its OptiScaler.ini and the file list of its release zip are identical to $($b.Old), so no option changed. Release: https://github.com/$($b.Repo)/releases/tag/$($b.New)"
    }
    ($lines -join "`n") + "`n"
}

# ---------- Claude's tree vs the base ----------

function Test-SafeRelPath([string]$rel) {
    # A name Windows would rewrite (trailing dot or space, 8.3 short name) or anything git treats specially (.git*)
    # could land somewhere else than it looks, for example on the bot worktree's .git file.
    foreach ($part in $rel -split '/') {
        if (-not $part -or $part -match '(?i)^\.git' -or $part -match '[. ]$' -or $part -match '~\d' -or $part -match '[\x00-\x1F:*?"<>|\\]') { return $false }
    }
    $true
}

function Get-TreeChanges([string]$base, [string]$work) {
    # Files Claude added, changed or deleted, found by comparing the two folders (no git runs in Claude's folder).
    # Reparse points (symlinks, junctions) anywhere and unsafe names among the changes are violations; links are never
    # followed and nothing is copied when there is a violation.
    $skip = '^(\.git|bin|obj|\.vs|TestResults|publish)$'
    function List([string]$root, $bad) {
        $map = @{}
        # Relative paths are built from the raw names: path APIs would turn ".git." into ".git".
        $stack = [Collections.Generic.Stack[object]]::new(); $stack.Push(@($root, ''))
        while ($stack.Count) {
            $d, $prefix = $stack.Pop()
            foreach ($i in [IO.DirectoryInfo]::new($d).EnumerateFileSystemInfos()) {
                $rel = "$prefix$($i.Name)"
                if ($rel -ceq '.git') { continue }
                if ($i.Attributes -band [IO.FileAttributes]::ReparsePoint) { $bad.Add("${rel}: link"); continue }
                if ($i -is [IO.DirectoryInfo]) { if ($i.Name -notmatch $skip) { $stack.Push(@($i.FullName, "$rel/")) } }
                else { $map[$rel] = $i }
            }
        }
        $map
    }
    $bad = [Collections.Generic.List[string]]::new()
    $a = List $base $bad; $b = List $work $bad
    $changes = [Collections.Generic.List[pscustomobject]]::new()
    foreach ($k in $b.Keys) {
        if (-not $a.ContainsKey($k)) { $changes.Add([pscustomobject]@{ Path = $k; Kind = 'add' }) }
        elseif ($a[$k].Length -ne $b[$k].Length -or (Get-FileHash -LiteralPath $a[$k].FullName).Hash -ne (Get-FileHash -LiteralPath $b[$k].FullName).Hash) {
            $changes.Add([pscustomobject]@{ Path = $k; Kind = 'change' })
        }
    }
    foreach ($k in $a.Keys) { if (-not $b.ContainsKey($k)) { $changes.Add([pscustomobject]@{ Path = $k; Kind = 'delete' }) } }
    foreach ($c in $changes) { if (-not (Test-SafeRelPath $c.Path)) { $bad.Add("$($c.Path): unsafe name") } }
    [pscustomobject]@{ Changes = @($changes | Sort-Object Path); Violations = @($bad) }
}

function Find-ReparsePoints([string]$root) {
    # Every symlink or junction under $root (the root included), as relative paths. Links are never followed.
    $found = [Collections.Generic.List[string]]::new()
    $top = [IO.DirectoryInfo]::new($root)
    if ($top.Attributes -band [IO.FileAttributes]::ReparsePoint) { $found.Add('.'); return @($found) }
    $stack = [Collections.Generic.Stack[object]]::new(); $stack.Push(@($top, ''))
    while ($stack.Count) {
        $d, $prefix = $stack.Pop()
        foreach ($i in $d.EnumerateFileSystemInfos()) {
            $rel = "$prefix$($i.Name)"
            if ($i.Attributes -band [IO.FileAttributes]::ReparsePoint) { $found.Add($rel) }
            elseif ($i -is [IO.DirectoryInfo]) { $stack.Push(@($i, "$rel/")) }
        }
    }
    @($found)
}

function Copy-SandboxBack([string]$from, [string]$to) {
    # The owner side copies the isolated user's sandbox back. That user could plant a link to the owner's files
    # (Notes, ~\.claude, ~\.ssh) for the owner's copy to follow into a public PR, so any link anywhere aborts the copy
    # before a byte is copied, and every item is checked again as it is copied. Build output is left behind.
    $links = @(Find-ReparsePoints $from)
    if ($links.Count) { throw "the isolated sandbox has links ($(Format-SafeText ($links -join ', ') 200)); nothing copied back" }
    if (Test-Path -LiteralPath $to) { throw "copy-back target $to already exists" }
    $skip = '^(bin|obj|\.vs|TestResults|publish)$'
    New-Item -ItemType Directory $to | Out-Null
    $stack = [Collections.Generic.Stack[object]]::new(); $stack.Push(@([IO.DirectoryInfo]::new($from), $to))
    while ($stack.Count) {
        $d, $dest = $stack.Pop()
        foreach ($i in $d.EnumerateFileSystemInfos()) {
            if ($i.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "$($i.FullName) turned into a link during the copy-back; stopping" }
            $target = Join-Path $dest $i.Name
            if ($i -is [IO.DirectoryInfo]) {
                if ($i.Name -match $skip) { continue }
                New-Item -ItemType Directory $target | Out-Null
                $stack.Push(@($i, $target))
            } else { [IO.File]::Copy($i.FullName, $target, $false) }
        }
    }
}

# ---------- Lecram's members-only release notes (Buy Me a Coffee) ----------

$script:BlockMark = '[[block]]'
$script:PageChrome = '(?i)^(\d+\s+)?(comments?|replies|likes?)$|^(write|leave|add|post) a (comment|reply)|^(reply|share|like|report)$|^(buy|support)\b.*\bcoffee\b|^(join|become) a (member|supporter)|^(log ?in|sign ?in|sign ?up)$'

function Test-ContainsPaidText([string]$text, [string[]]$notes, [int]$words = 8) {
    # True when $text repeats any run of $words words from the members-only notes (case, punctuation and spacing
    # ignored). Used on everything that leaves the machine after Claude saw the notes.
    $norm = { param($s) (("$s".ToLowerInvariant() -replace '[^\p{L}\p{N}=._\[\]-]+', ' ').Trim() -split ' ') | ForEach-Object { $_.Trim('.') } | Where-Object { $_ } }
    $hay = " $((& $norm $text) -join ' ') "
    foreach ($n in @($notes)) {
        $w = @(& $norm $n)
        for ($i = 0; $i + $words -le $w.Count; $i++) {
            if ($hay.Contains(" $($w[$i..($i + $words - 1)] -join ' ') ")) { return $true }
        }
    }
    $false
}

function ConvertFrom-HtmlText([string]$html) {
    # Visible text of a dumped DOM, one block element per line.
    $t = $html -replace '(?is)<(script|style|noscript|svg|template)\b.*?</\1\s*>', ' '
    # Page regions (comments, footer, sidebar) start a new block: a patch section never runs past one. Headings get a
    # level mark, so a section only ends at a heading of its own or a higher level.
    $t = $t -replace '(?i)<(footer|aside|form|section|nav|header|hr|dialog)\b|</article\s*>', "`n$($script:BlockMark)`n$&"
    $t = $t -replace '(?i)<h([1-6])\b', "`n[[h`$1]]`n$&"
    $t = $t -replace '(?i)<br\s*/?>', "`n" -replace '(?i)</(p|div|li|h[1-6]|tr|section|article|blockquote|pre)\s*>', "`n" -replace '(?i)<li\b[^>]*>', "`n- "
    $t = [Net.WebUtility]::HtmlDecode(($t -replace '(?s)<[^>]*>', ''))
    (($t -split "`n") | ForEach-Object { ($_ -replace '[\s ]+', ' ').Trim() } | Where-Object { $_ }) -join "`n"
}

function Get-PatchSections([string]$text, [int]$maxChars = 4000) {
    # "v11 Patch 11" headings and the text under each, newest last. A heading is a line that starts with it. A section
    # ends at the next patch heading, a heading of the same or a higher level (any heading when the patch heading wasn't
    # one), a page region, or a line that looks like the comments or the page around the post, so members' comments
    # under the post never end up in a section.
    $sections = [Collections.Generic.List[pscustomobject]]::new()
    $cur = $null; $level = $null
    foreach ($line in ($text -split "`n")) {
        if ($line -eq $script:BlockMark -or $line -match $script:PageChrome) { $cur = $null; $level = $null; continue }
        if ($line -match '^\[\[h([1-6])\]\]$') {
            $level = [int]$Matches[1]
            if ($cur -and $level -le $cur.Level) { $cur = $null }
            continue
        }
        if ($line -match '^\s*v(\d+)\s+Patch\s+(\d+)\b') {
            $cur = [pscustomobject]@{ Major = [int]$Matches[1]; Patch = [int]$Matches[2]; Key = "v$([int]$Matches[1]) Patch $([int]$Matches[2])"; Text = ''; Level = $level ?? 7 }
            if (-not ($sections | Where-Object Key -EQ $cur.Key)) { $sections.Add($cur) } else { $cur = $null }
            $level = $null
            continue
        }
        $level = $null
        if ($cur -and $cur.Text.Length -lt $maxChars) { $cur.Text = ($cur.Text + "`n" + $line).TrimStart("`n") }
    }
    @($sections | Sort-Object Major, Patch)
}

function Select-NewPatchSections($sections, [string]$seen) {
    # Sections after the one the bot saw last ("v11 Patch 10"); all of them when it never saw one.
    if ($seen -notmatch '^v(\d+) Patch (\d+)$') { return @($sections) }
    $m = [int]$Matches[1]; $p = [int]$Matches[2]
    @($sections | Where-Object { $_.Major -gt $m -or ($_.Major -eq $m -and $_.Patch -gt $p) })
}

Export-ModuleMember -Function *
