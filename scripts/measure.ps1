#Requires -Version 7
<#
.SYNOPSIS
Measures what thermi's processes cost for a while: CPU as a percent of one core and private memory, for the overlay,
the settings window with its WebView2 processes, and the sensor service, then checks each against its budget.

.DESCRIPTION
Budgets: the overlay under 48 MB private and 0.5 % of one core (docs/adr/0001-architecture.md), the window open and
idle under 1 % with its page's renderer (spec 002's R191; WebView2's browser, GPU and helper processes are reported apart), the service under 80 MB private
and 3 % of one core (docs/adr/0011-sensor-service-memory-budget.md). It measures what runs; with -Window it
opens the window of -Exe first, waits for it to settle and closes it after, if it opened it. Nothing is edited, and the
window reads the real settings as smoke-window.ps1's does. The service runs as LocalSystem, so its CPU is read from the
performance counters, which need no administrator; the others from their processes. Exits 1 when a budget is broken.
#>
param(
    # The thermi.exe whose overlay and window are measured; by default the one installed.
    [string]$Exe = (Join-Path $env:ProgramFiles 'thermi\thermi.exe'),
    [int]$Seconds = 60,
    # Open the window before measuring, and close it after.
    [switch]$Window,
    # How long the window has to load and settle before it is measured.
    [int]$Settle = 15
)

$ErrorActionPreference = 'Stop'
$percent = 100.0
$budgets = @{ Overlay = 0.5; Window = 1.0 }
$megabytes = @{ Overlay = 48 }
$serviceMegabytes = 80
$serviceCpu = 3.0

function Get-Thermi {
    $all = Get-CimInstance Win32_Process
    $mine = $all | Where-Object { $_.ExecutablePath -eq $Exe }
    $window = $mine | Where-Object { $_.CommandLine -match '\ssettings(\s|$)' }
    $overlay = $mine | Where-Object { $_.CommandLine -notmatch '\s(settings|preview|cpu-load)(\s|$)' }
    # The window's WebView2 processes: its browser process, a child of the window, and that one's children.
    $tree = [System.Collections.Generic.List[int]]::new()
    $window | ForEach-Object { $tree.Add([int]$_.ProcessId) }
    $grew = $true
    while ($grew) {
        $grew = $false
        foreach ($each in $all) {
            if ($tree.Contains([int]$each.ParentProcessId) -and -not $tree.Contains([int]$each.ProcessId)) {
                $tree.Add([int]$each.ProcessId)
                $grew = $true
            }
        }
    }
    # The window's budget holds it and its page's renderer; WebView2's browser, GPU and helper processes are reported apart.
    $renderers = $all | Where-Object { $tree.Contains([int]$_.ProcessId) -and $_.CommandLine -match '--type=renderer' } | ForEach-Object { [int]$_.ProcessId }
    $own = @(@($window.ProcessId) + @($renderers) | Where-Object { $_ })
    $rest = @($tree | Where-Object { $own -notcontains $_ })
    $service = (Get-CimInstance Win32_Service -Filter "Name='thermi-sensors'" -ErrorAction SilentlyContinue).ProcessId
    [pscustomobject]@{ Overlay = @($overlay.ProcessId); Window = $own; WebView2 = $rest; Service = $service }
}

function Get-Cpu([int[]]$Ids) {
    $sum = [TimeSpan]::Zero
    foreach ($id in $Ids) {
        $process = Get-Process -Id $id -ErrorAction SilentlyContinue
        # A process that ended, or one of WebView2's this user may not read, counts nothing.
        if ($process -and $null -ne $process.TotalProcessorTime) { $sum += $process.TotalProcessorTime }
    }
    $sum
}

function Get-Private([int[]]$Ids) {
    ($Ids | ForEach-Object { (Get-Process -Id $_ -ErrorAction SilentlyContinue).PrivateMemorySize64 } | Measure-Object -Sum).Sum / 1MB
}

# The counters' instance of a process, as "thermi-sensors" or "thermi-sensors#1".
function Get-Instance([int]$Id) {
    $samples = (Get-Counter '\Process(thermi-sensors*)\ID Process' -ErrorAction SilentlyContinue).CounterSamples
    $match = $samples | Where-Object { [int]$_.CookedValue -eq $Id } | Select-Object -First 1
    if ($match) { $match.InstanceName }
}

$opened = $null
if ($Window -and -not (Get-Thermi).Window) {
    # One settings window runs at a time, whichever thermi.exe opened it: asked of another, it only comes to the front,
    # and the window measured would be none.
    $other = Get-CimInstance Win32_Process -Filter "Name='thermi.exe'" |
        Where-Object { $_.ExecutablePath -ne $Exe -and $_.CommandLine -match '\ssettings(\s|$)' } | Select-Object -First 1
    if ($other) { throw "The settings window of $($other.ExecutablePath) is open; close it first." }
    $opened = Start-Process $Exe -ArgumentList 'settings' -PassThru
    Start-Sleep -Seconds $Settle
}

try {
    $found = Get-Thermi
    if ($Window -and -not $found.Window) { throw "No settings window of $Exe opened." }
    $instance = if ($found.Service) { Get-Instance $found.Service }
    $parts = 'Overlay', 'Window', 'WebView2'
    $before = @{}; foreach ($part in $parts) { $before[$part] = Get-Cpu $found.$part }
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    if ($instance) {
        $paths = "\Process($instance)\% Processor Time", "\Process($instance)\Private Bytes"
        $samples = (Get-Counter $paths -SampleInterval 1 -MaxSamples $Seconds).CounterSamples
    }
    else {
        Start-Sleep -Seconds $Seconds
    }
    $elapsed = $clock.Elapsed.TotalSeconds
    $after = @{}; foreach ($part in $parts) { $after[$part] = Get-Cpu $found.$part }

    $rows = foreach ($part in $parts) {
        if ($found.$part.Count -eq 0) { continue }
        $cpu = ($after.$part - $before.$part).TotalSeconds / $elapsed * $percent
        $budget = $budgets[$part]
        $memory = $megabytes[$part]
        $private = Get-Private $found.$part
        [pscustomobject]@{
            Part = $part; Processes = $found.$part.Count; CpuPercent = [Math]::Round($cpu, 2)
            PrivateMB = [Math]::Round($private, 1)
            Budget = (@($(if ($memory) { "$memory MB" }), $(if ($budget) { "$budget % CPU" })) | Where-Object { $_ }) -join ', '
            Within = (-not $budget -or $cpu -lt $budget) -and (-not $memory -or $private -lt $memory)
        }
    }
    if ($instance) {
        $cpu = ($samples | Where-Object Path -like '*% processor time' | Measure-Object CookedValue -Average).Average
        $private = ($samples | Where-Object Path -like '*private bytes' | Measure-Object CookedValue -Average).Average / 1MB
        $rows = @($rows) + [pscustomobject]@{
            Part = 'Service'; Processes = 1; CpuPercent = [Math]::Round($cpu, 2); PrivateMB = [Math]::Round($private, 1)
            Budget = "$serviceMegabytes MB, $serviceCpu % CPU"; Within = $private -lt $serviceMegabytes -and $cpu -lt $serviceCpu
        }
    }
    Write-Host "thermi over $([Math]::Round($elapsed)) s, CPU as a percent of one core, from $Exe"
    $rows | Format-Table -AutoSize | Out-Host
    if ($rows | Where-Object { -not $_.Within }) { exit 1 }
}
finally {
    if ($opened -and -not $opened.HasExited) {
        $opened.Refresh()
        $opened.CloseMainWindow() | Out-Null
    }
}
