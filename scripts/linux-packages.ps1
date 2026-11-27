<#
.SYNOPSIS
Packs thermi's Linux packages from this build, on Linux: today the sensors' .deb, thermi-sensors_<version>_<arch>.deb.

.DESCRIPTION
The package holds the service and thermi-cli, each published self-contained for the processor, so a PC needs no .NET;
the service's systemd units, its user and its udev rule, from src/Thermi.Sensors.Linux/Packaging; and the scripts that
make the user, apply the rule and start listening on install, and stop on removal (docs/adr/0019-linux.md).

.PARAMETER Runtime
The processor's runtime, linux-x64 or linux-arm64.

.PARAMETER Output
Where the packages go: artifacts/linux, where the Linux tests install them from.
#>
param(
    [ValidateSet('linux-x64', 'linux-arm64')]
    [string]$Runtime = 'linux-x64',
    [string]$Output = (Join-Path $PSScriptRoot '..' 'artifacts' 'linux')
)

$ErrorActionPreference = 'Stop'
if (-not $IsLinux) { throw 'The Linux packages are packed on Linux, where dpkg-deb is.' }

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$arch = @{ 'linux-x64' = 'amd64'; 'linux-arm64' = 'arm64' }[$Runtime]
$stage = Join-Path ([IO.Path]::GetTempPath()) "thermi-sensors-deb-$PID"
$lib = Join-Path $stage 'usr' 'lib' 'thermi'
$packaging = Join-Path $root 'src' 'Thermi.Sensors.Linux' 'Packaging'

function Publish($project) {
    dotnet publish (Join-Path $root 'src' $project) -c Release -r $Runtime --self-contained -o $lib -p:DebugType=none --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed." }
}

# A file of the package, with its mode, and its text with LF line endings, as Linux reads it.
function Put([string]$Text, [string]$Path, [string]$Mode = '644') {
    $file = Join-Path $stage $Path
    New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
    [IO.File]::WriteAllText($file, ($Text -replace "`r`n", "`n"))
    chmod $Mode $file
}

try {
    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
    Publish 'Thermi.Sensors.Linux'
    Publish 'Thermi.Linux'
    # The sensors' package runs thermi.dll as thermi-cli alone: the overlay's own launcher comes with thermi's package.
    Remove-Item (Join-Path $lib 'thermi')
    New-Item -ItemType Directory -Force (Join-Path $stage 'usr' 'bin') | Out-Null
    ln -s /usr/lib/thermi/thermi-cli (Join-Path $stage 'usr' 'bin' 'thermi-cli')

    $read = { param($name) Get-Content (Join-Path $packaging $name) -Raw }
    Put (& $read 'thermi-sensors.socket') 'usr/lib/systemd/system/thermi-sensors.socket'
    Put (& $read 'thermi-sensors.service') 'usr/lib/systemd/system/thermi-sensors.service'
    Put (& $read 'thermi-sensors.conf') 'usr/lib/sysusers.d/thermi-sensors.conf'
    Put (& $read '60-thermi-sensors.rules') 'usr/lib/udev/rules.d/60-thermi-sensors.rules'

    # Without systemd running, as in a container, each step that needs it is skipped.
    Put @'
#!/bin/sh
set -e
systemd-sysusers thermi-sensors.conf
udevadm control --reload-rules 2>/dev/null || true
udevadm trigger --subsystem-match=powercap --action=add 2>/dev/null || true
if [ -d /run/systemd/system ]; then
    systemctl daemon-reload
    systemctl enable --now thermi-sensors.socket
    systemctl try-restart thermi-sensors.service
fi
'@ 'DEBIAN/postinst' '755'
    Put @'
#!/bin/sh
set -e
if [ -d /run/systemd/system ] && [ "$1" = remove ]; then
    systemctl disable --now thermi-sensors.socket thermi-sensors.service || true
fi
'@ 'DEBIAN/prerm' '755'
    Put @'
#!/bin/sh
set -e
if [ -d /run/systemd/system ]; then
    systemctl daemon-reload || true
fi
'@ 'DEBIAN/postrm' '755'

    # What a build leaves beside the programs, which runs nothing, and the build host's umask, which a package does not keep.
    Remove-Item (Join-Path $lib '*.xml')
    chmod -R u=rwX,go=rX $stage
    $size = [math]::Ceiling(((Get-ChildItem -Recurse -File $stage | Measure-Object -Property Length -Sum).Sum) / 1KB)
    Put @"
Package: thermi-sensors
Version: $version
Architecture: $arch
Maintainer: thermi <17047466+claudiopi@users.noreply.github.com>
Installed-Size: $size
Depends: libc6, libgcc-s1, libstdc++6, libicu78 | libicu77 | libicu76 | libicu75 | libicu74 | libicu72, systemd
Recommends: libglib2.0-0t64 | libglib2.0-0
Section: utils
Priority: optional
Homepage: https://github.com/claudiopi/thermi
Description: thermi's sensor service and thermi-cli
 The service thermi reads its sensors from: the CPU, the GPU, the board's
 chips, the drives and the network, once a second while someone listens, as a
 user of its own with no capability. thermi-cli gives them to AI agents and
 other programs as JSON.

"@ 'DEBIAN/control'

    New-Item -ItemType Directory -Force $Output | Out-Null
    $deb = Join-Path $Output "thermi-sensors_${version}_$arch.deb"
    dpkg-deb --root-owner-group --build $stage $deb | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'dpkg-deb failed.' }
    Write-Host "Packed $deb"
}
finally {
    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
}
