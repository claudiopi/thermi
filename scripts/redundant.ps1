#Requires -Version 7
<#
.SYNOPSIS
    From a mutation report that lists every test that kills each mutant (Stryker run with bail off), finds the cases that
    hold nothing of their own: every mutant each kills has another killer, and every mutant each runs another case that
    runs it. They are taken out one by one, the ones that kill least first, each only while the cases left still hold all,
    so what is listed can go as a set. Fails when more than a few of the cases judged are listed, leaving out the rows of
    a table that says why each row is a behavior of its own (scripts/rows.ps1).
    A static mutant is left out: Stryker credits its kill to whichever case first loads the class. So is a case that runs
    no mutant, which tests code the run does not mutate and so cannot be judged here.
    A case listed may still be the only proof of what Stryker makes no mutant of: one operand of && or ||, a number at a
    boundary, a static table's entry, a bug's reproduction. Cut it only once a person checked that.
#>
param(
    [Parameter(Mandatory)]
    [string]$Report,
    # Where the report's test files are relative to, when it names them so.
    [string]$Root = (Get-Location).Path,
    # The share of the cases judged that may hold nothing of their own.
    [double]$Percent = 3,
    # Each case's name as its runner lists it, by its id, for a report that names a table's rows by their method alone.
    [hashtable]$Names = @{}
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'rows.ps1')
$json = Get-Content $Report -Raw | ConvertFrom-Json -AsHashtable

# Each case's name, and whether it is a row of a table, and of one that says why its rows are each a behavior of their own.
$named = @{}
$said = @{}
$row = @{}
foreach ($file in $json.testFiles.GetEnumerator()) {
    $path = if ([IO.Path]::IsPathRooted($file.Key)) { $file.Key } else { Join-Path $Root $file.Key }
    $lines = if (Test-Path $path) { Get-Content $path }
    foreach ($table in $file.Value.tests | Group-Object { $_.location.start.line }) {
        $why = $table.Count -gt 1 -and $lines -and (Test-RowsSayWhy $lines ([int]$table.Name - 1))
        foreach ($test in $table.Group) {
            $named[$test.id] = if ($Names[$test.id]) { $Names[$test.id] } else { $test.name }
            $said[$test.id] = $why
            $row[$test.id] = $table.Count -gt 1
        }
    }
}

# Per case, the mutants it kills and those it runs; per mutant, how many cases still held kill it and run it.
$kills = @{}
$runs = @{}
$killers = @{}
$runners = @{}
function Add-Mutant([hashtable]$Of, [string]$Test, [string]$Key) {
    if (-not $Of.ContainsKey($Test)) { $Of[$Test] = [Collections.Generic.List[string]]::new() }
    $Of[$Test].Add($Key)
}
foreach ($file in $json.files.GetEnumerator()) {
    foreach ($mutant in $file.Value.mutants) {
        if ($mutant.static) { continue }
        $key = "$($file.Key)|$($mutant.id)"
        $killedBy = if ($mutant.status -eq 'Killed' -and $mutant.killedBy) { @($mutant.killedBy) } else { @() }
        $coveredBy = if ($mutant.coveredBy) { @($mutant.coveredBy) } else { @() }
        $killers[$key] = $killedBy.Count
        $runners[$key] = $coveredBy.Count
        foreach ($test in $killedBy) { Add-Mutant $kills $test $key }
        foreach ($test in $coveredBy) { Add-Mutant $runs $test $key }
    }
}

function Test-Held([hashtable]$Left, $Keys) {
    foreach ($key in $Keys) { if ($Left[$key] -lt 2) { return $false } }
    $true
}
# A row of a table that says nothing goes first, then one that says why, then a test of its own, which is a behavior.
$judged = @($runs.Keys)
$order = { if (-not $row[$_]) { 2 } elseif ($said[$_]) { 1 } else { 0 } }
$free = foreach ($test in $judged | Sort-Object $order, { @($kills[$_]).Count }, { $named[$_] }) {
    if ((Test-Held $killers $kills[$test]) -and (Test-Held $runners $runs[$test])) {
        foreach ($key in $kills[$test]) { $killers[$key]-- }
        foreach ($key in $runs[$test]) { $runners[$key]-- }
        $test
    }
}

# A table's rows share their method's name up to its arguments; a page's case is listed by its own title. A test of its
# own is a behavior, which goes only with its requirement, so it is listed but not held to the share.
$unsaid = @($free | Where-Object { $row[$_] -and -not $said[$_] })
$alone = @($free | Where-Object { -not $row[$_] })
$method = { param($test) $named[$test] -replace '\(.*$', '' }
$all = $judged | Group-Object { & $method $_ } -AsHashTable
foreach ($group in $unsaid | Group-Object { & $method $_ } | Sort-Object Count -Descending) {
    Write-Host "     $($group.Count) of $($all[$group.Name].Count) $($group.Name)"
}
$share = if ($judged.Count) { 100 * $unsaid.Count / $judged.Count } else { 0 }
$line = ('{0} of {1} cases ({2:n1} %) are rows that kill and run nothing of their own, at most {3} % may; {4} more are rows of ' +
    'tables that say why, and {5} tests of their own') -f $unsaid.Count, $judged.Count, $share, $Percent,
    (@($free).Count - $unsaid.Count - $alone.Count), $alone.Count
if ($share -gt $Percent) {
    Write-Host "FAIL redundant cases: $line"
    Write-Host '  Cut each row listed, or merge its table, once a person checked it is not the only row of an operand of && or ||,'
    Write-Host "  of a number at a boundary, of a static table's entry or of a bug; or say on its table why, as '// Rows: ...'."
    exit 1
}
Write-Host "ok   redundant cases: $line"
