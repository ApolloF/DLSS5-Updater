# Builds the owner's private portable exe: the public app plus Lecram's MediaFire channel (-p:LecramChannel=true).
# Output: publish-private\DLSS5Updater.exe. Run from anywhere.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'publish-private'

dotnet publish (Join-Path $root 'src/DLSSUpdater') -c Release -p:LecramChannel=true -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

$exe = Join-Path $out 'DLSS5Updater.exe'
if (-not (Test-Path $exe)) { throw "Missing $exe after publish" }
Write-Host "Private build: $exe"
Write-Host "Version: $((Get-Item $exe).VersionInfo.ProductVersion)"
