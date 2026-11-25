#Requires -Version 7
<#
.SYNOPSIS
Builds thermi from this repo and installs it on this PC, or removes it with -Uninstall.

.DESCRIPTION
Run it as yourself, not as administrator: it builds as you and asks for admin rights (UAC) only to copy
the files to Program Files and register the thermi-sensors service and its event log source (see
docs/adr/0003-sensor-service.md) and the thermi-elevated service that runs the window's fixes, and to install the
WebView2 runtime the settings window needs, only where it is missing.
A first install starts the overlay at your sign-in; a reinstall keeps "Start with Windows" as the settings window
left it. Your config in %APPDATA%\thermi is never touched. -Uninstall removes the rest: the files, the service, the
folder earlier builds kept the driver's installer in, %ProgramData%\thermi, and the window's data in %LOCALAPPDATA%\thermi. It leaves the sensor
driver, which other programs may use.
#>
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$target = Join-Path $env:ProgramFiles 'thermi'
$service = 'thermi-sensors'
# The one privilege the service keeps of LocalSystem's: the sensors read the same with it alone as with all of them
# (docs/adr/0003-sensor-service.md).
$servicePrivileges = 'SeChangeNotifyPrivilege'
# Runs the window's fixes as SYSTEM, started through sc.exe behind a UAC prompt; only administrators and SYSTEM may start
# it, and interactive users only read its state, the fix's exit code among it
# (docs/adr/0010-driver-and-service-from-the-window.md).
$elevatedService = 'thermi-elevated'
$elevatedRights = 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLORC;;;IU)(A;;CCLCSWLORC;;;SU)'
# The settings window's "Start with Windows" switches this same value (src/Thermi/Ui/StartWithWindows.cs), and
# Task Manager's Startup tab turns it off in the second key without deleting it.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$approvedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'

# The thermi this installer started, which holds its files; one started elevated only the elevated step can see. Each
# is asked to close first, as its tray's "Exit thermi" asks, so its tray icon goes with it and a settings write still
# waiting lands; one still running 5 s later is ended.
function Stop-Overlay {
    $running = @(Get-Process thermi -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target\*" })
    if (-not $running) { return }
    if (-not ('ThermiInstall.Windows' -as [type])) {
        Add-Type -Namespace ThermiInstall -Name Windows -MemberDefinition (
            '[DllImport("user32.dll", CharSet = CharSet.Unicode)] ' +
            'public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);' +
            '[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);' +
            '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);')
    }
    $ids = $running.Id
    # The overlay's hidden host window and the settings window; WM_CLOSE is 0x10.
    foreach ($class in 'thermi-host', 'thermi-window') {
        $window = [IntPtr]::Zero
        while (($window = [ThermiInstall.Windows]::FindWindowEx([IntPtr]::Zero, $window, $class, [NullString]::Value)) -ne [IntPtr]::Zero) {
            $processId = [uint32]0
            [ThermiInstall.Windows]::GetWindowThreadProcessId($window, [ref]$processId) | Out-Null
            if ($ids -contains $processId) { [ThermiInstall.Windows]::PostMessage($window, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
        }
    }
    $running | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    $running | Where-Object { -not $_.HasExited } | Stop-Process -Force
}

function Remove-Service {
    # A fix running now ends on its own within minutes; it is never stopped halfway.
    if (Get-Service $elevatedService -ErrorAction SilentlyContinue) {
        sc.exe delete $elevatedService | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not remove the $elevatedService service (sc.exe exit code $LASTEXITCODE)." }
    }
    if (Get-Service $service -ErrorAction SilentlyContinue) {
        Stop-Service $service -Force
        sc.exe delete $service | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not remove the $service service (sc.exe exit code $LASTEXITCODE)." }
    }
    Stop-Overlay
    # A stopped service's process may still be closing, holding its files; a service shown in the
    # Services window is only marked for deletion until that window closes.
    Get-Process thermi, thermi-sensors -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target\*" } | Wait-Process -Timeout 30
    for ($wait = 0; (Get-Service $service -ErrorAction SilentlyContinue) -and $wait -lt 50; $wait++) { Start-Sleep -Milliseconds 200 }
    if (Get-Service $service -ErrorAction SilentlyContinue) { throw "The $service service is still registered: close the Services window and try again." }
    # The service writes its failures to the Application log under its own name.
    if ([System.Diagnostics.EventLog]::SourceExists($service)) { [System.Diagnostics.EventLog]::DeleteEventSource($service) }
}

function Register-Service {
    $sensors = Join-Path $target 'thermi-sensors.exe'
    New-Service $service -BinaryPathName "`"$sensors`"" -DisplayName 'thermi sensors' -StartupType Automatic `
        -Description 'Reads hardware sensors for the thermi overlay.' | Out-Null
    sc.exe failure $service reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not set the $service service's restarts (sc.exe exit code $LASTEXITCODE)." }
    # A service that stops itself with an error code counts as failed too, so it gets restarted.
    sc.exe failureflag $service 1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not have the $service service restarted after an error (sc.exe exit code $LASTEXITCODE)." }
    sc.exe privs $service $servicePrivileges | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not limit the $service service's privileges (sc.exe exit code $LASTEXITCODE)." }
    # A 2 MB young generation (the value is hex) keeps the service in its memory budget
    # (docs/adr/0011-sensor-service-memory-budget.md). .NET reads it only from the environment; this one is the
    # service's alone, and goes when its key is deleted.
    $serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$service"
    New-ItemProperty $serviceKey -Name Environment -PropertyType MultiString -Value @('DOTNET_GCgen0size=200000') | Out-Null
    [System.Diagnostics.EventLog]::CreateEventSource($service, 'Application')
    New-Service $elevatedService -BinaryPathName "`"$sensors`" --elevated" -DisplayName 'thermi fixes' -StartupType Manual `
        -Description "Installs the sensor driver or starts thermi's sensors, when an administrator asks from thermi." | Out-Null
    sc.exe sdset $elevatedService $elevatedRights | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not limit who starts the $elevatedService service (sc.exe exit code $LASTEXITCODE)." }
    Start-Service $service
}

# The old files wait beside the new ones until the new service is registered, so an install that fails there puts them
# back as they were, service and all.
function Install-Service([string]$From) {
    Remove-Service
    $old = "$target.old"
    if (Test-Path $old) { Remove-Item $old -Recurse -Force }
    # Setting the old files aside fails too, when something holds one open: the service, deleted by then, comes back.
    try {
        if (Test-Path $target) { Move-Item $target $old }
        if ($From) {
            Move-Item $From $target
            Register-Service
        }
    }
    catch {
        $failure = $_
        Restore-Service $old
        throw $failure
    }
    if (Test-Path $old) { Remove-Item $old -Recurse -Force }
}

# What failed is what the installer tells, so a restore that fails too only warns. Old files never set aside are still
# in place, and only registered again.
function Restore-Service([string]$Old) {
    try {
        Remove-Service
        if (Test-Path $Old) {
            Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
            if (Test-Path $target) {
                # A file held open keeps the folder, as it kept a move aside that copied the rest: what went comes back
                # from the old files, and the held one is the same old file.
                Get-ChildItem $Old -Force | Copy-Item -Destination $target -Recurse -Force -ErrorAction SilentlyContinue
                Remove-Item $Old -Recurse -Force
            }
            else { Move-Item $Old $target }
        }
        if (Test-Path (Join-Path $target 'thermi-sensors.exe')) { Register-Service }
    }
    catch {
        Write-Warning "The thermi installed before could not be put back: $($_.Exception.Message)"
    }
}

# The settings window is a page in WebView2 (docs/adr/0007-settings-window-in-webview2-and-react.md), whose Evergreen
# runtime Windows 11 ships. Microsoft's way to tell it is there: a version under its client id, for every user or for
# this one; 0.0.0.0 is what an uninstall leaves. Asked before the prompt, since the administrator who answers it may be
# another account, with another HKCU.
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
    # Hidden files too, as the hash counts them.
    Copy-Item (Join-Path $From '*') $To -Recurse -Force
    # A link copied as a link would still point into a folder the user can write.
    if ((Get-ChildItem $To -Recurse -Force -Attributes ReparsePoint) -or (Get-TreeHash $To) -ne $Hash) {
        Remove-Item $To -Recurse -Force
        throw 'The files to install changed after the build, so nothing was installed. Run the installer again.'
    }
    return $To
}

# The elevated half. Takes the folder to install from, or nothing to uninstall, the hash of its files, and whether the
# user has the WebView2 runtime. Uninstalling also removes %ProgramData%\thermi, where earlier builds kept the
# driver's installer (docs/adr/0010-driver-and-service-from-the-window.md). Its error is said in its own window, which waits to be read:
# nothing it writes may land where the user writes, where a link could send it anywhere.
function Install-Elevated([string]$From, [string]$Hash, [bool]$HasWebView2) {
    try {
        # Program Files, which only administrators write, on the same volume, so the install moves it into place.
        if ($From) { $From = Copy-Verified $From $Hash "$target.new" }
        Install-Service $From
        if (-not $From) { Remove-Item (Join-Path $env:ProgramData 'thermi') -Recurse -Force -ErrorAction SilentlyContinue }
        elseif (-not $HasWebView2) {
            # thermi is installed by then, and the overlay works without the runtime: only the settings window needs it.
            try { Install-WebView2 } catch { exit 3 }
        }
    }
    catch {
        Remove-Item "$target.new" -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "thermi's administrator step failed: $($_.Exception.Message)" -ForegroundColor Red
        # Without a console to read, as in a test, the window closes at once.
        try { Read-Host 'Press Enter to close this window' | Out-Null } catch [System.Management.Automation.PSInvalidOperationException] { }
        exit 1
    }
}

# The administrator step runs in this PowerShell, so it must be one only administrators could have put in place, signed
# as it shipped: a pwsh.exe in a folder the user writes could be swapped while the prompt waits.
function Get-TrustedPwsh([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith("$env:ProgramFiles\", [StringComparison]::OrdinalIgnoreCase) -or (Get-AuthenticodeSignature $full).Status -ne 'Valid') {
        throw "$full is not a PowerShell installed for every user, so it cannot run the administrator step. " +
            'Install PowerShell 7 from Microsoft and run this from it.'
    }
    $full
}

# A process UAC starts takes its environment from the user's registry, which any of their programs can write, and .NET
# loads a profiler, a startup hook or more assemblies from it before PowerShell runs a line. With one of those named
# there, nothing is asked; the elevated pwsh reads the registry again as the prompt closes, which this cannot see
# (docs/adr/0010-driver-and-service-from-the-window.md).
function Assert-NoCodeLoadingVariable([string[]]$Names) {
    $pattern = '^(CORECLR_ENABLE_PROFILING|CORECLR_PROFILER|COR_ENABLE_PROFILING|COR_PROFILER|' +
        'DOTNET_STARTUP_HOOKS|DOTNET_ADDITIONAL_DEPS|DOTNET_ROOT|DOTNET_SHARED_STORE)'
    $loading = @($Names | Where-Object { $_ -match $pattern })
    if ($loading) {
        throw "Your environment sets $($loading -join ', '), which would load code into the administrator step: remove them and run this again."
    }
}

# This script and the user's modules stay the user's to write while the UAC prompt waits, so the elevated half is
# written whole to $Script before the prompt, with its arguments in it, and loads modules only from PowerShell's own. The
# command line, which Windows caps at 32,767 characters, carries only its SHA-256: the administrator reads the file once
# and runs those bytes only if they match, so a file swapped while the prompt waits runs nothing.
function Get-ElevatedCommand([string]$From, [string]$Hash, [bool]$HasWebView2, [string]$Script) {
    $quoted = { param([string]$Text) "'$($Text -replace "'", "''")'" }
    $names = 'Stop-Overlay', 'Remove-Service', 'Register-Service', 'Install-Service', 'Restore-Service',
        'Install-WebView2', 'Get-TreeHash', 'Copy-Verified', 'Install-Elevated'
    $functions = foreach ($name in $names) {
        "function $name {$((Get-Command $name -CommandType Function).Definition)}"
    }
    $command = @(
        "`$env:PSModulePath = Join-Path `$PSHOME 'Modules'"
        "`$ErrorActionPreference = 'Stop'"
        "`$target = $(& $quoted $target)"
        "`$service = $(& $quoted $service)"
        "`$servicePrivileges = $(& $quoted $servicePrivileges)"
        "`$elevatedService = $(& $quoted $elevatedService)"
        "`$elevatedRights = $(& $quoted $elevatedRights)"
        $functions
        "Install-Elevated $(& $quoted $From) $(& $quoted $Hash) `$$HasWebView2"
    ) -join "`n"
    $bytes = [Text.Encoding]::UTF8.GetBytes($command)
    [IO.File]::WriteAllBytes($Script, $bytes)
    $sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    $check = "`$bytes = [IO.File]::ReadAllBytes($(& $quoted $Script))`n" +
        "if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(`$bytes)) -ne '$sha') { exit 4 }`n" +
        "& ([scriptblock]::Create([Text.Encoding]::UTF8.GetString(`$bytes)))"
    [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($check))
}

function Invoke-Elevated([string]$From, [string]$Hash, [bool]$HasWebView2) {
    $pwsh = Get-TrustedPwsh (Join-Path $PSHOME 'pwsh.exe')
    Assert-NoCodeLoadingVariable @(
        (Get-Item 'HKCU:\Environment').Property
        (Get-Item 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').Property)
    $script = Join-Path ([IO.Path]::GetTempPath()) "thermi-install-$([guid]::NewGuid()).ps1"
    try {
        $arguments = @('-NoProfile', '-EncodedCommand', (Get-ElevatedCommand $From $Hash $HasWebView2 $script))
        $elevated = Start-Process $pwsh -Verb RunAs -Wait -PassThru -ArgumentList $arguments
    }
    finally {
        Remove-Item $script -ErrorAction SilentlyContinue
    }
    if ($elevated.ExitCode -eq 4) { throw 'The administrator step changed while the prompt waited, so nothing was installed. Run the installer again.' }
    elseif ($elevated.ExitCode -eq 3) {
        Write-Warning ('thermi is installed, but the WebView2 runtime could not be: the settings window needs it. ' +
            'Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and run thermi settings.')
    }
    elseif ($elevated.ExitCode -ne 0) { throw "The administrator step failed (exit code $($elevated.ExitCode)); its window said why." }
}

$firstInstall = -not (Test-Path (Join-Path $target 'thermi.exe'))
$startsWithWindows = $null -ne (Get-ItemProperty $runKey -Name thermi -ErrorAction SilentlyContinue)

if ($Uninstall) {
    Stop-Overlay
    Remove-ItemProperty $runKey -Name thermi -ErrorAction SilentlyContinue
    Remove-ItemProperty $approvedKey -Name thermi -ErrorAction SilentlyContinue
    Invoke-Elevated '' '' $true
    # The window's WebView2 profile, its place on screen and the log; the settings stay.
    Remove-Item (Join-Path $env:LOCALAPPDATA 'thermi') -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'thermi is uninstalled. Your settings are still in' (Join-Path $env:APPDATA 'thermi')
    Write-Host 'The sensor driver, PawnIO, stays: other programs may use it. If none does, remove it from Installed apps.'
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
    # Without symbols or API docs, which nothing installed reads.
    dotnet publish $path -c Release -r "win-$arch" --self-contained false --no-restore -o $staging --nologo -v quiet `
        -p:DebugType=none -p:PublishDocumentationFile=false -p:PublishReferencesDocumentationFiles=false
    if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed." }
}

# Only now, so a build that fails leaves the running thermi alone.
Stop-Overlay
Invoke-Elevated $staging (Get-TreeHash $staging) (Test-WebView2)

$overlay = Join-Path $target 'thermi.exe'
Start-Process $overlay
if ($firstInstall -or $startsWithWindows) {
    Set-ItemProperty $runKey -Name thermi -Value "`"$overlay`""
    Write-Host "thermi is installed in $target and starts when you sign in."
}
else {
    Write-Host "thermi is installed in $target. It does not start with Windows, as its settings say."
}
