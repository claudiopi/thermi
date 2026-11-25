#Requires -Version 7
<#
.SYNOPSIS
    Writes THIRD-PARTY-NOTICES.md from the lock files: every package that ships in thermi-sensors, in thermi and in the
    settings window's page, with its license, its source, and its license's text with every copyright line it carries.
    -Check only compares, as scripts/check.ps1 runs it, so a package added, removed or updated updates the notices in
    the same change. It reads the license files of the NuGet packages a restore downloaded and of the page's packages
    the build installed, never the network. A package that holds no license file gets SPDX's text of the license it
    names, from scripts/licenses, with the copyright its package states, and the notices say so.
#>
param([switch]$Check)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$notices = Join-Path $root 'THIRD-PARTY-NOTICES.md'
$nuget = $env:NUGET_PACKAGES ?? (Join-Path $HOME '.nuget' 'packages')
$web = Join-Path $root 'src' 'Thermi.Settings.Web'
$templates = Join-Path $PSScriptRoot 'licenses'

# Packages whose nuspec points to a license file or link instead of naming it, as read in that file. A new one fails
# the script until a person reads its license and adds it here.
$declared = @{
    'HidSharp' = 'Apache-2.0'
    'Microsoft.Web.WebView2' = 'BSD-3-Clause'
    'Mono.Posix.NETStandard' = 'MIT'
}

$licenseFile = '^(LICEN[CS]E|COPYING)([.-][\w.-]*)?$'
$noticeFile = '^NOTICE(\.(txt|md))?$'
# A line that states a copyright, not one of a license's sentences about copyright holders or notices.
$copyrightLine = '^\s*(copyright\b(?!\s+(holders?|owners?|notices?|and|or|license|law|statement)\b)|\(c\)\s*\d|©)'
# A template's own blank, as the Apache license's appendix shows how to apply it.
$placeholder = '<year>|\[yyyy\]|<owner>|<copyright holders>'

# Analyzers and code generators, referenced with PrivateAssets="all", run in the build and ship nothing.
function Get-BuildOnly([string]$Folder) {
    foreach ($file in @(Join-Path $root 'Directory.Build.props') + (Get-ChildItem $Folder -Filter *.csproj).FullName) {
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
    $folder = Join-Path $nuget $lower $Version
    $metadata = ([xml](Get-Content (Join-Path $folder "$lower.nuspec") -Raw)).package.metadata
    $license = if ($metadata.license.type -eq 'expression') { $metadata.license.'#text' } else { $declared[$metadata.id] }
    if (-not $license) { throw "$($metadata.id) $Version names no license expression: read its license and add it to `$declared in scripts/notices.ps1." }
    $source = if ($metadata.repository.url) { $metadata.repository.url } else { $metadata.projectUrl }
    $files = if ($metadata.license.type -eq 'file') { @(Join-Path $folder $metadata.license.'#text') } else { @(Find-Files $folder $licenseFile) }
    [pscustomobject]@{
        Name = $metadata.id; Version = $Version; License = $license; Source = $source -replace '^git://', 'https://' -replace '\.git$', ''
        Licenses = $files; Notices = @(Find-Files $folder $noticeFile); Copyright = $metadata.copyright
    }
}

function Find-Files([string]$Folder, [string]$Pattern) {
    Get-ChildItem $Folder -File | Where-Object Name -Match $Pattern | Sort-Object Name | ForEach-Object FullName
}

# The page's dependencies, and theirs, without the ones only its build and tests use, and without type declarations,
# which hold no code. Each installed copy counts, as two versions of a package may carry different copyrights.
function Get-NpmRows {
    $lock = Get-Content (Join-Path $web 'package-lock.json') -Raw | ConvertFrom-Json -AsHashtable
    foreach ($package in $lock.packages.GetEnumerator()) {
        if (-not $package.Key -or $package.Value.dev) { continue }
        $name = $package.Key -replace '^.*node_modules/', ''
        if ($name -like '@types/*') { continue }
        if (-not $package.Value.license) { throw "$name names no license in package-lock.json: read its license before it ships." }
        $folder = Join-Path $web $package.Key
        $manifest = Join-Path $folder 'package.json'
        $installed = if (Test-Path $manifest) { (Get-Content $manifest -Raw | ConvertFrom-Json -AsHashtable).version }
        if ($installed -ne $package.Value.version) {
            throw "$name $($package.Value.version) of package-lock.json is not in node_modules: run npm ci --ignore-scripts in src/Thermi.Settings.Web."
        }
        $author = (Get-Content $manifest -Raw | ConvertFrom-Json -AsHashtable).author
        [pscustomobject]@{
            Name = $name; Version = $installed; License = $package.Value.license; Source = "https://www.npmjs.com/package/$name"
            Licenses = @(Find-Files $folder $licenseFile); Notices = @(Find-Files $folder $noticeFile)
            Copyright = if ($author -is [hashtable]) { $author.name } else { $author }
        }
    }
}

function Format-Table([string]$Title, [string]$Lead, $Rows) {
    "## $Title"
    ''
    $Lead
    ''
    '| Library | License | Source |'
    '| --- | --- | --- |'
    # A library installed in two versions shows its last one's license and source.
    $byLibrary = @{}
    foreach ($row in $Rows) { $byLibrary["$($row.Name) $($row.Version)"] = $row }
    $byName = @{}
    foreach ($library in Get-Sorted $byLibrary.Keys) { $byName[$byLibrary[$library].Name] = $byLibrary[$library] }
    foreach ($name in Get-Sorted $byName.Keys) { "| $name | $($byName[$name].License) | $($byName[$name].Source) |" }
    ''
}

function Get-Sorted($Items) {
    $sorted = [Collections.Generic.List[string]]::new([string[]]@($Items))
    $sorted.Sort([StringComparer]::Ordinal)
    $sorted
}

# A license text's lines, without the trailing spaces, the runs of blank lines, the blank lines at either end, nor a
# line that only names the library, as Fluent's licenses each begin.
function Read-Text([string]$Path, [string]$Name) {
    $lines = (Get-Content $Path -Raw) -replace "`r", '' -split "`n" | ForEach-Object { $_.TrimEnd() } | Where-Object { $_.Trim() -ne $Name }
    (($lines -join "`n") -replace "`n{3,}", "`n`n").Trim("`n") -split "`n"
}

# Each license text once, keyed by its words without its copyright lines; each copyright line once, where it was said.
function Add-Text($Texts, [string]$Kind, [string[]]$Lines, [string]$Library, [string[]]$Copyrights) {
    # A notice's copyright lines each belong to the part they head, so a notice stays as it was written.
    $isCopyright = { param($line) $Kind -ne 'NOTICE' -and $line -match $copyrightLine -and $line -notmatch $placeholder }
    $own = @($Lines | Where-Object { & $isCopyright $_ } | ForEach-Object Trim)
    $body = @($Lines | Where-Object { -not (& $isCopyright $_) })
    $body = ((($body -join "`n") -replace "`n{3,}", "`n`n").Trim("`n"))
    $key = "$Kind|$(($body -replace '\s+', ' ').Trim())"
    if (-not $Texts.ContainsKey($key)) {
        $Texts[$key] = [pscustomobject]@{
            Kind = $Kind; Body = $body
            Libraries = [Collections.Generic.HashSet[string]]::new(); Copyrights = [Collections.Generic.HashSet[string]]::new()
        }
    }
    [void]$Texts[$key].Libraries.Add($Library)
    $given = @($Copyrights | Where-Object { $_ } | ForEach-Object { if ($_ -match $copyrightLine) { $_ } else { "Copyright (c) $_" } })
    foreach ($line in $own + $given) { [void]$Texts[$key].Copyrights.Add(($line -replace '\s+', ' ').Trim()) }
}

function Get-Texts($Rows) {
    $texts = @{}
    foreach ($row in $Rows) {
        $library = $row.Name
        if ($row.Licenses.Count) {
            foreach ($file in $row.Licenses) { Add-Text $texts $row.License (Read-Text $file $row.Name) $library @() }
        }
        else {
            $template = Join-Path $templates "$($row.License).txt"
            if (-not (Test-Path $template)) {
                throw "$library holds no license file, and scripts/licenses has no $($row.License).txt: add SPDX's text of it."
            }
            Add-Text $texts $row.License (Read-Text $template $row.Name) "$library, whose package holds no license file" @($row.Copyright)
        }
        foreach ($file in $row.Notices) { Add-Text $texts 'NOTICE' (Read-Text $file $row.Name) $library @() }
    }
    $texts.Values
}

function Format-Texts($Rows) {
    '## License texts'
    ''
    ('Each text once, after the libraries under it and every copyright line they carry. A library whose package holds ' +
        'no license file has SPDX''s text of the license its package names, with the copyright its package states, or its author.')
    ''
    $byKind = @{}
    foreach ($text in Get-Texts $Rows) {
        if (-not $byKind.ContainsKey($text.Kind)) { $byKind[$text.Kind] = @{} }
        $byKind[$text.Kind][@(Get-Sorted $text.Libraries)[0]] = $text
    }
    foreach ($kind in Get-Sorted $byKind.Keys) {
        $all = @(Get-Sorted $byKind[$kind].Keys | ForEach-Object { $byKind[$kind][$_] })
        for ($i = 0; $i -lt $all.Count; $i++) {
            $text = $all[$i]
            "### $kind$(if ($all.Count -gt 1) { ", text $($i + 1) of $($all.Count)" })"
            ''
            (Get-Sorted $text.Libraries) -join '; '
            ''
            '````text'
            Get-Sorted $text.Copyrights
            if ($text.Copyrights.Count) { '' }
            $text.Body -split "`n"
            '````'
            ''
        }
    }
}

$sensors = @(Get-NuGetRows 'Thermi.SensorHost')
$thermi = @(Get-NuGetRows 'Thermi')
$page = @(Get-NpmRows)
# Each library and version once, in one order, so the text a license shows, its first library's, is the same on every run.
$shipped = @{}
foreach ($row in $sensors + $thermi + $page) { $shipped["$($row.Name) $($row.Version)"] = $row }

$text = @(
    '# Third-party notices'
    ''
    '<!-- Written by scripts/notices.ps1 from the lock files; scripts/check.ps1 fails when it is out of date. -->'
    ''
    'thermi is MIT licensed. It ships these libraries unmodified; their sources are at the links below, and their licenses at the end.'
    ''
    Format-Table 'thermi-sensors' 'The sensor service.' $sensors
    Format-Table 'thermi' 'The overlay and the settings window.' $thermi
    Format-Table 'The settings window''s page' 'Built into thermi.exe; only what the page imports ends up in it.' $page
    ('thermi does not ship the PawnIO driver that LibreHardwareMonitor uses, nor the WebView2 runtime: the driver comes from its own release ' +
        'when the window installs it (`docs/adr/0010-driver-and-service-from-the-window.md`), ' +
        'and the runtime with Windows or from Microsoft (`scripts/install.ps1`).')
    ''
    Format-Texts @(Get-Sorted $shipped.Keys | ForEach-Object { $shipped[$_] })
) -join "`r`n"
$text = $text.TrimEnd() + "`r`n"

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
