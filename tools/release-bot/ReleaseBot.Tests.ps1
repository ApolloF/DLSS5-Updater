# Pester 5+: Invoke-Pester tools/release-bot
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'ReleaseBot.psm1') -Force
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $NrAsset = '^OptiScaler-NR-v[\d.]+\.zip$'
    $F5Asset = '^OptiScaler-DLSSNR-F5-[\d.]+\.zip$'

    function New-TempRepo {
        $d = Join-Path ([IO.Path]::GetTempPath()) "rbtest-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
        New-Item -ItemType Directory $d | Out-Null
        git -C $d init --quiet
        git -C $d config user.name t; git -C $d config user.email t@invalid; git -C $d config core.autocrlf false
        $d
    }
    function Get-Diff([string]$d) {
        $raw = (git -C $d -c core.quotePath=false diff --raw -z --no-renames --no-ext-diff --no-textconv HEAD~1 HEAD | Out-String)
        $num = (git -C $d -c core.quotePath=false diff --numstat -z --no-renames --no-ext-diff --no-textconv HEAD~1 HEAD | Out-String)
        $entries = ConvertFrom-GitDiffZ $raw $num
        $hunks = @{}
        foreach ($e in $entries) { if (-not $e.Binary) { $hunks[$e.Path] = ConvertFrom-UnifiedZero ((git -C $d diff -U0 --no-ext-diff --no-textconv HEAD~1 HEAD -- $e.Path) -join "`n") } }
        @{ Entries = $entries; Hunks = $hunks }
    }
    function Copy-AppFiles([string]$d) {
        foreach ($rel in 'src/DLSSUpdater/DLSSUpdater.csproj', 'installer/DLSSUpdater.iss', 'README.md', 'src/DLSSUpdater/Core/OptiSources.cs') {
            $dst = Join-Path $d $rel
            New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
            Copy-Item (Join-Path $RepoRoot $rel) $dst
        }
        git -C $d add -A; git -C $d commit --quiet -m base
    }
    function Merge([hashtable]$a, [hashtable]$b) { $r = $a.Clone(); foreach ($k in $b.Keys) { $r[$k] = $b[$k] }; [pscustomobject]$r }
    function Edit([string]$d, [string]$rel, [scriptblock]$fn) {
        $p = Join-Path $d $rel
        [IO.File]::WriteAllText($p, (& $fn ([IO.File]::ReadAllText($p))))
    }
    function Bump([string]$d, [string]$ver, [string]$newTag) {
        $old = ([regex]::Match([IO.File]::ReadAllText((Join-Path $d 'src/DLSSUpdater/DLSSUpdater.csproj')), '<Version>([^<]+)</Version>')).Groups[1].Value
        foreach ($rel in 'src/DLSSUpdater/DLSSUpdater.csproj', 'installer/DLSSUpdater.iss', 'README.md') {
            $p = Join-Path $d $rel; [IO.File]::WriteAllText($p, (Update-VersionText $rel ([IO.File]::ReadAllText($p)) $old $ver))
        }
        $p = Join-Path $d 'src/DLSSUpdater/Core/OptiSources.cs'
        $pin = Get-PinnedTag ([IO.File]::ReadAllText($p)) $NrAsset
        [IO.File]::WriteAllText($p, (Update-PinText ([IO.File]::ReadAllText($p)) $NrAsset $pin $newTag))
        New-Item -ItemType Directory -Force (Join-Path $d 'docs/releases') | Out-Null
        $notes = New-PinNotes $ver @(@{ Name = 'OptiScaler-NR'; Old = $pin; New = $newTag; Repo = 'x/y' })
        [IO.File]::WriteAllText((Join-Path $d "docs/releases/v$ver.md"), $notes)
        $notes
    }
}

Describe 'versions' {
    It 'parses plain, four-part and pre-release versions' {
        ConvertTo-BotVersion 'v1.2.3' | Should -Be ([version]'1.2.3')
        ConvertTo-BotVersion '0.1.27.1' | Should -Be ([version]'0.1.27.1')
        ConvertTo-BotVersion '2.0.1-pre1' | Should -Be ([version]'2.0.1')
        ConvertTo-BotVersion 'v0.9.69-pre1' | Should -Be ([version]'0.9.69')
        ConvertTo-BotVersion '2.1' | Should -Be ([version]'2.1.0')
        ConvertTo-BotVersion 'nightly' | Should -BeNullOrEmpty
        ConvertTo-BotVersion 'v1.2.3; rm -rf' | Should -BeNullOrEmpty
    }
    It 'bumps the patch, ignoring a suffix' {
        Get-NextPatch '2.0.0' | Should -Be '2.0.1'
        Get-NextPatch '2.0.1-rc1' | Should -Be '2.0.2'
        { Get-NextPatch 'x' } | Should -Throw
    }
    It 'treats only vX.Y.Z(.W) as a plain tag' {
        Test-PlainTag 'v0.9.52' | Should -BeTrue
        Test-PlainTag 'v0.1.27.1' | Should -BeTrue
        Test-PlainTag 'v0.1.20-reuse-fix' | Should -BeFalse
        Test-PlainTag 'V0.9.52' | Should -BeFalse
    }
}

Describe 'public text' {
    It 'keeps only a plain subset of upstream names' {
        Format-SafeText "drop evil`n<img src=x>@owner `$(calc).zip" | Should -Be 'drop evilimg src=xowner (calc).zip'
        (Format-SafeText ('a' * 300) 10).Length | Should -Be 13
    }
    It 'quotes Claude text and defuses mentions and links' {
        $q = Format-Unreviewed "hi @ApolloF`nsee https://evil.example"
        $q | Should -Be "> hi (at)ApolloF`n> see https : //evil.example"
        Format-Unreviewed 'Closes #12, see www.evil.example' | Should -Be '> Closes # 12, see www .evil.example'
    }
    It 'only takes a real JSON false as Claude claiming neutral' {
        Get-ClaudeVerdict ('{"config_changed":false}' | ConvertFrom-Json) | Should -Be 'claims-neutral'
        Get-ClaudeVerdict ('{"config_changed":"false"}' | ConvertFrom-Json) | Should -Be 'config-change'
        Get-ClaudeVerdict ('{"config_changed":0}' | ConvertFrom-Json) | Should -Be 'config-change'
        Get-ClaudeVerdict ('{"status":"changed"}' | ConvertFrom-Json) | Should -Be 'config-change'
        Get-ClaudeVerdict $null | Should -Be 'config-change'
    }
}

Describe 'bot PRs (fork takeover, sticky review)' {
    BeforeAll {
        $bot = { param($n, $extra = @{}) Merge @{ number = $n; headRefName = "release-bot/$n"; isCrossRepository = $false; author = @{ login = 'ApolloF' }; headRefOid = "sha$n"; labels = @() } $extra }
    }
    It 'ignores a fork PR named release-bot/*' {
        $fork = & $bot 1 @{ isCrossRepository = $true; author = @{ login = 'mallory' } }
        Select-BotPr @($fork) 'ApolloF' 'release-bot/' | Should -BeNullOrEmpty
        $ownerFork = & $bot 2 @{ isCrossRepository = $true }
        Select-BotPr @($ownerFork, $fork) 'ApolloF' 'release-bot/' | Should -BeNullOrEmpty
    }
    It 'ignores other authors, other branches and PRs without the fork flag' {
        $other = & $bot 3 @{ author = @{ login = 'mallory' } }
        $branch = & $bot 4 @{ headRefName = 'feature/release-bot/x' }
        $noFlag = [pscustomobject]@{ number = 5; headRefName = 'release-bot/5'; author = @{ login = 'ApolloF' } }
        Select-BotPr @($other, $branch, $noFlag) 'ApolloF' 'release-bot/' | Should -BeNullOrEmpty
    }
    It 'picks the bot PR next to a stray fork PR' {
        $fork = & $bot 1 @{ isCrossRepository = $true; author = @{ login = 'mallory' } }
        (Select-BotPr @($fork, (& $bot 9), (& $bot 7)) 'ApolloF' 'release-bot/').number | Should -Be 7
    }
    It 'allows auto-merge only for the exact head the bot pushed on the auto path' {
        $pr = & $bot 7
        $rec = @{ mode = 'auto'; head = 'sha7' }
        Test-AutoEligible $pr $rec | Should -BeTrue
        Test-AutoEligible (& $bot 7 @{ headRefOid = 'pushed-by-someone' }) $rec | Should -BeFalse
        Test-AutoEligible (& $bot 7 @{ labels = @(@{ name = 'needs-review' }) }) $rec | Should -BeFalse
        Test-AutoEligible $pr @{ mode = 'auto'; head = 'sha7'; needsReview = $true } | Should -BeFalse
        Test-AutoEligible $pr @{ mode = 'review'; head = 'sha7' } | Should -BeFalse
        Test-AutoEligible $pr $null | Should -BeFalse
        Test-AutoEligible (& $bot 7 @{ isCrossRepository = $true }) $rec | Should -BeFalse
    }
    It 'tags a merged auto PR only when the merge commit is the verified diff' {
        $pending = @{ pr = 7; version = '2.0.1'; patchId = 'p1' }
        $rec = @{ mode = 'auto'; head = 'sha7'; patchId = 'p1' }
        $merged = & $bot 7 @{ state = 'MERGED' }
        Get-PendingDecision $pending $merged $rec 'p1' | Should -Be 'tag'
        Get-PendingDecision $pending $merged $rec 'p2' | Should -Be 'refuse'
        Get-PendingDecision $pending $merged $rec $null | Should -Be 'refuse'
        Get-PendingDecision $pending (& $bot 7 @{ state = 'MERGED'; labels = @(@{ name = 'needs-review' }) }) $rec 'p1' | Should -Be 'refuse'
        Get-PendingDecision $pending (& $bot 7 @{ state = 'MERGED'; headRefOid = 'other' }) $rec 'p1' | Should -Be 'refuse'
        Get-PendingDecision $pending $merged @{ mode = 'auto'; head = 'sha7'; patchId = 'p1'; needsReview = $true } 'p1' | Should -Be 'refuse'
        Get-PendingDecision $pending (& $bot 7 @{ state = 'OPEN' }) $rec $null | Should -Be 'wait'
        Get-PendingDecision $pending (& $bot 7 @{ state = 'CLOSED' }) $rec $null | Should -Be 'closed'
        Get-PendingDecision $null $merged $rec 'p1' | Should -Be 'none'
    }
}

Describe 'prerelease promotion' {
    BeforeAll {
        $rel = { param($extra = @{}) Merge @{ tagName = 'v2.0.1'; name = 'v2.0.1'; body = 'notes'; isPrerelease = $true; isDraft = $false
            assets = @(@{ name = 'a.exe'; size = 1 }, @{ name = 'b.exe'; size = 2 }, @{ name = 'SHA256SUMS'; size = 3 }) } $extra }
        $now = [datetime]'2026-10-21T12:00:00'
    }
    It 'records, waits 24 h, then promotes an untouched prerelease' {
        $r = & $rel
        $rec = @{ tagged = '2026-10-21T00:00:00'; notesHash = (Get-NotesHash "notes`r`n") }
        Get-PromoteDecision $r $rec $now | Should -Be 'record'
        $rec.fingerprint = Get-ReleaseFingerprint $r
        Get-PromoteDecision $r $rec $now | Should -Be 'wait'
        Get-PromoteDecision $r $rec $now.AddHours(13) | Should -Be 'promote'
    }
    It 'leaves deleted, edited, promoted and unfinished releases alone' {
        $r = & $rel
        $rec = @{ tagged = '2026-10-19T00:00:00'; fingerprint = (Get-ReleaseFingerprint $r) }
        Get-PromoteDecision $null $rec $now | Should -Be 'gone'
        Get-PromoteDecision (& $rel @{ body = 'owner edited' }) $rec $now | Should -Be 'edited'
        Get-PromoteDecision (& $rel @{ isDraft = $true }) $rec $now | Should -Be 'edited'
        Get-PromoteDecision (& $rel @{ isPrerelease = $false }) $rec $now | Should -Be 'already'
        Get-PromoteDecision (& $rel @{ assets = @() }) $rec $now | Should -Be 'building'
    }
    It 'does not record a release the owner edited before the bot first saw it (N1)' {
        $rec = @{ tagged = '2026-10-21T00:00:00'; notesHash = (Get-NotesHash 'notes') }
        Get-PromoteDecision (& $rel @{ body = 'owner edited' }) $rec $now | Should -Be 'edited'
        Get-PromoteDecision (& $rel @{ name = 'My release' }) $rec $now | Should -Be 'edited'
        Get-PromoteDecision (& $rel) @{ tagged = '2026-10-21T00:00:00' } $now | Should -Be 'edited'
        $rec.ContainsKey('fingerprint') | Should -BeFalse
    }
    It 'waits for release.yml before calling a missing release gone' {
        Get-PromoteDecision $null @{ tagged = '2026-10-21T11:59:00' } $now | Should -Be 'building'
        Get-PromoteDecision $null @{ tagged = '2026-10-20T11:00:00' } $now | Should -Be 'gone'
    }
}

Describe 'diff safety (finding 6: binaries, off-limits files)' {
    It 'parses -z output with odd path names' {
        $raw = ":100644 100644 aaa bbb M`0src/ä b.cs`0:000000 100644 000 ccc A`0docs/x.md`0"
        $num = "1`t1`tsrc/ä b.cs`0-`t-`tdocs/x.md`0"
        $e = ConvertFrom-GitDiffZ $raw $num
        $e.Count | Should -Be 2
        $e[0].Path | Should -Be 'src/ä b.cs'; $e[0].Binary | Should -BeFalse; $e[0].Added | Should -Be 1
        $e[1].Binary | Should -BeTrue
    }
    It 'treats a file missing from numstat as binary' {
        (ConvertFrom-GitDiffZ ":100644 100644 a b M`0src/a.cs`0" '')[0].Binary | Should -BeTrue
    }
    It 'rejects binaries, links, off-limits files and non-text files' -ForEach @(
        @{ P = 'src/a.cs'; Mode = '100644'; Bin = $true; Why = 'binary' }
        @{ P = 'src/a.cs'; Mode = '120000'; Bin = $false; Why = 'mode' }
        @{ P = 'src/a.cs'; Mode = '100755'; Bin = $false; Why = 'mode' }
        @{ P = '.gitattributes'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = 'src/.gitattributes'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = '.gitignore'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = 'Directory.Build.props'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = 'src/Directory.Build.targets'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = 'NuGet.Config'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = 'global.json'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = '.github/workflows/ci.yml'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = 'tools/release-bot/prompt.md'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = '.claude/settings.json'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = 'AGENTS.md'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = 'src/x.props'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = 'tests/New.Tests.csproj'; Mode = '100644'; Bin = $false; Why = 'version line' }
        @{ P = 'src/nvngx_dlssnr.cs'; Mode = '100644'; Bin = $false; Why = 'nvngx' }
        @{ P = 'src/logo.png'; Mode = '100644'; Bin = $false; Why = 'not an allowed' }
        @{ P = 'src/appsettings.json'; Mode = '100644'; Bin = $false; Why = 'not an allowed' }
        @{ P = 'build.cmd'; Mode = '100644'; Bin = $false; Why = 'off-limits' }
        @{ P = 'LICENSE'; Mode = '100644'; Bin = $false; Why = 'not an allowed' }
    ) {
        $e = [pscustomobject]@{ Path = $P; Status = 'M'; OldMode = '100644'; NewMode = $Mode; NewBlob = 'b'; Binary = $Bin }
        (Test-DiffSafety @($e) @{ $P = [pscustomobject]@{ Removed = @('a'); Added = @('b') } } @{ $P = 10 }) -join ';' | Should -Match $Why
    }
    It 'allows normal text changes' {
        $es = 'src/DLSSUpdater/Core/X.cs', 'tests/DLSSUpdater.Tests/XTests.cs', 'docs/addons/lecram.md', 'README.md', 'tests/data/x.json' |
            ForEach-Object { [pscustomobject]@{ Path = $_; Status = 'M'; NewMode = '100644'; Binary = $false } }
        Test-DiffSafety $es @{} @{} | Should -BeNullOrEmpty
    }
    It 'rejects files over 1 MB and an empty diff' {
        $e = [pscustomobject]@{ Path = 'src/a.cs'; Status = 'A'; NewMode = '100644'; Binary = $false }
        (Test-DiffSafety @($e) @{} @{ 'src/a.cs' = 2MB }) | Should -Match '1 MB'
        Test-DiffSafety @() @{} @{} | Should -Match 'empty'
    }
    It 'allows only the version line in the csproj and installer' {
        $csproj = [pscustomobject]@{ Path = 'src/DLSSUpdater/DLSSUpdater.csproj'; Status = 'M'; NewMode = '100644'; Binary = $false }
        $ok = @{ $csproj.Path = [pscustomobject]@{ Removed = @('    <Version>2.0.0</Version>'); Added = @('    <Version>2.0.1</Version>') } }
        Test-DiffSafety @($csproj) $ok @{} | Should -BeNullOrEmpty
        $pkg = @{ $csproj.Path = [pscustomobject]@{ Removed = @('    <Version>2.0.0</Version>'); Added = @('    <Version>2.0.1</Version><PackageReference Include="Evil" />') } }
        Test-DiffSafety @($csproj) $pkg @{} | Should -Match 'version line'
        $extra = @{ $csproj.Path = [pscustomobject]@{ Removed = @(); Added = @('<Target Name="X" BeforeTargets="Build"><Exec Command="calc" /></Target>') } }
        Test-DiffSafety @($csproj) $extra @{} | Should -Match 'version line'
        $iss = [pscustomobject]@{ Path = 'installer/DLSSUpdater.iss'; Status = 'M'; NewMode = '100644'; Binary = $false }
        $run = @{ $iss.Path = [pscustomobject]@{ Removed = @(); Added = @('[Run]') } }
        Test-DiffSafety @($iss) $run @{} | Should -Match 'version line'
    }
    It 'catches a real binary and .gitattributes in a git diff' {
        $d = New-TempRepo
        try {
            Set-Content (Join-Path $d 'a.txt') 'x'; git -C $d add -A; git -C $d commit --quiet -m one
            New-Item -ItemType Directory (Join-Path $d 'src') | Out-Null
            [IO.File]::WriteAllBytes((Join-Path $d 'src/evil.cs'), [byte[]](0, 1, 2, 0, 255))
            Set-Content (Join-Path $d '.gitattributes') '*.cs -diff'
            git -C $d add -A; git -C $d commit --quiet -m two
            $x = Get-Diff $d
            $v = (Test-DiffSafety $x.Entries $x.Hunks @{}) -join ';'
            $v | Should -Match 'src/evil.cs: binary'
            $v | Should -Match '.gitattributes: off-limits'
        } finally { Remove-Item -Recurse -Force $d }
    }
}

Describe 'pin bump (findings 2a/2d: allowlist of paths and line patterns)' {
    It 'reads and bumps the real OptiScaler pins' {
        $text = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src/DLSSUpdater/Core/OptiSources.cs'))
        $nr = Get-PinnedTag $text $NrAsset
        $f5 = Get-PinnedTag $text $F5Asset
        $nr | Should -Match '^v\d'; $f5 | Should -Match '^v\d'
        $new = Update-PinText $text $NrAsset $nr 'v9.9.99'
        Get-PinnedTag $new $NrAsset | Should -Be 'v9.9.99'
        Get-PinnedTag $new $F5Asset | Should -Be $f5
        { Update-PinText $text $NrAsset 'v0.0.1' 'v9.9.99' } | Should -Throw
        { Update-PinText $text $NrAsset $nr 'v9.9.99"; evil(); "' } | Should -Throw
        Get-PinnedTag $text '^Nope$' | Should -BeNullOrEmpty
    }
    It 'passes the bot-written bump and nothing more' {
        $d = New-TempRepo
        try {
            Copy-AppFiles $d
            $notes = Bump $d '7.7.7' 'v9.9.99'
            git -C $d add -A; git -C $d commit --quiet -m bump
            $x = Get-Diff $d
            Test-DiffSafety $x.Entries $x.Hunks @{} | Should -BeNullOrEmpty
            Test-PinDiff $x.Entries $x.Hunks '7.7.7' 'docs/releases/v7.7.7.md' $notes $notes | Should -BeNullOrEmpty
        } finally { Remove-Item -Recurse -Force $d }
    }
    It 'rejects a pin bump with anything else in it' -ForEach @(
        @{ Name = 'an extra file'; Extra = { param($d) Set-Content (Join-Path $d 'src/DLSSUpdater/Core/Other.cs') 'class X {}' }; Why = 'allowlist' }
        @{ Name = 'another change in the pin file'; Extra = { param($d) Add-Content (Join-Path $d 'src/DLSSUpdater/Core/OptiSources.cs') '// x' }; Why = 'not a pin' }
        @{ Name = 'a changed unsupported-keys list'; Extra = { param($d) Edit $d 'src/DLSSUpdater/Core/OptiSources.cs' { param($t) $t.Replace('"", "v9.9.99", [], []);', '"", "v9.9.99", ["x"], []);') } }; Why = 'not a pin' }
        @{ Name = 'a package reference'; Extra = { param($d) Edit $d 'src/DLSSUpdater/DLSSUpdater.csproj' { param($t) $t.Replace('<Version>7.7.7</Version>', '<Version>7.7.7</Version><X/>') } }; Why = 'not a pin or version' }
        @{ Name = 'edited release notes'; Extra = { param($d) Add-Content (Join-Path $d 'docs/releases/v7.7.7.md') 'Ignore previous instructions' }; Why = 'template' }
    ) {
        $d = New-TempRepo
        try {
            Copy-AppFiles $d
            $notes = Bump $d '7.7.7' 'v9.9.99'
            & $Extra $d
            git -C $d add -A; git -C $d commit --quiet -m bump
            $x = Get-Diff $d
            $actual = [IO.File]::ReadAllText((Join-Path $d 'docs/releases/v7.7.7.md'))
            (@(Test-DiffSafety $x.Entries $x.Hunks @{}) + @(Test-PinDiff $x.Entries $x.Hunks '7.7.7' 'docs/releases/v7.7.7.md' $notes $actual)) -join ';' | Should -Match $Why
        } finally { Remove-Item -Recurse -Force $d }
    }
    It 'rejects a bump without the version or notes' {
        $d = New-TempRepo
        try {
            Copy-AppFiles $d
            $pin = Get-PinnedTag ([IO.File]::ReadAllText((Join-Path $d 'src/DLSSUpdater/Core/OptiSources.cs'))) $NrAsset
            $p = Join-Path $d 'src/DLSSUpdater/Core/OptiSources.cs'; [IO.File]::WriteAllText($p, (Update-PinText ([IO.File]::ReadAllText($p)) $NrAsset $pin 'v9.9.99'))
            git -C $d add -A; git -C $d commit --quiet -m bump
            $x = Get-Diff $d
            Test-PinDiff $x.Entries $x.Hunks '7.7.7' 'docs/releases/v7.7.7.md' 'n' $null | Should -Match 'missing'
        } finally { Remove-Item -Recurse -Force $d }
    }
}

Describe 'config-neutral proof' {
    BeforeAll {
        $old = @{ Tag = 'v0.9.33'; Prerelease = $false; RawIni = 'i'; ZipIni = 'z'; Entries = 'e' }
    }
    It 'proves identical builds' {
        Get-PinProof $old @{ Tag = 'v0.9.34'; Prerelease = $false; RawIni = 'i'; ZipIni = 'z'; Entries = 'e' } | Should -BeNullOrEmpty
    }
    It 'refuses when anything differs or is unknown' -ForEach @(
        @{ New = @{ Tag = 'v0.9.34'; Prerelease = $false; RawIni = 'X'; ZipIni = 'z'; Entries = 'e' }; Why = 'RawIni differs' }
        @{ New = @{ Tag = 'v0.9.34'; Prerelease = $false; RawIni = 'i'; ZipIni = 'X'; Entries = 'e' }; Why = 'ZipIni differs' }
        @{ New = @{ Tag = 'v0.9.34'; Prerelease = $false; RawIni = 'i'; ZipIni = 'z'; Entries = 'X' }; Why = 'Entries differs' }
        @{ New = @{ Tag = 'v0.9.34'; Prerelease = $false; RawIni = 'i'; ZipIni = $null; Entries = 'e' }; Why = 'unknown' }
        @{ New = @{ Tag = 'v0.9.34'; Prerelease = $true; RawIni = 'i'; ZipIni = 'z'; Entries = 'e' }; Why = 'prerelease' }
        @{ New = @{ Tag = 'v0.9.34'; Prerelease = $null; RawIni = 'i'; ZipIni = 'z'; Entries = 'e' }; Why = 'prerelease' }
        @{ New = @{ Tag = 'v0.10.0'; Prerelease = $false; RawIni = 'i'; ZipIni = 'z'; Entries = 'e' }; Why = 'minor line' }
        @{ New = @{ Tag = 'v0.9.32'; Prerelease = $false; RawIni = 'i'; ZipIni = 'z'; Entries = 'e' }; Why = 'not newer' }
        @{ New = @{ Tag = 'v0.9.34-hotfix'; Prerelease = $false; RawIni = 'i'; ZipIni = 'z'; Entries = 'e' }; Why = 'plain' }
    ) {
        (Get-PinProof $old $New) -join ';' | Should -Match $Why
    }
    It 'normalises only BOM, line ends and trailing whitespace' {
        ConvertTo-NormalIni "$([char]0xFEFF)[A]  `r`nKey=1`t`r`n`r`n" | Should -Be "[A]`nKey=1"
        ConvertTo-NormalIni "[A]`nKey = 1" | Should -Not -Be (ConvertTo-NormalIni "[A]`nKey=1")
    }
    It 'masks the build version in zip folder names' {
        ConvertTo-EntryList @('OptiScaler-NR-v0.9.33/OptiScaler.dll', 'OptiScaler-NR-v0.9.33/', 'README.txt') 'v0.9.33' |
            Should -Be @('optiscaler-nr-{v}/optiscaler.dll', 'readme.txt')
    }
}

Describe "Claude's tree vs the base" {
    It 'finds added, changed and deleted files and skips .git, bin and obj' {
        $a = Join-Path ([IO.Path]::GetTempPath()) "rbA-$([guid]::NewGuid().ToString('N').Substring(0, 6))"
        $b = "$a-b"
        try {
            foreach ($d in $a, $b) { New-Item -ItemType Directory -Force "$d/src", "$d/.git", "$d/src/bin", "$d/.github" | Out-Null; Set-Content "$d/src/same.cs" 'x'; Set-Content "$d/.git/config" 'x'; Set-Content "$d/.gitignore" 'x'; Set-Content "$d/.github/ci.yml" 'x' }
            Set-Content "$a/src/gone.cs" 'x'; Set-Content "$a/src/edit.cs" 'x'; Set-Content "$b/src/edit.cs" 'y'; Set-Content "$b/src/new.cs" 'n'
            Set-Content "$b/.git/config" '[core] fsmonitor = calc'; Set-Content "$b/src/bin/out.dll" 'x'
            $r = Get-TreeChanges $a $b
            $r.Violations | Should -BeNullOrEmpty
            ($r.Changes | ForEach-Object { "$($_.Kind) $($_.Path)" }) -join ',' | Should -Be 'change src/edit.cs,delete src/gone.cs,add src/new.cs'
        } finally { Remove-Item -Recurse -Force $a, $b -ErrorAction SilentlyContinue }
    }
    It 'rejects names Windows rewrites or git treats specially' {
        foreach ($p in '.git.', 'src/.git', 'src/.gitattributes', 'docs/x.md.', 'docs/x.md ', 'GIT~1/config', 'src/a:b.cs') { Test-SafeRelPath $p | Should -BeFalse -Because $p }
        foreach ($p in 'src/DLSSUpdater/Core/X.cs', 'docs/releases/v2.0.1.md', 'README.md') { Test-SafeRelPath $p | Should -BeTrue -Because $p }
    }
    It 'reports a changed .gitignore' {
        $a = Join-Path ([IO.Path]::GetTempPath()) "rbI-$([guid]::NewGuid().ToString('N').Substring(0, 6))"
        $b = "$a-b"
        try {
            New-Item -ItemType Directory -Force $a, $b | Out-Null
            Set-Content "$a/.gitignore" 'x'; Set-Content "$b/.gitignore" 'y'
            (Get-TreeChanges $a $b).Violations | Should -Be @('.gitignore: unsafe name')
        } finally { Remove-Item -Recurse -Force $a, $b -ErrorAction SilentlyContinue }
    }
    It 'reports a .git. file instead of copying it over the worktree pointer' {
        $a = Join-Path ([IO.Path]::GetTempPath()) "rbG-$([guid]::NewGuid().ToString('N').Substring(0, 6))"
        $b = "$a-b"
        try {
            New-Item -ItemType Directory -Force $a, $b | Out-Null
            Set-Content "$a/.git" 'gitdir: C:/bot'
            # \\?\ keeps the trailing dot, the way Node (Claude's Write tool) creates it.
            [IO.File]::WriteAllText("\\?\$b\.git.", 'gitdir: C:/evil')
            (Get-TreeChanges $a $b).Violations | Should -Be @('.git.: unsafe name')
        } finally { Remove-Item -LiteralPath "\\?\$b\.git." -Force -ErrorAction SilentlyContinue; Remove-Item -Recurse -Force $a, $b -ErrorAction SilentlyContinue }
    }
    It 'reports a junction instead of following it' {
        $a = Join-Path ([IO.Path]::GetTempPath()) "rbJ-$([guid]::NewGuid().ToString('N').Substring(0, 6))"
        $b = "$a-b"
        try {
            New-Item -ItemType Directory -Force $a, $b | Out-Null
            New-Item -ItemType Junction -Path "$b/docs" -Target $env:TEMP | Out-Null
            (Get-TreeChanges $a $b).Violations | Should -Be @('docs: link')
        } finally { cmd /c rmdir "$b\docs" 2>$null; Remove-Item -Recurse -Force $a, $b -ErrorAction SilentlyContinue }
    }
}

Describe "Lecram's release-note sections" {
    BeforeAll {
        # A made-up page in the shape of the post (no real post text).
        $html = @'
<html><head><style>.x{color:red}</style><script>var notes = "v11 Patch 99";</script></head><body>
<nav>Home <a href="/x">Log in</a></nav>
<article><h1>DLSS generic</h1>
<h3>v11 Patch 10</h3><p><strong>New</strong></p><ul><li>Option&nbsp;A for testing</li></ul>
<h3>v11 Patch 11</h3><p>New</p><ul><li>Key <code>Foo=1</code> under [Bar]</li></ul><p>Fixed</p><ul><li>A crash &amp; a hang</li></ul>
<p>v11 Patch 11 is mentioned again in a sentence later on.</p>
</article><footer>Support me</footer></body></html>
'@
    }
    It 'turns the DOM into lines without scripts or styles' {
        $t = ConvertFrom-HtmlText $html
        $t | Should -Not -Match 'Patch 99'
        $t | Should -Not -Match 'color:red'
        $t -split "`n" | Should -Contain '- Option A for testing'
        $t -split "`n" | Should -Contain '- A crash & a hang'
    }
    It 'finds the patch sections and their text, newest last' {
        $s = @(Get-PatchSections (ConvertFrom-HtmlText $html))
        $s.Key | Should -Be @('v11 Patch 10', 'v11 Patch 11')
        $s[1].Text | Should -Match 'Foo=1'
        $s[1].Text | Should -Match 'A crash & a hang'
        $s[0].Text | Should -Not -Match 'Foo=1'
    }
    It 'picks only the sections after the last one seen' {
        $s = @(Get-PatchSections (ConvertFrom-HtmlText $html))
        (Select-NewPatchSections $s 'v11 Patch 10').Key | Should -Be @('v11 Patch 11')
        Select-NewPatchSections $s 'v11 Patch 11' | Should -BeNullOrEmpty
        @(Select-NewPatchSections $s $null).Count | Should -Be 2
        (Select-NewPatchSections $s 'v10 Patch 30').Count | Should -Be 2
    }
    It 'finds nothing on a logged-out page' {
        Get-PatchSections (ConvertFrom-HtmlText '<html><body><h1>Members only</h1><a>Log in</a></body></html>') | Should -BeNullOrEmpty
    }
    It 'ends the last section before the comments (N4)' {
        $page = @'
<html><body><article><h3>v11 Patch 11</h3><p>New</p><ul><li>Key Foo=1 under [Bar]</li></ul></article>
<section class="comments"><p>2 comments</p><div><p>member one</p><p>Private note from a member that must stay out</p></div></section>
<footer>Buy Lecram a coffee</footer></body></html>
'@
        $s = @(Get-PatchSections (ConvertFrom-HtmlText $page))
        $s.Key | Should -Be @('v11 Patch 11')
        $s[0].Text | Should -Match 'Foo=1'
        $s[0].Text | Should -Not -Match 'member'
        # Without any page structure, the comment box's own lines still end it.
        $flat = "v11 Patch 12`nNew key Baz=2`n3 Comments`nmember two: private text"
        $f = @(Get-PatchSections $flat)
        $f[0].Text | Should -Be 'New key Baz=2'
    }
    It 'spots the paid notes quoted in text that would be published (N4)' {
        $notes = @("#### v11 Patch 12`nThe new Sharpen Boost slider now goes up to 2.5 and defaults to 1.2 in every preset.")
        Test-ContainsPaidText 'Summary: the new sharpen boost slider now goes up to 2.5, and defaults to 1.2 in every preset!' $notes | Should -BeTrue
        Test-ContainsPaidText 'Raised SharpenBoost max to 2.5 (default 1.2), checked with strings.' $notes | Should -BeFalse
        Test-ContainsPaidText 'anything' @() | Should -BeFalse
    }
}

Describe 'gates (M1: Claude never as the owner by accident)' {
    It 'refuses the real Claude without -RunAs, dry run or not' {
        Get-ClaudeLaunchRefusal '' $false | Should -Match 'RunAs'
        Get-ClaudeLaunchRefusal $null $false | Should -Match 'RunAs'
        Get-ClaudeLaunchRefusal 'dlss5bot' $false | Should -BeNullOrEmpty
        Get-ClaudeLaunchRefusal '' $true | Should -BeNullOrEmpty
    }
    It 'starts Claude restricted, without settings sources' {
        $s = Get-Content -Raw (Join-Path $PSScriptRoot 'release-bot.ps1')
        $s | Should -Match "'--restricted'"
        $s | Should -Match "'--permission-prompts', 'none'"
        $s | Should -Not -Match "'--setting-sources'"
        $s | Should -Match 'Get-ClaudeLaunchRefusal \$RunAs \$UnsafeRunClaudeAsOwner'
    }
    It 'install.ps1 -Enable without -RunAs throws before it touches anything' {
        Mock Register-ScheduledTask {}; Mock Disable-ScheduledTask {}; Mock Unregister-ScheduledTask {}; Mock Copy-Item {}
        { & (Join-Path $PSScriptRoot 'install.ps1') -Enable } | Should -Throw '*-RunAs*'
        { & (Join-Path $PSScriptRoot 'install.ps1') -Enable -DryRun } | Should -Throw '*-RunAs*'
        Should -Invoke Register-ScheduledTask -Times 0 -Exactly
        Should -Invoke Copy-Item -Times 0 -Exactly
    }
}

Describe 'gates (M2: the auto path never releases unreleased work on main)' {
    BeforeAll {
        $d = New-TempRepo
        function Commit([string]$rel, [string]$text) {
            $p = Join-Path $d $rel; New-Item -ItemType Directory -Force (Split-Path $p) | Out-Null
            [IO.File]::WriteAllText($p, $text); git -C $d add -A; git -C $d commit --quiet -m $rel
        }
        Commit 'src/a.cs' 'one'
        git -C $d tag -a v2.0.1 -m 'v2.0.1'
        $g = Join-Path $d '.git'
    }
    AfterAll { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }
    It 'allows main that is the release tag, docs aside' {
        Get-UnreleasedReason $g '2.0.1' 'HEAD' | Should -BeNullOrEmpty
        Commit 'docs/releases/v2.0.2.md' 'notes'
        Get-UnreleasedReason $g '2.0.1' 'HEAD' | Should -BeNullOrEmpty
    }
    It 'blocks main with unreleased changes, and a version without a tag' {
        Commit 'src/b.cs' 'unreleased work'
        Get-UnreleasedReason $g '2.0.1' 'HEAD' | Should -Match 'unreleased'
        Get-UnreleasedReason $g '2.0.1' 'HEAD~1' | Should -BeNullOrEmpty
        Get-UnreleasedReason $g '2.0.2' 'HEAD' | Should -Match 'no tag v2.0.2'
    }
}

Describe 'gates (M3: owner hooks run, but never over repo content)' {
    BeforeEach {
        $wt = Join-Path $TestDrive "wt-$([guid]::NewGuid().ToString('N').Substring(0, 6))"; $gd = Join-Path $TestDrive "git-$([guid]::NewGuid().ToString('N').Substring(0, 6))"
        New-Item -ItemType Directory -Force $wt, (Join-Path $gd 'hooks') | Out-Null
        Set-Content (Join-Path $gd 'hooks\pre-push.sample') 'x'
    }
    It 'has no core.hooksPath override in the bot git calls' {
        Get-Content -Raw (Join-Path $PSScriptRoot 'release-bot.ps1') | Should -Not -Match 'core\.hooksPath='
    }
    It 'passes a clean tree and a mirror with only sample hooks' {
        Find-PushBlockers $wt $gd | Should -BeNullOrEmpty
    }
    It 'refuses a tree with a .prepush and a mirror with a repo hook' {
        Set-Content (Join-Path $wt '.prepush') 'dotnet build'
        @(Find-PushBlockers $wt $gd) | Should -HaveCount 1
        Set-Content (Join-Path $gd 'hooks\post-checkout') '#!/bin/sh'
        @(Find-PushBlockers $wt $gd) | Should -HaveCount 2
        @(Find-PushBlockers $null $gd) | Should -HaveCount 1
    }
    It 'finds AI trailers in bot commit messages' {
        Test-NoAiTrailer "Release bot: OptiScaler-NR v0.9.34 tested`n" | Should -BeTrue
        Test-NoAiTrailer "x`n`nCo-Authored-By: Claude <noreply@anthropic.com>`n" | Should -BeFalse
    }
}

Describe 'gates (N3: auto-merge really off on review PRs)' {
    It 'trusts only an explicit null autoMergeRequest' {
        Test-AutoMergeOff ('{"autoMergeRequest":null,"isDraft":false}' | ConvertFrom-Json) | Should -BeTrue
        Test-AutoMergeOff ('{"autoMergeRequest":{"mergeMethod":"SQUASH"},"isDraft":false}' | ConvertFrom-Json) | Should -BeFalse
        Test-AutoMergeOff ('{"isDraft":false}' | ConvertFrom-Json) | Should -BeFalse
        Test-AutoMergeOff $null | Should -BeFalse
    }
}
