<#
.SYNOPSIS
Reads a GitHub Projects (v2) Kanban board by its link, gets the signed-in GitHub account the access it needs, and moves cards. The helper of the /board skill (SKILL.md next to this file).

.DESCRIPTION
Actions:
  Access  Checks everything that is needed to read the board and does what it can about what is missing:
            - the GitHub CLI (gh) is not installed: says how to install it (it does not install anything),
            - not signed in: starts the browser sign-in of gh,
            - the token lacks the read:project scope (GitHub asks for it for every Projects query, public boards too): starts "gh auth refresh",
            - with -Write also the project scope (moving cards needs it).
          Sign-in and refresh are a device flow: the script prints a one-time code and the address https://github.com/login/device and waits until
          the person has entered the code in the browser (up to 15 minutes). Run it so that its output can be read meanwhile (a background task).
  List    Access, then the cards. Cards in the Done column are left out unless -All; -Status "Ready,Backlog" picks columns.
          Order: In progress, Ready, Backlog, In review, Done; inside a column P0 before P1 before P2 before no priority, then the board's own order.
          -Json writes one JSON document (board + cards with their full text) to the output, for a program to read.
  Move    Access (with write access), then sets the Status column of one card: -Item is the issue/PR number or the card id (PVTI_...), -Status the column name.
          -DryRun shows the gh command and runs nothing that changes the board.
  Last    Prints the link of the board that was used last (nothing when there is none), so that "/board" without a link can offer it again.
          Every successful Access / List / Move saves its link in last-board.txt next to this script (not under version control: it is per machine).

Everything that is not the answer (progress, the one-time code, warnings) goes to the error stream, so that -Json output is clean.
Exit code: 0 when it worked, 1 with a message when not.

.PARAMETER Action
Access, List or Move.

.PARAMETER Url
The link of the board, as the browser shows it: https://github.com/users/<name>/projects/<number>[/views/<view>] or https://github.com/orgs/<org>/projects/<number>[/views/<view>].
The view in the link is not used: all cards of the board are read (the filter of a view is not available to gh). Every action but Last needs it.

.PARAMETER Item
Move: the card, by issue/PR number (55 or #55) or by card id.

.PARAMETER Status
Move: the column to put the card in. List: the columns to show, separated by commas.

.PARAMETER Write
Access/List/Move: ask for the scope that lets cards be changed. Move always does.

.PARAMETER Json
List: JSON instead of the table.

.PARAMETER All
List: include the Done column.

.PARAMETER DryRun
Move: change nothing.

.NOTES
BOARD_GH (an environment variable) names another program to call instead of gh, BOARD_LAST_FILE another file to remember the last board in.
Both are for board.tests.ps1, which puts a fake gh there and keeps the real last-board.txt untouched.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('Access', 'List', 'Move', 'Last')]
    [string]$Action,

    [string]$Url,

    [string]$Item,
    [string]$Status,
    [switch]$Write,
    [switch]$Json,
    [switch]$All,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$gh = if ($env:BOARD_GH) { $env:BOARD_GH } else { 'gh' }
$lastBoardFile = if ($env:BOARD_LAST_FILE) { $env:BOARD_LAST_FILE } else { Join-Path $PSScriptRoot 'last-board.txt' }
$boardLinkPattern = '^\s*https?://github\.com/(?<kind>users|orgs)/(?<owner>[^/\s?#]+)/projects/(?<number>\d+)(?:[/?#]\S*)?\s*$'

# Messages go to the error stream: the output stays the answer (a JSON document must not get a progress line in front of it)
function Say([string]$text) { [Console]::Error.WriteLine($text) }

# Runs gh; the output, the error text and the exit code come back separately (the output is parsed, the error text is shown)
function Invoke-Gh {
    $errFile = [IO.Path]::GetTempFileName()
    try {
        $text = & $gh @args 2> $errFile | Out-String
        $code = $LASTEXITCODE
        [pscustomobject]@{ Code = $code; Text = $text; Error = [IO.File]::ReadAllText($errFile) }
    }
    finally { Remove-Item $errFile -ErrorAction SilentlyContinue }
}

function Get-Prop($object, [string]$name) {
    if ($null -eq $object) { return $null }
    $property = $object.PSObject.Properties[$name]
    if ($property) { $property.Value } else { $null }
}

function Get-BoardLink([string]$link) {
    if ($link -notmatch $boardLinkPattern) {
        throw "'$link' is not the link of a GitHub project board. It should look like https://github.com/users/<name>/projects/<number>/views/1 (or .../orgs/<org>/projects/<number>)."
    }
    [pscustomobject]@{ Owner = $Matches['owner']; Number = [int]$Matches['number']; Kind = $Matches['kind'] }
}

# The board that was used last. Saved only after the board could be read, so that a wrong link is not remembered; a file that cannot be written is a warning, not an error.
function Save-LastBoard([string]$link) {
    try { [IO.File]::WriteAllText($lastBoardFile, $link.Trim()) }
    catch { Say "Warning: could not remember the board ($($_.Exception.Message))" }
}

function Get-LastBoard {
    if (-not (Test-Path -LiteralPath $lastBoardFile)) { return $null }
    $saved = [IO.File]::ReadAllText($lastBoardFile).Trim()
    if ($saved -match $boardLinkPattern) { $saved } else { $null }
}

# What GitHub says when the token has not got the scope: "missing required scopes [read:project]" from gh, "INSUFFICIENT_SCOPES" from the API
function Test-ScopeProblem($result) { ($result.Error + $result.Text) -match 'required scopes|INSUFFICIENT_SCOPES|needs the .* scope' }

# The scopes of the token, from the header GitHub sends with every answer. $null: unknown (a fine-grained token or a GitHub App has no such header)
function Get-TokenScopes {
    $r = Invoke-Gh api -i user
    if ($r.Code -ne 0) { return $null }
    $m = [regex]::Match($r.Text, '(?im)^X-OAuth-Scopes:[ \t]*(.*?)\s*$')
    if (-not $m.Success) { return $null }
    , @($m.Groups[1].Value -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Get-Login {
    $r = Invoke-Gh api user --jq .login
    if ($r.Code -eq 0) { $r.Text.Trim() } else { $null }
}

# gh sign-in and scope changes are a device flow. Enter is answered for gh ("Press Enter to open the browser"), the code is shown at once.
function Start-DeviceFlow([string[]]$ghArgs, [string]$why) {
    if ($env:GH_TOKEN -or $env:GITHUB_TOKEN) {
        throw "$why gh cannot change a token that comes from GH_TOKEN / GITHUB_TOKEN. Give that token the scope on github.com (Settings > Developer settings), or remove the variable and run this again."
    }
    Say $why
    Say 'GitHub shows a one-time code below: open https://github.com/login/device, enter it and approve. This waits for you (up to 15 minutes).'
    '' | & $gh @ghArgs 2>&1 | ForEach-Object { Say "$_" }
    if ($LASTEXITCODE -ne 0) { throw "'gh $($ghArgs -join ' ')' did not finish (code not entered, or declined). Run this again when you are ready." }
}

# Everything that stands between the person and the board. Returns the project (gh's own JSON) once it can be read.
function Confirm-Access($board, [bool]$needWrite) {
    if (-not (Get-Command $gh -ErrorAction SilentlyContinue)) {
        throw 'The GitHub CLI (gh) is not installed. Install it with:  winget install --id GitHub.cli  then open a new terminal and run this again.'
    }

    if ((Invoke-Gh auth status).Code -ne 0) {
        Start-DeviceFlow @('auth', 'login', '--hostname', 'github.com', '--git-protocol', 'https', '--web', '--scopes', 'read:project') 'You are not signed in to GitHub.'
    }

    $probe = Invoke-Gh project view $board.Number --owner $board.Owner --format json
    if ($probe.Code -ne 0 -and (Test-ScopeProblem $probe)) {
        Start-DeviceFlow @('auth', 'refresh', '--hostname', 'github.com', '--scopes', 'read:project') 'Your GitHub login has no permission to read Projects (scope read:project).'
        $probe = Invoke-Gh project view $board.Number --owner $board.Owner --format json
    }
    if ($probe.Code -ne 0) {
        $who = Get-Login
        $account = if ($who) { "signed in as '$who'" } else { 'signed in' }
        throw "Cannot read project $($board.Number) of $($board.Owner) ($account): $($probe.Error.Trim()) Check the link, and that this account may see the board (gh auth status; gh auth switch)."
    }

    $scopes = Get-TokenScopes
    if ($needWrite -and $null -ne $scopes -and $scopes -notcontains 'project') {
        Start-DeviceFlow @('auth', 'refresh', '--hostname', 'github.com', '--scopes', 'project') 'Your GitHub login may read Projects but not change them (scope project), and moving cards changes the board.'
        $scopes = Get-TokenScopes
        if ($null -ne $scopes -and $scopes -notcontains 'project') { throw 'GitHub still shows no project scope on the token after the approval.' }
    }

    Save-LastBoard $Url
    $probe.Text | ConvertFrom-Json
}

function Get-StatusField($board) {
    $r = Invoke-Gh project field-list $board.Number --owner $board.Owner --format json
    if ($r.Code -ne 0) { throw "gh could not read the columns of the board: $($r.Error.Trim())" }
    $field = @(($r.Text | ConvertFrom-Json).fields) | Where-Object { $_.name -eq 'Status' } | Select-Object -First 1
    if (-not $field) { throw "The board has no 'Status' field, so it has no columns to read or move cards between." }
    $field
}

function Get-Cards($board) {
    $r = Invoke-Gh project item-list $board.Number --owner $board.Owner --limit 1000 --format json
    if ($r.Code -ne 0) { throw "gh could not read the cards of the board: $($r.Error.Trim())" }
    $data = $r.Text | ConvertFrom-Json
    $index = 0
    $cards = foreach ($raw in @($data.items)) {
        $content = Get-Prop $raw 'content'
        [pscustomobject][ordered]@{
            Index      = $index++
            Id         = $raw.id
            Status     = Get-Prop $raw 'status'
            Priority   = Get-Prop $raw 'priority'
            Size       = Get-Prop $raw 'size'
            Type       = Get-Prop $content 'type'
            Repository = Get-Prop $content 'repository'
            Number     = Get-Prop $content 'number'
            Title      = if ($raw.title) { $raw.title } else { Get-Prop $content 'title' }
            Url        = Get-Prop $content 'url'
            Labels     = @((Get-Prop $raw 'labels') | Where-Object { $_ })
            Body       = Get-Prop $content 'body'
        }
    }
    if ($data.totalCount -gt @($cards).Count) { Say "Warning: the board has $($data.totalCount) cards, only $(@($cards).Count) were read." }
    @($cards)
}

function Get-StatusRank($card) {
    $rank = @{ 'in progress' = 0; 'ready' = 1; 'backlog' = 2; 'in review' = 3; 'done' = 4 }
    if ($card.Status -and $rank.ContainsKey($card.Status.ToLowerInvariant())) { $rank[$card.Status.ToLowerInvariant()] } else { 5 }
}

function Get-PriorityRank($card) {
    if ($card.Priority -match '^P(\d+)$') { [int]$Matches[1] } else { 99 }
}

function Format-CardName($card) {
    if ($card.Number) { "$($card.Repository)#$($card.Number)" } else { "draft $($card.Id)" }
}

if ($Action -eq 'Last') {
    $last = Get-LastBoard
    if ($last) { Write-Output $last }
    return
}
if (-not $Url) { throw "$Action needs -Url (the link of the board)." }
$board = Get-BoardLink $Url

switch ($Action) {
    'Access' {
        $project = Confirm-Access $board $Write.IsPresent
        $scopes = Get-TokenScopes
        $canMove = if ($null -eq $scopes) { 'unknown (this token does not list its scopes)' } elseif ($scopes -contains 'project') { 'yes' } else { 'no (Access -Write asks for it)' }
        $who = Get-Login
        Write-Output "Signed in as  : $(if ($who) { $who } else { '?' })"
        Write-Output "Board         : $($project.title) ($(if ($project.public) { 'public' } else { 'private' }), $($project.items.totalCount) cards) $($project.url)"
        Write-Output 'Can read      : yes'
        Write-Output "Can move cards: $canMove"
    }

    'List' {
        $project = Confirm-Access $board $Write.IsPresent
        $field = Get-StatusField $board
        $cards = Get-Cards $board

        $shown = $cards
        if ($Status) {
            $wanted = @($Status -split ',' | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
            $shown = @($cards | Where-Object { $_.Status -and $wanted -contains $_.Status.ToLowerInvariant() })
        }
        elseif (-not $All) {
            $shown = @($cards | Where-Object { $_.Status -ne 'Done' })
        }
        $shown = @($shown | Sort-Object @{ Expression = { Get-StatusRank $_ } }, @{ Expression = { Get-PriorityRank $_ } }, Index)

        if ($Json) {
            [ordered]@{
                board = [ordered]@{
                    title     = $project.title
                    url       = $project.url
                    owner     = $board.Owner
                    number    = $board.Number
                    projectId = $project.id
                    public    = [bool]$project.public
                    statuses  = @($field.options | ForEach-Object { $_.name })
                }
                items = $shown
            } | ConvertTo-Json -Depth 6
            return
        }

        Write-Output "Board  : $($project.title) ($(if ($project.public) { 'public' } else { 'private' })) $($project.url)"
        Write-Output "Columns: $((@($field.options | ForEach-Object { $_.name })) -join ' | ')"
        Write-Output "Showing $($shown.Count) of $($cards.Count) cards$(if (-not $Status -and -not $All) { ' (Done is left out; -All shows it)' })"
        Write-Output ''
        if ($shown.Count -eq 0) { Write-Output 'No cards.'; return }
        Write-Output ('{0,-12} {1,-4} {2,-4} {3}' -f 'Status', 'Prio', 'Size', 'Card')
        foreach ($card in $shown) {
            Write-Output ('{0,-12} {1,-4} {2,-4} {3}  {4}' -f $(if ($card.Status) { $card.Status } else { '-' }), $(if ($card.Priority) { $card.Priority } else { '-' }), $(if ($card.Size) { $card.Size } else { '-' }), (Format-CardName $card), $card.Title)
        }
    }

    'Move' {
        if (-not $Item -or -not $Status) { throw 'Move needs -Item (issue number or card id) and -Status (the column).' }
        $project = Confirm-Access $board (-not $DryRun)
        $field = Get-StatusField $board
        $option = @($field.options) | Where-Object { $_.name -ieq $Status.Trim() } | Select-Object -First 1
        if (-not $option) { throw "The board has no column '$Status'. Columns: $((@($field.options | ForEach-Object { $_.name })) -join ', ')." }

        $wantedNumber = $Item.Trim().TrimStart('#')
        $found = @(Get-Cards $board | Where-Object { $_.Id -eq $Item.Trim() -or ($null -ne $_.Number -and "$($_.Number)" -eq $wantedNumber) })
        if ($found.Count -eq 0) { throw "No card '$Item' on the board." }
        if ($found.Count -gt 1) { throw "'$Item' is on the board more than once ($((@($found | ForEach-Object { Format-CardName $_ })) -join ', ')): use the card id (PVTI_...)." }
        $card = $found[0]

        $edit = @('project', 'item-edit', '--id', $card.Id, '--project-id', $project.id, '--field-id', $field.id, '--single-select-option-id', $option.id)
        $was = if ($card.Status) { $card.Status } else { 'no column' }
        if ($DryRun) {
            Write-Output "Dry run: would move $(Format-CardName $card) '$($card.Title)' from $was to $($option.name):"
            Write-Output "  gh $($edit -join ' ')"
            return
        }
        $r = Invoke-Gh @edit
        if ($r.Code -ne 0) { throw "gh could not move the card: $($r.Error.Trim())" }
        Write-Output "Moved $(Format-CardName $card) '$($card.Title)' from $was to $($option.name)."
    }
}
