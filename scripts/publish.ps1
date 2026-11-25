#Requires -Version 7
<#
.SYNOPSIS
Builds one version of thermi for one processor as it installs and updates: both programs self-contained in one folder,
with their launchers inside. scripts/install.ps1, the pack and the MSI of scripts/updates.ps1 dot-source it, so all
three build the same files.
#>

# A public key's PEM holds its SubjectPublicKeyInfo in base64, which the service takes as it is.
function Get-PublicKey([string]$Folder, [string]$Name) {
    $pem = Get-Content (Join-Path $Folder $Name) -Raw
    if ($pem -notmatch '-----BEGIN PUBLIC KEY-----([A-Za-z0-9+/=\s]+)-----END PUBLIC KEY-----') { throw "$Name holds no public key." }
    $Matches[1] -replace '\s', ''
}

function Get-Processor {
    switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
        'X64' { 'x64' }
        'Arm64' { 'arm64' }
        default { throw "thermi runs on x64 or Arm64 Windows, not $_." }
    }
}

# Self-contained, with ReadyToRun for the processor, so both start without compiling their code first. A build given a
# channel's address and keys checks it for updates; one given signers, SHA-256 certificate thumbprints separated by
# semicolons, takes only versions whose catalog one of them signed.
function Publish-Thermi([string]$Out, [string]$Processor, [string]$Version, [string]$UpdatesFrom, [string]$UpdatesKeys, [string]$Signers) {
    $root = Split-Path $PSScriptRoot -Parent
    $properties = @('-p:DebugType=none', '-p:PublishDocumentationFile=false', '-p:PublishReferencesDocumentationFiles=false')
    if ($Version) { $properties += "-p:Version=$Version" }
    if ($UpdatesFrom) {
        if (-not $UpdatesKeys) { throw 'A build that checks for updates needs the folder with the channel''s public keys.' }
        $properties += "-p:ThermiUpdatesFrom=$UpdatesFrom", "-p:ThermiUpdatesKey=$(Get-PublicKey $UpdatesKeys release.pub.pem)",
            "-p:ThermiUpdatesBackupKey=$(Get-PublicKey $UpdatesKeys backup.pub.pem)"
        # MSBuild reads a semicolon in a property as a list's, so it goes escaped.
        if ($Signers) { $properties += "-p:ThermiUpdatesSigners=$($Signers -replace ';', '%3B')" }
    }

    Remove-Item $Out -Recurse -Force -ErrorAction SilentlyContinue
    foreach ($project in 'src/Thermi.Sensors.Windows', 'src/Thermi.Windows') {
        $path = Join-Path $root $project
        # Restored apart, for both processors the project lists and with the ReadyToRun compiler, which only a restore that
        # asks for ReadyToRun downloads: a publish's own restore would put its processor on Thermi.Core too, and rewrite
        # Core's lock file.
        dotnet restore $path --locked-mode -p:PublishReadyToRun=true -p:SelfContained=true --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "Restoring $project failed." }
        # Without symbols or API docs, which nothing installed reads.
        dotnet publish $path -c Release -r "win-$Processor" --self-contained true --no-restore -o $Out --nologo -v quiet @properties
        if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed." }
    }

    # Every program and its launcher, as src/Thermi.Core/Install/InstallLayout.cs lists them: an update without one is refused.
    foreach ($program in 'thermi', 'thermi-cli', 'thermi-sensors') {
        foreach ($file in "$program.exe", "launchers/$program.exe") {
            if (-not (Test-Path (Join-Path $Out $file))) { throw "The publish has no $file." }
        }
    }
}

# The MSI of the version published in $Folder, which Windows Installer takes only with a version of numbers alone:
# per machine, or with -PerUser the per-user one (docs/adr/0016-admin-on-demand.md).
function Build-Msi([string]$Folder, [string]$Processor, [string]$Version, [string]$Out, [switch]$PerUser) {
    $root = Split-Path $PSScriptRoot -Parent
    $numbers = ($Version -split '-')[0]
    $scope, $suffix = if ($PerUser) { 'perUser', '-user' } else { 'perMachine', '' }
    $built = Join-Path $root "artifacts/msi/$Version-$Processor$suffix"
    $project = Join-Path $root 'src/Thermi.Installer'
    dotnet restore $project --locked-mode --nologo -v quiet "-p:Platform=$Processor"
    if ($LASTEXITCODE -ne 0) { throw 'Restoring the installer failed.' }
    # Built whole each time, so the other scope's build in obj\ is never taken for this one's.
    dotnet build $project -c Release -o $built --no-incremental --no-restore --nologo -v quiet "-p:Platform=$Processor" `
        "-p:ThermiVersion=$Version" "-p:ThermiMsiVersion=$numbers" "-p:ThermiPublishDir=$([IO.Path]::GetFullPath($Folder))\" "-p:ThermiScope=$scope"
    if ($LASTEXITCODE -ne 0) { throw "Building the $scope MSI of $Version for $Processor failed." }
    Copy-Item (Join-Path $built "thermi-$Version-$Processor$suffix.msi") $Out -Force
}
