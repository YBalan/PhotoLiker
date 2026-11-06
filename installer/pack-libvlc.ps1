<#
.SYNOPSIS
Packs the VLC engine that PhotoLiker downloads (the files of libvlc\win-x64 of a build) into a zip, and writes the list of its files.

.DESCRIPTION
A release is built with -p:BundleVlc=false (installer\build-installer.ps1), so the installer does not carry the engine (23 MB of it). The program downloads
it the first time videos are turned on or played (PhotoLiker.Video\VlcEngine.cs). This script makes what it downloads:

  publish\components\libvlc-<version>-win-x64.zip   the files of the engine, nothing else (about 40 MB), to be attached to a GitHub release
  PhotoLiker.Video\libvlc-files.txt                 the list of those files: the program unpacks only what is listed, from this zip or from VideoLAN's own zip

The engine is taken from a build with the same rules that the installer would have used (the plugins left out are in PhotoLikerUI.csproj), so the two
cannot differ. Run it again whenever the VLC version of the package (VideoLAN.LibVLC.Windows in PhotoLikerUI.csproj) or the rules change, then:
  1. put the SHA-256 and the size that it prints into VlcEngine.cs (the source named "PhotoLiker release"),
  2. attach the zip to the GitHub release that VlcEngine.cs names (the command is printed; the release is a pre-release, so "Check for updates" ignores it).

.PARAMETER OutputDir
Where the zip goes. Defaults to publish\components (not in git).
#>
[CmdletBinding()]
param(
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project  = Join-Path $repoRoot 'PhotoLikerUI\PhotoLikerUI.csproj'
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'publish\components' }

$version = ([xml](Get-Content $project -Raw)).Project.ItemGroup.PackageReference |
    Where-Object { $_.Include -eq 'VideoLAN.LibVLC.Windows' } | Select-Object -First 1 -ExpandProperty Version
if (-not $version) { throw 'The version of VideoLAN.LibVLC.Windows was not found in PhotoLikerUI.csproj.' }

$stage = Join-Path ([IO.Path]::GetTempPath()) ('photoliker-libvlc-' + [Guid]::NewGuid().ToString('N'))
try {
    Write-Host "Building PhotoLikerUI with the engine ($version) into $stage"
    dotnet publish $project -c Release -r win-x64 --self-contained false -p:BundleVlc=true -o $stage
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    $engine = Join-Path $stage 'libvlc\win-x64'
    if (-not (Test-Path (Join-Path $engine 'libvlc.dll'))) { throw "libvlc.dll is not in $engine" }

    # The list of the files: paths with /, in order, nothing else in the file
    $files = Get-ChildItem $engine -Recurse -File |
        ForEach-Object { $_.FullName.Substring($engine.Length + 1).Replace('\', '/') } |
        Sort-Object { $_ } -CaseSensitive
    $list = Join-Path $repoRoot 'PhotoLiker.Video\libvlc-files.txt'
    Set-Content -Path $list -Value $files -Encoding utf8
    Write-Host ("Wrote {0} ({1} files)" -f $list, @($files).Count)

    New-Item -ItemType Directory -Force $OutputDir | Out-Null
    $zip = Join-Path $OutputDir "libvlc-$version-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($engine, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $bytes = (Get-Item $zip).Length
    Write-Host ''
    Write-Host ("Created {0} ({1:N1} MB)" -f $zip, ($bytes / 1MB))
    Write-Host "  SHA-256: $hash"
    Write-Host "  bytes:   $bytes"
    Write-Host ''
    Write-Host 'Put the SHA-256 and the size into VlcEngine.cs, then attach the zip to its release (once for each VLC version):'
    Write-Host "  gh release create libvlc-$version --prerelease --title `"VLC engine $version (downloaded by PhotoLiker)`" --notes `"The files of libvlc $version for Windows x64 that PhotoLiker downloads when videos are turned on. VideoLAN's, unchanged; see libvlc\VLC-NOTICE.txt.`" `"$zip`""
}
finally {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
}
