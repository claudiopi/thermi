<#
.SYNOPSIS
Dot-source it for Invoke-RepositoryLever: the owner's levers of spec 004 on thermi's Linux repositories, the apt and the
dnf one of each channel, which scripts/updates.ps1 pulls when given --repository (docs/adr/0019-linux.md).

.DESCRIPTION
A repositories' folder holds, served as it is:
  apt/dists/<channel>/       each channel's apt suite, its Release clear-signed as InRelease and signed beside it
  apt/pool/<channel>/        the .debs, which never change once written
  rpm/<channel>/repodata/    each channel's dnf index, its repomd.xml signed beside it
  rpm/<channel>/packages/    the .rpms, signed each, which never change once written
  tarballs/<version>/        each version's build, which the AUR installs and a rollback packs again
and, for the levers alone, history/<channel>.json, signed, with each offer's indexes as they were signed.
Each lever checks the history's signature, and the hash of each file it may read against it, before it signs anything,
so whoever can write the folder cannot have a lever sign what they put there. A channel offers one version at a time; a
PC never takes an older one, so a rollback packs the version before as a higher one, "<highest>.really<version>",
which apt, dnf and pacman all sort above every version published. The indexes are apt-ftparchive's and createrepo_c's
(scripts/linux-index.sh), run from a Debian container where this PC has neither.
#>

. (Join-Path $PSScriptRoot 'gpg.ps1')

# As long as a manifest of spec 004: the monthly signing keeps both well inside it.
$Lifetime = [TimeSpan]::FromDays(45)

# How apt's Release writes a date.
$DateFormat = "ddd, dd MMM yyyy HH:mm:ss 'UTC'"

# The index tools' image, pinned, from which a PC without the tools runs them.
$IndexImage = 'debian@sha256:3cda3ca472ce45507a5109c6a3907093e24d9ae67d55954c9a07a45665dafba2'

# The lever's options, --name value, with a version alone as publish's first word.
function Read-LeverOptions([string[]]$Arguments) {
    $options = @{ channel = 'stable' }
    for ($at = 0; $at -lt $Arguments.Count; $at++) {
        if (-not $Arguments[$at].StartsWith('--')) { $options['version'] = $Arguments[$at]; continue }
        $name = $Arguments[$at].Substring(2)
        if ($name -eq 'before-last') { $options[$name] = $true; continue }
        $options[$name] = $Arguments[++$at]
    }
    if ($options['channel'] -notin 'stable', 'beta') { throw '--channel is stable or beta.' }
    $options
}

# A folder only this user reads, for the keys and what the levers stage, which the lever removes when it ends.
function New-PrivateFolder {
    $folder = Join-Path ([IO.Path]::GetTempPath()) "thermi-lever-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory $folder | Out-Null
    chmod 700 $folder
    $folder
}

# A key from its option, else from its variable, holding the armored key itself, into the lever's private folder, else
# the build's own beside its packages.
function Get-LeverKey([hashtable]$Options, [string]$Option, [string]$Variable, [string]$File, [string]$Private) {
    if ($Options[$Option]) { return (Resolve-Path $Options[$Option]).Path }
    if ([Environment]::GetEnvironmentVariable($Variable)) {
        $path = Join-Path $Private "$Option.asc"
        [IO.File]::WriteAllText($path, [Environment]::GetEnvironmentVariable($Variable))
        return $path
    }
    if ($Options['packages'] -and (Test-Path (Join-Path $Options['packages'] 'key' $File))) { return (Join-Path $Options['packages'] 'key' $File) }
    throw "Give --$Option or $Variable."
}

# The channel's history, once its signature and the hash of every package it names hold; refused when the channel is
# served with no history, which no lever starts over.
function Read-History([string]$Repository, [string]$Channel, [string]$Trust) {
    $file = Join-Path $Repository 'history' "$Channel.json"
    if (-not (Test-Path $file)) {
        if (Test-Path (Join-Path $Repository 'apt' 'dists' $Channel 'InRelease')) {
            throw "history/$Channel.json is gone while $Channel serves: no lever starts it over."
        }
        return [pscustomobject]@{ published = @(); withdrawn = @(); highest = $null; states = @() }
    }
    if (-not (Test-Signed $Trust $file "$file.asc")) { throw "history/$Channel.json is not signed by thermi's repository key, so no lever signs over it." }
    $history = Get-Content $file -Raw | ConvertFrom-Json
    foreach ($package in Get-LeverFiles $history) {
        $path = Join-Path $Repository $package.path
        if (-not (Test-Path $path) -or (Get-FileHash $path -Algorithm SHA256).Hash -ne $package.sha256) {
            throw "$($package.path) is not the file history/$Channel.json names."
        }
    }
    $history
}

# The files a lever may read, each with its hash, which channel-sync.ps1 fetches: the packages of the current offer and
# the one before, which sign and restore index again, and the tarballs of the version a rollback would pack again.
function Get-LeverFiles([pscustomobject]$History) {
    $states = @($History.states)
    @($states | Select-Object -Last 2 | ForEach-Object { $_.packages } | Where-Object { $_.path -notlike 'tarballs/*' })
    $target = Get-RollbackTarget $History
    if ($target) {
        $published = @($states | Where-Object { $_.lever -eq 'publish' -and $_.version -eq $target })[-1]
        @($published.packages | Where-Object { $_.path -like 'tarballs/*' })
    }
}

# The version a rollback takes the channel back to: the one published before the one offered, which no rollback withdrew.
function Get-RollbackTarget([pscustomobject]$History) {
    $current = @($History.states)[-1]
    if (-not $current -or -not $current.version) { return $null }
    $published = @($History.published)
    $at = [array]::IndexOf($published, $current.version)
    if ($at -lt 1) { return $null }
    @($published[0..($at - 1)] | Where-Object { $_ -notin @($History.withdrawn) })[-1]
}

# Copies a package into the repositories' folder, where a file of the same name must be the same bytes.
function Add-Package([string]$Repository, [string]$File, [string]$Under) {
    $target = Join-Path $Repository $Under (Split-Path $File -Leaf)
    $hash = (Get-FileHash $File -Algorithm SHA256).Hash
    if ((Test-Path $target) -and (Get-FileHash $target -Algorithm SHA256).Hash -ne $hash) {
        throw "$Under/$(Split-Path $File -Leaf) was published with other bytes: a package never changes."
    }
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item $File $target -Force
    [pscustomobject]@{ path = "$Under/$(Split-Path $File -Leaf)"; sha256 = $hash }
}

# A version's packages as linux-packages.ps1 packed them in $Folder, both packages for both processors, put in the channel.
function Add-Packed([hashtable]$Lever, [string]$Folder, [string]$Package) {
    $files = @(
        Get-ChildItem $Folder -File -Filter "thermi-sensors_${Package}_*.deb"
        Get-ChildItem $Folder -File -Filter "thermi_${Package}_*.deb"
        Get-ChildItem $Folder -File -Filter "thermi-sensors-$Package-1.*.rpm"
        Get-ChildItem $Folder -File -Filter "thermi-$Package-1.*.rpm"
    )
    if ($files.Count -ne 8) { throw "$Folder holds $($files.Count) of the 8 packages of ${Package}: pack them with scripts/linux-packages.ps1." }
    foreach ($file in $files) {
        Add-Package $Lever.Repository $file.FullName ($file.Extension -eq '.deb' ? "apt/pool/$($Lever.Channel)" : "rpm/$($Lever.Channel)/packages")
    }
}

# Puts the channel's next offer in place: its indexes written and signed, kept in the history as signed, served, and
# the history signed last.
function Write-Offer([hashtable]$Lever, [pscustomobject]$History, [pscustomobject]$State) {
    $number = @($History.states).Count + 1
    $kept = Join-Path $Lever.Repository 'history' $Lever.Channel $number
    Remove-Item -Recurse -Force $kept -ErrorAction SilentlyContinue
    Write-Index $Lever $State $kept
    $State | Add-Member -NotePropertyName offer -NotePropertyValue $number -Force
    $History.states = @($History.states) + $State
    Show-Offer $Lever.Repository $kept
    Save-History $Lever $History
}

# An offer's indexes, signed, into $Folder: the tools index a folder that holds only what the offer names.
function Write-Index([hashtable]$Lever, [pscustomobject]$State, [string]$Folder) {
    $channel = $Lever.Channel
    $stage = Join-Path $Lever.Private "index-$([Guid]::NewGuid().ToString('N'))"
    foreach ($package in $State.packages | Where-Object { $_.path -notlike 'tarballs/*' }) {
        $target = Join-Path $stage $package.path
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        Copy-Item (Join-Path $Lever.Repository $package.path) $target
    }
    New-Item -ItemType Directory -Force $stage | Out-Null
    $now = [DateTimeOffset]::UtcNow
    $dates = @($now, ($now + $Lifetime) | ForEach-Object { $_.ToString($DateFormat, [Globalization.CultureInfo]::InvariantCulture) })
    Invoke-IndexTools $stage $channel $dates
    foreach ($index in "apt/dists/$channel", "rpm/$channel/repodata") {
        New-Item -ItemType Directory -Force (Split-Path (Join-Path $Folder $index)) | Out-Null
        Copy-Item (Join-Path $stage $index) (Join-Path $Folder $index) -Recurse -Force
    }
    Remove-Item -Recurse -Force $stage
    $suite = Join-Path $Folder 'apt' 'dists' $channel
    Invoke-Signing $Lever.Key (Join-Path $suite 'Release') (Join-Path $suite 'InRelease') -Clear
    Invoke-Signing $Lever.Key (Join-Path $suite 'Release') (Join-Path $suite 'Release.gpg')
    $repomd = Join-Path $Folder 'rpm' $channel 'repodata' 'repomd.xml'
    Invoke-Signing $Lever.Key $repomd "$repomd.asc"
}

# scripts/linux-index.sh with this PC's apt-ftparchive and createrepo_c, else in an image of its own with both, made once
# from the pinned Debian, as this user, so what it writes is this user's.
function Invoke-IndexTools([string]$Stage, [string]$Channel, [string[]]$Dates) {
    $script = Join-Path $PSScriptRoot 'linux-index.sh'
    if ((Get-Command apt-ftparchive -ErrorAction SilentlyContinue) -and (Get-Command createrepo_c -ErrorAction SilentlyContinue)) {
        sh $script $Stage $Channel @Dates
    }
    else {
        $image = "thermi-index:$($IndexImage.Substring($IndexImage.Length - 12))"
        docker image inspect $image *> $null
        if ($LASTEXITCODE -ne 0) {
            $maker = "thermi-index-make-$PID"
            docker run --name $maker $IndexImage sh -c 'apt-get update -qq && apt-get install -y -qq --no-install-recommends apt-utils createrepo-c' | Out-Null
            $made = $LASTEXITCODE
            if ($made -eq 0) { docker commit $maker $image | Out-Null }
            docker rm --force $maker | Out-Null
            if ($made -ne 0) { throw 'The index tools were not installed in their image.' }
        }
        docker run --rm --name "thermi-index-$PID" --user "$(id -u):$(id -g)" --volume "${Stage}:/work" --volume "${script}:/linux-index.sh:ro" `
            $image sh /linux-index.sh /work $Channel @Dates
    }
    if ($LASTEXITCODE -ne 0) { throw "The $Channel channel's indexes were not written." }
}

# An offer's indexes served: those the signed ones name first, which a PC still reading the last offer may want too, and
# the signed ones last.
function Show-Offer([string]$Repository, [string]$Folder) {
    $signed = 'InRelease', 'Release', 'Release.gpg', 'Packages', 'Packages.gz', 'repomd.xml', 'repomd.xml.asc'
    $files = Get-ChildItem $Folder -Recurse -File | Sort-Object { $_.Name -in $signed }, { $_.Name -in 'InRelease', 'repomd.xml.asc' }
    foreach ($file in $files) {
        $target = Join-Path $Repository ([IO.Path]::GetRelativePath($Folder, $file.FullName))
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        Copy-Item $file.FullName $target -Force
    }
}

function Save-History([hashtable]$Lever, [pscustomobject]$History) {
    $file = Join-Path $Lever.Repository 'history' "$($Lever.Channel).json"
    $History | ConvertTo-Json -Depth 6 | Set-Content $file -NoNewline
    Invoke-Signing $Lever.Key $file "$file.asc"
}

function Get-Current([pscustomobject]$History) { @($History.states)[-1] }

function Publish-Version([hashtable]$Lever, [pscustomobject]$History, [string]$Version, [string]$Folder) {
    $parsed = [System.Management.Automation.SemanticVersion]$Version
    $before = @($History.published | Where-Object { [System.Management.Automation.SemanticVersion]$_ -ge $parsed })
    if ($before) { throw "$Version is not newer than $($before[0]), which $($Lever.Channel) published: a PC takes only a newer version." }
    $package = $Version.Replace('-', '~')
    $packages = @(Add-Packed $Lever $Folder $package)
    $packages += @(Get-ChildItem $Folder -File -Filter "thermi-$Version-linux-*.tar.gz" |
            ForEach-Object { Add-Package $Lever.Repository $_.FullName "tarballs/$Version" })
    $History.published = @($History.published) + $Version
    $History.highest = $package
    Write-Offer $Lever $History ([pscustomobject]@{ lever = 'publish'; version = $Version; package = $package; packages = $packages })
}

# The version published before the one offered, which no rollback withdrew, packed again under a higher number from its
# tarball, which holds the packaging it shipped with.
function Undo-Version([hashtable]$Lever, [pscustomobject]$History) {
    $current = Get-Current $History
    if (-not $current -or -not $current.version) { throw "$($Lever.Channel) offers no version to roll back." }
    $target = Get-RollbackTarget $History
    if (-not $target) { throw "$($Lever.Channel) published nothing before $($current.version) to roll back to." }
    $package = "$($History.highest).really$($target.Replace('-', '~'))"
    $packed = Join-Path $Lever.Private 'rollback'
    pwsh -NoProfile -File (Join-Path $PSScriptRoot 'linux-packages.ps1') -From (Join-Path $Lever.Repository 'tarballs' $target) `
        -PackageVersion $package -Key $Lever.Key -UpdatesFrom $Lever.UpdatesFrom -Output $packed
    if ($LASTEXITCODE -ne 0) { throw "$target was not packed again as $package." }
    $packages = @(Add-Packed $Lever $packed $package)
    $History.withdrawn = @($History.withdrawn) + $current.version
    $History.highest = $package
    Write-Offer $Lever $History ([pscustomobject]@{ lever = 'rollback'; version = $target; package = $package; packages = $packages })
}

# The offer before the current one, served again as it was signed then; signed again instead once its Release is a
# week from expiring, as apt refuses one past its Valid-Until.
function Restore-Offer([hashtable]$Lever, [pscustomobject]$History) {
    $states = @($History.states)
    if ($states.Count -lt 2) { throw "$($Lever.Channel) has no offer before its current one." }
    $before = $states[-2]
    $kept = Join-Path $Lever.Repository 'history' $Lever.Channel $before.offer
    $until = (Get-Content (Join-Path $kept 'apt' 'dists' $Lever.Channel 'Release') | Where-Object { $_ -like 'Valid-Until:*' }) -replace '^Valid-Until: *', ''
    $expires = [DateTimeOffset]::ParseExact($until, $DateFormat, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal)
    if ($expires -lt [DateTimeOffset]::UtcNow.AddDays(7)) {
        $kept = Join-Path $Lever.Private 'restore'
        Write-Index $Lever $before $kept
    }
    Show-Offer $Lever.Repository $kept
    $restored = $before | Select-Object * -ExcludeProperty lever
    $restored | Add-Member -NotePropertyName lever -NotePropertyValue 'restore'
    $History.states = $states + $restored
    Save-History $Lever $History
}

<#
Pulls $Command on the repositories: publish <version>, promote, rollback, disable, sign, restore --before-last or status,
with --repository <folder> --channel stable|beta, the packages' folder as --packages, the signing key as --key or
THERMI_REPOSITORY_KEY and the trusted public key as --trust or THERMI_REPOSITORY_PUB, else the build's own in the packages'
folder.
#>
function Invoke-RepositoryLever([string]$Command, [string[]]$Arguments) {
    $options = Read-LeverOptions $Arguments
    New-Item -ItemType Directory -Force $options['repository'] | Out-Null
    $private = New-PrivateFolder
    try {
        $lever = @{
            Repository = (Resolve-Path $options['repository']).Path; Channel = $options['channel']; Private = $private
            Key = $Command -eq 'status' ? $null : (Get-LeverKey $options 'key' 'THERMI_REPOSITORY_KEY' 'thermi.key.asc' $private)
            UpdatesFrom = $options['updates-from'] ?? 'http://127.0.0.1:8765/v1/'
        }
        $trust = Get-LeverKey $options 'trust' 'THERMI_REPOSITORY_PUB' 'thermi.asc' $private
        Invoke-Lever $Command $options $lever $trust
    }
    finally {
        Remove-Item -Recurse -Force $private -ErrorAction SilentlyContinue
    }
}

function Invoke-Lever([string]$Command, [hashtable]$Options, [hashtable]$Lever, [string]$Trust) {
    $history = Read-History $Lever.Repository $Lever.Channel $Trust
    switch ($Command) {
        'publish' { Publish-Version $Lever $history $Options['version'] $Options['packages'] }
        'promote' {
            $from = Read-History $Lever.Repository ($Options['from'] ?? 'beta') $Trust
            $Lever.Channel = $Options['to'] ?? 'stable'
            $history = Read-History $Lever.Repository $Lever.Channel $Trust
            $current = Get-Current $from
            if (-not $current.version) { throw "$($Options['from'] ?? 'beta') offers no version to promote." }
            $folder = Join-Path $Lever.Private 'promote'
            New-Item -ItemType Directory -Force $folder | Out-Null
            foreach ($package in $current.packages) { Copy-Item (Join-Path $Lever.Repository $package.path) $folder }
            Publish-Version $Lever $history $current.version $folder
        }
        'rollback' { Undo-Version $Lever $history }
        'disable' { Write-Offer $Lever $history ([pscustomobject]@{ lever = 'disable'; version = $null; package = $null; packages = @() }) }
        # The current offer signed again, before it expires or by another key: the history keeps offers, not signatures.
        'sign' {
            $current = Get-Current $history
            if (-not $current) { throw "$($Lever.Channel) offers nothing to sign." }
            $signing = Join-Path $Lever.Private 'sign'
            Write-Index $Lever $current $signing
            Show-Offer $Lever.Repository $signing
        }
        'restore' {
            if (-not $Options['before-last']) { throw 'restore takes --before-last.' }
            Restore-Offer $Lever $history
        }
        'status' {
            $current = Get-Current $history
            Write-Host ($current ? "$($Lever.Channel): $($current.lever), $($current.package ?? 'nothing')" : "$($Lever.Channel): nothing published")
            return
        }
        default { throw "$Command is no lever on the repositories: publish, promote, rollback, disable, sign, restore or status." }
    }
    Write-Host "$($Lever.Channel): $Command done."
}
