<#
.SYNOPSIS
Tests of board.ps1 against a fake gh (fake-gh.cmd): nothing here touches GitHub or the signed-in account.

.DESCRIPTION
Run:  pwsh -NoProfile -File .claude/skills/board/board.tests.ps1
Exit code 0 when everything passed, 1 otherwise.
#>
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$script = Join-Path $here 'board.ps1'
$fake = Join-Path $here 'fake-gh.cmd'
$root = Join-Path ([IO.Path]::GetTempPath()) ('board-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root | Out-Null

$failures = 0
$checks = 0
function Check($condition, [string]$what, [string]$detail = '') {
    # $condition may be the lines an array -match found: any line is a yes, none is a no
    $script:checks++
    if ($condition) { Write-Host "  ok    $what" }
    else { $script:failures++; Write-Host "  FAIL  $what" -ForegroundColor Red; if ($detail) { Write-Host ($detail -replace '(?m)^', '        ') -ForegroundColor DarkGray } }
}

$link = 'https://github.com/users/Someone/projects/5/views/1'

function Item($id, $status, $priority, $number, $title) {
    $content = if ($number) { @{ type = 'Issue'; number = $number; repository = 'Someone/Repo'; title = $title; url = "https://github.com/Someone/Repo/issues/$number"; body = "Body of $title" } }
    else { @{ type = 'DraftIssue'; title = $title; body = "Body of $title" } }
    $item = @{ id = $id; title = $title; content = $content; labels = @('bug') }
    if ($status) { $item.status = $status }
    if ($priority) { $item.priority = $priority }
    $item
}

$boardItems = @(
    (Item 'PVTI_10' 'Done' 'P1' 10 'Old thing'),
    (Item 'PVTI_55' 'Backlog' $null 55 'Trace'),
    (Item 'PVTI_70' 'Ready' 'P2' 70 'Ready low'),
    (Item 'PVTI_71' 'Ready' 'P0' 71 'Ready high'),
    (Item 'PVTI_72' 'In progress' 'P1' 72 'Working'),
    (Item 'PVTI_D1' 'Backlog' 'P1' $null 'A draft idea'),
    (Item 'PVTI_80' 'In review' $null 80 'Waiting'),
    (Item 'PVTI_NS' $null $null 90 'No column')
)

$base = @{
    'auth-status.out'       = 'Logged in to github.com account Someone'
    'api--i.out'            = "HTTP/2.0 200 OK`r`nX-Oauth-Scopes: gist, read:org, read:project, repo, workflow`r`nContent-Type: application/json`r`n`r`n{`"login`":`"Someone`"}"
    'api-user.out'          = 'Someone'
    'project-view.out'      = (@{ id = 'PVT_test'; number = 5; title = 'PhotoLiker'; public = $false; url = 'https://github.com/users/Someone/projects/5'; items = @{ totalCount = 8 } } | ConvertTo-Json -Depth 4)
    'project-field-list.out' = (@{ fields = @(
                @{ id = 'F_title'; name = 'Title'; type = 'ProjectV2Field' },
                @{ id = 'F_status'; name = 'Status'; type = 'ProjectV2SingleSelectField'; options = @(
                        @{ id = 'O_backlog'; name = 'Backlog' }, @{ id = 'O_ready'; name = 'Ready' }, @{ id = 'O_progress'; name = 'In progress' },
                        @{ id = 'O_review'; name = 'In review' }, @{ id = 'O_done'; name = 'Done' }) }) } | ConvertTo-Json -Depth 6)
    'project-item-list.out' = (@{ items = $boardItems; totalCount = $boardItems.Count } | ConvertTo-Json -Depth 6)
}

# Runs board.ps1 with the fake gh. $Files replace/add canned answers, $After are the answers that appear once the (fake) sign-in is approved.
function Invoke-Board([string[]]$BoardArgs, [hashtable]$Files = @{}, [hashtable]$After = @{}) {
    $dir = Join-Path $root ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $dir | Out-Null
    foreach ($table in @($base, $Files)) { foreach ($name in $table.Keys) { Set-Content -LiteralPath (Join-Path $dir $name) -Value $table[$name] -NoNewline } }
    if ($After.Count -gt 0) {
        New-Item -ItemType Directory (Join-Path $dir 'after-refresh') | Out-Null
        foreach ($name in $After.Keys) { Set-Content -LiteralPath (Join-Path $dir "after-refresh\$name") -Value $After[$name] -NoNewline }
    }
    $saved = @{ BOARD_GH = $env:BOARD_GH; FAKE_GH_DIR = $env:FAKE_GH_DIR; BOARD_LAST_FILE = $env:BOARD_LAST_FILE; GH_TOKEN = $env:GH_TOKEN; GITHUB_TOKEN = $env:GITHUB_TOKEN }
    $lastFile = Join-Path $dir 'last-board.txt'
    try {
        $env:BOARD_GH = $fake; $env:FAKE_GH_DIR = $dir; $env:BOARD_LAST_FILE = $lastFile; $env:GH_TOKEN = $null; $env:GITHUB_TOKEN = $null
        $out = & pwsh -NoProfile -File $script @BoardArgs 2> (Join-Path $dir 'stderr.txt')
        $code = $LASTEXITCODE
    }
    finally { foreach ($name in $saved.Keys) { Set-Item "env:$name" $saved[$name] } }
    $callsFile = Join-Path $dir 'calls.log'
    [pscustomobject]@{
        Code  = $code
        Out   = ($out -join "`n")
        Err   = [IO.File]::ReadAllText((Join-Path $dir 'stderr.txt'))
        Calls = if (Test-Path $callsFile) { @(Get-Content $callsFile) } else { @() }
        Saved = if (Test-Path $lastFile) { [IO.File]::ReadAllText($lastFile) } else { $null }
    }
}

function Numbers($result) { (($result.Out | ConvertFrom-Json).items | ForEach-Object { if ($_.Number) { "$($_.Number)" } else { $_.Id } }) -join ',' }

try {
    Write-Host 'Link'
    $r = Invoke-Board @('List', '-Url', 'https://example.com/foo')
    Check ($r.Code -eq 1 -and $r.Err -match 'not the link of a GitHub project board') 'a link that is not a board is refused' $r.Err
    $r = Invoke-Board @('Access', '-Url', 'https://github.com/orgs/Acme/projects/12')
    Check ($r.Code -eq 0 -and ($r.Calls -match 'project view 12 --owner Acme')) 'an organisation board without a view is read' ($r.Calls -join "`n")
    $r = Invoke-Board @('Access', '-Url', $link)
    Check (($r.Calls -match 'project view 5 --owner Someone')) 'the owner and number come out of a user board link' ($r.Calls -join "`n")

    Write-Host 'Access'
    Check ($r.Code -eq 0 -and $r.Out -match 'Signed in as\s+: Someone' -and $r.Out -match 'Can read\s+: yes') 'a signed-in account with the scope reads the board' $r.Out
    Check (-not ($r.Calls -match 'auth refresh|auth login')) 'nothing is refreshed when nothing is missing' ($r.Calls -join "`n")
    Check ($r.Out -match 'Can move cards: no') 'without the project scope it says cards cannot be moved' $r.Out

    $r = Invoke-Board @('Access', '-Url', $link) @{ 'project-view.err' = 'error: your authentication token is missing required scopes [read:project]' }
    Check ($r.Code -eq 0 -and ($r.Calls -match 'auth refresh --hostname github.com --scopes read:project')) 'a token without read:project is refreshed' ($r.Calls -join "`n")
    Check ($r.Err -match 'TEST-CODE' -and $r.Err -match 'github.com/login/device') 'the one-time code and the address reach the output' $r.Err
    Check ($r.Out -match 'Can read\s+: yes') 'after the refresh the board is read' $r.Out

    $r = Invoke-Board @('Access', '-Url', $link) @{ 'project-view.err' = 'error: your authentication token is missing required scopes [read:project]'; 'auth-refresh.err' = 'authentication failed' }
    Check ($r.Code -eq 1 -and $r.Err -match 'did not finish') 'a sign-in that is not completed stops with a message' $r.Err

    $r = Invoke-Board @('Access', '-Url', $link) @{ 'auth-status.err' = 'You are not logged into any GitHub hosts' }
    Check ($r.Code -eq 0 -and ($r.Calls -match 'auth login --hostname github.com --git-protocol https --web --scopes read:project')) 'a person who is not signed in gets the browser sign-in' ($r.Calls -join "`n")

    $r = Invoke-Board @('Access', '-Url', $link) @{ 'project-view.err' = 'GraphQL: Could not resolve to a ProjectV2 with the number 5.' }
    Check ($r.Code -eq 1 -and $r.Err -match "signed in as 'Someone'" -and $r.Err -match 'Could not resolve') 'a board the account cannot see names the account and the problem' $r.Err
    Check (-not ($r.Calls -match 'auth refresh')) 'a missing board does not start a refresh' ($r.Calls -join "`n")

    $env:GH_TOKEN = 'x'
    try {
        $dir = Join-Path $root 'envtoken'; New-Item -ItemType Directory $dir | Out-Null
        foreach ($name in $base.Keys) { Set-Content -LiteralPath (Join-Path $dir $name) -Value $base[$name] -NoNewline }
        Set-Content -LiteralPath (Join-Path $dir 'project-view.err') -Value 'missing required scopes [read:project]' -NoNewline
        $env:BOARD_GH = $fake; $env:FAKE_GH_DIR = $dir; $env:BOARD_LAST_FILE = Join-Path $dir 'last-board.txt'
        $null = & pwsh -NoProfile -File $script Access -Url $link 2> (Join-Path $dir 'stderr.txt')
        $code = $LASTEXITCODE
        $callsFile = Join-Path $dir 'calls.log'
        $calls = if (Test-Path $callsFile) { @(Get-Content $callsFile) } else { @() }
        Check ($code -eq 1 -and ([IO.File]::ReadAllText((Join-Path $dir 'stderr.txt')) -match 'GH_TOKEN') -and -not ($calls -match 'auth refresh')) 'a token from GH_TOKEN is not "refreshed": the script says why' ($calls -join "`n")
    }
    finally { $env:GH_TOKEN = $null; $env:BOARD_GH = $null; $env:FAKE_GH_DIR = $null; $env:BOARD_LAST_FILE = $null }

    $projectScope = "HTTP/2.0 200 OK`r`nX-Oauth-Scopes: gist, project, read:org, repo, workflow`r`n`r`n{}"
    $r = Invoke-Board @('Access', '-Url', $link, '-Write') @{} @{ 'api--i.out' = $projectScope }
    Check ($r.Code -eq 0 -and ($r.Calls -match 'auth refresh --hostname github.com --scopes project') -and $r.Out -match 'Can move cards: yes') '-Write refreshes to the project scope when it is missing' ($r.Calls -join "`n")
    $r = Invoke-Board @('Access', '-Url', $link, '-Write')
    Check ($r.Code -eq 1 -and $r.Err -match 'still shows no project scope') 'an approval that leaves the scope missing is reported, not believed' $r.Err
    $r = Invoke-Board @('Access', '-Url', $link, '-Write') @{} @{ 'api--i.out' = "HTTP/2.0 200 OK`r`nX-Oauth-Scopes: gist, project, read:org, repo`r`n`r`n{}" }
    Check ($r.Code -eq 0 -and $r.Out -match 'Can move cards: yes') 'once the project scope is there, cards can be moved' $r.Out
    $r = Invoke-Board @('Access', '-Url', $link, '-Write') @{ 'api--i.out' = "HTTP/2.0 200 OK`r`nContent-Type: application/json`r`n`r`n{}" }
    Check ($r.Code -eq 0 -and -not ($r.Calls -match 'auth refresh') -and $r.Out -match 'Can move cards: unknown') 'a token that lists no scopes is not nagged' ($r.Calls -join "`n")

    Write-Host 'The board that was used last'
    $r = Invoke-Board @('Last')
    Check ($r.Code -eq 0 -and $r.Out -eq '' -and $r.Calls.Count -eq 0) 'with nothing remembered, Last prints nothing and asks gh nothing' "[$($r.Out)] $($r.Calls -join ' ')"
    $r = Invoke-Board @('Access', '-Url', "  $link  ")
    Check ($r.Saved -eq $link) 'a board that was read is remembered (without the spaces around the link)' "[$($r.Saved)]"
    $r = Invoke-Board @('Last') @{ 'last-board.txt' = "$link`r`n" }
    Check ($r.Code -eq 0 -and $r.Out -eq $link -and $r.Calls.Count -eq 0) 'Last prints the remembered link' "[$($r.Out)]"
    $r = Invoke-Board @('Last') @{ 'last-board.txt' = 'not a link at all' }
    Check ($r.Code -eq 0 -and $r.Out -eq '') 'a remembered text that is not a board link is not offered' "[$($r.Out)]"
    $other = 'https://github.com/orgs/Acme/projects/12/views/3'
    $r = Invoke-Board @('List', '-Url', $other, '-Json') @{ 'last-board.txt' = $link }
    Check ($r.Saved -eq $other) 'reading another board replaces the remembered one' "[$($r.Saved)]"
    $r = Invoke-Board @('Access', '-Url', $other) @{ 'last-board.txt' = $link; 'project-view.err' = 'GraphQL: Could not resolve to a ProjectV2 with the number 12.' }
    Check ($r.Code -eq 1 -and $r.Saved -eq $link) 'a board that could not be read is not remembered' "[$($r.Saved)]"
    $r = Invoke-Board @('List', '-Url', 'https://example.com/foo')
    Check ($r.Code -eq 1 -and $null -eq $r.Saved) 'a link that is not a board is not remembered' "[$($r.Saved)]"
    $r = Invoke-Board @('Move', '-Url', $link, '-Item', '55', '-Status', 'Ready', '-DryRun')
    Check ($r.Saved -eq $link) 'a board used only for a dry run is remembered too' "[$($r.Saved)]"
    $r = Invoke-Board @('List')
    Check ($r.Code -eq 1 -and $r.Err -match 'needs -Url') 'List without a link is refused (only Last may go without)' $r.Err

    Write-Host 'List'
    $r = Invoke-Board @('List', '-Url', $link, '-Json')
    Check ($r.Code -eq 0) 'List -Json succeeds' $r.Err
    Check ((Numbers $r) -eq '72,71,70,PVTI_D1,55,80,90') 'order: In progress, Ready (P0 before P2), Backlog (P1 before none), In review, no column; Done left out' (Numbers $r)
    $doc = $r.Out | ConvertFrom-Json
    Check ($doc.board.projectId -eq 'PVT_test' -and $doc.board.title -eq 'PhotoLiker' -and ($doc.board.statuses -join ',') -eq 'Backlog,Ready,In progress,In review,Done') 'the JSON names the board, its id and its columns' $r.Out
    $first = $doc.items[0]
    Check ($first.Number -eq 72 -and $first.Repository -eq 'Someone/Repo' -and $first.Body -eq 'Body of Working' -and $first.Labels -contains 'bug' -and $first.Url -like '*issues/72') 'a card carries its number, repository, text, labels and link' $r.Out
    Check ($r.Out.TrimStart().StartsWith('{')) 'the JSON output has nothing in front of it (messages go to the error stream)' $r.Out

    $r = Invoke-Board @('List', '-Url', $link, '-Json', '-All')
    Check ((Numbers $r) -eq '72,71,70,PVTI_D1,55,80,10,90') '-All adds the Done column, after In review' (Numbers $r)
    $r = Invoke-Board @('List', '-Url', $link, '-Json', '-Status', 'ready, backlog')
    Check ((Numbers $r) -eq '71,70,PVTI_D1,55') '-Status picks columns (any case, spaces allowed)' (Numbers $r)
    $r = Invoke-Board @('List', '-Url', $link, '-Json', '-Status', 'Nonexistent')
    Check ($r.Code -eq 0 -and (@(($r.Out | ConvertFrom-Json).items).Count -eq 0)) 'an unknown column gives an empty list' $r.Out

    $r = Invoke-Board @('List', '-Url', $link)
    Check ($r.Out -match 'Columns: Backlog \| Ready \| In progress \| In review \| Done' -and $r.Out -match 'Showing 7 of 8 cards' -and $r.Out -match 'Someone/Repo#72\s+Working' -and $r.Out -match 'draft PVTI_D1\s+A draft idea') 'the table shows columns, counts and cards' $r.Out
    Check ($r.Out.IndexOf('#72') -lt $r.Out.IndexOf('#71') -and $r.Out.IndexOf('#71') -lt $r.Out.IndexOf('#70')) 'the table is in the same order' $r.Out

    $r = Invoke-Board @('List', '-Url', $link, '-Json') @{ 'project-item-list.out' = '{"items":[],"totalCount":0}' }
    Check ($r.Code -eq 0 -and $r.Out -match '"items": \[\]') 'an empty board gives an empty list' $r.Out

    Write-Host 'Move'
    $r = Invoke-Board @('Move', '-Url', $link, '-Item', '55', '-Status', 'In progress', '-DryRun')
    Check ($r.Code -eq 0 -and $r.Out -match 'Dry run' -and $r.Out -match 'from Backlog to In progress') 'a dry run says what it would do' $r.Out
    Check (-not ($r.Calls -match 'item-edit') -and -not ($r.Calls -match 'auth refresh')) 'a dry run changes nothing and asks for no new scope' ($r.Calls -join "`n")
    Check ($r.Out -match '--id PVTI_55 --project-id PVT_test --field-id F_status --single-select-option-id O_progress') 'the dry run shows the ids it would use' $r.Out

    $withProject = @{ 'api--i.out' = "HTTP/2.0 200 OK`r`nX-Oauth-Scopes: gist, project, read:org, repo`r`n`r`n{}" }
    $r = Invoke-Board @('Move', '-Url', $link, '-Item', '#55', '-Status', 'in review') $withProject
    $edit = @($r.Calls | Where-Object { $_ -match 'project item-edit' })
    Check ($r.Code -eq 0 -and $edit.Count -eq 1 -and $edit[0] -match '--id PVTI_55 --project-id PVT_test --field-id F_status --single-select-option-id O_review') 'Move calls item-edit once with the card, board, field and column ids (#55 and a lower-case column work)' ($r.Calls -join "`n")
    Check ($r.Out -match "Moved Someone/Repo#55 'Trace' from Backlog to In review") 'Move says what it did' $r.Out

    $r = Invoke-Board @('Move', '-Url', $link, '-Item', 'PVTI_D1', '-Status', 'Ready') $withProject
    Check ($r.Code -eq 0 -and ($r.Calls -match '--id PVTI_D1 ')) 'a draft card is moved by its card id' ($r.Calls -join "`n")

    $r = Invoke-Board @('Move', '-Url', $link, '-Item', '55', '-Status', 'In progress') @{} @{ 'api--i.out' = $projectScope }
    Check ($r.Code -eq 0 -and ($r.Calls -match 'auth refresh --hostname github.com --scopes project') -and ($r.Calls -match 'project item-edit')) 'a real move asks for the project scope first, then edits' ($r.Calls -join "`n")

    $r = Invoke-Board @('Move', '-Url', $link, '-Item', '55', '-Status', 'Doing') $withProject
    Check ($r.Code -eq 1 -and $r.Err -match "no column 'Doing'" -and $r.Err -match 'Backlog, Ready, In progress, In review, Done' -and -not ($r.Calls -match 'item-edit')) 'an unknown column is refused and the real ones are listed' $r.Err
    $r = Invoke-Board @('Move', '-Url', $link, '-Item', '999', '-Status', 'Ready') $withProject
    Check ($r.Code -eq 1 -and $r.Err -match "No card '999'" -and -not ($r.Calls -match 'item-edit')) 'an unknown card is refused' $r.Err
    $r = Invoke-Board @('Move', '-Url', $link, '-Status', 'Ready') $withProject
    Check ($r.Code -eq 1 -and $r.Err -match 'needs -Item') 'Move without a card is refused' $r.Err
    $dupe = @{ 'project-item-list.out' = (@{ items = @((Item 'PVTI_A' 'Backlog' $null 7 'Same number, repo one'), (Item 'PVTI_B' 'Backlog' $null 7 'Same number, repo two')); totalCount = 2 } | ConvertTo-Json -Depth 6) }
    $r = Invoke-Board @('Move', '-Url', $link, '-Item', '7', '-Status', 'Ready') ($withProject + $dupe)
    Check ($r.Code -eq 1 -and $r.Err -match 'more than once' -and -not ($r.Calls -match 'item-edit')) 'a number that two cards have is not guessed' $r.Err
    $r = Invoke-Board @('Move', '-Url', $link, '-Item', '55', '-Status', 'Ready') ($withProject + @{ 'project-item-edit.err' = 'GraphQL: forbidden' })
    Check ($r.Code -eq 1 -and $r.Err -match 'could not move the card' -and $r.Err -match 'forbidden') 'a failed edit is reported, not hidden' $r.Err
}
finally {
    Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "$($checks - $failures) of $checks checks passed."
if ($failures -gt 0) { exit 1 }
