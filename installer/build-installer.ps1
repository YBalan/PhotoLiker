<#
.SYNOPSIS
Publishes PhotoLikerUI and packs it into a small Inno Setup installer.

.DESCRIPTION
Output: publish\installer\PhotoLiker-Setup-<version>.exe
The app is published framework-dependent (like the CI build), so the installer stays small; setup checks for the
.NET 10 Desktop Runtime instead of bundling it. Requires Inno Setup 6 (winget install JRSoftware.InnoSetup).

.PARAMETER Version
Version stamped on the installer. Defaults to the assembly version of the freshly published build.

.PARAMETER IsccPath
Path to ISCC.exe when Inno Setup is not on PATH or in its default install folders.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'

$repoRoot  = Split-Path -Parent $PSScriptRoot
$project   = Join-Path $repoRoot 'PhotoLikerUI\PhotoLikerUI.csproj'
$script    = Join-Path $PSScriptRoot 'PhotoLiker.iss'
$stageDir  = Join-Path $repoRoot 'publish\PhotoLikerUI'
$outputDir = Join-Path $repoRoot 'publish\installer'

function Find-Iscc {
    if ($IsccPath) { return $IsccPath }

    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

$iscc = Find-Iscc
if (-not $iscc -or -not (Test-Path $iscc)) {
    throw 'Inno Setup 6 (ISCC.exe) not found. Install it with "winget install JRSoftware.InnoSetup" or pass -IsccPath.'
}

if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }

Write-Host "Publishing PhotoLikerUI to $stageDir"
dotnet publish $project -c Release -r win-x64 --self-contained false -o $stageDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

if (-not $Version) {
    $Version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $stageDir 'PhotoLikerUI.dll')).Version.ToString()
}

Write-Host "Packing installer $Version with $iscc"
& $iscc /Qp "/DAppVersion=$Version" "/DSourceDir=$stageDir" "/DOutputDir=$outputDir" $script
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$setup = Join-Path $outputDir "PhotoLiker-Setup-$Version.exe"
Write-Host ("Created {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
