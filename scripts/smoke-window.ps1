#Requires -Version 7
<#
.SYNOPSIS
Opens the settings window of a build with WebView2's debugging port open, clicks through every screen, and fails on any
error the page shows or logs.

.DESCRIPTION
For the look over a build before a person gets it. The window reads the real settings, and the clicks only select;
nothing is edited. Close any open settings window first, or the new one only brings it to the front.
#>
param(
    # The thermi.exe to open the window of; by default the one installed.
    [string]$Exe = (Join-Path $env:ProgramFiles 'thermi\thermi.exe'),
    [int]$Port = 9222
)

$ErrorActionPreference = 'Stop'
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$Port"
$window = Start-Process $Exe -ArgumentList 'settings' -PassThru
try {
    # The page answers on the port once WebView2 has started.
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-NetConnection 127.0.0.1 -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue)) {
        if ((Get-Date) -gt $deadline) { throw "The window opened no debugging port $Port within 30 s." }
        Start-Sleep -Milliseconds 500
    }
    node (Join-Path $PSScriptRoot 'smoke-window.mjs') $Port
    if ($LASTEXITCODE -ne 0) { throw 'The window showed an error.' }
}
finally {
    if (-not $window.HasExited) { $window.CloseMainWindow() | Out-Null }
}
