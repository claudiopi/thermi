#Requires -Version 7
<#
.SYNOPSIS
    The CI's dashboard, in GitHub-flavored Markdown: what a check's run measured, each number against the previous main's,
    with the trend over the last pushes to main. The CI's 📊 Stats dashboard job writes it into its run's summary; here it
    renders the same from the last `pwsh scripts/check.ps1` (or `-Mutation`), and prints it, or writes it to -Out.
    The history comes from the repo's `stats` branch, which only the CI writes. -Publish writes into a folder that branch's
    files with this run added: stats.json and the README's badges, as shields.io endpoints.
#>
param(
    # Where the check left what it measured.
    [string]$Results,
    # The history, a stats.json; by default the stats branch's, when git has fetched it.
    [string]$History,
    [string]$Out,
    [string]$Publish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Results) { $Results = Join-Path $root 'TestResults' }
. (Join-Path $PSScriptRoot 'stats-read.ps1')
. (Join-Path $PSScriptRoot 'stats-format.ps1')
$repo = 'https://github.com/claudiopi/thermi'
# How many of the last pushes to main the trend draws, and how many stats.json keeps.
$trend = 20
$kept = 200

$run = Read-Run $Results
$github = $run.GitHub
$entries = if ($History -and (Test-Path $History)) { @((Read-Json $History).entries) }
    elseif (-not $History) { @((git -C $root show 'origin/stats:stats.json' 2>$null | Out-String | ConvertFrom-Json -AsHashtable -ErrorAction Ignore).entries) }
$entries = @($entries | Where-Object { $_ })
$commit = if ($github) { $github.commit } else { git -C $root rev-parse --short HEAD }
$before = $entries | Where-Object { $_.commit -ne $commit } | Select-Object -Last 1
$specs = @(Read-Specs $root $run)

# This run's numbers, as the history keeps them; what it did not measure carries main's last.
function Get-Entry {
    $dotnet = $run.Tests | Where-Object { $_.half -eq 'dotnet' }
    $web = $run.Tests | Where-Object { $_.half -eq 'web' }
    $caught = Get-Sum $run.BugHunt.projects caught
    $missed = Get-Sum $run.BugHunt.projects missed
    $x64 = $run.Msi.files | Where-Object { $_.processor -eq 'x64' -and -not $_.perUser } | Select-Object -First 1
    [ordered]@{
        commit = $commit
        date = [DateTime]::UtcNow.ToString('o')
        dotnet = $dotnet.cases
        web = $web.cases
        tests = if ($dotnet -and $web) { $dotnet.cases + $web.cases }
        coreLines = $run.Coverage.'Thermi.Core'.lines
        coreBranches = $run.Coverage.'Thermi.Core'.branches
        bugsCaught = if ($caught + $missed) { 100 * $caught / ($caught + $missed) } else { $before.bugsCaught }
        doctor = (Get-DoctorScore $run).Score
        ciSeconds = if ($github) { $github.wallSeconds } else { @($run.Steps | ForEach-Object { $_.seconds } | Sort-Object)[-1] }
        msiX64 = $x64.megabytes
        passed = if ($github) { $github.passed }
    }
}

function Get-Sum([object[]]$Items, [string]$Key) {
    $sum = 0
    foreach ($item in $Items) { $sum += [double]($item[$Key] ?? 0) }
    $sum
}

$now = Get-Entry

function Write-Kpis {
    $dotnet = $now.dotnet; $web = $now.web
    $core = $run.Coverage.'Thermi.Core'
    $doctor = Get-DoctorScore $run
    $slowest = $github.jobs | Sort-Object { $_.seconds } -Descending | Select-Object -First 1
    $arm = $run.Msi.files | Where-Object { $_.processor -eq 'arm64' -and -not $_.perUser } | Select-Object -First 1
    $done = @($specs | Where-Object Status -eq 'done').Count
    $passing = @($specs.Steps | Where-Object { $_.passes }).Count
    $security = $github.results.security
    $first = @(
        ,@('🧪 Test cases', (Format-Count $now.tests), "$(Format-Count $dotnet) .NET · $(Format-Count $web) page · $(Format-Delta $now.tests $before.tests)")
        ,@('📏 Core coverage', (Format-Percent $now.coreLines),
            "branches $(Format-Percent $now.coreBranches) · floor $($core.floor ?? 90) % · $(Format-Delta $now.coreLines $before.coreLines -Decimals 1)")
        ,@('🐛 Bugs caught', (Format-Percent $now.bugsCaught -Decimals 0),
            "of the bugs planted each night · $(Format-Delta $now.bugsCaught $before.bugsCaught -Decimals 1)")
        ,@('⚛️ React Doctor', "$($now.doctor ?? '–')",
            "$(if ($doctor.Ok) { '0 issues' } elseif ($doctor) { 'issues in its log' } else { 'not run' }) · $(Format-Delta $now.doctor $before.doctor)")
        ,@('⏱️ CI time', (Format-Duration $now.ciSeconds),
            "$(if ($slowest) { "slowest: $(Get-JobLabel $slowest.name) · " })$(Format-Delta $now.ciSeconds $before.ciSeconds -Duration)")
    )
    $second = @(
        ,@('📋 Specs', "$done / $($specs.Count)", "done · $passing of $(@($specs.Steps).Count) acceptance checks pass")
        ,@('🐞 Open bugs', "$($github.openBugs ?? '–')", 'issues labeled bug')
        ,@('📦 x64 installer', (Format-Megabytes $now.msiX64),
            "Arm64 $(Format-Megabytes $arm.megabytes) · $(Format-Delta $now.msiX64 $before.msiX64 -Decimals 1 -Unit ' MB')")
        ,@('🔐 Security', $(switch ($security) { 'success' { '✅ clean' } $null { '–' } default { "❌ $security" } }), 'leaked secrets · vulnerable packages')
        ,@('🔄 Dependencies', "$($github.updates ?? '–')", 'update PRs open · Renovate opens them')
    )
    foreach ($row in $first, $second) {
        '| ' + (($row | ForEach-Object { $_[0] }) -join ' | ') + ' |'
        '| ' + (($row | ForEach-Object { ':-:' }) -join ' | ') + ' |'
        '| ' + (($row | ForEach-Object { "**$($_[1])**" }) -join ' | ') + ' |'
        '| ' + (($row | ForEach-Object { $_[2] }) -join ' | ') + ' |'
        ''
    }
}

function Write-Coverage {
    '## 📏 Coverage by project'
    ''
    '| Project | Lines | Branches | | Measured by |'
    '| --- | --: | --: | --- | --- |'
    foreach ($name in 'Thermi.Core', 'Thermi.Cli') {
        $rates = $run.Coverage.$name
        $floor = if ($rates) { " · floor $($rates.floor) %" }
        Format-CoverageRow $name $rates.lines $rates.branches "Thermi.Tests$floor"
    }
    foreach ($name in 'thermi', 'thermi-sensors', 'Thermi.App') {
        $module = $run.App[$name]
        $lines = if ($module) { Get-Rate $module.Lines }
        $branches = if ($module) { Get-Rate $module.Branches }
        Format-CoverageRow $name $lines $branches 'Thermi.Windows.Tests · shown, not gated'
    }
    ''
    '| Tests | Cases |'
    '| --- | --: |'
    foreach ($half in $run.Tests) {
        foreach ($runner in $half.runners.Keys) {
            $name = if ($half.half -eq 'dotnet') { "``$runner``" } else { "Settings page · $runner" }
            "| $name | $(Format-Count $half.runners[$runner]) |"
        }
    }
    $baseline = Get-Sum $run.Tests baseline
    "| **Total** | **$(Format-Count $now.tests)** of the ratchet's $(Format-Count $baseline) |"
    ''
}

function Write-Specs {
    '## 📋 Specs'
    ''
    '| Spec | Status | Checks | Tests / budget |'
    '| --- | --- | --: | --- |'
    foreach ($spec in $specs) {
        $passes = @($spec.Steps | Where-Object { $_.passes }).Count
        $budgets = foreach ($each in $spec.Budgets) {
            $half = if ($each.Half -eq 'dotnet') { '.NET' } else { 'page' }
            "$(Get-Bar (100 * $each.Cases / $each.Budget) 6) $($each.Cases) / $($each.Budget) $half"
        }
        $budgetsText = if ($budgets) { $budgets -join ' · ' } else { '–' }
        "| $($spec.Id) · $($spec.Title) | $(Format-Status $spec.Status) | ✓ $passes / $($spec.Steps.Count) | $budgetsText |"
    }
    ''
}

function Write-BugHunt {
    '## 🐛 Bug hunt'
    ''
    '> A tool plants thousands of small bugs, one at a time, and measures the share the tests catch: a `<` turned into `<=`,'
    '> a line removed. **Caught** is the share some test notices; **missed** are the ones none did. Coverage says which lines'
    '> the tests run; this says whether they would notice those lines going wrong. It runs each night.'
    ''
    if (-not $run.BugHunt) {
        "No Bug hunt has run yet: [run it]($repo/actions/workflows/mutation.yml) by hand, or wait for the night."
        ''
        return
    }
    '| Project | Caught | | Missed |'
    '| --- | --: | --- | --: |'
    foreach ($project in $run.BugHunt.projects) {
        $score = 100 * $project.caught / [Math]::Max(1, $project.caught + $project.missed)
        $break = ($run.BugHunt | Where-Object { $_.projects -contains $project }).break
        $bar = "$(Get-Dot $score $break ($break - 10)) $(Get-Bar $score)"
        "| ``$($project.name)`` | $(Format-Percent $score -Decimals 0) | $bar | $(Format-Count $project.missed) |"
    }
    $at = ($run.BugHunt | ForEach-Object { [DateTime]$_.at } | Sort-Object | Select-Object -First 1)
    $source = if ($github.bugHuntUrl) { " · [its run]($($github.bugHuntUrl))" }
    ''
    "_Run of $($at.ToString('yyyy-MM-dd HH:mm')) UTC$source · it fails below $(@($run.BugHunt | ForEach-Object { $_.break } | Sort-Object)[0]) %._"
    ''
}

function Write-Trend {
    $last = @($entries | Where-Object { $_.commit -ne $commit } | Select-Object -Last ($trend - 1)) + $now
    "## 📈 Trend · up to the last $trend pushes to ``main``, this run last"
    ''
    '| Metric | Trend | Now |'
    '| --- | --- | --: |'
    $metrics = @(
        ,@('Test cases', 'tests', { Format-Count $args[0] })
        ,@('Core coverage', 'coreLines', { Format-Percent $args[0] })
        ,@('Bugs caught', 'bugsCaught', { Format-Percent $args[0] -Decimals 0 })
        ,@('React Doctor', 'doctor', { "$($args[0] ?? '–')" })
        ,@('CI time', 'ciSeconds', { Format-Duration $args[0] })
        ,@('x64 installer', 'msiX64', { Format-Megabytes $args[0] })
    )
    foreach ($metric in $metrics) {
        $values = @($last | ForEach-Object { $_[$metric[1]] })
        "| $($metric[0]) | ``$(Get-Spark $values)`` | $(& $metric[2] $now[$metric[1]]) |"
    }
    ''
}

# The CI's jobs by how long each ran, or here the check's steps.
# The CI's jobs by how long each ran, with how each ended, then the slowest steps of the check's jobs; here the check's
# steps alone.
function Write-Time {
    '## ⏱️ Where the time goes'
    ''
    $steps = @($run.Steps | ForEach-Object { $job = $_.title; $_.steps | ForEach-Object { $_ + @{ Job = $job } } } | Where-Object { $_.Seconds -gt 0 })
    $icons = @{ success = '✅'; failure = '❌'; cancelled = '🚫'; skipped = '⏭️'; ok = '✅'; fail = '❌'; info = '📊' }
    $timed = if ($github.jobs) { $github.jobs | ForEach-Object { @{ Name = Get-JobLabel $_.name; Seconds = $_.seconds; Result = $_.result } } }
        else { $steps | ForEach-Object { @{ Name = $_.Name; Seconds = $_.Seconds; Result = $_.Status } } }
    if (-not $timed) { 'Nothing was timed.'; ''; return }
    Write-Timed $timed $(if ($github.jobs) { 'Job' } else { 'Step' }) $icons
    if (-not $github.jobs -or -not $steps) { return }
    '| Slowest steps | Job | Time |'
    '| --- | --- | --: |'
    foreach ($step in $steps | Sort-Object { $_.Seconds } -Descending | Select-Object -First 8) {
        "| $($icons[$step.Status]) $($step.Name) | $($step.Job) | $(Format-Duration $step.Seconds) |"
    }
    ''
}

function Write-Timed([object[]]$Timed, [string]$What, [hashtable]$Icons) {
    $longest = @($Timed | ForEach-Object { $_.Seconds } | Sort-Object)[-1]
    "| | $What | | Time |"
    '| :-: | --- | --- | --: |'
    foreach ($each in $Timed | Sort-Object { $_.Seconds } -Descending) {
        "| $($Icons[$each.Result]) | $($each.Name) | $(Get-Bar (100 * $each.Seconds / [Math]::Max(1, $longest)) 20) | $(Format-Duration $each.Seconds) |"
    }
    ''
}

# What only a person can see, which the tests leave to them before each release, with the requirements that name it.
function Write-ReleaseChecks {
    '## 🧰 Release checks · by a person, before each release'
    ''
    $checks = @(Read-ReleaseChecks $root)
    if (-not $checks) { "``docs/RELEASES.md`` lists none yet."; return }
    '| Check | Area | Requirements it covers |'
    '| --- | --- | --- |'
    foreach ($check in $checks) {
        $covers = foreach ($spec in $specs | Where-Object { $_.ReleaseChecks.ContainsKey($check.Name) }) {
            "$($spec.Id) $(($spec.ReleaseChecks[$check.Name] | Select-Object -Unique) -join ', ')"
        }
        "| $($check.Name) | $($check.Area) | $(if ($covers) { $covers -join ' · ' } else { '–' }) |"
    }
    ''
    "_``docs/RELEASES.md`` says how to run each on the release's installed build._"
}

function Write-Rest {
    '## ⚛️ React Doctor'
    ''
    $doctor = Get-DoctorScore $run
    if ($doctor) {
        '| Project | Score | | Issues |'
        '| --- | --: | --- | --: |'
        $issues = if ($doctor.Ok) { 0 } else { 'see the log' }
        "| ``Thermi.Settings.Web`` | $(Get-Dot $doctor.Score 100 90) **$($doctor.Score)**/100 | $(Get-Bar $doctor.Score) | $issues |"
    }
    else { "The page's half did not run." }
    ''
    Write-ReleaseChecks
    ''
    '## 🖥️ Tested on'
    ''
    '| PC | Recorded | Recordings the tests replay |'
    '| --- | --- | --: |'
    foreach ($pc in Read-RecordedPcs $root) {
        $rights = @(if ($pc.Admin) { 'as administrator' }) + @(if ($pc.NoAdmin) { 'without admin rights' })
        "| $($pc.Name) | $($rights -join ' and ') | $($pc.Recordings) |"
    }
    "| [Report yours →]($repo/issues/new?template=hardware-report.yml) | a ``thermi-sensors --dump`` and a screenshot | |"
    ''
}

$compared = if ($before) { "compared with the previous ``main`` (``$($before.commit)``)" } else { 'no earlier `main` to compare with yet' }
$where = if ($github.runUrl) { "[run]($($github.runUrl))" } else { 'this PC' }
$markdown = @(
    '# 🌡️ thermi · CI dashboard'
    ''
    "> commit ``$commit`` · $where · $compared"
    ''
    '## 🎯 At a glance'
    ''
    Write-Kpis
    Write-Coverage
    Write-Specs
    Write-BugHunt
    Write-Trend
    Write-Time
    Write-Rest
    "_🤖 Run it on your PC with ``pwsh scripts/stats.ps1`` after a check · the history lives in the ``stats`` branch, which feeds the README's badges._"
) -join "`n"

if ($Out) { Set-Content $Out $markdown -Encoding utf8 } else { $markdown }

if ($Publish) {
    $latest = [ordered]@{}
    foreach ($key in $now.Keys) { $latest[$key] = $now[$key] ?? $before.$key }
    $written = @($entries | Where-Object { $_.commit -ne $commit } | Select-Object -Last ($kept - 1)) + $latest
    New-Item -ItemType Directory -Force (Join-Path $Publish 'badges') | Out-Null
    @{ entries = $written } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Publish 'stats.json') -Encoding utf8
    $license = if ((Get-Content (Join-Path $root 'LICENSE') -TotalCount 1) -match '^(\S+) License') { $Matches[1] } else { 'see LICENSE' }
    $passing = $latest.passed -ne $false
    $caught = "$(Format-Percent $latest.bugsCaught -Decimals 0) caught"
    $badges = @{
        ci = @{ label = 'CI'; message = $(if ($passing) { 'passing' } else { 'failing' }); color = $(if ($passing) { 'brightgreen' } else { 'red' }) }
        coverage = @{ label = 'coverage'; message = (Format-Percent $latest.coreLines -Decimals 0); color = (Get-BadgeColor $latest.coreLines 90 80) }
        bughunt = @{ label = 'Bug hunt'; message = $caught; color = (Get-BadgeColor $latest.bugsCaught 78 70) }
        tests = @{ label = 'tests'; message = (Format-Count $latest.tests); color = 'blue' }
        'react-doctor' = @{ label = 'React Doctor'; message = "$($latest.doctor ?? '–')"; color = (Get-BadgeColor $latest.doctor 100 90) }
        license = @{ label = 'license'; message = $license; color = 'blue' }
    }
    foreach ($name in $badges.Keys) {
        @{ schemaVersion = 1 } + $badges[$name] | ConvertTo-Json | Set-Content (Join-Path $Publish 'badges' "$name.json") -Encoding utf8
    }
}
