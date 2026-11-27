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
so whoever can write the folder cannot have a lever sign what they put there. A channel offers one version at a time; a PC never takes
an older one, so a rollback packs the version before as a higher one, "<highest>+really<version>", which every PC takes.
#>

. (Join-Path $PSScriptRoot 'gpg.ps1')

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

# A key from its option, else from its variable, holding the armored key itself, else the build's own beside its packages.
function Get-LeverKey([hashtable]$Options, [string]$Option, [string]$Variable, [string]$File) {
    if ($Options[$Option]) { return (Resolve-Path $Options[$Option]).Path }
    if ([Environment]::GetEnvironmentVariable($Variable)) {
        $path = Join-Path ([IO.Path]::GetTempPath()) "thermi-$Option-$PID.asc"
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

# A version's packages and tarballs as linux-packages.ps1 packed them in $Folder, both packages for both processors.
function Get-Packed([string]$Folder, [string]$Package) {
    $files = @(
        Get-ChildItem $Folder -File -Filter "thermi-sensors_${Package}_*.deb"
        Get-ChildItem $Folder -File -Filter "thermi_${Package}_*.deb"
        Get-ChildItem $Folder -File -Filter "thermi-sensors-$Package-1.*.rpm"
        Get-ChildItem $Folder -File -Filter "thermi-$Package-1.*.rpm"
    )
    if ($files.Count -ne 8) { throw "$Folder holds $($files.Count) of the 8 packages of ${Package}: pack them with scripts/linux-packages.ps1." }
    $files
}

# Puts the channel's next offer in place: its indexes written and signed, kept in the history as signed, served, and
# the history signed last.
function Write-Offer([hashtable]$Lever, [pscustomobject]$History, [pscustomobject]$State) {
    $repository, $channel = $Lever.Repository, $Lever.Channel
    $number = @($History.states).Count + 1
    $kept = Join-Path $repository 'history' $channel $number
    Remove-Item -Recurse -Force $kept -ErrorAction SilentlyContinue
    Write-Index $Lever $State $kept
    Protect-Offer $Lever.Key $kept $channel
    $State | Add-Member -NotePropertyName offer -NotePropertyValue $number -Force
    $History.states = @($History.states) + $State
    Show-Offer $repository $channel $kept
    Save-History $Lever $History
}

function Write-Index([hashtable]$Lever, [pscustomobject]$State, [string]$Folder) {
    $paths = @($State.packages | ForEach-Object path)
    $debs = @($paths | Where-Object { $_ -like 'apt/*' } | ForEach-Object { $_.Substring('apt/'.Length) })
    $rpms = @($paths | Where-Object { $_ -like 'rpm/*' } | ForEach-Object { $_.Substring("rpm/$($Lever.Channel)/".Length) })
    $named = @(if ($debs) { '--debs'; $debs }) + @(if ($rpms) { '--rpms'; $rpms })
    & $Lever.Index[0] @($Lever.Index | Select-Object -Skip 1) index --repository $Lever.Repository --channel $Lever.Channel --out $Folder @named
    if ($LASTEXITCODE -ne 0) { throw "The $($Lever.Channel) channel's indexes were not written." }
}

# The suite's Release clear-signed as InRelease and signed beside it, and repomd.xml signed beside it.
function Protect-Offer([string]$Key, [string]$Folder, [string]$Channel) {
    $suite = Join-Path $Folder 'apt' 'dists' $Channel
    Invoke-Signing $Key (Join-Path $suite 'Release') (Join-Path $suite 'InRelease') -Clear
    Invoke-Signing $Key (Join-Path $suite 'Release') (Join-Path $suite 'Release.gpg')
    $repomd = Join-Path $Folder 'rpm' $Channel 'repodata' 'repomd.xml'
    Invoke-Signing $Key $repomd "$repomd.asc"
}

# An offer's indexes served: the files named by their hashes first, which a PC still reading the last offer may want, and
# the signed ones last, which name them.
function Show-Offer([string]$Repository, [string]$Channel, [string]$Folder) {
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
    $packages = @(Get-Packed $Folder $package | ForEach-Object {
            $under = $_.Extension -eq '.deb' ? "apt/pool/$($Lever.Channel)" : "rpm/$($Lever.Channel)/packages"
            Add-Package $Lever.Repository $_.FullName $under
        })
    $packages += @(Get-ChildItem $Folder -File -Filter "thermi-$Version-linux-*.tar.gz" |
            ForEach-Object { Add-Package $Lever.Repository $_.FullName "tarballs/$Version" })
    $History.published = @($History.published) + $Version
    $History.highest = $package
    Write-Offer $Lever $History ([pscustomobject]@{ lever = 'publish'; version = $Version; package = $package; packages = $packages })
}

# The version published before the one offered, which no rollback withdrew, packed again under a higher number.
function Undo-Version([hashtable]$Lever, [pscustomobject]$History) {
    $current = Get-Current $History
    if (-not $current -or -not $current.version) { throw "$($Lever.Channel) offers no version to roll back." }
    $target = Get-RollbackTarget $History
    if (-not $target) { throw "$($Lever.Channel) published nothing before $($current.version) to roll back to." }
    $package = "$($History.highest)+really$($target.Replace('-', '~'))"
    $packed = Join-Path ([IO.Path]::GetTempPath()) "thermi-rollback-$PID"
    try {
        pwsh -NoProfile -File (Join-Path $PSScriptRoot 'linux-packages.ps1') -From (Join-Path $Lever.Repository 'tarballs' $target) `
            -PackageVersion $package -Key $Lever.Key -UpdatesFrom $Lever.UpdatesFrom -Output $packed
        if ($LASTEXITCODE -ne 0) { throw "$target was not packed again as $package." }
        $packages = @(Get-Packed $packed $package | ForEach-Object {
                $under = $_.Extension -eq '.deb' ? "apt/pool/$($Lever.Channel)" : "rpm/$($Lever.Channel)/packages"
                Add-Package $Lever.Repository $_.FullName $under
            })
    }
    finally {
        Remove-Item -Recurse -Force $packed -ErrorAction SilentlyContinue
    }
    $History.withdrawn = @($History.withdrawn) + $current.version
    $History.highest = $package
    Write-Offer $Lever $History ([pscustomobject]@{ lever = 'rollback'; version = $target; package = $package; packages = $packages })
}

# The offer before the current one, served again as it was signed then.
function Restore-Offer([hashtable]$Lever, [pscustomobject]$History) {
    $states = @($History.states)
    if ($states.Count -lt 2) { throw "$($Lever.Channel) has no offer before its current one." }
    $before = $states[-2]
    Show-Offer $Lever.Repository $Lever.Channel (Join-Path $Lever.Repository 'history' $Lever.Channel $before.offer)
    $restored = $before | Select-Object * -ExcludeProperty lever
    $restored | Add-Member -NotePropertyName lever -NotePropertyValue 'restore'
    $History.states = $states + $restored
    Save-History $Lever $History
}

# The current offer signed again, as it expires 45 days after it is signed, or by another key: the history keeps offers,
# not their signatures.
function Protect-Again([hashtable]$Lever, [pscustomobject]$History) {
    $current = Get-Current $History
    if (-not $current) { throw "$($Lever.Channel) offers nothing to sign." }
    $signing = Join-Path ([IO.Path]::GetTempPath()) "thermi-sign-$PID"
    try {
        Write-Index $Lever $current $signing
        Protect-Offer $Lever.Key $signing $Lever.Channel
        Show-Offer $Lever.Repository $Lever.Channel $signing
    }
    finally {
        Remove-Item -Recurse -Force $signing -ErrorAction SilentlyContinue
    }
}

<#
Pulls $Command on the repositories: publish <version>, promote, rollback, disable, sign, restore --before-last or status,
with --repository <folder> --channel stable|beta, the packages' folder as --packages, the signing key as --key or
THERMI_REPOSITORY_KEY and the trusted public key as --trust or THERMI_REPOSITORY_PUB, else the build's own in the packages'
folder; $Index runs thermi-releases.
#>
function Invoke-RepositoryLever([string]$Command, [string[]]$Arguments, [string[]]$Index) {
    $options = Read-LeverOptions $Arguments
    $repository = $options['repository']
    New-Item -ItemType Directory -Force $repository | Out-Null
    $lever = @{
        Repository = (Resolve-Path $repository).Path; Channel = $options['channel']; Index = $Index
        Key = $Command -in 'status' ? $null : (Get-LeverKey $options 'key' 'THERMI_REPOSITORY_KEY' 'thermi.key.asc')
        UpdatesFrom = $options['updates-from'] ?? 'http://127.0.0.1:8765/v1/'
    }
    $trust = Get-LeverKey $options 'trust' 'THERMI_REPOSITORY_PUB' 'thermi.asc'
    $history = Read-History $lever.Repository $lever.Channel $trust
    switch ($Command) {
        'publish' { Publish-Version $lever $history $options['version'] $options['packages'] }
        'promote' {
            $from = Read-History $lever.Repository ($options['from'] ?? 'beta') $trust
            $lever.Channel = $options['to'] ?? 'stable'
            $history = Read-History $lever.Repository $lever.Channel $trust
            $current = Get-Current $from
            if (-not $current.version) { throw "$($options['from'] ?? 'beta') offers no version to promote." }
            $folder = Join-Path ([IO.Path]::GetTempPath()) "thermi-promote-$PID"
            New-Item -ItemType Directory -Force $folder | Out-Null
            try {
                foreach ($package in $current.packages) { Copy-Item (Join-Path $lever.Repository $package.path) $folder }
                Publish-Version $lever $history $current.version $folder
            }
            finally { Remove-Item -Recurse -Force $folder }
        }
        'rollback' { Undo-Version $lever $history }
        'disable' { Write-Offer $lever $history ([pscustomobject]@{ lever = 'disable'; version = $null; package = $null; packages = @() }) }
        'sign' { Protect-Again $lever $history }
        'restore' {
            if (-not $options['before-last']) { throw 'restore takes --before-last.' }
            Restore-Offer $lever $history
        }
        'status' {
            $current = Get-Current $history
            Write-Host ($current ? "$($lever.Channel): $($current.lever), $($current.package ?? 'nothing')" : "$($lever.Channel): nothing published")
            return
        }
        default { throw "$Command is no lever on the repositories: publish, promote, rollback, disable, sign, restore or status." }
    }
    Write-Host "$($lever.Channel): $Command done."
}
