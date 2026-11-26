#Requires -Version 7
<#
.SYNOPSIS
    Fails on a package with a known vulnerability: any NuGet package, transitive ones and build tools included, and any npm
    package that ships in the settings window's page. Advisories are published after a package is chosen, so the CI runs
    this on every PR and push and each day. An advisory without a fix passes only while $allowed lists it, with why it
    cannot reach a user and the day it is looked at again; past that day it fails once more.
#>
param(
    [ValidateSet('All', 'NuGet', 'Npm')]
    [string]$Part = 'All'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# Each entry: @{ Id = 'GHSA-...'; Why = 'how it cannot reach a user'; Until = 'yyyy-MM-dd' }, at most a month ahead.
$allowed = @()

function Get-AdvisoryId([string]$Url) {
    if ($Url -match 'GHSA(-[0-9a-z]{4}){3}') { $Matches[0] } else { $Url }
}

# The solution and the installer, which the solution lists but never builds, each restored as the build restores it.
function Get-NuGetFindings {
    $locked = if ($env:CI) { '--locked-mode' }
    foreach ($target in 'thermi.slnx', 'src/Thermi.Installer') {
        dotnet restore (Join-Path $root $target) --verbosity quiet $locked | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "$target does not restore." }
        $listed = dotnet list (Join-Path $root $target) package --vulnerable --include-transitive --format json --output-version 1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw "dotnet could not list $target's vulnerable packages: $listed" }
        foreach ($project in @(($listed | ConvertFrom-Json).projects)) {
            foreach ($framework in @($project.frameworks | Where-Object { $_ })) {
                foreach ($package in (@($framework.topLevelPackages) + @($framework.transitivePackages) | Where-Object { $_ })) {
                    foreach ($advisory in @($package.vulnerabilities | Where-Object { $_ })) {
                        [pscustomobject]@{
                            Id = Get-AdvisoryId $advisory.advisoryurl
                            Package = "$($package.id) $($package.resolvedVersion)"
                            Severity = $advisory.severity
                            Where = Split-Path $project.path -Leaf
                        }
                    }
                }
            }
        }
    }
}

# What ships: devDependencies build and test the page and never reach a user. An entry whose every cause is another
# package's advisory is reported at that package.
function Get-NpmFindings {
    $web = Join-Path $root 'src' 'Thermi.Settings.Web'
    $report = npm audit --omit=dev --json --prefix $web | Out-String | ConvertFrom-Json
    if ($report.error) { throw "npm audit failed: $($report.error.summary)" }
    foreach ($entry in @($report.vulnerabilities.PSObject.Properties.Value | Where-Object { $_ })) {
        foreach ($cause in @($entry.via | Where-Object { $_ -and $_ -isnot [string] })) {
            [pscustomobject]@{
                Id = Get-AdvisoryId $cause.url
                Package = "$($entry.name) $($cause.range)"
                Severity = $cause.severity
                Where = 'src/Thermi.Settings.Web'
            }
        }
    }
}

$findings = @(
    if ($Part -in 'All', 'NuGet') { Get-NuGetFindings }
    if ($Part -in 'All', 'Npm') { Get-NpmFindings }
) | Sort-Object Id, Package, Where -Unique

$today = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
$failed = $false
foreach ($finding in $findings) {
    $exception = $allowed | Where-Object { $_.Id -eq $finding.Id } | Select-Object -First 1
    $line = "$($finding.Package) in $($finding.Where): $($finding.Severity) $($finding.Id)"
    if ($exception -and $exception.Until -ge $today) {
        Write-Host "allowed until $($exception.Until): $line. $($exception.Why)"
        continue
    }
    $failed = $true
    $why = if ($exception) { "its exception ended on $($exception.Until)" }
        else { 'update it, or list the advisory in scripts/audit.ps1 with why it cannot reach a user' }
    Write-Host "vulnerable: $line; $why"
    if ($env:GITHUB_ACTIONS) { Write-Host "::error::$line" }
}
foreach ($stale in ($allowed | Where-Object { $_.Id -notin $findings.Id })) {
    Write-Host "no package has $($stale.Id) any longer: delete its exception from scripts/audit.ps1"
}
if ($failed) { exit 1 }
Write-Host "no known vulnerable package ($Part)"
