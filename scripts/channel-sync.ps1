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
fetches the histories, the indexes and the files a lever reads, and with -Version says whether the version is published,
as the output published, or refuses one a run left half sent; up sends the tarballs, the packages and the indexes named by
their hash first, then each history on the same condition, then the other indexes, those the signed ones name before them.
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

# The files no lever changes once written, sent only when new: the packages and tarballs, fetched only as a lever needs
# them, and the indexes named by their hash.
$immutable = '^(apt/pool/|apt/dists/[^/]+/main/binary-[^/]+/by-hash/|rpm/[^/]+/packages/|rpm/[^/]+/repodata/[0-9a-f]{64}-|tarballs/)'

if ($Linux) {
    $Folder, $remote, $fetched = (Join-Path $Folder 'linux'), "$remote/linux", "$Folder.linux.fetched"
    $signed = @('history/stable.json', 'history/beta.json')
}

# Whether -Version is the bucket's already. A history that published it says a run went through: it is published, and a
# run again only pushes to the AUR. Its tarballs without that say a run stopped midway; they go up first, so no package
# of it is in the bucket without them. A run again would build the packages anew, other bytes under names the bucket has,
# and none of them is in an index yet, so the owner removes them first.
function Test-LinuxVersion {
    $published = @('stable', 'beta' | ForEach-Object { Join-Path $Folder 'history' "$_.json" } | Where-Object { Test-Path $_ } |
            Where-Object { $Version -in @((Get-Content $_ -Raw | ConvertFrom-Json).published) })
    if ($published) {
        Write-Host "thermi $Version is published already: only the AUR is left to push."
        if ($env:GITHUB_OUTPUT) { 'published=true' >> $env:GITHUB_OUTPUT }
        return
    }
    if ($env:GITHUB_OUTPUT) { 'published=false' >> $env:GITHUB_OUTPUT }
    $listed = aws s3 ls "$remote/tarballs/$Version/" --endpoint-url $endpoint --region auto 2>$null
    if ($listed) {
        throw ("A run of thermi $Version stopped midway: v1/linux/tarballs/$Version/ is in the bucket, and no history published it. " +
            "Remove it and $Version's packages under v1/linux/apt/pool and v1/linux/rpm/*/packages, which no index names, then run again.")
    }
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
    if ($Version) { Test-LinuxVersion }
    return
}

if ($Linux) {
    $was = Get-Content (Join-Path $fetched 'fetched.json') -Raw | ConvertFrom-Json -AsHashtable
    $files = @(Get-ChildItem $Folder -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($Folder, $_.FullName).Replace('\', '/') })
    # Each goes up only if the bucket has none of its name, so none is ever written over; the tarballs first.
    foreach ($name in $files | Where-Object { $_ -match $immutable -and $_ -notin $was.files } | Sort-Object { $_ -notlike 'tarballs/*' }) {
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