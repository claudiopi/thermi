#Requires -Version 7
<#
.SYNOPSIS
    The one quality check: restore, format, Release build, the third-party notices, the test-count ratchet, tests, the Thermi.Core coverage gate
    and the app's coverage; then the settings window's page: type check, lint, react-doctor, unit tests, dead code and
    the page's tests with axe. The CI runs this same script, so passing here means passing there.
    Prints one line per step; on failure, only the errors and how to fix them.
#>
param(
    # CI mode: packages must match the committed lock files exactly.
    [switch]$Ci,
    # Then the mutation score of the tests of the parts run, too slow for every run here: Stryker.NET on Thermi.Core
    # (scripts/mutation.ps1) and StrykerJS on the page's logic.
    [switch]$Mutation,
    # One half alone: the CI runs .NET and the page in jobs of their own, side by side.
    [ValidateSet('All', 'DotNet', 'Web')]
    [string]$Part = 'All'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$results = Join-Path $root 'TestResults'
$minCoverage = 90
$appResults = Join-Path $results 'app'
# thermi and thermi-sensors, measured where their tests run them; CsWin32's generated declarations are not thermi's code.
$appCoverage = '--results-directory', $appResults, '--', '--coverlet', '--coverlet-include', '[thermi]*', '--coverlet-include', '[thermi-sensors]*',
    '--coverlet-exclude', '[*]Windows.Win32.*', '--coverlet-single-hit', '--coverlet-output-format', 'json'
# Each test project's report, named for it, and what it must have measured.
$appReports = [ordered]@{ 'Thermi.Windows.Tests' = 'thermi', 'thermi-sensors' }
Set-Location $root

. (Join-Path $PSScriptRoot 'check-lock.ps1')

# A thermi built from this repo and still running locks its own files and breaks the build.
# An installed thermi (the one you use every day) runs from elsewhere and is left alone.
function Stop-RepoThermi {
    Get-Process thermi -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith("$root\", [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object {
            Write-Host "stop thermi.exe $($_.Id), built from this repo"
            Stop-Process -Id $_.Id -Force
        }
}

function Invoke-Step([string]$Name, [scriptblock]$Command, [scriptblock]$Explain) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $output = & $Command 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -eq 0) {
        Write-Host ('ok   {0} ({1:n0}s)' -f $Name, $watch.Elapsed.TotalSeconds)
        $output | Where-Object { $_ -like 'note: *' } | ForEach-Object { Write-Host "     $_" }
        return
    }

    Write-Host "FAIL $Name"
    Show-Errors $output
    if ($Explain) { & $Explain }
    exit 1
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

function Show-Coverage {
    $report = Get-ChildItem $results -Filter '*.cobertura.*' -ErrorAction SilentlyContinue | Select-Object -Last 1
    if (-not $report) { return }
    $coverage = ([xml](Get-Content $report.FullName)).coverage
    if ([Math]::Min([double]$coverage.'line-rate', [double]$coverage.'branch-rate') * 100 -ge $minCoverage) { return }
    Write-Host ('  Thermi.Core coverage: lines {0:p0}, branches {1:p0}. The gate is {2} %: add behavior tests for the new code.' -f
        [double]$coverage.'line-rate', [double]$coverage.'branch-rate', $minCoverage)
}

# Shown, not gated: what the tests' windows run depends on the PC's monitors, DPI and theme, and a little on timing.
# Each test project's report must hold what it runs, so one that measured nothing never hides behind the other's;
# a line or branch that either test process ran counts once.
function Show-AppCoverage {
    $modules = @{}
    foreach ($tests in $appReports.Keys) {
        $report = Get-ChildItem $appResults -Filter "$tests.coverage.*.json" -ErrorAction SilentlyContinue | Select-Object -Last 1
        $measured = if ($report) { Get-Content $report.FullName -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
        $names = @{}
        foreach ($module in $measured.GetEnumerator()) { $names[[IO.Path]::GetFileNameWithoutExtension($module.Key)] = $module.Value }
        foreach ($name in $appReports[$tests]) {
            if (-not $names[$name]) {
                Write-Host ("FAIL app coverage: $tests did not measure $name. Coverlet leaves out an assembly the test process loaded " +
                    'before the tests, as a [ModuleInitializer] that uses its types does.')
                exit 1
            }
        }
        foreach ($name in $names.Keys) {
            if (-not $modules[$name]) { $modules[$name] = @{ Lines = @{}; Branches = @{} } }
            Add-Hits $modules[$name] $names[$name]
        }
    }

    $shown = foreach ($name in 'thermi', 'thermi-sensors') {
        '{0} lines {1:n0} %, branches {2:n0} %' -f $name, (Get-Rate $modules[$name].Lines), (Get-Rate $modules[$name].Branches)
    }
    Write-Host "info app coverage: $($shown -join '; ')"
}

# A report holds file, class, method, then each method's lines and branches.
function Add-Hits($Into, $Files) {
    foreach ($file in $Files.GetEnumerator()) {
        foreach ($method in $file.Value.Values.ForEach({ $_.GetEnumerator() })) {
            foreach ($line in $method.Value.Lines.GetEnumerator()) {
                $key = "$($file.Key)|$($line.Key)"
                $Into.Lines[$key] = $Into.Lines[$key] -or $line.Value -gt 0
            }
            foreach ($branch in $method.Value.Branches) {
                $key = "$($method.Key)|$($branch.Line)|$($branch.Offset)|$($branch.EndOffset)|$($branch.Path)|$($branch.Ordinal)"
                $Into.Branches[$key] = $Into.Branches[$key] -or $branch.Hits -gt 0
            }
        }
    }
}

function Get-Rate([hashtable]$Hits) {
    if ($Hits.Count -eq 0) { return 100 }
    100 * @($Hits.Values | Where-Object { $_ }).Count / $Hits.Count
}

function Invoke-DotNet {
    # The installer too, outside thermi.slnx, whose custom actions' package the notices list.
    Invoke-Step 'restore' {
        dotnet restore thermi.slnx $(if ($Ci) { '--locked-mode' })
        if ($LASTEXITCODE -eq 0) { dotnet restore src/Thermi.Installer $(if ($Ci) { '--locked-mode' }) }
    }
    Invoke-Step 'format' { dotnet format whitespace thermi.slnx --verify-no-changes } -Explain {
        Write-Host '  Fix it with: dotnet format whitespace thermi.slnx'
    }
    Invoke-Step 'build Release' { dotnet build thermi.slnx -c Release --no-restore }
    # From the lock files, the packages the restore just downloaded and the page's, which the build installs.
    Invoke-Step 'third-party notices' { pwsh -NoProfile -File scripts/notices.ps1 -Check }
    # The .NET SDK's package, as a release publishes it, so one that no longer packs fails here and not then.
    Invoke-Step 'pack the SDK' { dotnet pack src/Thermi.Client -c Release --no-build -o artifacts/packages --nologo }
    Invoke-Step 'test count' { pwsh -NoProfile -File scripts/test-count.ps1 -Part DotNet }
    # Coverage counts only Core's own tests: the Windows tests would inflate Core's numbers by counting code they run only in passing.
    Invoke-Step 'tests and coverage' {
        dotnet test --project tests/Thermi.Tests -c Release --no-build --results-directory $results -- `
            --coverlet --coverlet-include '[Thermi.Core]*' --coverlet-output-format cobertura `
            --coverlet-threshold $minCoverage --coverlet-threshold-type line,branch
    } -Explain { Show-Coverage }
    Invoke-Step 'Windows tests' {
        dotnet test --project tests/Thermi.Windows.Tests -c Release --no-build @appCoverage --coverlet-file-prefix Thermi.Windows.Tests
    }
    Show-AppCoverage
    if ($Mutation) {
        pwsh -NoProfile -File scripts/mutation.ps1
        if ($LASTEXITCODE -ne 0) { exit 1 }
    }
}

# The page's checks, each an npm script of src/Thermi.Settings.Web. The .NET build installs and builds the page when it
# changed, so only the page's half alone does both itself; installs never run a package's scripts.
function Invoke-Web {
    $web = Join-Path $root 'src' 'Thermi.Settings.Web'
    if ($Part -eq 'Web') {
        Invoke-Step 'web install' { npm ci --prefix $web --ignore-scripts --no-audit --no-fund }
        Invoke-Step 'web build' { npm run build --prefix $web }
    }
    Invoke-Step 'web typecheck' { npm run typecheck --prefix $web }
    Invoke-Step 'web test count' { pwsh -NoProfile -File scripts/test-count.ps1 -Part Web }
    Invoke-Step 'web lint' { npm run lint --prefix $web }
    Invoke-Step 'web doctor' { npm run doctor --prefix $web }
    Invoke-Step 'web unit tests' { npm run test:unit --prefix $web }
    Invoke-Step 'web dead code' { npm run deadcode --prefix $web } -Explain { Write-Host '  Delete what nothing uses, or say in knip.jsonc why it stays.' }
    # Playwright with axe, against the built page in the Edge that Windows ships.
    Invoke-Step 'web page tests' { npm run test:e2e --prefix $web }
    if ($Mutation) {
        Invoke-Step 'web mutation' { npm run mutation --prefix $web }
    }
}

try {
    if (-not $Ci) { Enter-Check }
    Stop-RepoThermi
    Remove-Item $results -Recurse -Force -ErrorAction SilentlyContinue
    if ($Part -ne 'Web') { Invoke-DotNet }
    if ($Part -ne 'DotNet') { Invoke-Web }
}
finally {
    if (-not $Ci) { Exit-Check }
}
