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
# channel's address and keys checks it for updates; one given a publisher takes only versions whose catalog it signed.
function Publish-Thermi([string]$Out, [string]$Processor, [string]$Version, [string]$UpdatesFrom, [string]$UpdatesKeys, [string]$Publisher) {
    $root = Split-Path $PSScriptRoot -Parent
    $properties = @('-p:DebugType=none', '-p:PublishDocumentationFile=false', '-p:PublishReferencesDocumentationFiles=false')
    if ($Version) { $properties += "-p:Version=$Version" }
    if ($UpdatesFrom) {
        if (-not $UpdatesKeys) { throw 'A build that checks for updates needs the folder with the channel''s public keys.' }
        $properties += "-p:ThermiUpdatesFrom=$UpdatesFrom", "-p:ThermiUpdatesKey=$(Get-PublicKey $UpdatesKeys release.pub.pem)",
            "-p:ThermiUpdatesBackupKey=$(Get-PublicKey $UpdatesKeys backup.pub.pem)"
        if ($Publisher) { $properties += "-p:ThermiUpdatesPublisher=$Publisher" }
    }

    Remove-Item $Out -Recurse -Force -ErrorAction SilentlyContinue
    foreach ($project in 'src/Thermi.SensorHost', 'src/Thermi') {
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
}

# The MSI of the version published in $Folder, which Windows Installer takes only with a version of numbers alone.
function Build-Msi([string]$Folder, [string]$Processor, [string]$Version, [string]$Out) {
    $root = Split-Path $PSScriptRoot -Parent
    $numbers = ($Version -split '-')[0]
    $built = Join-Path $root "artifacts/msi/$Version-$Processor"
    dotnet build (Join-Path $root 'src/Thermi.Installer') -c Release -o $built --nologo -v quiet "-p:Platform=$Processor" `
        "-p:ThermiVersion=$Version" "-p:ThermiMsiVersion=$numbers" "-p:ThermiPublishDir=$([IO.Path]::GetFullPath($Folder))\"
    if ($LASTEXITCODE -ne 0) { throw "Building the MSI of $Version for $Processor failed." }
    Copy-Item (Join-Path $built "thermi-$Version-$Processor.msi") $Out -Force
}
