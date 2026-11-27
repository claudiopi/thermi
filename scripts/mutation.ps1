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
    pwsh scripts/mutation.ps1 -Shard 2/4 mutates only the second of four shares of both projects' files and keeps its
    reports, with the tests' names, under TestResults/mutation; pwsh scripts/mutation.ps1 -Gate <folder> then merges the
    shares found under the folder and gates them as one run does. The nightly runs the shares side by side, as one run
    on a CI machine takes longer than a job may.
    pwsh scripts/mutation.ps1 -Web measures the page's unit tests with StrykerJS on the page's logic the same way, its
    report under TestResults/mutation-web.
#>
# No param block: PowerShell would take the arguments meant for Stryker.

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$mode = if ($args.Count -gt 0 -and $args[0] -in '-Web', '-Shard', '-Gate') { $args[0] }
$value = if ($mode -in '-Shard', '-Gate') { $args[1] }
$passed = @($args | Select-Object -Skip $(if ($value) { 2 } elseif ($mode) { 1 } else { 0 }) | Where-Object { $_ -ne '--disable-bail' })
$projects = 'Thermi.Core', 'Thermi.Cli'
$tests = Join-Path $root 'tests' 'Thermi.Tests'
$output = Join-Path $root 'TestResults' 'mutation'
$costsFile = Join-Path $root 'TestResults' 'bughunt-costs.json'
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

function Read-Report([string]$Path) { Get-Content $Path -Raw | ConvertFrom-Json -AsHashtable }

function Get-Report([string]$Project) { Join-Path $output $Project 'reports' 'mutation-report.json' }

function Get-Code {
    foreach ($project in $projects) {
        $folder = Join-Path $root 'src' $project
        foreach ($path in git -C $folder ls-files -- '*.cs') {
            [pscustomobject]@{ Project = $project; Path = $path; Bytes = (Get-Item (Join-Path $folder $path)).Length }
        }
    }
}

# A report's file, which Stryker names by its full path, as a path from its project's folder.
function Get-Named([string]$Key, [string]$Project) { ($Key -replace '\\', '/') -replace "^.*?/src/$([regex]::Escape($Project))/", '' }

# What each file cost the last run that measured it, as Assert-DotNet saves it: the tests its mutants ran, one each at
# least. A file it did not measure costs as much per byte as those it did; with no last run, each costs its bytes.
function Get-Costs {
    $last = if (Test-Path $costsFile) { Read-Report $costsFile } else { @{} }
    $code = @(Get-Code)
    foreach ($file in $code) { $file | Add-Member Key "$($file.Project)/$($file.Path)" }
    $known = @($code | Where-Object { $last.ContainsKey($_.Key) })
    $perByte = if ($known) { ($known | ForEach-Object { $last[$_.Key] } | Measure-Object -Sum).Sum / ($known | Measure-Object Bytes -Sum).Sum } else { 1 }
    $costs = @{}
    foreach ($file in $code) { $costs[$file.Key] = $last[$file.Key] ?? $file.Bytes * $perByte }
    $costs
}

# A file's size predicts its cost poorly (ConfigRules.cs: 7 KB, 17 % of all test runs), so the costliest file joins the
# share that costs least so far. Every share reads the same costs, so together they split the files exactly once.
function Get-Share([int]$At, [int]$Of) {
    $costs = Get-Costs
    $sums = [double[]]::new($Of)
    $share = @{}
    foreach ($file in Get-Code | Sort-Object @{ Expression = { $costs["$($_.Project)/$($_.Path)"] }; Descending = $true }, Project, Path) {
        $least = (0..($Of - 1) | Sort-Object { $sums[$_] }, { $_ })[0]
        $sums[$least] += $costs["$($file.Project)/$($file.Path)"]
        if ($least -eq $At - 1) { $share[$file.Project] += @($file.Path) }
    }
    $share
}

function Save-Costs([hashtable]$Reports) {
    $costs = [ordered]@{}
    foreach ($project in $Reports.Keys) {
        foreach ($file in $Reports[$project] | ForEach-Object { (Read-Report $_).files.GetEnumerator() }) {
            $tested = @($file.Value.mutants | Where-Object { $_.status -in 'Killed', 'Timeout', 'Survived', 'NoCoverage' })
            $costs["$project/$(Get-Named $file.Key $project)"] = ($tested | ForEach-Object { 1 + @($_.coveredBy).Count } | Measure-Object -Sum).Sum
        }
    }
    $costs | ConvertTo-Json | Set-Content $costsFile
}

# Stryker.NET finds its config and the test project from where it runs, and each project it mutates among those the
# tests reference. It breaks at nothing: the gate is the score of both, below. Given a share, each project mutates only
# its files of it, and its report keeps only those, so the shares' reports add up to one run's.
function Invoke-DotNet([hashtable]$Share) {
    Push-Location $tests
    try {
        dotnet tool restore | Out-Null
        foreach ($project in $projects) {
            $mutate = @(foreach ($path in $Share.$project) { '--mutate', "**/$path" })
            if ($Share -and -not $mutate) { continue }
            Invoke-Stryker "Stryker.NET on $project" (Get-Report $project) (@('dotnet', 'dotnet-stryker', '--project', "$project.csproj",
                    '--output', (Join-Path $output $project), '--skip-version-check', '--break-at', '0', '--disable-bail') + $mutate + $passed)
            if (-not $Share) { continue }
            $report = Read-Report (Get-Report $project)
            foreach ($key in @($report.files.Keys)) { if ((Get-Named $key $project) -notin $Share.$project) { $report.files.Remove($key) } }
            $report | ConvertTo-Json -Depth 100 -Compress | Set-Content (Get-Report $project)
        }
    }
    finally {
        Pop-Location
    }
}

function Join-Reports([string[]]$Reports) {
    $json = Join-Path $output 'reports' 'mutation-report.json'
    $merged = Read-Report $Reports[0]
    foreach ($report in $Reports | Select-Object -Skip 1) {
        (Read-Report $report).files.GetEnumerator() | ForEach-Object { $merged.files[$_.Key] = $_.Value }
    }
    New-Item -ItemType Directory -Force (Split-Path $json -Parent) | Out-Null
    $merged | ConvertTo-Json -Depth 100 -Compress | Set-Content $json
    $json
}

# A static initializer runs once, before Stryker.NET's runner turns any mutant on, so a static field's mutant reads
# Survived whatever the tests do: blanking "GpuNvidia" in SensorMap's table fails 19 tests by hand, yet Stryker lists it
# alive. The score left after them is what the tests can be held to. StrykerJS leaves them out itself.
function Measure-Caught([string[]]$Reports) {
    $mutants = $Reports | ForEach-Object { (Read-Report $_).files.Values } | ForEach-Object { $_.mutants }
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
    $measured = foreach ($project in $Reports.Keys) { @{ name = $project } + (Measure-Caught $Reports[$project]) }
    Save-Stats "bughunt-$Name" @{ at = [DateTime]::UtcNow.ToString('o'); break = $Break; projects = @($measured) }
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

# The report names a table's rows by their method alone; the built tests' own listing names each row.
function Get-TestNames {
    $names = @{}
    $exe = Get-ChildItem (Join-Path $tests 'bin') -Recurse -Filter 'Thermi.Tests.exe' -ErrorAction Ignore | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($exe) {
        & $exe.FullName -list full/json -preEnumerateTheories | Out-String | ConvertFrom-Json | ForEach-Object { $names[$_.ID] = $_.DisplayName }
    }
    $names
}

function Assert-DotNet([hashtable]$Reports, [hashtable]$Names) {
    $json = Join-Reports @($Reports.Values | ForEach-Object { $_ })
    $break = (Get-Content (Join-Path $tests 'stryker-config.json') -Raw | ConvertFrom-Json).'stryker-config'.thresholds.break
    Save-BugHunt 'dotnet' $Reports $break
    Save-Costs $Reports
    Assert-Score $json 'Thermi.Core and Thermi.Cli' $break
    Assert-Redundant $json @{ Names = $Names }
}

function Measure-DotNet {
    Invoke-DotNet
    $reports = [ordered]@{}
    foreach ($project in $projects) { $reports[$project] = Get-Report $project }
    foreach ($project in $projects) {
        $report = Get-ChildItem (Join-Path $output $project) -Recurse -Filter 'mutation-report.html' |
            Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
        Write-Host "     report: $report"
    }
    Assert-DotNet $reports (Get-TestNames)
}

function Measure-Share([string]$Of) {
    $at, $count = [int[]]($Of -split '/')
    Invoke-DotNet (Get-Share $at $count)
    Get-TestNames | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $output 'test-names.json')
}

function Measure-Shares([string]$Folder) {
    $reports = [ordered]@{}
    foreach ($project in $projects) {
        $reports[$project] = @(Get-ChildItem $Folder -Recurse -Filter 'mutation-report.json' | Where-Object { $_.Directory.Parent.Name -eq $project } |
                ForEach-Object FullName)
        $named = @($reports[$project] | ForEach-Object { (Read-Report $_).files.Keys } | ForEach-Object { Get-Named $_ $project })
        $missing = @(Get-Code | Where-Object { $_.Project -eq $project -and $_.Path -notin $named } | ForEach-Object Path)
        $twice = @($named | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
        if ($missing -or $twice) {
            Write-Host "FAIL mutation: the shares of $project miss $($missing -join ', ') and hold twice $($twice -join ', ')"
            exit 1
        }
    }
    $names = @{}
    $listed = Get-ChildItem $Folder -Recurse -Filter 'test-names.json' | Select-Object -First 1
    if ($listed) { (Read-Report $listed.FullName).GetEnumerator() | ForEach-Object { $names[$_.Key] = $_.Value } }
    Assert-DotNet $reports $names
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
    switch ($mode) {
        '-Web' { Measure-Page }
        '-Shard' { Measure-Share $value }
        '-Gate' { Measure-Shares $value }
        default { Measure-DotNet }
    }
}
finally {
    $title, $name = switch ($mode) {
        '-Web' { "🐛 Bug hunt · the page's logic", 'BugHuntWeb' }
        '-Shard' { "🐛 Bug hunt · Thermi.Core and Thermi.Cli, share $value", 'BugHuntShare' }
        default { '🐛 Bug hunt · Thermi.Core and Thermi.Cli', 'BugHunt' }
    }
    Write-Summary $title $name
}
