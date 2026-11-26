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
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'TestResults' 'mutation'
$tests = Join-Path $root 'tests' 'Thermi.Tests'
$json = Join-Path $output 'reports' 'mutation-report.json'
$projects = 'Thermi.Core', 'Thermi.Cli'
Remove-Item $json -ErrorAction Ignore
# Stryker keeps every core busy for minutes: below normal priority, which its processes inherit, the PC stays usable.
$self = [Diagnostics.Process]::GetCurrentProcess()
$priority = $self.PriorityClass
$self.PriorityClass = 'BelowNormal'
# Stryker finds its config and the test project from where it runs, and each project it mutates among those the tests
# reference. It breaks at nothing: the gate is below.
Push-Location $tests
try {
    dotnet tool restore | Out-Null
    $passed = @($args | Where-Object { $_ -ne '--disable-bail' })
    $reports = foreach ($project in $projects) {
        $into = Join-Path $output $project
        $report = Join-Path $into 'reports' 'mutation-report.json'
        Remove-Item $report -ErrorAction Ignore
        $log = dotnet dotnet-stryker --project "$project.csproj" --output $into --skip-version-check --break-at 0 --disable-bail @passed 2>&1 |
            ForEach-Object { "$_" }
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $report)) {
            $log | Select-Object -Last 40 | ForEach-Object { Write-Host "  $_" }
            Write-Host "FAIL mutation: $project"
            exit 1
        }
        $report
    }
}
finally {
    Pop-Location
    $self.PriorityClass = $priority
}

# One report of both: the same tests, by the same ids, and each project's files with their mutants.
$merged = Get-Content $reports[0] -Raw | ConvertFrom-Json -AsHashtable
foreach ($report in $reports | Select-Object -Skip 1) {
    (Get-Content $report -Raw | ConvertFrom-Json -AsHashtable).files.GetEnumerator() | ForEach-Object { $merged.files[$_.Key] = $_.Value }
}
New-Item -ItemType Directory -Force (Split-Path $json -Parent) | Out-Null
$merged | ConvertTo-Json -Depth 100 -Compress | Set-Content $json

# A static initializer runs once, before Stryker.NET's runner turns any mutant on, so a static field's mutant reads
# Survived whatever the tests do: blanking "GpuNvidia" in SensorMap's table fails 19 tests by hand, yet Stryker lists it
# alive. The score left after them is what the tests can be held to.
$mutants = (Get-Content $json -Raw | ConvertFrom-Json -AsHashtable).files.Values | ForEach-Object { $_.mutants }
$counted = @($mutants | Where-Object { -not $_.static -and $_.status -in 'Killed', 'Timeout', 'Survived', 'NoCoverage' })
$killed = @($counted | Where-Object { $_.status -in 'Killed', 'Timeout' }).Count
$score = [math]::Round(100 * $killed / $counted.Count, 2)
$break = (Get-Content (Join-Path $tests 'stryker-config.json') -Raw | ConvertFrom-Json).'stryker-config'.thresholds.break
$static = @($mutants | Where-Object { $_.static }).Count
$line = "$score % of $($counted.Count) mutants of $($projects -join ' and ') ($static in static initializers left out), break at $break %"
if ($score -lt $break) {
    Write-Host "FAIL mutation: $line"
    exit 1
}
Write-Host "ok   mutation: $line"
foreach ($project in $projects) {
    $report = Get-ChildItem (Join-Path $output $project) -Recurse -Filter 'mutation-report.html' | Sort-Object LastWriteTime |
        Select-Object -Last 1 -ExpandProperty FullName
    Write-Host "     report: $report"
}

# The report names a table's rows by their method alone; the built tests' own listing names each row.
$names = @{}
$exe = Get-ChildItem (Join-Path $tests 'bin') -Recurse -Filter 'Thermi.Tests.exe' -ErrorAction Ignore | Sort-Object LastWriteTime | Select-Object -Last 1
if ($exe) {
    & $exe.FullName -list full/json -preEnumerateTheories | Out-String | ConvertFrom-Json | ForEach-Object { $names[$_.ID] = $_.DisplayName }
}
& (Join-Path $PSScriptRoot 'redundant.ps1') -Report $json -Names $names
exit $LASTEXITCODE
