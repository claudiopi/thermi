#Requires -Version 7
<#
.SYNOPSIS
    Asks the real bridge what the page's fake lacks. The page's tests run, and their fake records each call its table,
    src/Thermi.Settings.Web/tests/oracle/edits.json, has no answer for; Thermi.Windows.Tests' BridgeOracleTests then runs
    those calls through the real WindowBridge and writes its answers into the table. Again, until no test asks for more.
    Check the new answers in the diff: they are what C# does, which the tests then hold the page to.
    -Prune also drops what no test asks for any longer, after a run of every test.
#>
param(
    [switch]$Prune,
    [int]$Rounds = 40,
    [string]$ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$web = Join-Path $root 'src' 'Thermi.Settings.Web'
$misses = Join-Path $web 'test-results' 'oracle-misses'
$tests = Join-Path $root 'tests' 'Thermi.Windows.Tests'
$artifacts = $ArtifactsPath ? @('--artifacts-path', $ArtifactsPath) : @()

dotnet build $tests @artifacts | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Thermi.Windows.Tests does not build.' }

function Invoke-Answers([string]$Mode) {
    $env:THERMI_ORACLE = $Mode
    try {
        dotnet test --project $tests --no-build @artifacts -- --filter-method '*TheFakeBridgeAnswersAsTheRealOne' | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'The real bridge could not answer.' }
    }
    finally {
        Remove-Item Env:THERMI_ORACLE
    }
}

# A test stops at its first call without an answer, so each round answers one more call of each; after the first, only
# the tests that failed run again, and a last run of every test confirms none asks for more.
$every = $true
for ($round = 1; $round -le $Rounds; $round++) {
    Remove-Item $misses -Recurse -Force -ErrorAction SilentlyContinue
    Push-Location $web
    try {
        $only = $every ? @() : @('--last-failed')
        npx playwright test --reporter=dot @only | Out-Host
    }
    finally {
        Pop-Location
    }

    $asked = @(Get-ChildItem $misses -Filter 'misses-*.jsonl' -ErrorAction SilentlyContinue).Count -gt 0
    if (-not $asked -and -not $every) {
        $every = $true
        continue
    }

    if (-not $asked) {
        if ($Prune) { Invoke-Answers 'prune' }
        Write-Host 'ok   oracle: no test asks the real bridge for more'
        exit 0
    }

    $every = $false

    Write-Host "     oracle: round $round, asking the real bridge"
    Invoke-Answers 'write'
}

Write-Host "FAIL oracle: tests still ask for more after $Rounds rounds; a test whose calls change from run to run never settles."
exit 1
