# Dot-sourced by the check and the mutation scripts: on GitHub Actions, the job's summary, one table of its steps, each
# with its result, its time and what it measured. Anywhere, what they measured as data, which `scripts/stats.ps1` and the
# CI's dashboard read.

$summaryRows = [Collections.Generic.List[object]]::new()
$summaryWatch = [Diagnostics.Stopwatch]::StartNew()
$summaryIcons = @{ ok = '✅'; fail = '❌'; info = '📊' }
$statsFolder = Join-Path (Split-Path $PSScriptRoot -Parent) 'TestResults' 'stats'

# What a step measured, as TestResults/stats/<name>.json; the CI's jobs hand the folder to the dashboard's.
function Save-Stats([string]$Name, $Data) {
    New-Item -ItemType Directory -Force $statsFolder | Out-Null
    $Data | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $statsFolder "$Name.json") -Encoding utf8
}

# A step's row: ok or fail, or info for what is shown and not gated; what it measured, one line each.
function Add-SummaryRow([string]$Name, [ValidateSet('ok', 'fail', 'info')][string]$Status, [double]$Seconds, [string[]]$Shows = @()) {
    $summaryRows.Add([pscustomobject]@{ Name = $Name; Status = $Status; Seconds = $Seconds; Shows = @($Shows | Where-Object { $_ }) })
}

function Format-Seconds([double]$Seconds) {
    if ($Seconds -lt 60) { return '{0:n0} s' -f $Seconds }
    '{0:n0} min {1:00} s' -f [Math]::Floor($Seconds / 60), [Math]::Floor($Seconds % 60)
}

# The steps' table, and the steps as data under $Name, whose times the dashboard shows when no CI timed the jobs.
function Write-Summary([string]$Title, [string]$Name) {
    if (-not $summaryRows.Count) { return }
    Save-Stats "steps-$Name" @{ title = $Title; seconds = $summaryWatch.Elapsed.TotalSeconds; steps = @($summaryRows) }
    if (-not $env:GITHUB_STEP_SUMMARY) { return }
    $failed = $summaryRows | Where-Object Status -eq 'fail'
    $lines = @(
        "### $(if ($failed) { $summaryIcons.fail } else { $summaryIcons.ok }) $Title · $(Format-Seconds $summaryWatch.Elapsed.TotalSeconds)"
        ''
        '| | Step | Time | Result |'
        '| :-: | --- | --: | --- |'
    )
    $lines += foreach ($row in $summaryRows) {
        $time = if ($row.Seconds -gt 0) { Format-Seconds $row.Seconds } else { '' }
        $shows = ($row.Shows | ForEach-Object { $_ -replace '\|', '\|' }) -join '<br>'
        "| $($summaryIcons[$row.Status]) | $($row.Name) | $time | $shows |"
    }
    $lines + '' | Add-Content $env:GITHUB_STEP_SUMMARY -Encoding utf8
}
