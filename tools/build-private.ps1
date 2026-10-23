# Builds the owner's private app: the public app plus Lecram's MediaFire channel (-p:LecramChannel=true).
# Output: publish-private\DLSS5Updater.exe (portable) and DLSS5Updater-Setup-<version>-private.exe (installer, needs
# Inno Setup 6; -NoInstaller skips it). Run from anywhere.
param([switch]$NoInstaller)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'publish-private'

dotnet publish (Join-Path $root 'src/DLSSUpdater') -c Release -p:LecramChannel=true -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

$exe = Join-Path $out 'DLSS5Updater.exe'
if (-not (Test-Path $exe)) { throw "Missing $exe after publish" }
$vi = (Get-Item $exe).VersionInfo
Write-Host "Private build: $exe"
Write-Host "Version: $($vi.ProductVersion)"
if ($NoInstaller) { return }

$iscc = @((Get-Command iscc -CommandType Application -ErrorAction SilentlyContinue).Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
    Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found; install it or pass -NoInstaller' }
$version = "$($vi.FileMajorPart).$($vi.FileMinorPart).$($vi.FileBuildPart)"
& $iscc /Q /DPrivate "/DAppVersion=$version" (Join-Path $root 'installer\DLSSUpdater.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }
Write-Host "Installer: $(Join-Path $out "DLSS5Updater-Setup-$version-private.exe")"
