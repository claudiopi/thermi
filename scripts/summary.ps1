# Dot-sourced by the check and the mutation scripts: on GitHub Actions, the job's summary, one table of its steps, each
# with its result, its time and what it measured. Anywhere else it writes nothing.

$summaryRows = [Collections.Generic.List[object]]::new()
$summaryWatch = [Diagnostics.Stopwatch]::StartNew()
$summaryIcons = @{ ok = '✅'; fail = '❌'; info = '📊' }

# A step's row: ok or fail, or info for what is shown and not gated; what it measured, one line each.
function Add-SummaryRow([string]$Name, [ValidateSet('ok', 'fail', 'info')][string]$Status, [double]$Seconds, [string[]]$Shows = @()) {
    $summaryRows.Add([pscustomobject]@{ Name = $Name; Status = $Status; Seconds = $Seconds; Shows = @($Shows | Where-Object { $_ }) })
}

function Format-Seconds([double]$Seconds) {
    if ($Seconds -lt 60) { return '{0:n0} s' -f $Seconds }
    '{0:n0} min {1:00} s' -f [Math]::Floor($Seconds / 60), [Math]::Floor($Seconds % 60)
}

function Write-Summary([string]$Title) {
    if (-not $env:GITHUB_STEP_SUMMARY -or -not $summaryRows.Count) { return }
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
