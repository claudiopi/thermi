# Dot-sourced by scripts/check.ps1: how a step runs and reports, alone or beside others, and the coverage it shows.
. (Join-Path $PSScriptRoot 'sources.ps1')
. (Join-Path $PSScriptRoot 'summary.ps1')
. (Join-Path $PSScriptRoot 'coverage.ps1')

# What a step's output measured, for the dashboard and a failed job's summary: the lines a script of the check marks for it,
# and react-doctor's score.
$measuredLine = '^(summary: |Score: \d+ / 100)'

# The Windows tests' collection whose test spins every core: it runs after the rest, which would only measure its load.
$underLoad = 'FullLoadTests'

$oneTest = 'Run one test while you fix it: pwsh scripts/test.ps1 --project tests/{0} --filter-method "*<name>*"'
$stepFixes = @{
    'restore' = ('A lock file out of date: dotnet restore thermi.slnx --force-evaluate, and src/Thermi.Installer alike, then commit them. ' +
        'A vulnerable package: update it in Directory.Packages.props.')
    'build Release' = 'Fix what each error names; a rule is turned off only in Thermi.globalconfig, with the reason above it.'
    'tests and coverage' = $oneTest -f 'Thermi.Tests'
    'Windows tests' = $oneTest -f 'Thermi.Windows.Tests'
    'Windows tests alone' = $oneTest -f 'Thermi.Windows.Tests'
    'Windows tests under load' = $oneTest -f 'Thermi.Windows.Tests'
    'web typecheck and build' = ("A type error in src/Thermi.Settings.Web/src/bridge's .g.ts files comes from Core: build thermi.slnx, " +
        'which writes them again; never edit them by hand.')
    'web lint' = 'Fix what ESLint can with: npm run lint --prefix src/Thermi.Settings.Web -- --fix'
    'web page tests' = 'Run one test while you fix it: pwsh scripts/test.ps1 -Web tests/<file>.spec.ts -g "<title>"'
}

function Invoke-Step([string]$Name, [scriptblock]$Command, [scriptblock]$Explain) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $output = & $Command 2>&1 | ForEach-Object { "$_" }
    if (-not (Write-Step $Name $LASTEXITCODE $output $watch.Elapsed.TotalSeconds $Explain)) { exit 1 }
}

function New-Step([string]$Name, [string[]]$Run, [scriptblock]$Explain) { [pscustomobject]@{ Name = $Name; Run = $Run; Explain = $Explain } }

# Steps that need nothing of each other, at once: each starts its program from a thread of its own, and prints its line as
# it ends. The check stops once they all have, if any failed.
function Invoke-Together([object[]]$Steps) {
    $Steps = @($Steps | Where-Object { $_ })
    if (-not $Steps) { return }
    $jobs = foreach ($step in $Steps) {
        Start-ThreadJob -Name $step.Name -ThrottleLimit 16 -ArgumentList $root, $step.Run -ScriptBlock {
            param([string]$Root, [string[]]$Run)
            Set-Location $Root
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $program, $arguments = $Run
            $output = & $program @arguments 2>&1 | ForEach-Object { "$_" }
            @{ Code = $LASTEXITCODE; Output = @($output); Seconds = $watch.Elapsed.TotalSeconds }
        }
    }
    $failed = $false
    $pending = [Collections.Generic.List[object]]::new([object[]]$jobs)
    while ($pending.Count) {
        $done = Wait-Job -Job $pending.ToArray() -Any
        [void]$pending.Remove($done)
        $result = Receive-Job $done -Wait -AutoRemoveJob
        $step = $Steps | Where-Object Name -eq $done.Name
        if (-not (Write-Step $step.Name $result.Code $result.Output $result.Seconds $step.Explain)) { $failed = $true }
    }
    if ($failed) { exit 1 }
}

function Write-Step([string]$Name, [int]$Code, [string[]]$Output, [double]$Seconds, [scriptblock]$Explain) {
    $measured = $Output | ForEach-Object { $_ -replace '\e\[[\d;]*m', '' } | Where-Object { $_ -match $measuredLine } |
        ForEach-Object { $_ -replace '^summary: ', '' }
    Add-SummaryRow $Name $(if ($Code -eq 0) { 'ok' } else { 'fail' }) $Seconds $measured
    if ($Code -eq 0) {
        Write-Host ('ok   {0} ({1:n0}s)' -f $Name, $Seconds)
        $Output | Where-Object { $_ -like 'note: *' } | ForEach-Object { Write-Host "     $_" }
        return $true
    }
    Write-Host "FAIL $Name"
    Show-Errors $Output
    if ($Explain) { & $Explain }
    elseif ($stepFixes[$Name]) { Write-Host "  $($stepFixes[$Name])" }
    $false
}

# Build errors, and each failed test with its assertion message (no stack traces).
# The same error repeated on many lines of a file is shown once, with a count.
function Show-Errors([string[]]$Output) {
    $inFailure = $false
    $errors = foreach ($line in $Output) {
        if ($line -match '^failed ') { $inFailure = $true; $line; continue }
        if ($line -notmatch '^\s') { $inFailure = $false }
        if (($inFailure -and $line -match '^\s{2}\S' -and $line -notmatch '^\s+from ') -or $line -match ' error ') { $line }
    }
    if (-not $errors) {
        $Output | Select-Object -Last 40 | ForEach-Object { Write-Host "  $_" }
        return
    }
    $seen = [ordered]@{}
    foreach ($line in $errors) {
        $line = $line.Replace("$root\", '') -replace ' \[[^\]]*\.csproj\]$', ''
        $key = $line -replace '\(\d+,\d+\)', ''
        if ($seen.Contains($key)) { $seen[$key].Count++ } else { $seen[$key] = @{ First = $line; Count = 1 } }
    }
    $seen.Values | Select-Object -First 20 | ForEach-Object {
        $more = if ($_.Count -gt 1) { " (+$($_.Count - 1) more like it)" } else { '' }
        Write-Host "  $($_.First)$more"
    }
}

# The classes of the Windows tests' collections that run one test at a time, as their tests count the whole process's
# handles or windows: those run in a process of their own, beside the rest, and the one under load after both.
function Get-AloneClasses {
    $texts = Get-SourceFiles (Join-Path $root 'tests' 'Thermi.Windows.Tests') | ForEach-Object { Get-Content $_.FullName -Raw }
    $alone = $texts | ForEach-Object { [regex]::Matches($_, 'CollectionDefinition\(nameof\((\w+)\), DisableParallelization = true\)') } |
        ForEach-Object { $_.Groups[1].Value }
    $classes = foreach ($text in $texts) {
        $namespace = [regex]::Match($text, '(?m)^namespace ([\w.]+);').Groups[1].Value
        $declared = '\[Collection\(nameof\((\w+)\)\)\]\s*(?:\[[^\]]*\]\s*)*(?:public |internal )?(?:sealed )?(?:partial )?class (\w+)'
        foreach ($m in [regex]::Matches($text, $declared)) {
            if ($m.Groups[1].Value -in $alone) { [pscustomobject]@{ Collection = $m.Groups[1].Value; Class = "$namespace.$($m.Groups[2].Value)" } }
        }
    }
    @{
        Alone = @($classes | Where-Object Collection -ne $underLoad | ForEach-Object Class)
        Load = @($classes | Where-Object Collection -eq $underLoad | ForEach-Object Class)
    }
}

# One of the Windows tests' three runs: the rest, those that run alone, or the one under load; within a namespace when the
# change reaches only it. Each writes the app's coverage report of its own.
function New-WindowsStep([string]$Name, [string]$Namespace, $Split, [string]$Run, [string[]]$Options = @()) {
    $within = { param($classes) @($classes | Where-Object { -not $Namespace -or $_.StartsWith("$Namespace.", [StringComparison]::Ordinal) }) }
    $filters = switch ($Run) {
        'rest' { @($Split.Alone + $Split.Load | ForEach-Object { '--filter-not-class', $_ }) + @(if ($Namespace) { '--filter-namespace', $Namespace }) }
        'alone' { @(& $within $Split.Alone | ForEach-Object { '--filter-class', $_ }) }
        'load' { @(& $within $Split.Load | ForEach-Object { '--filter-class', $_ }) }
    }
    if ($Run -ne 'rest' -and -not $filters) { return }
    # thermi, thermi-sensors and what they share beyond Core, measured where their tests run them; CsWin32's generated
    # declarations are not thermi's code.
    New-Step $Name (@('dotnet', 'test', '--project', 'tests/Thermi.Windows.Tests', '-c', 'Release', '--no-build', '--results-directory', $appResults, '--',
            '--coverlet', '--coverlet-include', '[thermi]*', '--coverlet-include', '[thermi-sensors]*', '--coverlet-include', '[Thermi.App]*',
            '--coverlet-exclude', '[*]Windows.Win32.*',
            '--coverlet-single-hit', '--coverlet-output-format', 'json', '--coverlet-file-prefix', "Thermi.Windows.Tests.$Run") + $filters + $Options)
}

# Each project Thermi.Tests measures against its floor, from the report its run just wrote.
function Assert-Coverage {
    $report = Get-ChildItem $results -Filter '*.cobertura.*' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
    $packages = if ($report) { ([xml](Get-Content $report.FullName)).coverage.packages.package }
    $shown = @()
    $measured = [ordered]@{}
    $low = foreach ($name in $coverageFloors.Keys) {
        $package = $packages | Where-Object name -eq $name
        $rates = if ($package) { [double]$package.'line-rate', [double]$package.'branch-rate' } else { 0, 0 }
        $measured[$name] = @{ lines = 100 * $rates[0]; branches = 100 * $rates[1]; floor = $coverageFloors[$name] }
        $shown += '{0}: lines {1:n0} %, branches {2:n0} %, floor {3} %' -f $name, ($rates[0] * 100), ($rates[1] * 100), $coverageFloors[$name]
        if ([Math]::Min($rates[0], $rates[1]) * 100 -lt $coverageFloors[$name]) {
            '  {0} coverage: lines {1:p0}, branches {2:p0}. Its gate is {3} %: add behavior tests for the new code.' -f
                $name, $rates[0], $rates[1], $coverageFloors[$name]
        }
    }
    Add-SummaryRow 'coverage' $(if ($low) { 'fail' } else { 'ok' }) 0 $shown
    Save-Stats 'coverage' $measured
    if (-not $low) { return }
    Write-Host 'FAIL coverage'
    $low | ForEach-Object { Write-Host $_ }
    exit 1
}

# Shown, not gated: what the tests' windows run depends on the PC's monitors, DPI and theme, and a little on timing.
# Each test project's runs must hold what it runs, so one that measured nothing never hides behind the other's.
# -Partial shows what one CI job's runs measured, which need not hold it all, as the tests that run alone run in a job of
# their own.
function Show-AppCoverage([switch]$Partial) {
    $coverage = Read-AppCoverage $appResults @($appReports.Keys)
    foreach ($tests in $appReports.Keys) {
        foreach ($name in $appReports[$tests]) {
            if ($name -notin $coverage.Measured[$tests] -and -not $Partial) {
                Write-Host ("FAIL app coverage: $tests did not measure $name. Coverlet leaves out an assembly the test process loaded " +
                    'before the tests, as a [ModuleInitializer] that uses its types does.')
                exit 1
            }
        }
    }

    $modules = $coverage.Modules
    $shown = foreach ($name in 'thermi', 'thermi-sensors', 'Thermi.App') {
        if (-not $modules[$name]) { "$name not measured"; continue }
        '{0} lines {1:n0} %, branches {2:n0} %' -f $name, (Get-Rate $modules[$name].Lines), (Get-Rate $modules[$name].Branches)
    }
    $of = if ($Partial) { ' of the tests that run in parallel' } else { '' }
    Write-Host "info app coverage${of}: $($shown -join '; ')"
    Add-SummaryRow "app coverage$of" 'info' 0 $shown
}
