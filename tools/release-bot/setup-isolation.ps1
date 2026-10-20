#Requires -Version 7.2
#Requires -RunAsAdministrator
<#
.SYNOPSIS
  One-time setup of the isolated user the release bot runs Claude as (README, "Full isolation", steps 1, 3 and 4).
.DESCRIPTION
  Run elevated as the owner (gsudo), not as another admin: the bot user's password is stored with DPAPI for the
  account that runs it, and only that account can read it back. Safe to run again; it then updates the tools and
  isolation.ps1 and re-applies the folder rules, but keeps the user and its password (-ResetPassword makes a new one).
  - creates the standard local user (default dlss5bot) with a random password nobody sees, hidden from the sign-in
    screen, and stores the credential for the owner in %LocalAppData%\dlss5updater-releasebot\<user>.cred.xml;
  - creates the isolation root (default C:\dlss5bot-iso: owner full, bot modify) with its home\ for the bot's caches,
    and C:\dlss5bot-tools (owner full, bot read) with a copy of the owner's claude.exe;
  - denies the bot user every access to the top-level folders that all users can write to (C:\dev and the like):
    it must not be able to change or read the owner's repos or tools;
  - does the same for folders on the PATH that all users can write to;
  - copies isolation.ps1 next to the bot's state, where release-bot.ps1 looks for it.
.EXAMPLE
  gsudo pwsh -NoProfile -File tools/release-bot/setup-isolation.ps1
#>
[CmdletBinding()]
param(
    [string]$User = 'dlss5bot',
    [string]$IsolationRoot = 'C:\dlss5bot-iso',
    [string]$ToolsDir = 'C:\dlss5bot-tools',
    [switch]$ResetPassword
)

$ErrorActionPreference = 'Stop'
$Root = Join-Path $env:LOCALAPPDATA 'dlss5updater-releasebot'
$Owner = "$env:USERDOMAIN\$env:USERNAME"
$CredFile = Join-Path $Root "$User.cred.xml"
Import-Module (Join-Path $PSScriptRoot 'ReleaseBot.psm1') -Force
$why = Get-IsolationRootRefusal $IsolationRoot $Root
if ($why) { throw "-IsolationRoot refused: $why" }
if ($User -notmatch '^[A-Za-z][A-Za-z0-9_-]{2,19}$') { throw "-User must be a plain local user name" }

# 1. The user: standard (Users group only), random password, never shown.
function New-Password {
    $chars = [char[]]'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789-_!#%+='
    $sec = [Security.SecureString]::new()
    foreach ($b in [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) { $sec.AppendChar($chars[$b % $chars.Length]) }
    $sec.MakeReadOnly(); $sec
}
$existing = Get-LocalUser -Name $User -ErrorAction SilentlyContinue
if (-not $existing) {
    $pw = New-Password
    New-LocalUser -Name $User -Password $pw -PasswordNeverExpires -UserMayNotChangePassword -AccountNeverExpires `
        -Description 'DLSS5 Updater release bot: isolated Claude runs' | Out-Null
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $User  # Users, needed to log on; never Administrators
    [pscredential]::new($User, $pw) | Export-Clixml $CredFile
    Write-Host "Created user $User; credential stored for $Owner in $CredFile"
} elseif ($ResetPassword -or -not (Test-Path $CredFile)) {
    $pw = New-Password
    Set-LocalUser -Name $User -Password $pw
    [pscredential]::new($User, $pw) | Export-Clixml $CredFile
    Write-Host "Reset the password of $User; credential stored in $CredFile"
} else { Write-Host "User $User exists; credential kept" }
if (Get-LocalGroupMember -SID 'S-1-5-32-544' | Where-Object Name -like "*\$User") { throw "$User is an administrator; remove it from Administrators first" }
$winlogon = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList'
New-Item -Force $winlogon | Out-Null
New-ItemProperty -Path $winlogon -Name $User -Value 0 -PropertyType DWord -Force | Out-Null
# The credential file is the owner's alone (it sits in the owner's profile; make sure of it anyway).
icacls $CredFile /inheritance:r /grant:r "${Owner}:F" 'SYSTEM:F' | Out-Null

# 2. Folders with their own, non-inherited rules.
function Set-Folder([string]$path, [string]$botRights) {
    New-Item -ItemType Directory -Force $path | Out-Null
    icacls $path /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' "${Owner}:(OI)(CI)F" "${User}:(OI)(CI)$botRights" | Out-Null
    icacls $path /setowner $Owner | Out-Null
}
Set-Folder $IsolationRoot 'M'
New-Item -ItemType Directory -Force (Join-Path $IsolationRoot 'home') | Out-Null
Set-Folder $ToolsDir 'RX'
Copy-Item -Force (Join-Path $env:USERPROFILE '.local\bin\claude.exe') (Join-Path $ToolsDir 'claude.exe')
Write-Host "Isolation root $IsolationRoot, tools $ToolsDir (claude $(& (Join-Path $ToolsDir 'claude.exe') --version))"

# 3. Folders every user may write to get an explicit deny for the bot: C:\ hands Authenticated Users modify to every
# new top-level folder (C:\dev with the owner's repos among them), and a PATH folder like that could hold a program
# the owner runs next. Checked on the folder's own rules, inherited ones included.
$sid = (Get-LocalUser -Name $User).SID.Value
$openTo = 'S-1-5-11', 'S-1-5-32-545', 'S-1-1-0'  # Authenticated Users, Users, Everyone
function Deny-IfOpen([string]$path) {
    $rules = (Get-Acl -LiteralPath $path -ErrorAction SilentlyContinue).Access
    $sids = { param($r) try { $r.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { '' } }
    $open = $rules | Where-Object { $_.AccessControlType -eq 'Allow' -and (& $sids $_) -in $openTo -and
        ($_.FileSystemRights -band ([Security.AccessControl.FileSystemRights]'WriteData, AppendData, Delete, ChangePermissions, TakeOwnership')) }
    $denied = $rules | Where-Object { $_.AccessControlType -eq 'Deny' -and (& $sids $_) -eq $sid -and $_.FileSystemRights -eq 'FullControl' }
    if ($open -and -not $denied) {
        icacls $path /deny "${User}:(OI)(CI)F" | Out-Null
        if ($LASTEXITCODE) { Write-Warning "Could not deny $User on $path; check it by hand" } else { Write-Host "Denied $User on $path" }
    }
}
$skip = 'Windows', 'Program Files', 'Program Files (x86)', 'ProgramData', 'Users', 'PerfLogs', 'Recovery', 'System Volume Information'
# Fixed disks only: touching an empty optical drive can stop the script on a "no disk" dialog.
foreach ($d in Get-CimInstance Win32_LogicalDisk -Filter 'DriveType = 3') {
    foreach ($f in Get-ChildItem "$($d.DeviceID)\" -Directory -Force -ErrorAction SilentlyContinue) {
        if ($f.Name -in $skip -or $f.Name.StartsWith('$')) { continue }
        if ($f.FullName.TrimEnd('\') -in $IsolationRoot.TrimEnd('\'), $ToolsDir.TrimEnd('\')) { continue }
        Deny-IfOpen $f.FullName
    }
}
$path = @([Environment]::GetEnvironmentVariable('Path', 'User'), [Environment]::GetEnvironmentVariable('Path', 'Machine')) -join ';'
foreach ($p in $path -split ';' | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) } | Select-Object -Unique) { Deny-IfOpen $p }

# 4. The hook the bot calls.
Copy-Item -Force (Join-Path $PSScriptRoot 'isolation.ps1') (Join-Path $Root 'isolation.ps1')
Write-Host "Installed $(Join-Path $Root 'isolation.ps1')"
Write-Host "Done. Next: README, Full isolation, step 2 (token) and step 5 (dry run)."
