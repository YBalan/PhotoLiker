# Builds the MAUI app for Windows and runs its smoke tour: the real window opens on a generated demo folder,
# drives its own viewer, checks every step and exits with 0 (all checks passed) or 1. The log of the checks is
# printed here. See PhotoLiker.Maui\SmokeTour\SmokeTourOptions.cs for the arguments the app understands.
#
#   .\tools\maui-smoke-tour.ps1                 build Debug and run the tour
#   .\tools\maui-smoke-tour.ps1 -Configuration Release
#   .\tools\maui-smoke-tour.ps1 -Keep           leave the demo folder behind
#   .\tools\maui-smoke-tour.ps1 -NoBuild        run what is already built
param(
    [string]$Configuration = 'Debug',
    [switch]$Keep,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'PhotoLiker.Maui\PhotoLiker.Maui.csproj'
$framework = 'net10.0-windows10.0.19041.0'

if (-not $NoBuild) {
    # Only the Windows head is built: the tour never compiles Android
    dotnet build $project -f $framework -c $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$exe = Get-ChildItem -Path (Join-Path $repo "PhotoLiker.Maui\bin\$Configuration\$framework") -Filter 'PhotoLiker.Maui.exe' -Recurse | Select-Object -First 1
if (-not $exe) {
    Write-Error "PhotoLiker.Maui.exe was not built under bin\$Configuration\$framework"
    exit 2
}

$log = Join-Path $env:TEMP ("photoliker-maui-smoke-tour-" + [guid]::NewGuid().ToString('N') + ".log")
$tourArgs = @('--smoke-tour', '--smoke-tour-out', $log)
if ($Keep) { $tourArgs += '--keep' }

Write-Host "Running the smoke tour: $($exe.FullName)"
Write-Host 'The window takes the screen for a few seconds: leave the mouse and the keyboard alone.'
$process = Start-Process -FilePath $exe.FullName -ArgumentList $tourArgs -PassThru -Wait

if (Test-Path $log) {
    Get-Content $log | Write-Host
    Remove-Item $log
} else {
    Write-Warning "The tour wrote no log ($log): it probably crashed before it could."
}

exit $process.ExitCode
