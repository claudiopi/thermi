<#
.SYNOPSIS
Dot-source it for Get-Nfpm: the path of nfpm, the tool that packs thermi's .deb and .rpm (docs/adr/0019-linux.md),
downloaded once into artifacts/tools from its GitHub release and used only when its SHA-256 is the one pinned here.

.DESCRIPTION
The version and the hashes are pinned together, from the release's checksums.txt: a new version changes all three, so a
release asset replaced after the fact, or a download cut short, never packs anything.
#>

$NfpmVersion = '2.49.0'
$NfpmHashes = @{
    'x86_64' = '2afaa5ffb5e3493402e31e7c67a5a1fb42133e3f66fc315ca0051934125561c7'
    'arm64' = 'd4eee71839e92c68cbd3b101640bad32d2856fc551acae0635be9118e48c9d9c'
}

function Get-Nfpm {
    if (-not $IsLinux) { throw 'nfpm packs thermi on Linux.' }
    $processor = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x86_64' }
    $folder = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts' 'tools' "nfpm-$NfpmVersion-$processor"
    $nfpm = Join-Path $folder 'nfpm'
    if (Test-Path $nfpm) { return $nfpm }

    $archive = "$folder.tar.gz"
    New-Item -ItemType Directory -Force (Split-Path $folder) | Out-Null
    Invoke-WebRequest "https://github.com/goreleaser/nfpm/releases/download/v$NfpmVersion/nfpm_${NfpmVersion}_Linux_$processor.tar.gz" -OutFile $archive
    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $NfpmHashes[$processor]) {
        Remove-Item $archive
        throw "nfpm $NfpmVersion for $processor has SHA-256 $hash, not the one pinned in scripts/nfpm.ps1."
    }
    # Unpacked beside its final folder and renamed into it, so a run cut short leaves no nfpm half there.
    $unpacking = "$folder.unpacking-$PID"
    New-Item -ItemType Directory -Force $unpacking | Out-Null
    tar -xzf $archive -C $unpacking nfpm
    if ($LASTEXITCODE -ne 0) { throw "nfpm's archive did not unpack." }
    Move-Item $unpacking $folder
    Remove-Item $archive
    $nfpm
}
