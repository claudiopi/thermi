# Dot-sourced by scripts/stats.ps1: what a check's run measured, from the files its steps saved under TestResults/stats
# and the app's coverage reports, and what the repo itself says: the specs, their checks and its code.
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
        [pscustomobject]@{
            Id = $folder.Name.Substring(0, 3); Title = $title; Status = $status; Steps = $steps; Budgets = @($budgets)
        }
    }
}

# The code the repo holds, from git's own list, so nothing built, installed or recorded counts: each file's lines of code,
# its project (its folder under src or tests, or scripts), and whether it is a test, as the tests' projects and the
# page's tests and unit tests are. What a build writes from Core, as the page's .g.ts, is left out.
function Read-Code([string]$Root) {
    $languages = @{ '.cs' = 'C#'; '.ts' = 'TypeScript'; '.tsx' = 'TypeScript'; '.ps1' = 'PowerShell'; '.js' = 'JS'; '.mjs' = 'JS'; '.cjs' = 'JS' }
    foreach ($path in git -C $Root ls-files) {
        $language = $languages[[IO.Path]::GetExtension($path)]
        if (-not $language -or $path -like '*.g.ts') { continue }
        $parts = $path -split '/'
        $test = $parts[0] -eq 'tests' -or $path -match '^src/[^/]+/tests/|\.test\.tsx?$'
        $markers = if ($language -eq 'PowerShell') { '#', '<#', '#>' } else { '//', '/*', '*/' }
        $lines = Measure-CodeLines ([IO.File]::ReadAllLines((Join-Path $Root $path))) @markers
        [pscustomobject]@{ Project = $(if ($parts[0] -eq 'scripts') { 'scripts' } else { $parts[1] }); Test = $test; Language = $language; Lines = $lines }
    }
}

# Lines of code as cloc counts them: those that hold something besides blanks and comments, a line's comment from its
# marker on and a block's from its opening to its closing, unless it opens after code and runs past the line, as a glob's
# /* does. A marker inside a string literal may still be miscounted.
function Measure-CodeLines([string[]]$Lines, [string]$Line, [string]$Open, [string]$Close) {
    $count = 0
    $inBlock = $false
    foreach ($text in $Lines) {
        $code = ''
        while ($text) {
            if ($inBlock) {
                $end = $text.IndexOf($Close, [StringComparison]::Ordinal)
                if ($end -lt 0) { break }
                $text = $text.Substring($end + $Close.Length)
                $inBlock = $false
                continue
            }
            $start = $text.IndexOf($Open, [StringComparison]::Ordinal)
            $comment = $text.IndexOf($Line, [StringComparison]::Ordinal)
            if ($comment -ge 0 -and ($start -lt 0 -or $comment -lt $start)) { $code += $text.Substring(0, $comment); break }
            $after = $start + $Open.Length
            if ($start -lt 0 -or (($code + $text.Substring(0, $start)).Trim() -and $text.IndexOf($Close, $after, [StringComparison]::Ordinal) -lt 0)) {
                $code += $text
                break
            }
            $code += $text.Substring(0, $start)
            $text = $text.Substring($after)
            $inBlock = $true
        }
        if ($code.Trim()) { $count++ }
    }
    $count
}
