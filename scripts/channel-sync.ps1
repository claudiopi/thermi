#Requires -Version 7
<#
.SYNOPSIS
Copies the update channels between the release workflow's channels folder and the R2 bucket that serves them
(docs/RELEASES.md): down before a lever, up after it.

.DESCRIPTION
Down fetches the manifests and their histories, and with -Version that version's zips and machine MSIs only, so one
published again with other bytes is refused, and keeps a copy of what it fetched with each one's ETag. Up sends the zips
and MSIs first, which never change once written, then each history and manifest the lever changed, each on the condition that the bucket still
holds what was fetched, so two runs never sign over each other's sequence. The bucket's credentials come from
AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY and R2_ACCOUNT_ID.
With -Linux it copies the Linux repositories instead, <Folder>/linux and v1/linux (scripts/repository.ps1): down
fetches the histories, the indexes and the files a lever reads; up sends the packages and tarballs first, then each
history on the same condition, then the indexes, each named by its hash before those that name them.
#>
param(
    [Parameter(Mandatory)][ValidateSet('down', 'up')][string]$Direction,
    [string]$Folder = 'channels',
    [string]$Version,
    [string]$Bucket = 'thermi-updates',
    [switch]$Linux)

$ErrorActionPreference = 'Stop'

# Conditional puts, --if-match and --if-none-match, came with AWS CLI 2.22: an older one would put over another run's.
if ((aws --version) -notmatch '^aws-cli/(\d+)\.(\d+)' -or [version]"$($Matches[1]).$($Matches[2])" -lt [version]'2.22') {
    throw "channel-sync.ps1 needs AWS CLI 2.22 or later, for conditional puts; this is $(aws --version)."
}
$endpoint = "https://$($env:R2_ACCOUNT_ID).r2.cloudflarestorage.com"
$remote = "s3://$Bucket/v1"
$fetched = "$Folder.fetched"

function Invoke-Aws([string[]]$Arguments) {
    $quiet = $Arguments[0] -eq 's3' ? @('--only-show-errors') : @('--output', 'text')
    aws @Arguments --endpoint-url $endpoint --region auto @quiet | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "aws $($Arguments[0..1] -join ' ') failed (exit code $LASTEXITCODE)." }
}

# The manifests and their histories, as paths under the folder and keys under v1/ alike.
$signed = @('stable.json', 'beta.json', 'history/stable.json', 'history/beta.json')

function Get-Hash([string]$Path) {
    if (Test-Path $Path) { (Get-FileHash $Path -Algorithm SHA256).Hash } else { '' }
}

# The files no lever changes once written, which are fetched only as a lever needs them and sent only when new.
$immutable = '^(apt/pool|rpm/[^/]+/packages|tarballs)/'

if ($Linux) {
    $Folder, $remote, $fetched = (Join-Path $Folder 'linux'), "$remote/linux", "$Folder.linux.fetched"
    $signed = @('history/stable.json', 'history/beta.json')
}

if ($Linux -and $Direction -eq 'down') {
    . (Join-Path $PSScriptRoot 'repository.ps1')
    New-Item $Folder -ItemType Directory -Force | Out-Null
    $tags = @{}
    foreach ($name in $signed) {
        $tag = aws s3api head-object --bucket $Bucket --key "v1/linux/$name" --query ETag --output text --endpoint-url $endpoint --region auto 2>$null
        $tags[$name] = $LASTEXITCODE -eq 0 ? $tag : $null
    }
    Invoke-Aws @('s3', 'sync', $remote, $Folder, '--exclude', 'apt/pool/*', '--exclude', 'rpm/*/packages/*', '--exclude', 'tarballs/*')
    foreach ($channel in 'stable', 'beta') {
        $history = Join-Path $Folder 'history' "$channel.json"
        if (-not (Test-Path $history)) { continue }
        foreach ($file in Get-LeverFiles (Get-Content $history -Raw | ConvertFrom-Json)) {
            Invoke-Aws @('s3', 'cp', "$remote/$($file.path)", (Join-Path $Folder $file.path))
        }
    }
    Remove-Item $fetched -Recurse -Force -ErrorAction SilentlyContinue
    New-Item (Join-Path $fetched 'history') -ItemType Directory -Force | Out-Null
    foreach ($name in $signed | Where-Object { Test-Path (Join-Path $Folder $_) }) { Copy-Item (Join-Path $Folder $_) (Join-Path $fetched $_) }
    @{ tags = $tags; files = @(Get-ChildItem $Folder -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($Folder, $_.FullName).Replace('\', '/') }) } |
        ConvertTo-Json -Depth 3 | Set-Content (Join-Path $fetched 'fetched.json')
    return
}

if ($Linux) {
    $was = Get-Content (Join-Path $fetched 'fetched.json') -Raw | ConvertFrom-Json -AsHashtable
    $files = @(Get-ChildItem $Folder -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($Folder, $_.FullName).Replace('\', '/') })
    # A package or tarball goes up only if the bucket has none of its name, so none is ever written over.
    foreach ($name in $files | Where-Object { $_ -match $immutable -and $_ -notin $was.files }) {
        Invoke-Aws @('s3api', 'put-object', '--bucket', $Bucket, '--key', "v1/linux/$name", '--body', (Join-Path $Folder $name),
            '--cache-control', 'public, max-age=31536000, immutable', '--if-none-match', '*')
    }
    $changed = $signed | Where-Object { (Get-Hash (Join-Path $Folder $_)) -ne (Get-Hash (Join-Path $fetched $_)) }
    foreach ($name in $changed) {
        $condition = $was.tags[$name] ? @('--if-match', $was.tags[$name]) : @('--if-none-match', '*')
        Invoke-Aws (@('s3api', 'put-object', '--bucket', $Bucket, '--key', "v1/linux/$name", '--body', (Join-Path $Folder $name),
                '--cache-control', 'no-store') + $condition)
    }
    # The rest after the histories: what a lever kept and signed, and the indexes, the ones named by a hash before those
    # that name them, each signature last.
    $rest = $files | Where-Object { $_ -notmatch $immutable -and $_ -notin $signed } |
        Sort-Object { $_ -match '(^|/)(InRelease|Release|Release\.gpg|Packages(\.gz)?|repomd\.xml(\.asc)?)$' -and $_ -notlike 'history/*' },
            { $_ -match '(InRelease|\.asc)$' }
    foreach ($name in $rest) {
        Invoke-Aws @('s3', 'cp', (Join-Path $Folder $name), "$remote/$name", '--cache-control', ($name -like 'history/*' ? 'no-store' : 'public, max-age=60'))
    }
    return
}

if ($Direction -eq 'down') {
    New-Item $Folder -ItemType Directory -Force | Out-Null
    # Each one's ETag before its bytes: one changed in between fails the upload's condition rather than pass it.
    $tags = @{}
    foreach ($name in $signed) {
        $tag = aws s3api head-object --bucket $Bucket --key "v1/$name" --query ETag --output text --endpoint-url $endpoint --region auto 2>$null
        $tags[$name] = $LASTEXITCODE -eq 0 ? $tag : $null
    }
    # A sync skips a local file of the same size, and a history at its cap keeps its size: each is fetched afresh.
    foreach ($name in $signed) { Remove-Item (Join-Path $Folder $name) -ErrorAction SilentlyContinue }
    Invoke-Aws @('s3', 'sync', $remote, $Folder, '--exclude', 'zips/*', '--exclude', 'msis/*', '--exclude', 'linux/*')
    if ($Version) {
        foreach ($files in 'zips', 'msis') { Invoke-Aws @('s3', 'sync', "$remote/$files/$Version", (Join-Path $Folder "$files/$Version")) }
    }
    Remove-Item $fetched -Recurse -Force -ErrorAction SilentlyContinue
    New-Item (Join-Path $fetched 'history') -ItemType Directory -Force | Out-Null
    foreach ($name in $signed | Where-Object { Test-Path (Join-Path $Folder $_) }) { Copy-Item (Join-Path $Folder $_) (Join-Path $fetched $_) }
    $tags | ConvertTo-Json | Set-Content (Join-Path $fetched 'etags.json')
    return
}

foreach ($files in 'zips', 'msis' | Where-Object { Test-Path (Join-Path $Folder $_) }) {
    Invoke-Aws @('s3', 'sync', (Join-Path $Folder $files), "$remote/$files", '--cache-control', 'public, max-age=31536000, immutable')
}

# Each history before its manifest, each only if the bucket still holds what this run fetched: R2 refuses the put
# otherwise, so of two runs that pulled levers at once, the second fails at the first file both changed, a history, before any
# manifest of the same channel lands, and is run again.
$tags = Get-Content (Join-Path $fetched 'etags.json') -Raw | ConvertFrom-Json -AsHashtable
$changed = $signed | Where-Object { (Get-Hash (Join-Path $Folder $_)) -ne (Get-Hash (Join-Path $fetched $_)) }
foreach ($name in $changed | Sort-Object { $_ -notlike 'history/*' }) {
    $cache = $name -like 'history/*' ? 'no-store' : 'public, max-age=60'
    $condition = $tags[$name] ? @('--if-match', $tags[$name]) : @('--if-none-match', '*')
    Invoke-Aws (@('s3api', 'put-object', '--bucket', $Bucket, '--key', "v1/$name", '--body', (Join-Path $Folder $name),
            '--cache-control', $cache, '--content-type', 'application/json') + $condition)
}