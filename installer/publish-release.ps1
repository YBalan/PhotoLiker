<#
.SYNOPSIS
Publishes a release: tags the current commit v<Version> and pushes the tag. GitHub Actions (.github\workflows\release.yml) does the rest:
tests, installer, zip, and the GitHub release with both files.

.DESCRIPTION
Nothing is built here. The script only makes sure that what is tagged is what you mean to release, then pushes the tag:
  - the version is MAJOR.MINOR.PATCH and newer than every v* tag that exists (the update button of the About box compares those numbers),
  - there is nothing uncommitted (a release is built from the commit, not from your files),
  - the commit is on GitHub already (pushed) and has release.yml in it,
  - the tag does not exist, here or on GitHub (a version number is not used twice).
It shows the commit and asks for the version again to confirm. Releasing from a branch other than master asks one more question.

.PARAMETER Version
2.1.0 (a leading v is accepted).

.PARAMETER Yes
Do not ask anything (for a script). The checks still apply.

.PARAMETER DryRun
Run every check and say what would happen; tag nothing, push nothing.

.PARAMETER Watch
After the push, follow the workflow run until it ends (needs the GitHub CLI, gh, signed in).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,
    [switch]$Yes,
    [switch]$DryRun,
    [switch]$Watch
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

function Invoke-Git {
    $output = & git @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed with exit code $LASTEXITCODE" }
    $output
}

$Version = $Version.Trim().TrimStart('v', 'V')
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "'$Version' is not a version. Use <major>.<minor>.<patch>, for example 2.1.0."
}
$tag = "v$Version"

if (Invoke-Git status --porcelain) {
    throw "There are uncommitted changes. Commit (and push) them first: the release is built from the commit, not from your files.`n$(Invoke-Git status --short | Out-String)"
}

Invoke-Git fetch origin --tags --quiet

$branch = (Invoke-Git branch --show-current)
$sha = (Invoke-Git rev-parse --short HEAD)
$subject = (Invoke-Git log -1 --format=%s)

if (-not (Invoke-Git branch -r --contains HEAD)) {
    throw "The commit $sha is not on GitHub yet. Push the branch first (git push)."
}

& git cat-file -e 'HEAD:.github/workflows/release.yml' 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "This commit has no .github/workflows/release.yml, so pushing the tag would release nothing."
}

if (Invoke-Git tag -l $tag) { throw "The tag $tag exists here already." }
if (Invoke-Git ls-remote --tags origin "refs/tags/$tag") { throw "The tag $tag exists on GitHub already. A version number is not used twice." }

$latest = Invoke-Git tag -l 'v*' |
    Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } |
    ForEach-Object { [pscustomobject]@{ Tag = $_; Version = [version]$_.Substring(1) } } |
    Sort-Object Version -Descending | Select-Object -First 1
if ($latest -and [version]$Version -le $latest.Version) {
    throw "$tag is not newer than the latest release tag, $($latest.Tag). The update button would not offer it."
}

Write-Host ''
Write-Host "Release   : Photo Liker $Version  (tag $tag)"
Write-Host "Commit    : $sha  $subject"
Write-Host "Branch    : $branch"
if ($latest) {
    Write-Host "Since     : $($latest.Tag), $(Invoke-Git rev-list --count "$($latest.Tag)..HEAD") commit(s)"
} else {
    Write-Host 'Since     : this is the first release'
}
Write-Host ''

if ($DryRun) {
    Write-Host "Dry run: every check passed. Without -DryRun this would run  git tag -a $tag  and  git push origin $tag."
    return
}

if (-not $Yes) {
    if ($branch -notin 'master', 'main') {
        $answer = Read-Host "You are on '$branch', not master. Release this branch anyway? (y/N)"
        if ($answer -notmatch '^(y|yes)$') { Write-Host 'Nothing done.'; return }
    }
    $answer = Read-Host "Type the version ($Version) to publish it"
    if ($answer.Trim().TrimStart('v', 'V') -ne $Version) { Write-Host 'Not the same: nothing done.'; return }
}

Invoke-Git tag -a $tag -m "Photo Liker $Version"
try {
    Invoke-Git push origin $tag
}
catch {
    Invoke-Git tag -d $tag | Out-Null # not pushed: do not leave the tag behind
    throw
}

$repo = ((Invoke-Git remote get-url origin) -replace '\.git$', '') -replace '^git@github\.com:', 'https://github.com/'
Write-Host ''
Write-Host "Pushed $tag. GitHub Actions is building the release now (a few minutes)."
Write-Host "  Progress: $repo/actions/workflows/release.yml"
Write-Host "  Release : $repo/releases/tag/$tag"

if ($Watch) {
    $runId = $null
    for ($i = 0; $i -lt 30 -and -not $runId; $i++) {
        Start-Sleep -Seconds 2
        $runId = gh run list --workflow release.yml --branch $tag --limit 1 --json databaseId --jq '.[0].databaseId' 2>$null
    }
    if (-not $runId) {
        Write-Host 'The run did not show up yet; follow it on the page above.'
        return
    }
    gh run watch $runId --exit-status
    if ($LASTEXITCODE -ne 0) { throw "The release run failed. See $repo/actions/runs/$runId" }
    Write-Host "Published: $repo/releases/tag/$tag"
}
