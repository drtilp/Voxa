# Builds the installer: publishes Voxa (Installer publish profile), then compiles installer\Voxa.iss.
# Usage:  powershell -ExecutionPolicy Bypass -File installer\build.ps1 [-Version 0.2.0] [-ModelSelectionPage 0]
param(
    # Overrides <Version> of Voxa.Wpf.csproj; CI passes the git tag (v0.2.0 -> 0.2.0).
    [string] $Version = '',
    # Feature flag of the installer: 1 = model pages in the installer (variant B), 0 = none (variant A in the app).
    [int] $ModelSelectionPage = 1
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $root 'artifacts\publish\installer'

# Built with += : "$x = if (...) { @('one') }" unrolls to a plain string, and splatting a string passes it char by char.
$versionArgs = @()
if ($Version) { $versionArgs += "/p:Version=$Version" }
dotnet publish (Join-Path $root 'src\Voxa.Wpf\Voxa.Wpf.csproj') -p:PublishProfile=Installer -o $publishDir @versionArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup' }

& $iscc "/DModelSelectionPage=$ModelSelectionPage" "/DPublishDir=$publishDir" (Join-Path $PSScriptRoot 'Voxa.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
Get-ChildItem (Join-Path $root 'artifacts\installer') | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
