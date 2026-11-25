#Requires -Version 7
<#
.SYNOPSIS
    Measures Thermi.Core's tests with Stryker.NET: each mutant of the code that no test kills is a behavior no test holds.
    Too slow for every check, so it runs on its own or as `pwsh scripts/check.ps1 -Mutation`.
    Prints the score; the HTML report, with every surviving mutant, is under TestResults/mutation.
    Arguments go on to Stryker: `--disable-bail` lists every test that kills each mutant, to see which test kills none of its own.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'TestResults' 'mutation'
# Stryker keeps every core busy for minutes: below normal priority, which its processes inherit, the PC stays usable.
$self = [Diagnostics.Process]::GetCurrentProcess()
$priority = $self.PriorityClass
$self.PriorityClass = 'BelowNormal'
# Stryker finds its config and the project under test from where it runs.
Push-Location (Join-Path $root 'tests' 'Thermi.Tests')
try {
    dotnet tool restore | Out-Null
    $log = dotnet dotnet-stryker --output $output --skip-version-check @args 2>&1 | ForEach-Object { "$_" }
    $failed = $LASTEXITCODE -ne 0
}
finally {
    Pop-Location
    $self.PriorityClass = $priority
}

$score = $log | Select-String 'final mutation score is' | Select-Object -Last 1
if ($failed -or -not $score) {
    $log | Select-Object -Last 40 | ForEach-Object { Write-Host "  $_" }
    Write-Host 'FAIL mutation'
    exit 1
}
Write-Host "ok   mutation: $($score.Line.Trim())"
$report = Get-ChildItem $output -Recurse -Filter 'mutation-report.html' | Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
Write-Host "     report: $report"
