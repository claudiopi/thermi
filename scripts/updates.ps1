#Requires -Version 7
<#
.SYNOPSIS
The owner's levers on thermi's update channels (docs/adr/0008-updates.md).

.DESCRIPTION
pack builds a version for a processor and zips its folder as an update carries it:
  pwsh scripts/updates.ps1 pack -Version 0.9.1 -Out <zip> [-Processor x64|arm64] [-UpdatesFrom <url> -UpdatesKeys <folder>]
Every other command goes to thermi-releases (src/Thermi.Releases), which writes the channel's next signed manifest into
a channels folder laid out as the server is:
  keys --out <folder>
  publish --folder <channels> --channel beta --version 0.9.1 --zip x64=<zip> arm64=<zip> --base-url <url> [--rollout 10] [--urgent]
  promote --folder <channels> [--from beta --to stable] [--rollout 100]
  rollback | disable | refresh --folder <channels> --channel stable
  status --folder <channels>
  serve --folder <channels> [--port 8765]
The levers sign with --key <private PEM>, or the PEM in THERMI_UPDATES_KEY.
#>
param(
    [Parameter(Mandatory, Position = 0)][string]$Command,
    [string]$Version,
    [string]$Out,
    [ValidateSet('x64', 'arm64')][string]$Processor,
    [string]$UpdatesFrom,
    [string]$UpdatesKeys,
    [Parameter(ValueFromRemainingArguments)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if ($Command -ne 'pack') {
    dotnet run --project (Join-Path $root 'src/Thermi.Releases') -c Release --nologo -v quiet -- $Command @Rest
    exit $LASTEXITCODE
}

if (-not $Version -or -not $Out) { throw 'pack needs -Version and -Out.' }
if (-not $Processor) {
    $Processor = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
}

# The same build as scripts/install.ps1's, at the version given, and pointed at the same channel when asked.
function Get-PublicKey([string]$Name) {
    $pem = Get-Content (Join-Path $UpdatesKeys $Name) -Raw
    if ($pem -notmatch '-----BEGIN PUBLIC KEY-----([A-Za-z0-9+/=\s]+)-----END PUBLIC KEY-----') { throw "$Name holds no public key." }
    $Matches[1] -replace '\s', ''
}
$updates = @()
if ($UpdatesFrom) {
    $updates = "-p:ThermiUpdatesFrom=$UpdatesFrom", "-p:ThermiUpdatesKey=$(Get-PublicKey release.pub.pem)",
        "-p:ThermiUpdatesBackupKey=$(Get-PublicKey backup.pub.pem)"
}

$staging = Join-Path $root "artifacts/pack/$Version-$Processor"
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
foreach ($project in 'src/Thermi.SensorHost', 'src/Thermi') {
    $path = Join-Path $root $project
    dotnet restore $path --locked-mode -p:PublishReadyToRun=true -p:SelfContained=true --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Restoring $project failed." }
    dotnet publish $path -c Release -r "win-$Processor" --self-contained true --no-restore -o $staging --nologo -v quiet `
        -p:Version=$Version -p:DebugType=none -p:PublishDocumentationFile=false -p:PublishReferencesDocumentationFiles=false @updates
    if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed." }
}

Remove-Item $Out -ErrorAction SilentlyContinue
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, [IO.Path]::GetFullPath($Out), 'Optimal', $false)
$zip = Get-Item $Out
Write-Host "Packed $Version for $Processor into $($zip.FullName), $($zip.Length) bytes."
