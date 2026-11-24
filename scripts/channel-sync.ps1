#Requires -Version 7
<#
.SYNOPSIS
Copies the update channels between the release workflow's channels folder and the R2 bucket that serves them
(docs/RELEASES.md): down before a lever, up after it.

.DESCRIPTION
Down fetches the manifests and their histories, and with -Version that version's zips only, so a zip published again
with other bytes is refused, and keeps a copy of what it fetched. Up sends the zips first, which never change once
written, then each history and manifest the lever changed, and refuses any that another run changed in the bucket meanwhile,
so two runs never sign over each other's sequence. The bucket's credentials come from AWS_ACCESS_KEY_ID,
AWS_SECRET_ACCESS_KEY and R2_ACCOUNT_ID.
#>
param(
    [Parameter(Mandatory)][ValidateSet('down', 'up')][string]$Direction,
    [string]$Folder = 'channels',
    [string]$Version,
    [string]$Bucket = 'thermi-updates')

$ErrorActionPreference = 'Stop'
$endpoint = "https://$($env:R2_ACCOUNT_ID).r2.cloudflarestorage.com"
$remote = "s3://$Bucket/v1"
$fetched = "$Folder.fetched"

function Invoke-Aws([string[]]$Arguments) {
    aws @Arguments --endpoint-url $endpoint --region auto --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw "aws $($Arguments[0..1] -join ' ') failed (exit code $LASTEXITCODE)." }
}

# The manifests and histories, as paths under the folder.
function Get-Signed([string]$Root) {
    if (-not (Test-Path $Root)) { return @() }
    @(Get-ChildItem $Root -Filter *.json -File) + @(Get-ChildItem (Join-Path $Root 'history') -Filter *.json -File -ErrorAction SilentlyContinue) |
        ForEach-Object { [IO.Path]::GetRelativePath($Root, $_.FullName) -replace '\\', '/' }
}

function Get-Hash([string]$Path) {
    if (Test-Path $Path) { (Get-FileHash $Path -Algorithm SHA256).Hash } else { '' }
}

if ($Direction -eq 'down') {
    New-Item $Folder -ItemType Directory -Force | Out-Null
    Invoke-Aws @('s3', 'sync', $remote, $Folder, '--exclude', 'zips/*')
    if ($Version) { Invoke-Aws @('s3', 'sync', "$remote/zips/$Version", (Join-Path $Folder "zips/$Version")) }
    Remove-Item $fetched -Recurse -Force -ErrorAction SilentlyContinue
    New-Item $fetched -ItemType Directory | Out-Null
    foreach ($name in Get-Signed $Folder) {
        New-Item (Split-Path (Join-Path $fetched $name)) -ItemType Directory -Force | Out-Null
        Copy-Item (Join-Path $Folder $name) (Join-Path $fetched $name)
    }
    return
}

$changed = @(Get-Signed $Folder | Where-Object { (Get-Hash (Join-Path $Folder $_)) -ne (Get-Hash (Join-Path $fetched $_)) })
$now = Join-Path ([IO.Path]::GetTempPath()) "channels-now-$([guid]::NewGuid())"
New-Item $now -ItemType Directory | Out-Null
try {
    foreach ($name in $changed) {
        $there = Join-Path $now ($name -replace '/', '_')
        aws s3 cp "$remote/$name" $there --endpoint-url $endpoint --region auto --only-show-errors 2>$null | Out-Null
        if ((Get-Hash $there) -ne (Get-Hash (Join-Path $fetched $name))) {
            throw "$name changed in the bucket since this run fetched it: run the lever again."
        }
    }
} finally {
    Remove-Item $now -Recurse -Force
}

if (Test-Path (Join-Path $Folder 'zips')) {
    Invoke-Aws @('s3', 'sync', (Join-Path $Folder 'zips'), "$remote/zips", '--cache-control', 'public, max-age=31536000, immutable')
}
foreach ($name in $changed | Sort-Object { $_ -notlike 'history/*' }) {
    $cache = $name -like 'history/*' ? 'no-store' : 'public, max-age=60'
    Invoke-Aws @('s3', 'cp', (Join-Path $Folder $name), "$remote/$name", '--cache-control', $cache, '--content-type', 'application/json')
}
