#Requires -Version 7
<#
.SYNOPSIS
    The test-case ratchet: counts every case the runners would run, each row of a [Theory] (inline or member data), each
    Playwright test a loop or a describe makes and each Vitest case of a table, and fails when a half's count is above its
    baseline in tests/test-count.json, or when this change raised that baseline along with anything else.
    Rows count as cases so that splitting a test that bundles many checks into rows never looks like growth, and bundling
    them never looks like thrift. A new behavior takes the cheapest test that proves it, and a test an edge test now
    covers goes; so the count stays flat while the code grows.
    It also holds each spec to the budget of cases its spec.md declares, counting the cases of the tests its acceptance
    steps name, and each table to a cap of rows, past which its table says why.
    The .NET half lists the built test projects, so it runs after the build; the page's half lists with its installed runners.
    -Raise writes what it counted into tests/test-count.json and commits that file alone, the raise's change of its own;
    -Spec <folder> writes what a spec's tests hold into its budget, for the person who approved the spec to agree to.
#>
param(
    [ValidateSet('All', 'DotNet', 'Web')]
    [string]$Part = 'All',
    [string]$Configuration = 'Release',
    # Where the build put its output, when it was given --artifacts-path.
    [string]$ArtifactsPath,
    [switch]$Raise,
    [string]$Spec
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$baselineFile = 'tests/test-count.json'
$web = Join-Path $root 'src' 'Thermi.Settings.Web'
. (Join-Path $PSScriptRoot 'rows.ps1')
. (Join-Path $PSScriptRoot 'sources.ps1')
. (Join-Path $PSScriptRoot 'summary.ps1')
# The most rows a table holds without saying why: the 95th percentile of the tables once their redundant rows were cut.
$rowCap = 17

function Get-HalfName([string]$Half) { @{ dotnet = '.NET'; web = 'page' }[$Half] }

# xUnit's own listing, with each theory's rows enumerated as the runner finds them: per case, the test it is a row of.
function Get-DotNetCases {
    foreach ($project in Get-ChildItem (Join-Path $root 'tests') -Directory | Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") }) {
        $exe = if ($ArtifactsPath) { Join-Path $ArtifactsPath 'bin' $project.Name $Configuration.ToLowerInvariant() "$($project.Name).exe" }
            else {
                Join-Path $project.FullName 'bin' $Configuration '*' "$($project.Name).exe" |
                    Resolve-Path -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Path
            }
        if (-not $exe -or -not (Test-Path $exe)) { throw "$($project.Name) is not built: build $Configuration first." }
        $listed = & $exe -list full/json -preEnumerateTheories | Out-String
        if ($LASTEXITCODE -ne 0) { throw "$exe could not list its tests." }
        foreach ($case in @($listed | ConvertFrom-Json)) {
            [pscustomobject]@{ Names = @($case.Method); Table = "$($case.Class).$($case.Method)"; Folder = $project.FullName; Line = 0; Runner = $project.Name }
        }
    }
}

function Get-PlaywrightCases($Suites, [string[]]$Titles) {
    foreach ($suite in $Suites) {
        $names = @($Titles) + $suite.title
        foreach ($spec in $suite.specs) {
            foreach ($test in $spec.tests) {
                $table = "$($spec.file):$($spec.line)"
                [pscustomobject]@{ Names = $names + $spec.title; Table = $table; Folder = (Join-Path $web 'tests'); Line = $spec.line; Runner = 'Playwright' }
            }
        }
        Get-PlaywrightCases $suite.suites $names
    }
}

# Playwright's and Vitest's listings, as each runner expands its loops and tables; a fixme or a skip is still a case.
# A Vitest case is named by no acceptance step, which names Playwright's tests, so it counts in no spec's budget.
function Get-WebCases {
    Push-Location $web
    try {
        $playwright = npx --no-install playwright test --list --reporter=json | Out-String | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or $playwright.errors) { throw "Playwright could not list its tests: $($playwright.errors.message)" }
        # Vitest's listing names a table once, so its cases are counted from a run, which takes a second.
        $report = Join-Path $root 'TestResults' 'vitest-count.json'
        npx --no-install vitest run --dir src --reporter=json --silent --includeTaskLocation "--outputFile.json=$report" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Vitest could not run its tests.' }
        $vitest = Get-Content $report -Raw | ConvertFrom-Json
    }
    finally {
        Pop-Location
    }
    Get-PlaywrightCases $playwright.suites @()
    foreach ($file in $vitest.testResults) {
        foreach ($case in $file.assertionResults) {
            [pscustomobject]@{ Names = @(); Table = "$($file.name):$($case.location.line)"; Folder = ''; Line = $case.location.line; Runner = 'Vitest' }
        }
    }
}

# Where a table's case starts, as the lines of its file and the line to look from: a .NET test's [Theory], a page's title.
function Test-TableSaysWhy($Case) {
    if ($Case.Line -eq 0) {
        $class, $method = $Case.Table -split '\.(?=[^.]+$)'
        $class = $class -replace '^.*\.', ''
        $test = "\[Theory\b[^\n]*\n((?!\s*public\s)[^\n]*\n)*?\s*public\s+(async\s+)?(void|Task)\s+$method\s*\("
        foreach ($file in Get-SourceFiles $Case.Folder) {
            $source = (Get-Content $file.FullName -Raw) -replace "`r", ''
            if ($source -notmatch "\bclass\s+$class\b" -or $source -notmatch $test) { continue }
            $at = ($source.Substring(0, $source.IndexOf($Matches[0], [StringComparison]::Ordinal)) -split "`n").Count - 1
            if (Test-RowsSayWhy ($source -split "`n") $at) { return $true }
        }
        return $false
    }
    $path = $Case.Table -replace ':\d+$', ''
    Test-RowsSayWhy (Get-Content $(if ([IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $Case.Folder $path })) ($Case.Line - 1)
}

# Each spec's budget, from its spec.md, and the tests its acceptance steps name; a dropped spec has none.
function Get-Specs {
    foreach ($folder in Get-ChildItem (Join-Path $root 'docs' 'specs') -Directory) {
        $spec = Get-Content (Join-Path $folder.FullName 'spec.md') -Raw
        if ($spec -match '\*\*Status\*\*: dropped') { continue }
        $budget = if ($spec -match '(?m)^\*\*Budget\*\*: (?<dotnet>\d+) \.NET and (?<web>\d+) page cases') {
            @{ dotnet = [int]$Matches.dotnet; web = [int]$Matches.web }
        }
        $steps = (Get-Content (Join-Path $folder.FullName 'acceptance.json') -Raw | ConvertFrom-Json).steps
        [pscustomobject]@{ Name = $folder.Name; Budget = $budget; Tests = @($steps.check | Sort-Object -Unique) }
    }
}

function Get-SpecCount($Spec, $Cases) { @($Cases | Where-Object { $_.Names | Where-Object { $Spec.Tests -contains $_ } }).Count }

# Committed alone so the raise is a change of its own, as Get-RaiseFaults wants; staged work would stay out of the commit
# while it looked committed, so it refuses.
function Save-Raise([string]$Root, [hashtable]$Counts, [string]$File = 'tests/test-count.json') {
    $staged = @(git -C $Root diff --cached --name-only | Where-Object { $_ -ne $File })
    if ($staged) { return "Other work is staged ($($staged -join ', ')): commit it first, or unstage it with git restore --staged, then raise again." }
    $path = Join-Path $Root $File
    $held = [ordered]@{}
    (Get-Content $path -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $held[$_.Name] = $_.Value }
    if (-not @($Counts.Keys | Where-Object { $held[$_] -ne $Counts[$_] })) { return }
    foreach ($half in $Counts.Keys) { $held[$half] = $Counts[$half] }
    $held | ConvertTo-Json | Set-Content $path
    $counted = @($held.Keys | ForEach-Object { "$($held[$_]) $(Get-HalfName $_)" }) -join ' and '
    git -C $Root commit -q -m "test: the ratchet holds $counted cases" -- $File
    if ($LASTEXITCODE -ne 0) { return "git could not commit $File." }
}

# Only the halves counted; the other half's budget stays as it was.
function Set-Budget([string]$Name, $Cases) {
    $found = Get-Specs | Where-Object Name -eq $Name
    if (-not $found.Budget) { throw "docs/specs/$Name is no spec with a budget line: name its folder, as 002-settings-window." }
    foreach ($half in $Cases.Keys) { $found.Budget[$half] = Get-SpecCount $found $Cases[$half] }
    $file = Join-Path $root 'docs' 'specs' $Name 'spec.md'
    $line = "**Budget**: $($found.Budget.dotnet) .NET and $($found.Budget.web) page cases"
    Set-Content $file ((Get-Content $file -Raw) -replace '(?m)^\*\*Budget\*\*: \d+ \.NET and \d+ page cases', $line) -NoNewline
}

$cases = [ordered]@{}
if ($Part -ne 'Web') { $cases.dotnet = @(Get-DotNetCases) }
if ($Part -ne 'DotNet') { $cases.web = @(Get-WebCases) }
if ($Raise) {
    $counts = @{}
    foreach ($half in $cases.Keys) { $counts[$half] = $cases[$half].Count }
    $refused = Save-Raise $root $counts $baselineFile
    if ($refused) {
        Write-Host $refused
        exit 1
    }
}
if ($Spec) { Set-Budget $Spec $cases }
$baseline = Get-Content (Join-Path $root $baselineFile) -Raw | ConvertFrom-Json
$failed = @()

foreach ($half in $cases.Keys) {
    if ($cases[$half].Count -gt $baseline.$half) {
        $failed += "$($cases[$half].Count) $half test cases, above the baseline of $($baseline.$half)."
    }
}
if ($failed) {
    $failed += 'Merge near-identical cases into one table, and delete tests an edge test already covers.'
    $failed += ("To add tests beyond it, raise it in $baselineFile in a change of its own, which a person approves: commit your work, " +
        'then pwsh scripts/test-count.ps1 -Raise makes that change; an agent runs it only when a person asks.')
}

$notes = @()
# A line starting "summary: " goes into the CI's summary, as scripts/check-steps.ps1 picks it out.
$measured = @($cases.Keys | ForEach-Object { "summary: $($cases[$_].Count) $(Get-HalfName $_) cases of the ratchet's $($baseline.$_)" })
$specCases = @{}
foreach ($budgeted in Get-Specs) {
    if (-not $budgeted.Budget) {
        $failed += "$($budgeted.Name)'s spec.md declares no budget: a line '**Budget**: <n> .NET and <n> page cases' under its status."
        continue
    }
    foreach ($half in $cases.Keys) {
        $count = Get-SpecCount $budgeted $cases[$half]
        $budget = $budgeted.Budget.$half
        $specCases["$($budgeted.Name)|$half"] = @{ cases = $count; budget = $budget }
        if ($budget) { $measured += "summary: ``$($budgeted.Name)``: $count of its budget of $budget" }
        if ($count -gt $budget) {
            $failed += ("$($budgeted.Name)'s tests have $count $(Get-HalfName $half) cases, over the budget of $budget its spec.md declares: " +
                "merge rows and cut what another case proves, or ask the person who approved the spec to raise it: -Spec $($budgeted.Name).")
        }
        elseif ($count -lt $budget) {
            $notes += "$($budgeted.Name)'s tests have $count $(Get-HalfName $half) cases, under its budget of ${budget}: lower it with -Spec $($budgeted.Name)."
        }
    }
}

foreach ($half in $cases.Keys) {
    foreach ($table in $cases[$half] | Group-Object Table | Where-Object Count -GT $rowCap) {
        if (-not (Test-TableSaysWhy $table.Group[0])) {
            $failed += ("$($table.Name) has $($table.Count) rows, more than the $rowCap a table holds: merge or cut rows, or say why " +
                "each is a behavior of its own in a '// Rows: ...' comment on its [Theory] line, or on or above the line that opens its loop.")
        }
    }
}

# The change is what the working tree changed since the last commit, or, with nothing changed, that commit: a change
# that raises the baseline raises it alone, so a raise inside a commit of other work fails. Its baseline is compared with
# the one before it, the last commit's or its first parent's; in a pull request's run, GitHub's merge of it into its base
# branch, that is the base branch's. A merge, made or under way, brings in its other side's work along with that side's
# raise, so the raise passes when each commit there that raised it raised it alone; a merge that holds more than any of
# its parents raised it itself, and fails, on either side. GitHub's squash of a pull request is all its commits in one,
# which that pull request's own run checked one by one, so it passes. With no baseline before, no raise could be seen, so
# that fails too: main holds the baseline before any branch raises it.
function Get-RaiseFaults([string]$Root, $Baseline, [string[]]$Halves, [string]$File = 'tests/test-count.json') {
    $changed = @(git -C $Root status --porcelain 2>$null)
    $from = if ($changed) { 'HEAD' } elseif (git -C $Root rev-parse --verify --quiet 'HEAD~1' 2>$null) { 'HEAD~1' }
    if (-not $from) { return 'git has no commit before this one to compare the baseline with: fetch the whole history (fetch-depth: 0).' }
    $before = Read-Baseline $Root $from $File
    if (-not $before) {
        return "$from has no $File, so a raise cannot be seen: commit the baseline to main in a change of its own before a branch raises it."
    }
    $raised = @($Halves | Where-Object { $before.$_ -lt $Baseline.$_ })
    if (-not $raised) { return }
    $sides = if ($changed) { @(git -C $Root rev-parse --verify --quiet MERGE_HEAD 2>$null) } else { @(git -C $Root rev-parse 'HEAD^@' | Select-Object -Skip 1) }
    if (-not $sides -and -not $changed -and (Test-Squash $Root)) { return }
    if (-not $sides) {
        $others = @(git -C $Root diff --name-only $from) + @(git -C $Root ls-files --others --exclude-standard) | Where-Object { $_ -ne $File }
        if ($others) {
            return ("This change raises the baseline of $($raised -join ' and ') in $File along with other files: " +
                ('raise it in a change of its own, which a person approves, and which pwsh scripts/test-count.ps1 -Raise makes; ' +
                'an agent runs it only when a person asks.')
        }
        return
    }
    if ((git -C $Root rev-parse --is-shallow-repository) -eq 'true') {
        return "This merge raises the baseline in $File, and a shallow clone cannot show where: fetch the whole history (fetch-depth: 0)."
    }
    foreach ($half in $raised) {
        $side = $sides | Where-Object { (Read-Baseline $Root $_ $File).$half -eq $Baseline.$half } | Select-Object -First 1
        if (-not $side) { "This merge raises the baseline of $half in $File above all its parents': raise it in a change of its own, which a person approves." }
        else { Get-SideRaiseFaults $Root $side $from $half $File }
    }
}

function Read-Baseline([string]$Root, [string]$Commit, [string]$File) {
    git -C $Root show "${Commit}:$File" 2>$null | ConvertFrom-Json
}

# The last commit is GitHub's own, squashing a pull request, whose subject it ends with the pull request's number.
function Test-Squash([string]$Root) {
    $committer, $subject = (git -C $Root log -1 --format='%ce%n%s' HEAD) -split "`n"
    $committer -eq 'noreply@github.com' -and $subject -match '\(#\d+\)$'
}

# The commits a merge brings in from $Side that raise $Half's baseline above all their parents', each of which must touch
# nothing else, and none of which may be a merge.
function Get-SideRaiseFaults([string]$Root, [string]$Side, [string]$From, [string]$Half, [string]$File) {
    $raises = @(git -C $Root rev-list $Side "^$From" -- $File | Where-Object {
            $before = @(git -C $Root rev-parse "$_^@" | ForEach-Object { [int](Read-Baseline $Root $_ $File).$Half } | Sort-Object)[-1]
            $before -lt (Read-Baseline $Root $_ $File).$Half
        })
    if (-not $raises) {
        return "This merge raises the baseline of $Half in $File, and no commit it brings in raised it alone: raise it in a change of its own."
    }
    foreach ($commit in $raises) {
        if (@(git -C $Root rev-parse "$commit^@").Count -gt 1) {
            "$(git -C $Root log -1 --format='%h "%s"' $commit), which this merge brings in, is a merge that raises the baseline of $Half in $File " +
                "above all its parents': raise it in a change of its own, which a person approves."
        }
        elseif (git -C $Root diff-tree --no-commit-id --name-only -r $commit | Where-Object { $_ -ne $File }) {
            "$(git -C $Root log -1 --format='%h "%s"' $commit), which this merge brings in, raises the baseline of $Half in $File along with other files: " +
                'raise it in a change of its own, which a person approves.'
        }
    }
}

$failed += @(Get-RaiseFaults $root $baseline @($cases.Keys))
$measured | ForEach-Object { Write-Host $_ }
foreach ($half in $cases.Keys) {
    $runners = [ordered]@{}
    $cases[$half] | Group-Object Runner | ForEach-Object { $runners[$_.Name] = $_.Count }
    $specs = [ordered]@{}
    $specCases.Keys | Where-Object { $_.EndsWith("|$half") } | Sort-Object | ForEach-Object { $specs[$_.Split('|')[0]] = $specCases[$_] }
    Save-Stats "tests-$half" @{ cases = $cases[$half].Count; baseline = $baseline.$half; runners = $runners; specs = $specs }
}

if ($failed) {
    $failed | ForEach-Object { Write-Host $_ }
    exit 1
}
foreach ($half in $cases.Keys) {
    if ($cases[$half].Count -lt $baseline.$half) {
        $notes += ("$($cases[$half].Count) $half test cases, below the baseline of $($baseline.$half): " +
            "lower it in $baselineFile so the ratchet holds what was won, as pwsh scripts/test-count.ps1 -Raise does.")
    }
}
$notes | ForEach-Object { Write-Host "note: $_" }
exit 0
