#Requires -Version 7
<#
.SYNOPSIS
The owner's levers on thermi's update channels (docs/adr/0008-updates.md).

.DESCRIPTION
pack builds a version for a processor and zips its folder as an update carries it:
  pwsh scripts/updates.ps1 pack --version 0.9.1 --out <zip> [--processor x64|arm64] [--updates-from <url> --updates-keys <folder>]
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
# No param block: PowerShell would read the tool's --options as its own.
if ($args.Count -lt 1) { throw 'Give a command: pack, keys, publish, promote, rollback, disable, refresh, status or serve.' }
$Command = [string]$args[0]
[string[]]$Rest = @($args | Select-Object -Skip 1)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if ($Command -ne 'pack') {
    # Run from a copy, so a server left running never holds the build's files.
    $tool = Join-Path $root 'artifacts/releases'
    dotnet build (Join-Path $root 'src/Thermi.Releases') -c Release -o (Join-Path $root 'artifacts/releases-build') --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Building thermi-releases failed.' }
    if ($Command -eq 'serve') { $tool = Join-Path $root "artifacts/releases-serve-$PID" }
    New-Item $tool -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $root 'artifacts/releases-build/*') $tool -Recurse -Force
    dotnet (Join-Path $tool 'thermi-releases.dll') $Command @Rest
    exit $LASTEXITCODE
}

# pack's own options, written as the tool's are.
$options = @{}
for ($at = 0; $at -lt $Rest.Count; $at += 2) { $options[$Rest[$at].TrimStart('-')] = $Rest[$at + 1] }
$Version, $Out, $Processor = $options['version'], $options['out'], $options['processor']
$UpdatesFrom, $UpdatesKeys = $options['updates-from'], $options['updates-keys']
if (-not $Version -or -not $Out) { throw 'pack needs --version and --out.' }
if ($Processor -and $Processor -notin 'x64', 'arm64') { throw '--processor is x64 or arm64.' }
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
