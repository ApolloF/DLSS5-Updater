#Requires -Version 7.2
<#
.SYNOPSIS
  Installs the release bot into %LocalAppData%\dlss5updater-releasebot\bin and registers the scheduled task.
.EXAMPLE
  pwsh tools/release-bot/install.ps1 -DryRun     # task runs the bot with -DryRun (for checking the task environment)
  pwsh tools/release-bot/install.ps1             # the real schedule, every 6 hours
  pwsh tools/release-bot/install.ps1 -Remove     # unregister the task (state, logs and clone stay)
#>
[CmdletBinding()]
param([switch]$DryRun, [int]$Hours = 6, [switch]$Remove)

$ErrorActionPreference = 'Stop'
$TaskName = 'DLSS5UpdaterReleaseBot'

if ($Remove) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Removed scheduled task $TaskName."
    return
}

foreach ($v in 'DLSS5_DROP_PW', 'DLSS5_BOT_NTFY_TOKEN') {
    if (-not [Environment]::GetEnvironmentVariable($v, 'User')) { Write-Warning "user env var $v is not set (see README.md)" }
}

$bin = Join-Path $env:LOCALAPPDATA 'dlss5updater-releasebot\bin'
New-Item -ItemType Directory -Force $bin | Out-Null
Copy-Item -Force (Join-Path $PSScriptRoot 'release-bot.ps1'), (Join-Path $PSScriptRoot 'prompt.md') $bin
$script = Join-Path $bin 'release-bot.ps1'

$pwsh = (Get-Command pwsh).Source
$botArgs = "-NoProfile -ExecutionPolicy Bypass -File `"$script`"$(if ($DryRun) { ' -DryRun' })"
# conhost --headless keeps the console window from flashing up every run.
$action = New-ScheduledTaskAction -Execute 'conhost.exe' -Argument "--headless `"$pwsh`" $botArgs"
$start = (Get-Date).Date.AddHours([math]::Ceiling((Get-Date).TimeOfDay.TotalHours / $Hours) * $Hours)
$trigger = New-ScheduledTaskTrigger -Once -At $start -RepetitionInterval (New-TimeSpan -Hours $Hours)
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 2)
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
    -Description "DLSS5 Updater release bot (tools/release-bot in ApolloF/dlss5updater)$(if ($DryRun) { ' - DRY RUN' })" -Force | Out-Null
Write-Host "Registered $TaskName$(if ($DryRun) { ' (dry run)' }): every $Hours h from $start; bot installed in $bin."
