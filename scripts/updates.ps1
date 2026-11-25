#Requires -Version 7
<#
.SYNOPSIS
The owner's levers on thermi's update channels (docs/adr/0008-updates.md).

.DESCRIPTION
pack builds a version for a processor and zips its folder as an update carries it:
  pwsh scripts/updates.ps1 pack --version 0.9.1 --out <zip> [--processor x64|arm64] [--msi <msi>, and beside it <msi>-user.msi]
      [--updates-from <url> --updates-keys <folder> [--signers <thumbprints>]]
A release names the SHA-256 thumbprints of the certificates, separated by semicolons, one of which signs every catalog.
Every other command goes to thermi-releases (src/Thermi.Releases), which writes the channel's next signed manifest into
a channels folder laid out as the server is:
  keys --out <folder>
  publish --folder <channels> --channel beta --version 0.9.1 --zip x64=<zip> arm64=<zip> [--msi x64=<msi> arm64=<msi>] --base-url <url>
          [--rollout 10] [--urgent]
  promote --folder <channels> [--from beta --to stable] [--rollout 100]
  rollback | disable | refresh | unrevoke --folder <channels> --channel stable
  revoke --folder <channels> --channel stable --release-key <release.pub.pem> --backup-key <backup.key.pem>
  status --folder <channels>
  serve --folder <channels> [--port 8765]
The levers sign with --key <private PEM>, or the PEM in THERMI_UPDATES_KEY, and open the signed histories with the public
keys in --trust <folder>, release.pub.pem and backup.pub.pem, or THERMI_UPDATES_RELEASE_PUB and THERMI_UPDATES_BACKUP_PUB.
#>
# No param block: PowerShell would read the tool's --options as its own.
if ($args.Count -lt 1) { throw 'Give a command: pack, keys, publish, promote, rollback, disable, refresh, revoke, unrevoke, status or serve.' }
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
. (Join-Path $PSScriptRoot 'publish.ps1')
$staging = Join-Path $root "artifacts/pack/$Version-$Processor"
Publish-Thermi $staging $Processor $Version $UpdatesFrom $UpdatesKeys $options['signers']

Remove-Item $Out -ErrorAction SilentlyContinue
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, [IO.Path]::GetFullPath($Out), 'Optimal', $false)
$zip = Get-Item $Out
Write-Host "Packed $Version for $Processor into $($zip.FullName), $($zip.Length) bytes."
if ($options['msi']) {
    $perUser = [IO.Path]::ChangeExtension($options['msi'], $null).TrimEnd('.') + '-user.msi'
    Build-Msi $staging $Processor $Version $options['msi']
    Build-Msi $staging $Processor $Version $perUser -PerUser
    Write-Host "Built its MSIs, $((Get-Item $options['msi']).FullName) and, per user, $((Get-Item $perUser).FullName)."
}