# Dot-sourced by scripts/stats.ps1: how the dashboard writes a number, a change, a bar and a trend, in text a job's
# summary renders as it is. A number no step measured shows as a dash.

function Format-Count($Value) { if ($null -eq $Value) { '–' } else { '{0:n0}' -f [double]$Value } }

function Format-Percent($Value, [int]$Decimals = 1) { if ($null -eq $Value) { '–' } else { "{0:n$Decimals} %" -f [double]$Value } }

function Format-Megabytes($Value) { if ($null -eq $Value) { '–' } else { '{0:n1} MB' -f [double]$Value } }

function Format-Duration($Seconds) {
    if ($null -eq $Seconds) { return '–' }
    $whole = [int][Math]::Round([double]$Seconds)
    if ($whole -lt 60) { return "${whole}s" }
    '{0}m {1:00}s' -f [Math]::Floor($whole / 60), ($whole % 60)
}

# The change from main's last: ▲ up, ▼ down, = none.
function Format-Delta($Now, $Before, [int]$Decimals = 0, [string]$Unit = '', [switch]$Duration) {
    if ($null -eq $Now) { return 'not measured' }
    if ($null -eq $Before) { return 'first measure' }
    $change = [double]$Now - [double]$Before
    if ([Math]::Abs($change) -lt 0.5 * [Math]::Pow(10, -$Decimals)) { return '= 0' }
    $size = if ($Duration) { Format-Duration ([Math]::Abs($change)) } else { "{0:n$Decimals}$Unit" -f [Math]::Abs($change) }
    "$(if ($change -gt 0) { '▲' } else { '▼' }) $size"
}

function Get-Bar($Percent, [int]$Width = 10) {
    if ($null -eq $Percent) { return '' }
    $filled = [int][Math]::Round([Math]::Clamp([double]$Percent, 0, 100) / 100 * $Width)
    ('█' * $filled) + ('░' * ($Width - $filled))
}

function Get-Dot($Value, [double]$Good, [double]$Fair) {
    if ($null -eq $Value) { '⚪' } elseif ($Value -ge $Good) { '🟢' } elseif ($Value -ge $Fair) { '🟡' } else { '🔴' }
}

function Get-BadgeColor($Value, [double]$Good, [double]$Fair) {
    if ($null -eq $Value) { 'lightgrey' } elseif ($Value -ge $Good) { 'brightgreen' } elseif ($Value -ge $Fair) { 'yellow' } else { 'red' }
}

# One block per value, from the lowest to the highest of them; a gap where a push measured nothing.
function Get-Spark([object[]]$Values) {
    $known = @($Values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ })
    if (-not $known) { return '–' }
    $low = ($known | Measure-Object -Minimum).Minimum
    $span = ($known | Measure-Object -Maximum).Maximum - $low
    $blocks = '▁▂▃▄▅▆▇█'
    -join ($Values | ForEach-Object {
            if ($null -eq $_) { ' ' } elseif ($span -eq 0) { $blocks[3] } else { $blocks[[int][Math]::Round(([double]$_ - $low) / $span * 7)] }
        })
}

function Format-Status([string]$Status) {
    switch ($Status) { 'done' { '🟢 done' } 'approved' { '🔵 approved' } 'draft' { '⚪ draft' } default { $Status } }
}

function Format-CoverageRow([string]$Name, $Lines, $Branches, [string]$By) {
    "| ``$Name`` | $(Format-Percent $Lines) | $(Format-Percent $Branches) | $(Get-Dot $Lines 90 75) $(Get-Bar $Lines) | $By |"
}

# A job's name as its row shows it: a called workflow's jobs without the caller's name.
function Get-JobLabel([string]$Name) { $Name -replace '^[^/]+ / ', '' }
