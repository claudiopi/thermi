#Requires -Version 7
<#
.SYNOPSIS
    Writes THIRD-PARTY-NOTICES.md from the lock files: every package that ships in thermi-sensors, in thermi, in the
    MSIs and in the settings window's page, with its license and its source. -Check only compares, as scripts/check.ps1
    runs it, so a package added or removed updates the notices in the same change. Reads the NuGet packages a restore
    downloaded.
#>
param([switch]$Check)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$notices = Join-Path $root 'THIRD-PARTY-NOTICES.md'
$nuget = $env:NUGET_PACKAGES ?? (Join-Path $HOME '.nuget' 'packages')

# Packages whose nuspec points to a license file or link instead of naming it, as read in that file. A new one fails
# the script until a person reads its license and adds it here.
$declared = @{
    'HidSharp' = 'Apache-2.0'
    'Microsoft.Web.WebView2' = 'BSD-3-Clause'
    'Mono.Posix.NETStandard' = 'MIT'
}

# Analyzers and code generators, referenced with PrivateAssets="all", run in the build and ship nothing.
function Get-BuildOnly([string]$Folder) {
    foreach ($file in @(Join-Path $root 'Directory.Build.props') + @(Get-ChildItem $Folder -Filter *.csproj | ForEach-Object FullName)) {
        ([xml](Get-Content $file -Raw)).SelectNodes('//PackageReference[@PrivateAssets="all"]') | ForEach-Object Include
    }
}

function Get-NuGetRows([string]$Project) {
    $folder = Join-Path $root 'src' $Project
    $targets = (Get-Content (Join-Path $folder 'packages.lock.json') -Raw | ConvertFrom-Json -AsHashtable).dependencies
    $graph = @{}
    foreach ($package in $targets[@($targets.Keys | Where-Object { $_ -notmatch '/' })[0]].GetEnumerator()) { $graph[$package.Key] = $package.Value }
    $buildOnly = @(Get-BuildOnly $folder)
    $queue = [Collections.Generic.Queue[string]]::new()
    $graph.Keys | Where-Object { $graph[$_].type -eq 'Direct' -and $_ -notin $buildOnly } | ForEach-Object { $queue.Enqueue($_) }
    $seen = @{}
    while ($queue.Count) {
        $id = $queue.Dequeue()
        # Native parts for other systems, which a Windows build leaves out.
        if ($seen[$id] -or $id -match '^runtime\.') { continue }
        $seen[$id] = $true
        $entry = $graph[$id]
        if ($entry.dependencies) { foreach ($dependency in $entry.dependencies.Keys) { $queue.Enqueue($dependency) } }
        Get-NuGetRow $id $entry.resolved
    }
}

function Get-NuGetRow([string]$Id, [string]$Version) {
    $lower = $Id.ToLowerInvariant()
    $metadata = ([xml](Get-Content (Join-Path $nuget $lower $Version "$lower.nuspec") -Raw)).package.metadata
    $license = if ($metadata.license.type -eq 'expression') { $metadata.license.'#text' } else { $declared[$metadata.id] }
    if (-not $license) { throw "$($metadata.id) $Version names no license expression: read its license and add it to `$declared in scripts/notices.ps1." }
    $source = if ($metadata.repository.url) { $metadata.repository.url } else { $metadata.projectUrl }
    [pscustomobject]@{ Name = $metadata.id; License = $license; Source = $source -replace '^git://', 'https://' -replace '\.git$', '' }
}

# The page's dependencies, and theirs, without the ones only its build and tests use, and without type declarations,
# which hold no code.
function Get-NpmRows {
    $lock = Get-Content (Join-Path $root 'src' 'Thermi.Settings.Web' 'package-lock.json') -Raw | ConvertFrom-Json -AsHashtable
    $rows = @{}
    foreach ($package in $lock.packages.GetEnumerator()) {
        if (-not $package.Key -or $package.Value.dev) { continue }
        $name = $package.Key -replace '^.*node_modules/', ''
        if ($name -like '@types/*') { continue }
        if (-not $package.Value.license) { throw "$name names no license in package-lock.json: read its license before it ships." }
        $rows[$name] = [pscustomobject]@{ Name = $name; License = $package.Value.license; Source = "https://www.npmjs.com/package/$name" }
    }
    $rows.Values
}

function Format-Table([string]$Title, [string]$Lead, $Rows) {
    "## $Title"
    ''
    $Lead
    ''
    '| Library | License | Source |'
    '| --- | --- | --- |'
    $sorted = [Collections.Generic.List[object]]::new([object[]]@($Rows))
    $sorted.Sort([Comparison[object]] { param($a, $b) [string]::CompareOrdinal($a.Name, $b.Name) })
    foreach ($row in $sorted) { "| $($row.Name) | $($row.License) | $($row.Source) |" }
    ''
}

$text = @(
    '# Third-party notices'
    ''
    '<!-- Written by scripts/notices.ps1 from the lock files; scripts/check.ps1 fails when it is out of date. -->'
    ''
    'thermi is MIT licensed. It ships these libraries unmodified; their sources are at the links below.'
    ''
    Format-Table 'thermi-sensors' 'The sensor service.' (Get-NuGetRows 'Thermi.SensorHost')
    Format-Table 'thermi' 'The overlay and the settings window.' (Get-NuGetRows 'Thermi')
    Format-Table 'The MSIs' 'The custom actions each MSI carries and runs while it installs.' (Get-NuGetRows 'Thermi.Installer')
    Format-Table 'The settings window''s page' 'Built into thermi.exe; only what the page imports ends up in it.' (Get-NpmRows)
    ('thermi does not ship the PawnIO driver that LibreHardwareMonitor uses, nor the WebView2 runtime: the driver comes from its own release ' +
        'when the window installs it (`docs/adr/0010-driver-and-service-from-the-window.md`), ' +
        'and the runtime with Windows or from Microsoft (`scripts/install.ps1`).')
) -join "`r`n"
$text += "`r`n"

if (-not $Check) {
    [IO.File]::WriteAllText($notices, $text)
    return
}

$current = if (Test-Path $notices) { (Get-Content $notices -Raw) -replace "`r?`n", "`r`n" } else { '' }
if ($current -ne $text) {
    Write-Host 'THIRD-PARTY-NOTICES.md is out of date with the lock files. Write it again with: pwsh scripts/notices.ps1'
    exit 1
}
exit 0
