<#
.SYNOPSIS
Writes the sentences that CHANGELOG.md has for one version, one "- sentence" line each. Fails if the version has no section, or a section with no sentence.

.DESCRIPTION
Used twice: by publish-release.ps1, which refuses to tag a version that has nothing written for it, and by .github\workflows\release.yml, which puts these lines on the
GitHub release. The format is read the way PhotoLiker.Core\Changelog.cs reads it: a heading "## 2.1.0 - 2026-11-30" (the date is for people; "v2.1.0" and "[2.1.0]" are
the same heading) and, under it, one line that starts with "- " for each change, up to the next "## " heading.

.PARAMETER Version
2.1.0 (a leading v is accepted).

.PARAMETER Path
The changelog. Default: CHANGELOG.md at the root of the repository.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,
    [string]$Path = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'

$Version = $Version.Trim().TrimStart('v', 'V')
if (-not (Test-Path -LiteralPath $Path)) {
    throw "There is no changelog at $Path."
}

# "## 2.1.0", "## 2.1.0 - 2026-11-30", "## v2.1.0", "## [2.1.0]": the version is the whole first word ("4.1.1" is not the heading of 4.1.12)
$heading = '^##\s+\[?v?' + [regex]::Escape($Version) + '\]?(\s.*)?$'

$found = $false
$sentences = @()
foreach ($line in Get-Content -LiteralPath $Path -Encoding utf8) {
    if ($line -match '^##\s') {
        if ($found) { break }
        $found = $line -match $heading
        continue
    }
    if ($found -and $line -match '^\s*[-*]\s+(\S.*?)\s*$') {
        $sentences += "- $($Matches[1])"
    }
}

if (-not $found) {
    throw "CHANGELOG.md has no section '## $Version'. Write one sentence per feature or fix of this release under '## Unreleased', rename that heading to '## $Version - $(Get-Date -Format yyyy-MM-dd)' and commit it."
}
if ($sentences.Count -eq 0) {
    throw "The section '## $Version' of CHANGELOG.md has no sentence. Each change is a line that starts with '- '."
}

$sentences
