<#
.SYNOPSIS
Dot-source it for the OpenPGP steps of thermi's Linux repositories (docs/adr/0019-linux.md), each through gpg with a
keyring of its own that it deletes after, so nothing lands in the person's keyring and no agent of gpg's outlives it.
#>

# Runs $Steps with the folder of a keyring of its own, which holds the armored keys $Keys, and ends its agent before the
# folder goes. Each step's gpg takes the options Get-GpgOptions gives.
function Use-Keyring([string[]]$Keys, [scriptblock]$Steps) {
    $keyring = Join-Path ([IO.Path]::GetTempPath()) "thermi-gpg-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory $keyring | Out-Null
    chmod 700 $keyring
    try {
        foreach ($key in $Keys) {
            gpg --homedir $keyring --batch --quiet --import $key 2>$null
            if ($LASTEXITCODE -ne 0) { throw "gpg did not read the key $key." }
        }
        & $Steps $keyring
    }
    finally {
        gpgconf --homedir $keyring --kill all 2>$null
        Remove-Item -Recurse -Force $keyring -ErrorAction SilentlyContinue
    }
}

function Get-GpgOptions([string]$Keyring) { '--homedir', $Keyring, '--batch', '--yes', '--quiet', '--pinentry-mode', 'loopback', '--passphrase', '' }

# A key pair for thermi's repositories in $Folder, thermi.key.asc and its public half thermi.asc, unless one is there:
# RSA, which apt, dnf and rpm all check, and with no end, as a PC holds only the key its package installed. It never
# writes over a key, which each PC that installed a package with it trusts. It gives the secret key's file.
function New-RepositoryKey([string]$Folder, [string]$Name) {
    $secret = Join-Path $Folder 'thermi.key.asc'
    if (Test-Path $secret) { return $secret }
    New-Item -ItemType Directory -Force $Folder | Out-Null
    Use-Keyring @() {
        param($keyring)
        $gpg = Get-GpgOptions $keyring
        gpg @gpg --quick-generate-key "$Name <17047466+claudiopi@users.noreply.github.com>" rsa3072 sign never 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'gpg made no key.' }
        gpg @gpg --armor --output "$secret.new" --export-secret-keys
        gpg @gpg --armor --output (Join-Path $Folder 'thermi.asc') --export
        chmod 600 "$secret.new"
        Move-Item "$secret.new" $secret
    } | Out-Null
    $secret
}

function Get-PublicKey([string]$Key) {
    Use-Keyring @($Key) {
        param($keyring)
        $gpg = Get-GpgOptions $keyring
        (gpg @gpg --armor --export) -join "`n"
    }
}

# Signs $File with the secret key in $Key: clear-signed into $Out with -Clear, else a detached, armored signature.
function Invoke-Signing([string]$Key, [string]$File, [string]$Out, [switch]$Clear) {
    Use-Keyring @($Key) {
        param($keyring)
        $gpg = Get-GpgOptions $keyring
        $how = $Clear ? '--clearsign' : '--detach-sign'
        gpg @gpg --armor --digest-algo SHA256 $how --output $Out $File 2>$null
        if ($LASTEXITCODE -ne 0) { throw "gpg did not sign $File." }
    }
}

# Whether $Signature, detached, or $File clear-signed when there is none, is signed by the public key in $Key.
function Test-Signed([string]$Key, [string]$File, [string]$Signature) {
    Use-Keyring @($Key) {
        param($keyring)
        $gpg = Get-GpgOptions $keyring
        if ($Signature) { gpg @gpg --verify $Signature $File 2>$null } else { gpg @gpg --verify $File 2>$null }
        $LASTEXITCODE -eq 0
    }
}
