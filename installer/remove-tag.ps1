<#
.SYNOPSIS
Deletes a tag here and on GitHub (the remote one first). For a release that failed before it published anything, so that the same version can be tagged again.

.DESCRIPTION
Without -Tag it lists the 5 newest versions among the tags (marked local / GitHub / both) and asks which one: a number, or the name of any tag. Then it looks at what would go and refuses when that would hurt:
  - the tag has a release on GitHub (published, draft or pre-release; a release that died while its files were uploaded stays as a draft): deleting only the tag
    would leave the release without its tag. Delete the release first (GitHub: Releases > the release > Delete) and run this again,
  - a run of the release workflow for the tag is still going: it would publish the release after the tag is gone,
  - the check for those two cannot be made (gh, the GitHub CLI, is missing or not signed in): that needs a person to answer, so -Yes does not go on.
It shows the commit the tag is on and asks for the tag to be typed again. The tag on GitHub is deleted first: if that fails (no network) nothing is deleted here.

.PARAMETER Tag
The tag, for example v2.1.0. Left out: the script lists the tags and asks.

.PARAMETER Yes
Do not ask for the tag to be typed again (for a script). The checks still apply.

.PARAMETER DryRun
Run every check and say what would happen; delete nothing.
#>
[CmdletBinding()]
param(
    [string]$Tag,
    [switch]$Yes,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

function Invoke-Git {
    $output = & git @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed with exit code $LASTEXITCODE" }
    $output
}

# Where each tag is: the names here, and the names on GitHub (ls-remote is asked, not the remote-tracking refs, which a deleted tag does not leave)
function Get-TagPlaces {
    $here = @(Invoke-Git tag -l)
    $there = @(Invoke-Git ls-remote --tags --refs origin | ForEach-Object { ($_ -split "`t")[1] -replace '^refs/tags/', '' })
    $places = @{}
    foreach ($name in $here) { $places[$name] = [pscustomobject]@{ Name = $name; Local = $true; Remote = $false } }
    foreach ($name in $there) {
        if ($places.ContainsKey($name)) { $places[$name].Remote = $true }
        else { $places[$name] = [pscustomobject]@{ Name = $name; Local = $false; Remote = $true } }
    }
    $places
}

function Show-Where($place) {
    if ($place.Local -and $place.Remote) { 'both' } elseif ($place.Local) { 'local only' } else { 'GitHub only' }
}

$places = Get-TagPlaces

if (-not $Tag) {
    $list = @($places.Values |
        Sort-Object @{ Expression = { if ($_.Name -match '^v(\d+)\.(\d+)\.(\d+)$') { [version]"$($Matches[1]).$($Matches[2]).$($Matches[3])" } else { [version]'0.0.0' } }; Descending = $true }, Name |
        Select-Object -First 5)
    if ($list.Count -eq 0) { throw 'There are no tags, here or on GitHub.' }

    Write-Host ''
    Write-Host "Tags (the $($list.Count) newest versions; type the name of any other):"
    for ($i = 0; $i -lt $list.Count; $i++) {
        Write-Host ('  {0,2}. {1,-16} {2}' -f ($i + 1), $list[$i].Name, (Show-Where $list[$i]))
    }
    Write-Host ''
    $answer = (Read-Host 'Number or name of the tag to remove (empty: none)').Trim()
    if (-not $answer) { Write-Host 'Nothing done.'; return }
    $Tag = if ($answer -match '^\d+$' -and [int]$answer -ge 1 -and [int]$answer -le $list.Count) { $list[[int]$answer - 1].Name } else { $answer }
}

$Tag = $Tag.Trim()
& git check-ref-format "refs/tags/$Tag" 2>$null
if ($LASTEXITCODE -ne 0) { throw "'$Tag' is not a name that a tag can have." }
if (-not $places.ContainsKey($Tag)) { throw "There is no tag '$Tag', here or on GitHub." }
$place = $places[$Tag]

# What the tag is on. A tag that is only on GitHub is not here to ask: it is fetched under a name that is not a tag (so that no tag is left here by the looking), and the name is dropped at once
$probe = $Tag
if (-not $place.Local) {
    $probe = 'refs/remove-tag/probe'
    Invoke-Git fetch origin --no-tags "refs/tags/${Tag}:$probe" --quiet # --no-tags: git would otherwise store the tags it finds on the way, this one included
}
try {
    $commit = Invoke-Git log -1 --format='%h  %s  (%ad)' --date=short "$probe^{commit}"
    $localSha = (Invoke-Git rev-parse "$probe^{commit}")
}
finally {
    if (-not $place.Local) { & git update-ref -d $probe }
}
$remoteSha = if ($place.Remote) { ((Invoke-Git ls-remote origin "refs/tags/$Tag^{}") -split "`t")[0] } else { $null }
if ($place.Remote -and -not $remoteSha) { $remoteSha = ((Invoke-Git ls-remote origin "refs/tags/$Tag") -split "`t")[0] } # a tag that is not annotated has no ^{} line

# Is there a release for it, and is a run of the release workflow going? Both are answered by the GitHub CLI.
$release = 'unknown'
$running = 'unknown'
if (Get-Command gh -ErrorAction SilentlyContinue) {
    # The list, not "gh release view <tag>": a release whose upload died is left as a draft, and a draft is not found by its tag
    $listing = (& gh release list --limit 200 --json tagName,isDraft,isPrerelease 2>&1 | Out-String)
    if ($LASTEXITCODE -eq 0) {
        $found = @(($listing | ConvertFrom-Json) | Where-Object { $_.tagName -eq $Tag })
        $release = if ($found.Count -eq 0) { 'none' } else { 'yes: ' + $(if ($found[0].isDraft) { 'a draft' } elseif ($found[0].isPrerelease) { 'a pre-release' } else { 'published' }) }
    }

    $runs = (& gh run list --workflow release.yml --branch $Tag --limit 20 --json status 2>&1 | Out-String)
    if ($LASTEXITCODE -eq 0) { $running = @(($runs | ConvertFrom-Json) | Where-Object { $_.status -ne 'completed' }).Count }
}

Write-Host ''
Write-Host "Tag       : $Tag  ($(Show-Where $place))"
Write-Host "Commit    : $commit"
if ($place.Local -and $place.Remote -and $remoteSha -and $remoteSha -ne $localSha) {
    Write-Host "            the tag here and the tag on GitHub are on different commits ($($localSha.Substring(0, 7)) here, $($remoteSha.Substring(0, 7)) on GitHub)"
}
Write-Host "Release   : $release"
Write-Host "Running   : $(if ($running -eq 'unknown') { 'unknown' } else { "$running run(s) of the release workflow" })"
Write-Host ''

if ($release -like 'yes*') {
    throw "GitHub has a release for $Tag ($($release.Substring(5))). Deleting the tag alone would leave the release without it. Delete the release first (GitHub: Releases > the release > Delete; a draft is in the same list), then run this again."
}
if ($running -ne 'unknown' -and [int]$running -gt 0) {
    throw "A run of the release workflow for $Tag is still going: it would publish the release after the tag is gone. Wait for it to end (or cancel it on GitHub), then run this again."
}
if ($release -eq 'unknown' -or $running -eq 'unknown') {
    Write-Host 'The GitHub CLI (gh) did not answer, so it is not known whether GitHub has a release for this tag or a run going.' -ForegroundColor Yellow
    if ($Yes) { throw 'Not going on without an answer: run this without -Yes and decide yourself, or sign in with  gh auth login.' }
    $answer = Read-Host 'Delete the tag anyway? (y/N)'
    if ($answer -notmatch '^(y|yes)$') { Write-Host 'Nothing done.'; return }
}

$steps = @()
if ($place.Remote) { $steps += "git push origin --delete $Tag" }
if ($place.Local) { $steps += "git tag -d $Tag" }

if ($DryRun) {
    Write-Host "Dry run: every check passed. Without -DryRun this would run  $($steps -join '  and  ')."
    return
}

if (-not $Yes) {
    $answer = Read-Host "Type the tag ($Tag) to remove it"
    if ($answer.Trim() -ne $Tag) { Write-Host 'Not the same: nothing done.'; return }
}

# GitHub first: if that fails the tag here is still there, and the two are not left saying different things
if ($place.Remote) {
    Invoke-Git push origin --delete $Tag
    Write-Host "Deleted $Tag on GitHub."
}
if ($place.Local) {
    Invoke-Git tag -d $Tag | Out-Null
    Write-Host "Deleted $Tag here."
}
Write-Host 'Done.'
