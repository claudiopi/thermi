<#
.SYNOPSIS
Packs thermi's Linux packages from one build per processor, on Linux: thermi-sensors and thermi, as .deb and .rpm for
amd64 and arm64, the build's tarball for each, and the AUR's PKGBUILD that installs the same files from it.

.DESCRIPTION
Each processor's build is one dotnet publish of the service and one of thermi, self-contained, so a PC needs no .NET,
staged as the packages install it: thermi-sensors' tree, with thermi-cli and the service's units, user and rule from
src/Thermi.Sensors.Linux/Packaging; thermi's, with the overlay's launcher, its user unit and launcher's entry from
src/Thermi.Linux/Packaging, the icons of src/Thermi.Windows/thermi.ico and the GNOME Shell extension of src/Thermi.Gnome.
The tarball, thermi-<version>-linux-<processor>.tar.gz, is both trees and, in packaging/, each package's nfpm.yaml and
scripts, and the .deb's AppArmor profile; nfpm (scripts/nfpm.ps1) packs each package from it with them, and signs the .rpm, which dnf checks
(docs/adr/0019-linux.md). A rollback packs a tarball again with the packaging it holds, as that version shipped.

.PARAMETER Version
The build's version, as Directory.Build.props writes it; a beta's "-beta.<n>" packs as "~beta.<n>", which sorts before
the release in apt and dnf. pacman has no such sort, and the AUR takes only releases, so a beta writes no PKGBUILD.

.PARAMETER PackageVersion
The packages' version when it is not the build's, as a rollback's, which offers the build before under a higher number.

.PARAMETER From
A folder with a build's tarballs, which are packed again at PackageVersion instead of building: a rollback's, or a
release's as served, whose PKGBUILD a run again of the release pushes to the AUR.

.PARAMETER Key
The OpenPGP secret key, armored, that thermi's repository is signed with: it signs the .rpm, and its public half goes in
the packages, which add the repository with it. Without one, the build makes a key of its own in <Output>/key, and the
levers (scripts/updates.ps1) given its packages sign with it.

.PARAMETER UpdatesFrom
Where the update channels are served, ending in /v1/, as the Windows build's -UpdatesFrom: the repositories are under
its linux/. Without one, the repository the packages add is the one scripts/updates.ps1 serve answers on this PC.

.PARAMETER Channel
The channel the packages' repository follows until a person changes it in its file: stable, as for every package a
channel offers, beta's too, which promote offers on stable as they are.

.PARAMETER Output
Where the packages go: artifacts/linux, where the Linux tests install them from.
#>
param(
    [string]$Version,
    [string]$PackageVersion,
    [string]$From,
    [string]$Key,
    [string]$UpdatesFrom = 'http://127.0.0.1:8765/v1/',
    [ValidateSet('stable', 'beta')]
    [string]$Channel = 'stable',
    [string]$Output = (Join-Path $PSScriptRoot '..' 'artifacts' 'linux')
)

$ErrorActionPreference = 'Stop'
if (-not $IsLinux) { throw 'The Linux packages are packed on Linux, where their file modes and links are.' }
. (Join-Path $PSScriptRoot 'nfpm.ps1')
. (Join-Path $PSScriptRoot 'gpg.ps1')

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Version) {
    $Version = @(([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ })[0]
}
if (-not $PackageVersion) { $PackageVersion = $Version.Replace('-', '~') }
New-Item -ItemType Directory -Force $Output | Out-Null
$Output = (Resolve-Path $Output).Path
if (-not $Key) { $Key = New-RepositoryKey (Join-Path $Output 'key') "thermi, a build's own" }
$nfpm = Get-Nfpm
$processors = [ordered]@{ 'x64' = 'amd64'; 'arm64' = 'arm64' }
$stages = Join-Path $root 'artifacts' 'linux-build' $PackageVersion

$packages = @{ Name = 'thermi-sensors'; Project = 'Thermi.Sensors.Linux' }, @{ Name = 'thermi'; Project = 'Thermi.Linux' }

# A file of the package with its text as Linux reads it, LF line endings, and its mode.
function Write-Text([string]$Text, [string]$Path, [string]$Mode = '644') {
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    [IO.File]::WriteAllText($Path, ($Text -replace "`r`n", "`n"))
    chmod $Mode $Path
}

function Copy-Text([string]$From, [string]$To, [string]$Mode = '644') { Write-Text (Get-Content $From -Raw) $To $Mode }

function Publish-Build([string]$Rid, [string]$Lib) {
    foreach ($project in 'Thermi.Sensors.Linux', 'Thermi.Linux') {
        # The packages' versions as the lock file holds them, as the Windows build's are.
        dotnet restore (Join-Path $root 'src' $project) --locked-mode -p:SelfContained=true --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "Restoring $project failed." }
        dotnet publish (Join-Path $root 'src' $project) -c Release -r $Rid --self-contained --no-restore -o $Lib -p:Version=$Version -p:DebugType=none `
            --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "Publishing $project for $Rid failed." }
    }
    # What a build leaves beside the programs, which runs nothing.
    Remove-Item (Join-Path $Lib '*.xml')
}

# The icon at each size the desktop's icon theme has a folder for, the PNG frames thermi.ico holds as they are.
function Write-Icons([string]$Share) {
    $ico = [IO.File]::ReadAllBytes((Join-Path $root 'src' 'Thermi.Windows' 'thermi.ico'))
    for ($at = 0; $at -lt [BitConverter]::ToUInt16($ico, 4); $at++) {
        $entry = 6 + 16 * $at
        $size = $ico[$entry] -eq 0 ? 256 : $ico[$entry]
        if ($size -notin 16, 24, 32, 48, 64, 256) { continue }
        $png = Join-Path $Share 'icons' 'hicolor' "${size}x$size" 'apps' 'io.github.claudiopi.thermi.png'
        New-Item -ItemType Directory -Force (Split-Path $png) | Out-Null
        $length, $offset = [BitConverter]::ToUInt32($ico, $entry + 8), [BitConverter]::ToUInt32($ico, $entry + 12)
        [IO.File]::WriteAllBytes($png, $ico[$offset..($offset + $length - 1)])
    }
}

function New-Trees([string]$Rid, [string]$Files) {
    $sensors, $thermi = (Join-Path $Files 'thermi-sensors'), (Join-Path $Files 'thermi')
    $lib = Join-Path $sensors 'usr' 'lib' 'thermi'
    Publish-Build $Rid $lib
    # The sensors' package runs thermi.dll as thermi-cli alone: the overlay's own launcher comes with thermi's.
    New-Item -ItemType Directory -Force (Join-Path $thermi 'usr' 'lib' 'thermi'), (Join-Path $sensors 'usr' 'bin'), (Join-Path $thermi 'usr' 'bin') | Out-Null
    Move-Item (Join-Path $lib 'thermi') (Join-Path $thermi 'usr' 'lib' 'thermi' 'thermi')
    ln -s ../lib/thermi/thermi-cli (Join-Path $sensors 'usr' 'bin' 'thermi-cli')
    ln -s ../lib/thermi/thermi (Join-Path $thermi 'usr' 'bin' 'thermi')

    $packaging = Join-Path $root 'src' 'Thermi.Sensors.Linux' 'Packaging'
    Copy-Text (Join-Path $packaging 'thermi-sensors.socket') (Join-Path $sensors 'usr/lib/systemd/system/thermi-sensors.socket')
    Copy-Text (Join-Path $packaging 'thermi-sensors.service') (Join-Path $sensors 'usr/lib/systemd/system/thermi-sensors.service')
    Copy-Text (Join-Path $packaging 'thermi-sensors.conf') (Join-Path $sensors 'usr/lib/sysusers.d/thermi-sensors.conf')
    Copy-Text (Join-Path $packaging '60-thermi-sensors.rules') (Join-Path $sensors 'usr/lib/udev/rules.d/60-thermi-sensors.rules')

    $packaging = Join-Path $root 'src' 'Thermi.Linux' 'Packaging'
    Copy-Text (Join-Path $packaging 'thermi.service') (Join-Path $thermi 'usr/lib/systemd/user/thermi.service')
    Copy-Text (Join-Path $packaging 'io.github.claudiopi.thermi.desktop') (Join-Path $thermi 'usr/share/applications/io.github.claudiopi.thermi.desktop')
    Write-Icons (Join-Path $thermi 'usr' 'share')
    # The extension's own files, not its README or its lint's settings.
    $extension = Join-Path $thermi 'usr/share/gnome-shell/extensions/thermi@claudiopi.github.io'
    Get-ChildItem (Join-Path $root 'src' 'Thermi.Gnome') -File |
        Where-Object { $_.Extension -in '.js', '.json', '.css' -and $_.Name -ne 'eslint.config.js' } |
        ForEach-Object { Copy-Text $_.FullName (Join-Path $extension $_.Name) }
    foreach ($package in $sensors, $thermi) {
        $doc = Join-Path $package 'usr' 'share' 'doc' (Split-Path $package -Leaf)
        foreach ($file in 'LICENSE', 'THIRD-PARTY-NOTICES.md') { Copy-Text (Join-Path $root $file) (Join-Path $doc $file) }
    }
    foreach ($package in $packages) {
        Get-ChildItem (Join-Path $root 'src' $package.Project 'Packaging') -File |
            Where-Object { $_.Name -eq 'nfpm.yaml' -or $_.Extension -in '.sh', '.apparmor' } |
            ForEach-Object { Copy-Text $_.FullName (Join-Path $Files 'packaging' $package.Project $_.Name) }
    }
    # The build host's umask, which a package does not keep.
    chmod -R u=rwX,go=rX $Files
}


function Get-Script([string]$Path) { (Get-Content $Path -Raw).Replace('@REPOSITORY@', "${UpdatesFrom}linux/").Replace('@CHANNEL@', $Channel) }

function New-Inputs([string]$Stage, [string]$Files) {
    Write-Text (Get-PublicKey $Key) (Join-Path $Stage 'key' 'thermi.asc')
    foreach ($package in $packages) {
        Get-ChildItem (Join-Path $Files 'packaging' $package.Project) -Filter '*.sh' | ForEach-Object {
            Write-Text (Get-Script $_.FullName) (Join-Path $Stage 'scripts' $package.Name $_.Name) '755'
        }
    }
}

function Invoke-Nfpm([string]$Stage, [string]$Files, [string]$Arch) {
    $tokens = @{ THERMI_VERSION = $PackageVersion; THERMI_ARCH = $Arch; THERMI_STAGE = $Stage; THERMI_FILES = $Files; THERMI_KEY = $Key }
    foreach ($package in $packages) {
        $config = Get-Content (Join-Path $Files 'packaging' $package.Project 'nfpm.yaml') -Raw
        foreach ($token in $tokens.GetEnumerator()) { $config = $config.Replace("`${$($token.Key)}", $token.Value) }
        $yaml = Join-Path $Stage "$($package.Name).yaml"
        Write-Text $config $yaml
        foreach ($format in 'deb', 'rpm') {
            # The key has no passphrase, which nfpm still asks for.
            $env:NFPM_RPM_PASSPHRASE = ''
            & $nfpm package --config $yaml --packager $format --target $Output | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "nfpm did not pack $($package.Name)'s .$format for $Arch." }
        }
    }
}

# The AUR's PKGBUILD for this version's tarballs, with its install scripts, the same as the .deb's and the .rpm's, and
# the tarballs beside it, where makepkg takes them before downloading.
function Write-Aur([hashtable]$Tarballs) {
    $aur = Join-Path $Output 'aur'
    Remove-Item -Recurse -Force $aur -ErrorAction SilentlyContinue
    $packaging = Join-Path $root 'src' 'Thermi.Linux' 'Packaging'
    $pkgbuild = (Get-Content (Join-Path $packaging 'PKGBUILD') -Raw).Replace('@VERSION@', $PackageVersion).
        Replace('@TARBALLS@', "${UpdatesFrom}linux/tarballs/$Version/").
        Replace('@SHA256_X64@', (Get-FileHash $Tarballs['x64'] -Algorithm SHA256).Hash.ToLowerInvariant()).
        Replace('@SHA256_ARM64@', (Get-FileHash $Tarballs['arm64'] -Algorithm SHA256).Hash.ToLowerInvariant())
    Write-Text $pkgbuild (Join-Path $aur 'PKGBUILD')
    foreach ($package in $packages) {
        Write-Text (Get-PacmanInstall (Join-Path $root 'src' $package.Project 'Packaging')) (Join-Path $aur "$($package.Name)-bin.install")
    }
    foreach ($tarball in $Tarballs.Values) { Copy-Item $tarball $aur }
}

# pacman calls an install script's functions by the step, with no argument that says which: each runs the same script as
# the .rpm's step, with what rpm gives it, the count of the package left installed.
function Get-PacmanInstall([string]$Packaging) {
    $steps = [ordered]@{ post_install = 'postinstall', 1; post_upgrade = 'postinstall', 2; pre_remove = 'preremove', 0; post_remove = 'postremove', 0 }
    $functions = foreach ($step in $steps.GetEnumerator()) {
        $script = Join-Path $Packaging "$($step.Value[0]).sh"
        if (-not (Test-Path $script)) { continue }
        # Not indented, as a here-document's end must start its line.
        $body = (Get-Script $script) -split "`r?`n" | Where-Object { $_ -notmatch '^(#|set -e$)' }
        "$($step.Key)() {`nset -- $($step.Value[1])`n$(($body | Where-Object { $_ }) -join "`n")`n}`n"
    }
    "# pacman's install script, made by scripts/linux-packages.ps1 from the .deb's and the .rpm's.`n`n" + ($functions -join "`n")
}

Remove-Item -Recurse -Force $stages -ErrorAction SilentlyContinue
# What an earlier build packed here, so the folder holds one version's packages; its key stays, which they all trust.
if (-not $From) {
    Get-ChildItem $Output -File | Where-Object { $_.Name -match '\.(deb|rpm|tar\.gz)$' } | Remove-Item
}
$tarballs = @{}
foreach ($processor in $processors.GetEnumerator()) {
    $rid = "linux-$($processor.Key)"
    $stage = Join-Path $stages $rid
    $name = "thermi-$Version-$rid.tar.gz"
    New-Item -ItemType Directory -Force $stage | Out-Null
    if ($From) {
        $tarball = Get-ChildItem $From -Filter "thermi-*-$rid.tar.gz" | Select-Object -First 1
        if (-not $tarball) { throw "$From holds no tarball for $rid." }
        $tarballs[$processor.Key] = $tarball.FullName
        tar -xzf $tarball.FullName -C $stage
        if ($LASTEXITCODE -ne 0) { throw "$($tarball.Name) did not unpack." }
        $files = (Get-ChildItem $stage -Directory | Select-Object -First 1).FullName
    }
    else {
        $files = Join-Path $stage "thermi-$Version"
        New-Trees $rid $files
        $tarballs[$processor.Key] = Join-Path $Output $name
        tar --owner=0 --group=0 --numeric-owner -czf $tarballs[$processor.Key] -C $stage (Split-Path $files -Leaf)
        if ($LASTEXITCODE -ne 0) { throw "The tarball for $rid was not written." }
    }
    New-Inputs $stage $files
    Invoke-Nfpm $stage $files $processor.Value
}
# A release's own version, not a beta's nor a rollback's higher number, which the AUR never offers.
if ($PackageVersion -eq $Version -and -not $Version.Contains('-')) { Write-Aur $tarballs }
Remove-Item -Recurse -Force $stages
Write-Host "Packed thermi $PackageVersion into $Output"
