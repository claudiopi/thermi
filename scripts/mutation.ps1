#Requires -Version 7
<#
.SYNOPSIS
    Measures Thermi.Tests with Stryker.NET on Thermi.Core and Thermi.Cli, whose tests both live there: each mutant of the
    code that no test kills is a behavior no test holds. Too slow for every check, so it runs on its own or as
    `pwsh scripts/check.ps1 -Mutation`.
    Prints the score of both; each project's HTML report, with every surviving mutant, is under TestResults/mutation. Bail
    is off, so the reports list every test that kills each mutant, and the cases that hold nothing of their own are listed
    and gated (scripts/redundant.ps1) over both, so a case told apart by what the CLI does is judged by the CLI's mutants
    too. Arguments go on to Stryker.
    pwsh scripts/mutation.ps1 -Web measures the page's unit tests with StrykerJS on the page's logic the same way, its
    report under TestResults/mutation-web.
#>
# No param block: PowerShell would take the arguments meant for Stryker.

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$web = $args.Count -gt 0 -and $args[0] -eq '-Web'
$passed = @($args | Select-Object -Skip $(if ($web) { 1 } else { 0 }) | Where-Object { $_ -ne '--disable-bail' })
$tests = Join-Path $root 'tests' 'Thermi.Tests'
$page = Join-Path $root 'src' 'Thermi.Settings.Web'
. (Join-Path $PSScriptRoot 'summary.ps1')

# One Stryker run, at idle priority, which its processes inherit, so the PC stays usable while it keeps every core busy
# for minutes. StrykerJS fails below its own break once its report is written, which the score's gate then tells.
function Invoke-Stryker([string]$Name, [string]$Report, [string[]]$Run, [switch]$BreaksItself) {
    Remove-Item $Report -ErrorAction Ignore
    $self = [Diagnostics.Process]::GetCurrentProcess()
    $priority = $self.PriorityClass
    $self.PriorityClass = 'Idle'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $program, $arguments = $Run
    try { $log = & $program @arguments 2>&1 | ForEach-Object { "$_" } }
    finally { $self.PriorityClass = $priority }
    $ok = ($LASTEXITCODE -eq 0 -or $BreaksItself) -and (Test-Path $Report)
    Add-SummaryRow $Name $(if ($ok) { 'ok' } else { 'fail' }) $watch.Elapsed.TotalSeconds
    if ($ok) { return }
    $log | Select-Object -Last 40 | ForEach-Object { Write-Host "  $_" }
    Write-Host "FAIL mutation: $Name"
    exit 1
}

# Stryker.NET finds its config and the test project from where it runs, and each project it mutates among those the
# tests reference. It breaks at nothing: the gate is the score of both, below. Returns the report of both, merged: the
# same tests, by the same ids, and each project's files with their mutants.
function Invoke-DotNet {
    $output = Join-Path $root 'TestResults' 'mutation'
    $json = Join-Path $output 'reports' 'mutation-report.json'
    Remove-Item $json -ErrorAction Ignore
    Push-Location $tests
    try {
        dotnet tool restore | Out-Null
        $reports = foreach ($project in 'Thermi.Core', 'Thermi.Cli') {
            $into = Join-Path $output $project
            $report = Join-Path $into 'reports' 'mutation-report.json'
            Invoke-Stryker "Stryker.NET on $project" $report (@('dotnet', 'dotnet-stryker', '--project', "$project.csproj", '--output', $into,
                    '--skip-version-check', '--break-at', '0', '--disable-bail') + $passed)
            $report
        }
    }
    finally {
        Pop-Location
    }
    $merged = Get-Content $reports[0] -Raw | ConvertFrom-Json -AsHashtable
    foreach ($report in $reports | Select-Object -Skip 1) {
        (Get-Content $report -Raw | ConvertFrom-Json -AsHashtable).files.GetEnumerator() | ForEach-Object { $merged.files[$_.Key] = $_.Value }
    }
    New-Item -ItemType Directory -Force (Split-Path $json -Parent) | Out-Null
    $merged | ConvertTo-Json -Depth 100 -Compress | Set-Content $json
    $json
}

# A static initializer runs once, before Stryker.NET's runner turns any mutant on, so a static field's mutant reads
# Survived whatever the tests do: blanking "GpuNvidia" in SensorMap's table fails 19 tests by hand, yet Stryker lists it
# alive. The score left after them is what the tests can be held to. StrykerJS leaves them out itself.
function Measure-Caught([string]$Report) {
    $mutants = (Get-Content $Report -Raw | ConvertFrom-Json -AsHashtable).files.Values | ForEach-Object { $_.mutants }
    $counted = @($mutants | Where-Object { -not $_.static -and $_.status -in 'Killed', 'Timeout', 'Survived', 'NoCoverage' })
    $caught = @($counted | Where-Object { $_.status -in 'Killed', 'Timeout' }).Count
    @{ caught = $caught; missed = $counted.Count - $caught; static = @($mutants | Where-Object { $_.static }).Count }
}

function Assert-Score([string]$Report, [string]$Of, [double]$Break) {
    $measured = Measure-Caught $Report
    $counted = $measured.caught + $measured.missed
    $score = [math]::Round(100 * $measured.caught / [Math]::Max(1, $counted), 2)
    $left = if ($measured.static) { " ($($measured.static) in static initializers left out)" } else { '' }
    $line = "$score % of $counted mutants of $Of$left, break at $Break %"
    $ok = $score -ge $Break
    Add-SummaryRow 'bugs caught' $(if ($ok) { 'ok' } else { 'fail' }) 0 "$score % of $counted planted bugs, break at $Break %"
    if (-not $ok) {
        Write-Host "FAIL mutation: $line"
        exit 1
    }
    Write-Host "ok   mutation: $line"
}

# The Bug hunt's caught and missed of each project, for the CI's dashboard, saved before any gate stops the run.
function Save-BugHunt([string]$Name, [hashtable]$Reports, [double]$Break) {
    $projects = foreach ($project in $Reports.Keys) { @{ name = $project } + (Measure-Caught $Reports[$project]) }
    Save-Stats "bughunt-$Name" @{ at = [DateTime]::UtcNow.ToString('o'); break = $Break; projects = @($projects) }
}

function Assert-Redundant([string]$Report, [hashtable]$Arguments) {
    $printed = & (Join-Path $PSScriptRoot 'redundant.ps1') -Report $Report @Arguments 6>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $printed | ForEach-Object { Write-Host $_ }
    $line = $printed | Where-Object { $_ -match '^(ok  |FAIL) redundant cases: ' } | Select-Object -First 1
    $shown = ($line -replace '^(ok  |FAIL) redundant cases: ', '') -replace ', at most.*$', ''
    Add-SummaryRow 'cases that hold nothing of their own' $(if ($code -eq 0) { 'ok' } else { 'fail' }) 0 $shown
    if ($code -ne 0) { exit $code }
}

function Measure-DotNet {
    $json = Invoke-DotNet
    $break = (Get-Content (Join-Path $tests 'stryker-config.json') -Raw | ConvertFrom-Json).'stryker-config'.thresholds.break
    $reports = [ordered]@{}
    foreach ($project in 'Thermi.Core', 'Thermi.Cli') {
        $reports[$project] = Join-Path $root 'TestResults' 'mutation' $project 'reports' 'mutation-report.json'
    }
    Save-BugHunt 'dotnet' $reports $break
    Assert-Score $json 'Thermi.Core and Thermi.Cli' $break
    foreach ($project in 'Thermi.Core', 'Thermi.Cli') {
        $report = Get-ChildItem (Join-Path $root 'TestResults' 'mutation' $project) -Recurse -Filter 'mutation-report.html' |
            Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
        Write-Host "     report: $report"
    }
    # The report names a table's rows by their method alone; the built tests' own listing names each row.
    $names = @{}
    $exe = Get-ChildItem (Join-Path $tests 'bin') -Recurse -Filter 'Thermi.Tests.exe' -ErrorAction Ignore | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($exe) {
        & $exe.FullName -list full/json -preEnumerateTheories | Out-String | ConvertFrom-Json | ForEach-Object { $names[$_.ID] = $_.DisplayName }
    }
    Assert-Redundant $json @{ Names = $names }
}

function Measure-Page {
    $json = Join-Path $root 'TestResults' 'mutation-web' 'mutation-report.json'
    $run = @('npm', 'run', 'mutation', '--prefix', $page) + @(if ($passed) { '--' } ) + $passed
    Invoke-Stryker "StrykerJS on the page's logic" $json $run -BreaksItself
    $break = (Get-Content (Join-Path $page 'stryker.config.json') -Raw | ConvertFrom-Json).thresholds.break
    Save-BugHunt 'web' @{ "The page's logic" = $json } $break
    Assert-Score $json "the page's logic" $break
    Write-Host "     report: $(Join-Path $root 'TestResults' 'mutation-web' 'mutation-report.html')"
    Assert-Redundant $json @{ Root = $page }
}

try {
    if ($web) { Measure-Page } else { Measure-DotNet }
}
finally {
    if ($web) { Write-Summary "🐛 Bug hunt · the page's logic" 'BugHuntWeb' -Always }
    else { Write-Summary '🐛 Bug hunt · Thermi.Core and Thermi.Cli' 'BugHunt' -Always }
}
