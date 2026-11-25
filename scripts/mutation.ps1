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
$tests = Join-Path $root 'tests' 'Thermi.Tests'
$json = Join-Path $output 'reports' 'mutation-report.json'
Remove-Item $json -ErrorAction Ignore
# Stryker keeps every core busy for minutes: below normal priority, which its processes inherit, the PC stays usable.
$self = [Diagnostics.Process]::GetCurrentProcess()
$priority = $self.PriorityClass
$self.PriorityClass = 'BelowNormal'
# Stryker finds its config and the project under test from where it runs. It breaks at nothing: the gate is below.
Push-Location $tests
try {
    dotnet tool restore | Out-Null
    $log = dotnet dotnet-stryker --output $output --skip-version-check --break-at 0 @args 2>&1 | ForEach-Object { "$_" }
    $failed = $LASTEXITCODE -ne 0
}
finally {
    Pop-Location
    $self.PriorityClass = $priority
}

if ($failed -or -not (Test-Path $json)) {
    $log | Select-Object -Last 40 | ForEach-Object { Write-Host "  $_" }
    Write-Host 'FAIL mutation'
    exit 1
}

# A static initializer runs once, before Stryker.NET's runner turns any mutant on, so a static field's mutant reads
# Survived whatever the tests do: blanking "GpuNvidia" in SensorMap's table fails 19 tests by hand, yet Stryker lists it
# alive. The score left after them is what the tests can be held to.
$mutants = (Get-Content $json -Raw | ConvertFrom-Json -AsHashtable).files.Values | ForEach-Object { $_.mutants }
$counted = @($mutants | Where-Object { -not $_.static -and $_.status -in 'Killed', 'Timeout', 'Survived', 'NoCoverage' })
$killed = @($counted | Where-Object { $_.status -in 'Killed', 'Timeout' }).Count
$score = [math]::Round(100 * $killed / $counted.Count, 2)
$break = (Get-Content (Join-Path $tests 'stryker-config.json') -Raw | ConvertFrom-Json).'stryker-config'.thresholds.break
$static = @($mutants | Where-Object { $_.static }).Count
$line = "$score % of $($counted.Count) mutants ($static in static initializers left out), break at $break %"
if ($score -lt $break) {
    Write-Host "FAIL mutation: $line"
    exit 1
}
Write-Host "ok   mutation: $line"
$report = Get-ChildItem $output -Recurse -Filter 'mutation-report.html' | Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
Write-Host "     report: $report"
