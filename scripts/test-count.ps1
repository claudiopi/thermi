#Requires -Version 7
<#
.SYNOPSIS
    The test-case ratchet: counts every case the runners would run, each row of a [Theory] (inline or member data), each
    Playwright test a loop or a describe makes and each Vitest case of a table, and fails when a half's count is above its
    baseline in tests/test-count.json, or when this change raised that baseline.
    Rows count as cases so that splitting a test that bundles many checks into rows never looks like growth, and bundling
    them never looks like thrift. A new behavior takes the cheapest test that proves it, and a test an edge test now
    covers goes; so the count stays flat while the code grows.
    The .NET half lists the built test projects, so it runs after the build; the page's half lists with its installed runners.
#>
param(
    [ValidateSet('All', 'DotNet', 'Web')]
    [string]$Part = 'All',
    [string]$Configuration = 'Release',
    # Where the build put its output, when it was given --artifacts-path.
    [string]$ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$baselineFile = 'tests/test-count.json'
$web = Join-Path $root 'src' 'Thermi.Settings.Web'

# xUnit's own listing, with each theory's rows enumerated as the runner finds them.
function Get-DotNetCases {
    $total = 0
    foreach ($project in Get-ChildItem (Join-Path $root 'tests') -Directory | Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") }) {
        $exe = if ($ArtifactsPath) { Join-Path $ArtifactsPath 'bin' $project.Name $Configuration.ToLowerInvariant() "$($project.Name).exe" }
            else { Join-Path $project.FullName 'bin' $Configuration '*' "$($project.Name).exe" | Resolve-Path -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Path }
        if (-not $exe -or -not (Test-Path $exe)) { throw "$($project.Name) is not built: build $Configuration first." }
        $listed = & $exe -list full/json -preEnumerateTheories | Out-String
        if ($LASTEXITCODE -ne 0) { throw "$exe could not list its tests." }
        $total += @($listed | ConvertFrom-Json).Count
    }
    $total
}

function Get-PlaywrightCases($Suites) {
    $count = 0
    foreach ($suite in $Suites) {
        foreach ($spec in $suite.specs) { $count += @($spec.tests).Count }
        $count += Get-PlaywrightCases $suite.suites
    }
    $count
}

# Playwright's and Vitest's listings, as each runner expands its loops and tables; a fixme or a skip is still a case.
function Get-WebCases {
    Push-Location $web
    try {
        $playwright = npx --no-install playwright test --list --reporter=json | Out-String | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or $playwright.errors) { throw "Playwright could not list its tests: $($playwright.errors.message)" }
        # Vitest's listing names a table once, so its cases are counted from a run, which takes a second.
        $report = Join-Path $root 'TestResults' 'vitest-count.json'
        npx --no-install vitest run --dir src --reporter=json --silent "--outputFile.json=$report" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Vitest could not run its tests.' }
        $vitest = Get-Content $report -Raw | ConvertFrom-Json
    }
    finally {
        Pop-Location
    }
    (Get-PlaywrightCases $playwright.suites) + $vitest.numTotalTests
}

$baseline = Get-Content (Join-Path $root $baselineFile) -Raw | ConvertFrom-Json
$halves = [ordered]@{}
if ($Part -ne 'Web') { $halves.dotnet = Get-DotNetCases }
if ($Part -ne 'DotNet') { $halves.web = Get-WebCases }

$raise = "To add tests beyond it, raise it in $baselineFile in a change of its own, which a person approves."
$failed = $false
foreach ($half in $halves.Keys) {
    if ($halves[$half] -gt $baseline.$half) {
        Write-Host "$($halves[$half]) $half test cases, above the baseline of $($baseline.$half)."
        $failed = $true
    }
}
if ($failed) {
    Write-Host 'Merge near-identical cases into one table, and delete tests an edge test already covers.'
    Write-Host $raise
    exit 1
}

# The baseline where this change branched off main, when git can tell; a change that raises it raises it alone. A
# baseline that counted something else then has nothing to compare with.
$main = @('origin/main', 'main') | Where-Object { git -C $root rev-parse --verify --quiet $_ 2>$null } | Select-Object -First 1
$base = if ($main) { git -C $root merge-base HEAD $main 2>$null }
$before = if ($base) { git -C $root show "${base}:$baselineFile" 2>$null | ConvertFrom-Json }
$raised = foreach ($half in $halves.Keys) { if ($null -ne $before.$half -and $before.$half -lt $baseline.$half) { $half } }
if ($raised) {
    $others = @(git -C $root diff --name-only $base) + @(git -C $root ls-files --others --exclude-standard) | Where-Object { $_ -ne $baselineFile }
    if ($others) {
        Write-Host "This change raises the baseline of $($raised -join ' and ') in $baselineFile along with other files."
        Write-Host $raise
        exit 1
    }
}

foreach ($half in $halves.Keys) {
    if ($halves[$half] -lt $baseline.$half) {
        Write-Host "note: $($halves[$half]) $half test cases, below the baseline of $($baseline.$half): lower it in $baselineFile so the ratchet holds what was won."
    }
}
exit 0
