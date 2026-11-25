#Requires -Version 7
<#
.SYNOPSIS
    Lists the docs that may have gone false with the current change: every Markdown file that names a file the change
    touched, or a type declared in one. RepoRulesTests catches a name that no longer exists; this finds the docs whose
    names still exist but whose words may not hold, which only a reader can judge. The reviewer reads each one it lists.
.EXAMPLE
    pwsh scripts/doc-impact.ps1
    pwsh scripts/doc-impact.ps1 -Base origin/main
#>
param(
    # What the change is measured from: the branch it will merge into.
    [string]$Base = 'main'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$from = git merge-base HEAD $Base
if ($LASTEXITCODE -ne 0) { throw "No common commit with $Base." }
$changed = @(git diff --name-only --find-renames $from) + @(git ls-files --others --exclude-standard) | Where-Object { $_ } | Sort-Object -Unique
# A deleted or renamed file's old path, which a doc may still name.
$removed = @(git diff --diff-filter=DR --find-renames --name-status $from | ForEach-Object { ($_ -split "`t")[1] })

# What a doc would call each changed file: its path, its name, and the types declared in it now or before the change.
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($path in $changed + $removed | Where-Object { $_ -notlike '*.md' -and $_ -notlike '*.json' }) {
    [void]$names.Add($path)
    [void]$names.Add((Split-Path $path -Leaf))
    if ($path -match '\.(cs|ts|tsx)$') {
        [void]$names.Add([IO.Path]::GetFileNameWithoutExtension($path))
        $now = if (Test-Path $path) { Get-Content $path -Raw } else { '' }
        $before = git show "$($from):$path" 2>$null | Out-String
        foreach ($m in [regex]::Matches("$now`n$before", '\b(?:class|record|struct|interface|enum|function|type)\s+([A-Z]\w*)')) {
            [void]$names.Add($m.Groups[1].Value)
        }
    }
}

$docs = git ls-files '*.md' | Where-Object { $_ -ne 'THIRD-PARTY-NOTICES.md' }
$found = foreach ($doc in $docs) {
    $text = Get-Content $doc -Raw
    $hits = @($names | Where-Object { $text -match "(?<![\w/.])$([regex]::Escape($_))(?![\w])" } | Sort-Object)
    if ($hits.Count) { [pscustomobject]@{ Doc = $doc; Changed = $doc -in $changed; Names = $hits -join ', ' } }
}

if (-not $found) {
    Write-Host 'No doc names what this change touched.'
    exit 0
}
Write-Host 'Docs that name what this change touched; read each and say which are now false:'
foreach ($each in $found) {
    $mark = if ($each.Changed) { ' (changed here)' } else { '' }
    Write-Host "  $($each.Doc)$mark"
    Write-Host "      $($each.Names)"
}
