#Requires -Version 7
<#
.SYNOPSIS
    The one quality check: restore, format, Release build, the third-party notices, the test-count ratchet, tests, the
    coverage gates of Thermi.Core and Thermi.Cli and the app's coverage; then the settings window's page: lint, react-doctor, unit tests,
    dead code and the page's tests with axe, its type check in its build.
    Here it runs only what the change can affect (scripts/check-scope.ps1), with the .NET half and the page's side by side
    and the steps that need nothing of each other at once; -Full runs every step. The CI runs every step of this same
    script, each part in a job of its own, so nothing merges red.
    On Linux it builds and runs Thermi.Tests and the Linux tests.
    Prints one line per step; on failure, only the errors and how to fix them.
#>
param(
    # CI mode: every step, and packages must match the committed lock files exactly.
    [switch]$Ci,
    # Then the mutation score of the tests of the halves run, too slow for every run: Stryker.NET on Thermi.Core and
    # Thermi.Cli (scripts/mutation.ps1) and StrykerJS on the page's logic, each with the cases that hold nothing of their own
    # (scripts/redundant.ps1).
    [switch]$Mutation,
    # One part alone. DotNet is Core, Windows and WindowsAlone: Core's tests with the build's own checks, the Windows tests
    # that run in parallel, and those that run alone. On Linux it is LinuxCore and Linux: Thermi.Tests and the Linux tests,
    # and there is no page's half, whose tests run in the Edge that Windows ships. The CI runs each part in a job of its own.
    [ValidateSet('All', 'DotNet', 'Core', 'Windows', 'WindowsAlone', 'Web', 'LinuxCore', 'Linux')]
    [string]$Part = 'All',
    # Every step, as the CI runs them, whatever the change.
    [switch]$Full,
    # Where the change is measured from while this worktree has no green check of its own.
    [string]$Base = 'main',
    # Given by a check of both halves to the page's half it starts: whether the .NET build just built the page.
    [switch]$FromCheck,
    [switch]$PageBuilt
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$results = Join-Path $root 'TestResults'
$web = Join-Path $root 'src' 'Thermi.Settings.Web'
# The least line and branch coverage of each project Thermi.Tests measures. thermi-cli's commands are mostly answered by
# Core, so its own branches are what it adds: the console, the MCP session and the environment's answers.
$coverageFloors = [ordered]@{ 'Thermi.Core' = 90; 'Thermi.Cli' = 85 }
$appResults = Join-Path $results 'app'
# Each test project's reports, named for it, and what they must have measured.
$appReports = [ordered]@{ 'Thermi.Windows.Tests' = 'thermi', 'thermi-sensors', 'Thermi.App' }
$everything = $Ci -or $Full
$half = if ($Part -eq 'Web') { 'web' } else { 'dotnet' }
# Each OS builds its own projects: the Windows ones target Windows, and the Linux tests start what only Linux has.
if ($IsWindows -and $Part -in 'LinuxCore', 'Linux') { throw "-Part $Part runs on Linux." }
if (-not $IsWindows -and $Part -in 'Core', 'Windows', 'WindowsAlone') { throw "-Part $Part runs on Windows." }
Set-Location $root

. (Join-Path $PSScriptRoot 'check-lock.ps1')
. (Join-Path $PSScriptRoot 'check-scope.ps1')
. (Join-Path $PSScriptRoot 'check-steps.ps1')

# A thermi built from this repo and still running locks its own files and breaks the build.
# An installed thermi (the one you use every day) runs from elsewhere and is left alone.
function Stop-RepoThermi {
    Get-Process thermi -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith("$root$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object {
            Write-Host "stop thermi.exe $($_.Id), built from this repo"
            Stop-Process -Id $_.Id -Force
        }
}

function Get-HalfScope([string]$Tree) {
    if ($everything) { return Get-Scope $root $null }
    Get-Scope $root (Get-ChangedFiles $half $Tree $Base)
}

function Invoke-DotNet($Scope) {
    $core = $Part -in 'All', 'DotNet', 'Core'
    $windows = $Scope.WindowsTests -and $Part -in 'All', 'DotNet', 'Windows'
    $alone = $Scope.WindowsTests -and $Part -in 'All', 'DotNet', 'WindowsAlone'
    # Core's job builds all, for the count lists every test project and the Linux projects build here too; the Windows jobs
    # build what their tests run.
    $built = if (-not $core) { 'tests/Thermi.Windows.Tests' }
        elseif ($Scope.WindowsTests -or $Scope.LinuxTests -or $Scope.TestCount) { 'thermi.slnx' }
        else { 'tests/Thermi.Tests' }
    # Thermi.Windows' build writes what the page builds from and builds it, so the page's half waits for it.
    $script:buildsPage = $built -ne 'tests/Thermi.Tests'
    if ($Part -eq 'All' -and -not $buildsPage) { Start-WebHalf }
    $locked = if ($Ci) { '--locked-mode' }
    $missing = git ls-files '*.csproj' | Where-Object { -not (Test-Path (Join-Path (Split-Path $_ -Parent) 'obj' 'project.assets.json')) }
    if ($Scope.All -or $Scope.Restore -or $missing) {
        # The installer too, which thermi.slnx lists but never builds, whose custom actions' package the notices list.
        Invoke-Step 'restore' {
            dotnet restore thermi.slnx $locked
            if ($LASTEXITCODE -eq 0 -and $core) { dotnet restore src/Thermi.Installer $locked }
        }
    }
    if ($core) {
        # A file's whitespace depends on it and .editorconfig alone, so every C# file is checked as a folder's: the same
        # errors as through the solution, without loading it, in a second instead of a minute.
        Invoke-Step 'format' {
            dotnet format whitespace --folder --verify-no-changes --exclude '**/bin/**' '**/obj/**' '**/node_modules/**' '.claude/**' artifacts TestResults
        } -Explain { Write-Host '  Fix it with: dotnet format whitespace thermi.slnx' }
    }
    Invoke-Step 'build Release' { dotnet build $built -c Release --no-restore }
    if ($Part -eq 'All' -and $buildsPage) { Start-WebHalf }

    # Beside the page's half, the tests take half the PC's threads, which leaves its browsers the other half: past that,
    # the page's tests wait past their timeouts.
    $share = @(if ($Part -eq 'All') { '--max-threads', '0.5x' })
    $together = @()
    if ($core) {
        # From the lock files, the packages the restore just downloaded and the page's, which the build installs.
        if ($Scope.Notices) {
            $together += New-Step 'third-party notices' 'pwsh', '-NoProfile', '-File', 'scripts/notices.ps1', '-Check'
        }
        # The .NET SDK's package, as a release publishes it, so one that no longer packs fails here and not then.
        if ($Scope.Pack) {
            $together += New-Step 'pack the SDK' 'dotnet', 'pack', 'src/Thermi.Client', '-c', 'Release', '--no-build', '-o', 'artifacts/packages', '--nologo'
        }
        if ($Scope.TestCount) {
            $together += New-Step 'test count' 'pwsh', '-NoProfile', '-File', 'scripts/test-count.ps1', '-Part', 'DotNet'
        }
        # Coverage counts only Thermi.Tests: the Windows tests would inflate Core's and the CLI's numbers by counting code
        # they run only in passing. Each project's floor is gated once the tests passed.
        $measured = @($coverageFloors.Keys | ForEach-Object { '--coverlet-include', "[$_]*" })
        $together += New-Step 'tests and coverage' (@('dotnet', 'test', '--project', 'tests/Thermi.Tests', '-c', 'Release', '--no-build',
                '--results-directory', $results, '--', '--coverlet') + $measured + @('--coverlet-output-format', 'cobertura') + $share)
    }
    $split = Get-AloneClasses
    if ($windows) { $together += New-WindowsStep 'Windows tests' $Scope.WindowsFilter $split 'rest' $share }
    Invoke-Together $together
    if ($core) { Assert-Coverage }
    # The tests that run alone paint ten thousand rounds of overlays, and the page's tests beside them time out: those
    # wait for the page's half.
    if ($alone -and $webHalf) { $webHalf.WaitForExit() }
    Invoke-Together @(if ($alone) { New-WindowsStep 'Windows tests alone' $Scope.WindowsFilter $split 'alone' })
    Invoke-Together @(if ($alone) { New-WindowsStep 'Windows tests under load' $Scope.WindowsFilter $split 'load' })
    if ($windows -and $alone -and -not $Scope.WindowsFilter) { Show-AppCoverage }
    elseif ($windows -and $Ci) { Show-AppCoverage -Partial }
    if ($Mutation) {
        pwsh -NoProfile -File scripts/mutation.ps1
        if ($LASTEXITCODE -ne 0) { exit 1 }
    }
}

# The .NET half on Linux: Thermi.Tests, whose Core and rules hold on every OS, and the Linux tests, each project restored
# and built alone, as the Windows projects build on Windows only.
function Invoke-Linux($Scope) {
    $projects = @(if ($Part -in 'All', 'DotNet', 'LinuxCore') { 'tests/Thermi.Tests' }) +
        @(if ($Scope.LinuxTests -and $Part -in 'All', 'DotNet', 'Linux') { 'tests/Thermi.Linux.Tests' })
    $locked = @(if ($Ci) { '--locked-mode' })
    Invoke-Step 'restore' { foreach ($project in $projects) { dotnet restore $project @locked; if ($LASTEXITCODE -ne 0) { break } } }
    Invoke-Step 'build Release' { foreach ($project in $projects) { dotnet build $project -c Release --no-restore; if ($LASTEXITCODE -ne 0) { break } } }
    Invoke-Together @(foreach ($project in $projects) {
            $name = if ($project -eq 'tests/Thermi.Tests') { 'tests on Linux' } else { 'Linux tests' }
            New-Step $name 'dotnet', 'test', '--project', $project, '-c', 'Release', '--no-build', '--results-directory', $results
        })
}

# The page's half, of the same check, in a process of its own beside this one, once the .NET build wrote what the page
# builds from; its lines print as they come.
function Start-WebHalf {
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $arguments = '-NoProfile', '-File', $PSCommandPath, '-Part', 'Web', '-Base', $Base, '-FromCheck'
    $arguments += @(if ($Full) { '-Full' }) + @(if ($Mutation) { '-Mutation' }) + @(if ($buildsPage) { '-PageBuilt' })
    foreach ($each in $arguments) { $start.ArgumentList.Add($each) }
    $start.UseShellExecute = $false
    $script:webHalf = [Diagnostics.Process]::Start($start)
}

# The page's checks, each an npm script of src/Thermi.Settings.Web: all at once, then its tests, which take the most
# cores and time out when others take them.
# Its build type-checks it, so nothing else does; installs never run a package's scripts.
function Invoke-Web {
    if ($Ci -or -not (Test-Path (Join-Path $web 'node_modules' '.package-lock.json')) -or
        (Get-Item (Join-Path $web 'package-lock.json')).LastWriteTimeUtc -gt (Get-Item (Join-Path $web 'node_modules' '.package-lock.json')).LastWriteTimeUtc) {
        Invoke-Step 'web install' { npm ci --prefix $web --ignore-scripts --no-audit --no-fund }
    }
    if (-not $PageBuilt -and ($Ci -or -not (Test-PageBuilt))) { Invoke-Step 'web typecheck and build' { npm run build --prefix $web } }
    $npm = 'npm', 'run', '--prefix', $web
    $steps = @(
        New-Step 'web test count' 'pwsh', '-NoProfile', '-File', 'scripts/test-count.ps1', '-Part', 'Web'
        New-Step 'web lint' ($npm + 'lint')
        # The GNOME Shell extension, by its own config, which needs no packages of its own.
        New-Step 'gnome extension lint' 'node', (Join-Path $web 'node_modules' 'eslint' 'bin' 'eslint.js'), '--max-warnings', '0', 'src/Thermi.Gnome'
        New-Step 'web doctor' ($npm + 'doctor')
        New-Step 'web unit tests' ($npm + 'test:unit')
        New-Step 'web dead code' ($npm + 'deadcode') -Explain { Write-Host '  Delete what nothing uses, or say in knip.jsonc why it stays.' }
    )
    Invoke-Together $steps
    # Playwright with axe, against the built page in the Edge that Windows ships.
    Invoke-Step 'web page tests' { npm run test:e2e --prefix $web }
    if ($Mutation) {
        pwsh -NoProfile -File scripts/mutation.ps1 -Web
        if ($LASTEXITCODE -ne 0) { exit 1 }
    }
}

# Whether the page's dist is newer than everything it builds from, as thermi's .NET build tells.
function Test-PageBuilt {
    $index = Join-Path $web 'dist' 'index.html'
    if (-not (Test-Path $index)) { return $false }
    $inputs = @(Get-ChildItem (Join-Path $web 'src'), (Join-Path $web 'tests') -Recurse -File) + @(Get-ChildItem $web -File) +
        @(Get-Item (Join-Path $web 'node_modules' '.package-lock.json') -ErrorAction SilentlyContinue)
    -not ($inputs | Where-Object { $_.LastWriteTimeUtc -gt (Get-Item $index).LastWriteTimeUtc })
}

try {
    if (-not $Ci) { Enter-Check $half }
    if (-not $FromCheck) {
        Stop-RepoThermi
        Remove-Item $results -Recurse -Force -ErrorAction SilentlyContinue
    }
    $tree = if (-not $Ci) { Get-WorkTree }
    $scope = Get-HalfScope $tree
    if (-not $everything) { Write-Host (Format-Scope $scope $half) }
    if ($half -eq 'web') {
        if ($scope.Web) { Invoke-Web }
    }
    elseif (-not $IsWindows) {
        if ($scope.CoreTests) { Invoke-Linux $scope }
    }
    elseif ($scope.CoreTests) {
        Invoke-DotNet $scope
    }
    elseif ($Part -eq 'All') {
        $script:buildsPage = $false
        Start-WebHalf
    }
    if (-not $Ci -and $Part -in 'All', 'DotNet', 'Web') { Save-Green $half (Get-CheckedTree $tree (Get-WorkTree)) }
    if ($webHalf) {
        $webHalf.WaitForExit()
        if ($webHalf.ExitCode -ne 0) { exit 1 }
    }
}
finally {
    if ($webHalf -and -not $webHalf.HasExited) { $webHalf.WaitForExit() }
    if (-not $Ci) { Exit-Check }
    $titles = @{
        All = 'Check'; DotNet = '.NET'; Core = 'Build and Thermi.Tests'; Windows = 'Windows tests'; WindowsAlone = 'Windows tests alone'; Web = 'Settings page'
        LinuxCore = 'Thermi.Tests on Linux'; Linux = 'Linux tests'
    }
    Write-Summary $titles[$Part] $Part
}
