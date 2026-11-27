#Requires -Version 7
<#
.SYNOPSIS
    Prints, in Markdown, the checks a person runs on a release's installed build before it goes out, from
    docs/RELEASES.md's tables under "Before each release", each with the requirements of the specs that name it on a
    "Release check: <check>." line. The Release workflow shows it in its run's summary, which the owner reads while the
    run waits for approval.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# Each check's name in bold, under the heading of its area.
function Read-ReleaseChecks {
    $within = $false
    $area = $null
    foreach ($line in Get-Content (Join-Path $root 'docs' 'RELEASES.md')) {
        if ($line -match '^## ') { $within = $line -eq '## Before each release' }
        elseif ($within -and $line -match '^### (.+)$') { $area = $Matches[1] }
        elseif ($within -and $line -match '^\| \*\*(.+?)\*\* \|') { [pscustomobject]@{ Name = $Matches[1]; Area = $area } }
    }
}

# Per check, the specs' requirements whose indented "Release check: <check>." line names it, as "001 R5".
function Read-Covered {
    $covered = @{}
    foreach ($folder in Get-ChildItem (Join-Path $root 'docs' 'specs') -Directory | Sort-Object Name) {
        $requirement = $null
        foreach ($line in Get-Content (Join-Path $folder.FullName 'spec.md')) {
            if ($line -match '^- (R\d+)\.') { $requirement = "$($folder.Name.Substring(0, 3)) $($Matches[1])" }
            elseif ($line -match '^\s+Release check: (.+)\.$') { $covered[$Matches[1]] += @($requirement) }
        }
    }
    $covered
}

$covered = Read-Covered
@(
    '## 🧰 Release checks · by a person, on the installed build'
    ''
    '| Check | Area | Requirements it covers |'
    '| --- | --- | --- |'
    foreach ($check in Read-ReleaseChecks) {
        $covers = if ($covered.ContainsKey($check.Name)) { ($covered[$check.Name] | Select-Object -Unique) -join ' · ' } else { '–' }
        "| $($check.Name) | $($check.Area) | $covers |"
    }
    ''
    '_How to run each: [docs/RELEASES.md](https://github.com/claudiopi/thermi/blob/main/docs/RELEASES.md#before-each-release)._'
) -join "`n"
