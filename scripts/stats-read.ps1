# Dot-sourced by scripts/stats.ps1: what a check's run measured, from the files its steps saved under TestResults/stats
# and the app's coverage reports, and what the repo itself says: the specs, their checks and the recorded PCs.
. (Join-Path $PSScriptRoot 'coverage.ps1')

function Read-Json([string]$Path) {
    if (Test-Path $Path) { Get-Content $Path -Raw | ConvertFrom-Json -AsHashtable }
}

# Everything one run measured; what no step saved stays null, as a job that failed before it leaves it.
function Read-Run([string]$Results) {
    $stats = Join-Path $Results 'stats'
    $tests = @(foreach ($half in 'dotnet', 'web') {
            $read = Read-Json (Join-Path $stats "tests-$half.json")
            if ($read) { $read.half = $half; $read }
        })
    $bugHunt = @('dotnet', 'web' | ForEach-Object { Read-Json (Join-Path $stats "bughunt-$_.json") } | Where-Object { $_ })
    @{
        Coverage = Read-Json (Join-Path $stats 'coverage.json')
        App = (Read-AppCoverage (Join-Path $Results 'app') @('Thermi.Windows.Tests')).Modules
        Tests = $tests
        Steps = @(Get-ChildItem $stats -Filter 'steps-*.json' -ErrorAction SilentlyContinue | ForEach-Object { Read-Json $_.FullName })
        Msi = Read-Json (Join-Path $stats 'msi.json')
        BugHunt = $bugHunt
        GitHub = Read-Json (Join-Path $stats 'github.json')
    }
}

# The step a check's half ran under that name, as its steps file saved it.
function Find-Step($Run, [string]$Name) {
    $Run.Steps | ForEach-Object { $_.steps } | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
}

function Get-DoctorScore($Run) {
    $step = Find-Step $Run 'web doctor'
    $line = $step.Shows | Where-Object { $_ -match '^Score: (\d+) / 100' } | Select-Object -First 1
    if ($line -match '^Score: (\d+)') { @{ Score = [int]$Matches[1]; Ok = $step.Status -eq 'ok' } }
}

# Each spec with its title, status, acceptance steps and, from the ratchet's count, its tests against its budget.
function Read-Specs([string]$Root, $Run) {
    foreach ($folder in Get-ChildItem (Join-Path $Root 'docs' 'specs') -Directory | Sort-Object Name) {
        $spec = Get-Content (Join-Path $folder.FullName 'spec.md') -Raw
        $status = if ($spec -match '(?m)^\*\*Status\*\*: (\w+)') { $Matches[1] } else { 'draft' }
        if ($status -eq 'dropped') { continue }
        $title = if ($spec -match '(?m)^# \d+\. (.+)$') { $Matches[1].Trim() } else { $folder.Name }
        $steps = @((Get-Content (Join-Path $folder.FullName 'acceptance.json') -Raw | ConvertFrom-Json -AsHashtable).steps)
        $budgets = foreach ($half in $Run.Tests) {
            $counted = $half.specs[$folder.Name]
            if ($counted -and $counted.budget) { @{ Half = $half.half; Cases = $counted.cases; Budget = $counted.budget } }
        }
        # A requirement's indented "Release check: <check>." line names what a person checks of it before each release.
        $named = @{}
        $requirement = $null
        foreach ($line in $spec -split "`r?`n") {
            if ($line -match '^- (R\d+)\.') { $requirement = $Matches[1] }
            elseif ($line -match '^\s+Release check: (.+)\.$') { $named[$Matches[1]] += @($requirement) }
        }
        [pscustomobject]@{
            Id = $folder.Name.Substring(0, 3); Title = $title; Status = $status; Steps = $steps; Budgets = @($budgets); ReleaseChecks = $named
        }
    }
}

# The code the repo holds, from git's own list, so nothing built, installed or recorded counts: each file's non-blank
# lines, its project (its folder under src or tests, or scripts), and whether it is a test, as the tests' projects and the
# page's tests and unit tests are. What a build writes from Core, as the page's .g.ts, is left out.
function Read-Code([string]$Root) {
    $languages = @{ '.cs' = 'C#'; '.ts' = 'TypeScript'; '.tsx' = 'TypeScript'; '.ps1' = 'PowerShell'; '.js' = 'JS'; '.mjs' = 'JS'; '.cjs' = 'JS' }
    foreach ($path in git -C $Root ls-files) {
        $language = $languages[[IO.Path]::GetExtension($path)]
        if (-not $language -or $path -like '*.g.ts') { continue }
        $parts = $path -split '/'
        $test = $parts[0] -eq 'tests' -or $path -match '^src/[^/]+/tests/|\.test\.tsx?$'
        $lines = @([IO.File]::ReadAllLines((Join-Path $Root $path)) | Where-Object { $_.Trim() }).Count
        [pscustomobject]@{ Project = $(if ($parts[0] -eq 'scripts') { 'scripts' } else { $parts[1] }); Test = $test; Language = $language; Lines = $lines }
    }
}

# The checks a person runs before each release, from docs/RELEASES.md's tables under "Before each release": each
# check's name in bold, under the heading of its area.
function Read-ReleaseChecks([string]$Root) {
    $within = $false
    $area = $null
    foreach ($line in Get-Content (Join-Path $Root 'docs' 'RELEASES.md')) {
        if ($line -match '^## ') { $within = $line -eq '## Before each release' }
        elseif ($within -and $line -match '^### (.+)$') { $area = $Matches[1] }
        elseif ($within -and $line -match '^\| \*\*(.+?)\*\* \|') { [pscustomobject]@{ Name = $Matches[1]; Area = $area } }
    }
}

# The PCs whose sensors the tests replay, one per recording's name before its admin rights: each snapshot's CPU and
# its discrete GPU before the CPU's own.
function Read-RecordedPcs([string]$Root) {
    $folder = Join-Path $Root 'tests' 'Thermi.Tests' 'Recordings'
    $files = @(Get-ChildItem $folder -File -Include '*.json', '*.jsonl' -Recurse)
    $admin = '-(no-)?admin(-.*)?$'
    $files | Where-Object { $_.Extension -eq '.json' -and $_.BaseName -match $admin } | Group-Object { $_.BaseName -replace $admin, '' } | ForEach-Object {
        $prefix = $_.Name
        $sensors = (Get-Content $_.Group[0].FullName -Raw | ConvertFrom-Json).sensors
        $cpu = $sensors | Where-Object hardwareType -eq 'Cpu' | Select-Object -First 1 -ExpandProperty hardware
        $gpu = foreach ($type in 'GpuNvidia', 'GpuAmd', 'GpuIntel') { $sensors | Where-Object hardwareType -eq $type | Select-Object -ExpandProperty hardware }
        [pscustomobject]@{
            Name = (@($cpu) + @($gpu | Select-Object -First 1) | Where-Object { $_ }) -join ' · '
            Admin = [bool]($_.Group | Where-Object { $_.BaseName -notmatch '-no-admin' })
            NoAdmin = [bool]($_.Group | Where-Object { $_.BaseName -match '-no-admin' })
            Recordings = @($files | Where-Object { $_.BaseName.StartsWith("$prefix-", [StringComparison]::Ordinal) }).Count
        }
    }
}
