#Requires -Version 7
<#
.SYNOPSIS
    The unique-test ratchet: counts each [Fact], each [Theory] (its rows are one test) and each Playwright or Vitest
    test, and fails when the count is above the baseline in tests/test-count.json, or when this change raised that
    baseline.
    A new behavior takes the cheapest test that proves it, a new case of one is a row of its table, and a test an edge
    test now covers goes; so the count stays flat while the code grows.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$baselineFile = 'tests/test-count.json'
$skipped = 'bin', 'obj', 'node_modules', 'TestResults', 'test-results', 'playwright-report', 'dist'

# Skipped folders are never entered: node_modules alone holds thousands of files.
function Get-Files([string]$Folder, [string[]]$Patterns) {
    if (-not (Test-Path $Folder)) { return }
    foreach ($pattern in $Patterns) { Get-ChildItem $Folder -File -Filter $pattern }
    Get-ChildItem $Folder -Directory | Where-Object Name -NotIn $skipped | ForEach-Object { Get-Files $_.FullName $Patterns }
}

function Get-Count([string]$Pattern, $Files) {
    ($Files | ForEach-Object { [regex]::Matches((Get-Content $_.FullName -Raw), $Pattern).Count } | Measure-Object -Sum).Sum
}

# An attribute on a line of its own, so a mention in a comment or a string is never counted.
$dotnet = Get-Count '(?m)^\s*\[(Fact|Theory)\b' (Get-Files (Join-Path $root 'tests') '*.cs')
# A test as Playwright or Vitest declares it, also when it waits behind skip, fixme or fail, and a table (test.each)
# as one; describe and hooks are no tests.
$web = Get-Count '\btest(\.(skip|fixme|fail|only))?\(\s*[''"`]|\btest\.each\(' (Get-Files (Join-Path $root 'src' 'Thermi.Settings.Web') '*.spec.ts', '*.spec.tsx', '*.test.ts', '*.test.tsx')
$count = $dotnet + $web
$baseline = (Get-Content (Join-Path $root $baselineFile) -Raw | ConvertFrom-Json).tests

$raise = "To add tests beyond it, raise `"tests`" in $baselineFile in a change of its own, which a person approves."
if ($count -gt $baseline) {
    Write-Host "$count unique tests ($dotnet .NET, $web web), above the baseline of $baseline."
    Write-Host 'Merge near-identical cases into one table-driven test, and delete tests an edge test already covers.'
    Write-Host $raise
    exit 1
}

# The baseline where this change branched off main, when git can tell; a change that raises it raises it alone.
$main = @('origin/main', 'main') | Where-Object { git -C $root rev-parse --verify --quiet $_ 2>$null } | Select-Object -First 1
$base = if ($main) { git -C $root merge-base HEAD $main 2>$null }
$before = if ($base) { git -C $root show "${base}:$baselineFile" 2>$null | ConvertFrom-Json }
if ($before -and $before.tests -lt $baseline) {
    $others = @(git -C $root diff --name-only $base) + @(git -C $root ls-files --others --exclude-standard) | Where-Object { $_ -ne $baselineFile }
    if ($others) {
        Write-Host "This change raises the baseline in $baselineFile from $($before.tests) to $baseline along with other files."
        Write-Host $raise
        exit 1
    }
}

if ($count -lt $baseline) {
    Write-Host "note: $count unique tests, below the baseline of ${baseline}: lower it in $baselineFile so the ratchet holds what was won."
}
exit 0
