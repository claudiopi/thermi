#Requires -Version 7
<#
.SYNOPSIS
    The one quality check: restore, format, Release build, tests and the Thermi.Core coverage gate.
    The CI runs this same script, so passing here means passing there.
    Prints one line per step; on failure, only the errors and how to fix them.
#>
param(
    # CI mode: packages must match the committed lock files exactly.
    [switch]$Ci
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$results = Join-Path $root 'TestResults'
$minCoverage = 90
Set-Location $root

# A thermi built from this repo and still running locks its own files and breaks the build.
# An installed thermi (the one you use every day) runs from elsewhere and is left alone.
Get-Process thermi -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith("$root\", [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object {
        Write-Host "stop thermi.exe $($_.Id), built from this repo"
        Stop-Process -Id $_.Id -Force
    }

function Invoke-Step([string]$Name, [scriptblock]$Command, [scriptblock]$Explain) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $output = & $Command 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -eq 0) {
        Write-Host ('ok   {0} ({1:n0}s)' -f $Name, $watch.Elapsed.TotalSeconds)
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
    Write-Host ('  Thermi.Core coverage: lines {0:p0}, branches {1:p0}. The gate is {2} %: add behavior tests for the new code.' -f [double]$coverage.'line-rate', [double]$coverage.'branch-rate', $minCoverage)
}

Remove-Item $results -Recurse -Force -ErrorAction SilentlyContinue

Invoke-Step 'restore' { dotnet restore thermi.slnx $(if ($Ci) { '--locked-mode' }) }
Invoke-Step 'format' { dotnet format whitespace thermi.slnx --verify-no-changes } -Explain { Write-Host '  Fix it with: dotnet format whitespace thermi.slnx' }
Invoke-Step 'build Release' { dotnet build thermi.slnx -c Release --no-restore }
# Coverage counts only Core's own tests: the Windows tests touch Core in passing and would dilute it.
Invoke-Step 'tests and coverage' {
    dotnet test --project tests/Thermi.Tests -c Release --no-build --results-directory $results -- `
        --coverlet --coverlet-include '[Thermi.Core]*' --coverlet-output-format cobertura `
        --coverlet-threshold $minCoverage --coverlet-threshold-type line,branch
} -Explain { Show-Coverage }
Invoke-Step 'Windows tests' { dotnet test --project tests/Thermi.Windows.Tests -c Release --no-build }
