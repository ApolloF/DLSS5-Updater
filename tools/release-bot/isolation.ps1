#Requires -Version 7.2
<#
.SYNOPSIS
  The release bot's isolation hook: runs Claude as the low-privilege user in its own copy of the sandbox.
.DESCRIPTION
  setup-isolation.ps1 installs this as %LocalAppData%\dlss5updater-releasebot\isolation.ps1 and release-bot.ps1 calls
  it with -RunAs (README, "Full isolation"). It copies the bot's sandbox into a fresh folder in the isolation root,
  points every path at that copy, starts the program as the user with the credential setup-isolation.ps1 stored, and
  returns the copy without copying anything back: the bot does that itself and refuses links. Everything the run
  starts sits in a job object that is killed when the run ends or times out, so no build server outlives it.
#>
param(
    [Parameter(Mandatory)][string]$User,
    [Parameter(Mandatory)][string]$Exe,
    [string[]]$ArgumentList = @(),
    [Parameter(Mandatory)][string]$WorkingDirectory,
    [Parameter(Mandatory)][hashtable]$Environment,
    [string]$Stdin = '',
    [int]$TimeoutMinutes = 60,
    [Parameter(Mandatory)][string]$Sandbox,
    [string]$IsolationRoot = 'C:\dlss5bot-iso',
    [string]$ToolsDir = 'C:\dlss5bot-tools'
)

$ErrorActionPreference = 'Stop'
$cred = Import-Clixml (Join-Path $PSScriptRoot "$User.cred.xml")
if ($cred.UserName -ne $User) { throw "credential file is for $($cred.UserName), not $User" }

# The owner's claude.exe sits in the owner's profile, which the user can't read: run the copy in the tools folder.
if ((Split-Path $Exe -Leaf) -ieq 'claude.exe') { $Exe = Join-Path $ToolsDir 'claude.exe' }

# Runs older than a week go. rmdir /s removes a link the user planted without following it.
foreach ($old in Get-ChildItem $IsolationRoot -Directory -Filter 'run-*' -ErrorAction SilentlyContinue | Where-Object LastWriteTime -lt (Get-Date).AddDays(-7)) {
    cmd /c rmdir /s /q "`"$($old.FullName)`"" 2>$null
}

$run = Join-Path $IsolationRoot "run-$(Get-Date -Format yyyyMMdd-HHmmss)-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
Copy-Item -LiteralPath $Sandbox -Destination $run -Recurse
$swap = { param([string]$s) [regex]::Replace("$s", [regex]::Escape($Sandbox.TrimEnd('\')), { $run }, 'IgnoreCase') }

# The bot's environment with the sandbox moved, and the user's own profile and caches instead of the owner's.
$botHome = Join-Path $IsolationRoot 'home'
$e = @{}
foreach ($k in $Environment.Keys) { $e[$k] = & $swap $Environment[$k] }
# Windows makes the profile at the first logon and may not name it after the user; ask for it, guess before that.
$sid = ([Security.Principal.NTAccount]$User).Translate([Security.Principal.SecurityIdentifier]).Value
$profileDir = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid" -ErrorAction SilentlyContinue).ProfileImagePath
if (-not $profileDir) { $profileDir = Join-Path (Split-Path $env:USERPROFILE) $User }
$e.USERNAME = $User; $e.USERPROFILE = $profileDir; $e.HOMEDRIVE = $profileDir.Substring(0, 2); $e.HOMEPATH = $profileDir.Substring(2)
$e.APPDATA = Join-Path $botHome 'AppData\Roaming'; $e.LOCALAPPDATA = Join-Path $botHome 'AppData\Local'
$e.CLAUDE_CONFIG_DIR = Join-Path $botHome 'config'; $e.NUGET_PACKAGES = Join-Path $botHome 'nuget-packages'
$e.GIT_CONFIG_GLOBAL = Join-Path $botHome 'gitconfig'
# No MSBuild node reuse or compiler server: nothing a build starts should keep running after the run.
$e.MSBUILDDISABLENODEREUSE = '1'; $e.DOTNET_CLI_USE_MSBUILD_SERVER = '0'; $e.UseSharedCompilation = 'false'
$e.PATH = (@($ToolsDir) + @("$($e.PATH)" -split ';' | Where-Object { $_ -and -not $_.StartsWith($env:USERPROFILE, 'OrdinalIgnoreCase') })) -join ';'
foreach ($d in $e.APPDATA, $e.LOCALAPPDATA, $e.CLAUDE_CONFIG_DIR, $e.NUGET_PACKAGES) { New-Item -ItemType Directory -Force $d | Out-Null }
if (-not (Test-Path $e.GIT_CONFIG_GLOBAL)) {
    $hooks = Join-Path $botHome 'nohooks'; New-Item -ItemType Directory -Force $hooks | Out-Null
    [IO.File]::WriteAllText($e.GIT_CONFIG_GLOBAL, "[user]`n`tname = release-bot sandbox`n`temail = noreply@invalid`n[core]`n`thooksPath = $($hooks -replace '\\', '/')`n`tfsmonitor = false`n")
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace ReleaseBot {
public static class Job {
    [StructLayout(LayoutKind.Sequential)] struct Basic {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit;
        public UIntPtr Affinity; public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] struct Io { public ulong R, W, O, RB, WB, OB; }
    [StructLayout(LayoutKind.Sequential)] struct Extended {
        public Basic BasicLimits; public Io IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr a, string name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int cls, ref Extended info, int len);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateJobObject(IntPtr job, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);
    // A job that kills everything in it when its last handle closes, so a crash of this script also ends the run.
    public static IntPtr Create() {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        var info = new Extended(); info.BasicLimits.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(job, 9, ref info, Marshal.SizeOf(typeof(Extended)))) throw new System.ComponentModel.Win32Exception();
        return job;
    }
    public static void Assign(IntPtr job, IntPtr process) { if (!AssignProcessToJobObject(job, process)) throw new System.ComponentModel.Win32Exception(); }
    public static void Kill(IntPtr job) { TerminateJobObject(job, 1); }
}
}
'@

$psi = [Diagnostics.ProcessStartInfo]::new($Exe)
foreach ($a in $ArgumentList) { $psi.ArgumentList.Add((& $swap $a)) }
$psi.WorkingDirectory = & $swap $WorkingDirectory
$psi.UserName = $User; $psi.Domain = '.'; $psi.Password = $cred.Password; $psi.LoadUserProfile = $true
$psi.Environment.Clear(); foreach ($k in $e.Keys) { $psi.Environment[$k] = $e[$k] }
$psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$psi.StandardInputEncoding = [Text.UTF8Encoding]::new($false); $psi.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)

$job = [ReleaseBot.Job]::Create()
try {
    $p = [Diagnostics.Process]::Start($psi)
    [ReleaseBot.Job]::Assign($job, $p.Handle)
    $out = $p.StandardOutput.ReadToEndAsync(); $err = $p.StandardError.ReadToEndAsync()
    $p.StandardInput.Write((& $swap $Stdin)); $p.StandardInput.Close()
    $code = if ($p.WaitForExit($TimeoutMinutes * 60 * 1000)) { $p.ExitCode } else { -1 }
    # Kill what's left before reading: a leftover child holds the output pipe open and the read would wait for it.
    [ReleaseBot.Job]::Kill($job); $p.WaitForExit()
    $stdout = $out.Result
} finally {
    # Whatever the run left behind (dotnet, MSBuild nodes, a planted watcher) ends here.
    [ReleaseBot.Job]::Kill($job); [void][ReleaseBot.Job]::CloseHandle($job)
}

@{ ExitCode = $code; Stdout = $stdout; Sandbox = $run }
