#Requires -Version 7
<#
.SYNOPSIS
Builds thermi from this repo and installs it on this PC, or removes it with -Uninstall.

.DESCRIPTION
Run it as yourself, not as administrator: it builds as you and asks for admin rights (UAC) only to copy
the files to Program Files and register the thermi-sensors service (see docs/adr/0003-sensor-service.md).
The overlay starts at your sign-in. Your config in %APPDATA%\thermi is never touched.
#>
param(
    [switch]$Uninstall,
    # Internal: the elevated half. Takes the folder to install from, or nothing to uninstall, and the
    # file to leave its error in, because its own window closes before anyone can read it.
    [string]$ElevatedFrom,
    [string]$ErrorFile
)

$ErrorActionPreference = 'Stop'
$target = Join-Path $env:ProgramFiles 'thermi'
$service = 'thermi-sensors'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Install-Service([string]$From) {
    if (Get-Service $service -ErrorAction SilentlyContinue) {
        Stop-Service $service -Force
        sc.exe delete $service | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not remove the $service service (sc.exe exit code $LASTEXITCODE)." }
    }
    # An overlay started elevated holds its files too, and only this elevated step can see its path.
    Get-Process thermi -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target\*" } | Stop-Process -Force
    # A stopped service's process may still be closing, holding its files; a service shown in the
    # Services window is only marked for deletion until that window closes.
    Get-Process thermi, thermi-sensors -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target\*" } | Wait-Process -Timeout 30
    for ($wait = 0; (Get-Service $service -ErrorAction SilentlyContinue) -and $wait -lt 50; $wait++) { Start-Sleep -Milliseconds 200 }
    if (Get-Service $service -ErrorAction SilentlyContinue) { throw "The $service service is still registered: close the Services window and try again." }
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    if (-not $From) { return }

    New-Item $target -ItemType Directory | Out-Null
    Copy-Item (Join-Path $From '*') $target -Recurse
    $sensors = Join-Path $target 'thermi-sensors.exe'
    New-Service $service -BinaryPathName "`"$sensors`"" -DisplayName 'thermi sensors' -StartupType Automatic `
        -Description 'Reads hardware sensors for the thermi overlay.' | Out-Null
    sc.exe failure $service reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null
    # A service that stops itself with an error code counts as failed too, so it gets restarted.
    sc.exe failureflag $service 1 | Out-Null
    Start-Service $service
}

if ($PSBoundParameters.ContainsKey('ElevatedFrom')) {
    try {
        Install-Service $ElevatedFrom
    }
    catch {
        Set-Content $ErrorFile $_.Exception.Message -ErrorAction SilentlyContinue
        exit 1
    }
    return
}

function Invoke-Elevated([string]$From) {
    $errorFile = New-TemporaryFile
    try {
        $arguments = @('-NoProfile', '-File', "`"$PSCommandPath`"", '-ElevatedFrom', "`"$From`"", '-ErrorFile', "`"$errorFile`"")
        $elevated = Start-Process pwsh -Verb RunAs -Wait -PassThru -ArgumentList $arguments
        if ($elevated.ExitCode -ne 0) {
            $reason = (Get-Content $errorFile -Raw -ErrorAction SilentlyContinue) ?? "exit code $($elevated.ExitCode)"
            throw "The administrator step failed: $($reason.Trim())"
        }
    }
    finally {
        Remove-Item $errorFile -ErrorAction SilentlyContinue
    }
}

Get-Process thermi -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target\*" } | Stop-Process -Force
Remove-ItemProperty $runKey -Name thermi -ErrorAction SilentlyContinue

if ($Uninstall) {
    Invoke-Elevated ''
    Write-Host 'thermi is uninstalled. Your settings are still in' (Join-Path $env:APPDATA 'thermi')
    return
}

$staging = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/publish'
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
foreach ($project in 'src/Thermi.SensorHost', 'src/Thermi') {
    dotnet publish (Join-Path (Split-Path $PSScriptRoot -Parent) $project) -c Release -o $staging --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed." }
}

Invoke-Elevated $staging

$overlay = Join-Path $target 'thermi.exe'
Set-ItemProperty $runKey -Name thermi -Value "`"$overlay`""
Start-Process $overlay
Write-Host "thermi is installed in $target and starts when you sign in."
