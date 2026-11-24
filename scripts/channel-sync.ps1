#Requires -Version 7
<#
.SYNOPSIS
Copies the update channels between the release workflow's channels folder and the R2 bucket that serves them
(docs/RELEASES.md): down before a lever, up after it.

.DESCRIPTION
Up sends the zips first, which never change once written, and the manifests last, so no PC is offered a version whose
zip is not there yet. The bucket's credentials come from AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY and R2_ACCOUNT_ID.
#>
param([Parameter(Mandatory)][ValidateSet('down', 'up')][string]$Direction, [string]$Folder = 'channels', [string]$Bucket = 'thermi-updates')

$ErrorActionPreference = 'Stop'
$endpoint = "https://$($env:R2_ACCOUNT_ID).r2.cloudflarestorage.com"
$remote = "s3://$Bucket/v1"

function Invoke-Aws([string[]]$Arguments) {
    aws @Arguments --endpoint-url $endpoint --region auto --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw "aws $($Arguments[0..1] -join ' ') failed (exit code $LASTEXITCODE)." }
}

if ($Direction -eq 'down') {
    New-Item $Folder -ItemType Directory -Force | Out-Null
    Invoke-Aws @('s3', 'sync', $remote, $Folder)
    return
}

Invoke-Aws @('s3', 'sync', (Join-Path $Folder 'zips'), "$remote/zips", '--cache-control', 'public, max-age=31536000, immutable')
Invoke-Aws @('s3', 'sync', (Join-Path $Folder 'history'), "$remote/history", '--cache-control', 'no-store')
foreach ($manifest in Get-ChildItem $Folder -Filter *.json -File) {
    Invoke-Aws @('s3', 'cp', $manifest.FullName, "$remote/$($manifest.Name)", '--cache-control', 'public, max-age=60', '--content-type', 'application/json')
}
