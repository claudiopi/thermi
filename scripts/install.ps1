#Requires -Version 7
<#
.SYNOPSIS
Builds thermi from this repo and installs it on this PC, or removes it with -Uninstall.

.DESCRIPTION
Run it as yourself, not as administrator: it builds as you and asks for admin rights (UAC) only to copy
the files to Program Files and have the build's thermi-sensors.exe --adopt them, as the MSI does: it registers the
thermi-sensors service and its event log source (see docs/adr/0003-sensor-service.md) and the thermi-elevated service
that runs the window's fixes. It also installs the WebView2 runtime the settings window needs, only where it is missing.
A first install starts the overlay at your sign-in; a reinstall keeps "Start with Windows" as the settings window
left it. Your config in %APPDATA%\thermi is never touched. -Uninstall removes the rest: the files, the service, the
folder earlier builds kept the driver's installer in, %ProgramData%\thermi, and the window's data in %LOCALAPPDATA%\thermi. It leaves the sensor
driver, which other programs may use.
thermi is laid out as updates keep it (docs/adr/0008-updates.md): each version self-contained in a folder of its own, the
service run from it, and launchers at the top that start it. A build never updates itself, unless -UpdatesFrom gives it a
channel's address, as `scripts/updates.ps1 serve` answers, and -UpdatesKeys the folder whose release.pub.pem and
backup.pub.pem it trusts, and -UpdatesSigners the SHA-256 thumbprints of the certificates, separated by semicolons,
one of which must sign every version's catalog.
#>
param([switch]$Uninstall, [string]$UpdatesFrom, [string]$UpdatesKeys, [string]$UpdatesSigners)

$ErrorActionPreference = 'Stop'
$target = Join-Path $env:ProgramFiles 'thermi'
$service = 'thermi-sensors'
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
        while (($window = [ThermiInstall.Windows]::FindWindowEx([IntPtr]::Zero, $window, $class, $null)) -ne [IntPtr]::Zero) {
            $processId = [uint32]0
            [ThermiInstall.Windows]::GetWindowThreadProcessId($window, [ref]$processId) | Out-Null
            if ($ids -contains $processId) { [ThermiInstall.Windows]::PostMessage($window, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
        }
    }
    $running | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    $running | Where-Object { -not $_.HasExited } | Stop-Process -Force
}

# The version's own thermi-sensors.exe lays it down, as the MSI's does (docs/adr/0008-updates.md): beside the versions an
# update kept, both services pointed at it, its launchers at the top. It runs from a copy only administrators write,
# checked against the hash taken before the prompt.
function Install-Version([string]$From, [string]$Hash) {
    New-Item $target -ItemType Directory -Force | Out-Null
    $copy = Copy-Verified $From $Hash (Join-Path $target "install-$([guid]::NewGuid().ToString('N'))")
    try {
        Stop-Overlay
        & (Join-Path $copy 'thermi-sensors.exe') --adopt
        if ($LASTEXITCODE -ne 0) { throw "thermi-sensors.exe --adopt failed (exit code $LASTEXITCODE), and left the thermi installed before." }
        & (Join-Path $copy 'thermi-sensors.exe') --forget
        if ($LASTEXITCODE -ne 0) { Write-Warning "thermi-sensors.exe --forget failed (exit code $LASTEXITCODE): the next install deletes its record." }
    }
    finally {
        Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# The version the service runs takes thermi off, as the MSI's uninstall has it; the folder it ran from goes once it ended.
function Remove-Thermi {
    # The MSI owns what it installed, and Installed apps would keep an entry whose uninstall finds nothing.
    $app = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\thermi' -Name UninstallString -ErrorAction SilentlyContinue
    if ($app.UninstallString -like 'MsiExec.exe*') { throw 'thermi was installed by its MSI: uninstall it from Installed apps.' }
    Stop-Overlay
    $image = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$service" -Name ImagePath -ErrorAction SilentlyContinue).ImagePath
    $sensors = if ($image) { $image.Split('"', [StringSplitOptions]::RemoveEmptyEntries)[0] }
    if ($sensors -and (Test-Path $sensors)) {
        & $sensors --remove
        if ($LASTEXITCODE -ne 0) {
            throw "$sensors --remove failed (exit code $LASTEXITCODE). A thermi from before version folders needs this build installed first."
        }
    }
    Get-Process thermi, thermi-sensors -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target\*" } | Wait-Process -Timeout 30
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
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
        if ($From) { Install-Version $From $Hash }
        else {
            Remove-Thermi
            Remove-Item (Join-Path $env:ProgramData 'thermi') -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ($From -and -not $HasWebView2) {
            # thermi is installed by then, and the overlay works without the runtime: only the settings window needs it.
            try { Install-WebView2 } catch { exit 3 }
        }
    }
    catch {
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
    $names = 'Stop-Overlay', 'Install-Version', 'Remove-Thermi', 'Install-WebView2', 'Get-TreeHash', 'Copy-Verified', 'Install-Elevated'
    $functions = foreach ($name in $names) {
        "function $name {$((Get-Command $name -CommandType Function).Definition)}"
    }
    $command = @(
        "`$env:PSModulePath = Join-Path `$PSHOME 'Modules'"
        "`$ErrorActionPreference = 'Stop'"
        "`$target = $(& $quoted $target)"
        "`$service = $(& $quoted $service)"
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

$root = Split-Path $PSScriptRoot -Parent
$staging = Join-Path $root 'artifacts/publish'
. (Join-Path $PSScriptRoot 'publish.ps1')
Publish-Thermi $staging (Get-Processor) '' $UpdatesFrom $UpdatesKeys $UpdatesSigners
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
