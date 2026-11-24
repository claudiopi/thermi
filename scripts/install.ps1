#Requires -Version 7
<#
.SYNOPSIS
Builds thermi from this repo and installs it on this PC, or removes it with -Uninstall.

.DESCRIPTION
Run it as yourself, not as administrator: it builds as you and asks for admin rights (UAC) only to copy
the files to Program Files and register the thermi-sensors service and its event log source (see
docs/adr/0003-sensor-service.md), and to install the WebView2 runtime the settings window needs, only where it is missing.
A first install starts the overlay at your sign-in; a reinstall keeps "Start with Windows" as the settings window
left it. Your config in %APPDATA%\thermi is never touched.
#>
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$target = Join-Path $env:ProgramFiles 'thermi'
$service = 'thermi-sensors'
# The settings window's "Start with Windows" switches this same value (src/Thermi/Ui/StartWithWindows.cs), and
# Task Manager's Startup tab turns it off in the second key without deleting it.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$approvedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'

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
    # The service writes its failures to the Application log under its own name.
    if ([System.Diagnostics.EventLog]::SourceExists($service)) { [System.Diagnostics.EventLog]::DeleteEventSource($service) }
    if (-not $From) { return }

    Move-Item $From $target
    $sensors = Join-Path $target 'thermi-sensors.exe'
    New-Service $service -BinaryPathName "`"$sensors`"" -DisplayName 'thermi sensors' -StartupType Automatic `
        -Description 'Reads hardware sensors for the thermi overlay.' | Out-Null
    sc.exe failure $service reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null
    # A service that stops itself with an error code counts as failed too, so it gets restarted.
    sc.exe failureflag $service 1 | Out-Null
    # A 2 MB young generation (the value is hex) keeps the service in its memory budget
    # (docs/adr/0011-sensor-service-memory-budget.md). .NET reads it only from the environment; this one is the
    # service's alone, and goes when its key is deleted.
    New-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$service" -Name Environment -PropertyType MultiString -Value @('DOTNET_GCgen0size=200000') | Out-Null
    [System.Diagnostics.EventLog]::CreateEventSource($service, 'Application')
    Start-Service $service
}

# The settings window is a page in WebView2 (docs/adr/0007-settings-window-in-webview2-and-react.md), whose Evergreen
# runtime Windows 11 ships. Microsoft's way to tell it is there: a version under its client id, for every user or for
# this one; 0.0.0.0 is what an uninstall leaves.
function Test-WebView2 {
    $client = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
    foreach ($key in "HKLM:\SOFTWARE\WOW6432Node\$client", "HKCU:\Software\$client") {
        $version = (Get-ItemProperty $key -Name pv -ErrorAction SilentlyContinue).pv
        if ($version -and $version -ne '0.0.0.0') { return $true }
    }
    return $false
}

# Only where the runtime is missing: Microsoft's Evergreen bootstrapper, from its official link, checked to be signed by
# Microsoft and run silently for every user. With the runtime there, nothing is downloaded. It waits where only
# administrators write, not in the user's temp folder, so nothing can swap it between the check and the run.
function Install-WebView2 {
    if (Test-WebView2) { return }
    $setup = Join-Path $target "MicrosoftEdgeWebview2Setup-$([guid]::NewGuid()).exe"
    try {
        Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $setup
        $signature = Get-AuthenticodeSignature $setup
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '\bO=Microsoft Corporation\b') {
            throw "The WebView2 runtime's installer is not signed by Microsoft."
        }
        $setupRun = Start-Process $setup -ArgumentList '/silent', '/install' -Wait -PassThru
        if ($setupRun.ExitCode -ne 0) { throw "Installing the WebView2 runtime failed (exit code $($setupRun.ExitCode))." }
    }
    finally {
        Remove-Item $setup -ErrorAction SilentlyContinue
    }
}

# One SHA-256 over every file's path and SHA-256, sorted ordinally, so a file changed, added or removed changes it, and
# an administrator with another culture computes the same.
function Get-TreeHash([string]$Folder) {
    [string[]]$lines = Get-ChildItem $Folder -File -Recurse -Force | ForEach-Object {
        "$([IO.Path]::GetRelativePath($Folder, $_.FullName))`t$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)"
    }
    [Array]::Sort($lines, [StringComparer]::Ordinal)
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($lines -join "`n")))
}

# The service runs these files as SYSTEM, and the user's build folder can change while the prompt waits: they are copied
# where only administrators write, and checked there against the hash taken before the prompt, before anything is replaced.
function Copy-Verified([string]$From, [string]$Hash, [string]$To) {
    if (Test-Path $To) { Remove-Item $To -Recurse -Force }
    New-Item $To -ItemType Directory | Out-Null
    Copy-Item (Join-Path $From '*') $To -Recurse
    # A link copied as a link would still point into a folder the user can write.
    if ((Get-ChildItem $To -Recurse -Force -Attributes ReparsePoint) -or (Get-TreeHash $To) -ne $Hash) {
        Remove-Item $To -Recurse -Force
        throw 'The files to install changed after the build, so nothing was installed. Run the installer again.'
    }
    return $To
}

# The elevated half. Takes the folder to install from, or nothing to uninstall, the hash of its files, and the file to
# leave its error in, because its own window closes before anyone can read it.
function Install-Elevated([string]$From, [string]$Hash, [string]$ErrorFile) {
    try {
        # Program Files, which only administrators write, on the same volume, so the install moves it into place.
        if ($From) { $From = Copy-Verified $From $Hash "$target.new" }
        Install-Service $From
        if ($From) { Install-WebView2 }
    }
    catch {
        Remove-Item "$target.new" -Recurse -Force -ErrorAction SilentlyContinue
        Set-Content $ErrorFile $_.Exception.Message -ErrorAction SilentlyContinue
        exit 1
    }
}

# This script and the user's modules stay the user's to write while the UAC prompt waits, so the elevated half is
# handed over whole, as text taken before the prompt, with its arguments in it, and loads modules only from PowerShell's own.
function Get-ElevatedCommand([string]$From, [string]$Hash, [string]$ErrorFile) {
    $quoted = { param([string]$Text) "'$($Text -replace "'", "''")'" }
    $functions = foreach ($name in 'Install-Service', 'Test-WebView2', 'Install-WebView2', 'Get-TreeHash', 'Copy-Verified', 'Install-Elevated') {
        "function $name {$((Get-Command $name -CommandType Function).Definition)}"
    }
    $command = @(
        "`$env:PSModulePath = Join-Path `$PSHOME 'Modules'"
        "`$ErrorActionPreference = 'Stop'"
        "`$target = $(& $quoted $target)"
        "`$service = $(& $quoted $service)"
        $functions
        "Install-Elevated $(& $quoted $From) $(& $quoted $Hash) $(& $quoted $ErrorFile)"
    ) -join "`n"
    [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
}

function Invoke-Elevated([string]$From, [string]$Hash) {
    $errorFile = New-TemporaryFile
    try {
        $arguments = @('-NoProfile', '-EncodedCommand', (Get-ElevatedCommand $From $Hash $errorFile))
        $elevated = Start-Process (Join-Path $PSHOME 'pwsh.exe') -Verb RunAs -Wait -PassThru -ArgumentList $arguments
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
$firstInstall = -not (Test-Path (Join-Path $target 'thermi.exe'))
$startsWithWindows = $null -ne (Get-ItemProperty $runKey -Name thermi -ErrorAction SilentlyContinue)

if ($Uninstall) {
    Remove-ItemProperty $runKey -Name thermi -ErrorAction SilentlyContinue
    Remove-ItemProperty $approvedKey -Name thermi -ErrorAction SilentlyContinue
    Invoke-Elevated '' ''
    Write-Host 'thermi is uninstalled. Your settings are still in' (Join-Path $env:APPDATA 'thermi')
    return
}

$staging = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/publish'
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
# ReadyToRun for this PC's processor, so both start without compiling their code first; the .NET runtime stays shared.
$arch = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    'X64' { 'x64' }
    'Arm64' { 'arm64' }
    default { throw "thermi runs on x64 or Arm64 Windows, not $_." }
}
foreach ($project in 'src/Thermi.SensorHost', 'src/Thermi') {
    $path = Join-Path (Split-Path $PSScriptRoot -Parent) $project
    # Restored apart, for both processors the project lists and with the ReadyToRun compiler, which only a restore that
    # asks for ReadyToRun downloads: a publish's own restore would put its processor on Thermi.Core too, and rewrite
    # Core's lock file.
    dotnet restore $path --locked-mode -p:PublishReadyToRun=true --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Restoring $project failed." }
    dotnet publish $path -c Release -r "win-$arch" --self-contained false --no-restore -o $staging --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed." }
}

Invoke-Elevated $staging (Get-TreeHash $staging)

$overlay = Join-Path $target 'thermi.exe'
Start-Process $overlay
if ($firstInstall -or $startsWithWindows) {
    Set-ItemProperty $runKey -Name thermi -Value "`"$overlay`""
    Write-Host "thermi is installed in $target and starts when you sign in."
}
else {
    Write-Host "thermi is installed in $target. It does not start with Windows, as its settings say."
}
