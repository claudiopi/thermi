#Requires -Version 7
<#
.SYNOPSIS
    The CI's side of the dashboard, which the 📊 Stats dashboard job runs once the other jobs handed it what they measured:
    what GitHub knows of the run, its jobs' times and results, the open bugs and update PRs, the last Bug hunt's results
    and main's history, saved beside the rest under TestResults/stats. -Push adds this run to the `stats` branch's
    history and badges, on a push to main alone.
    What GitHub cannot answer is left out, and the dashboard shows it as missing; GH_TOKEN reads, and with -Push writes.
#>
param(
    [string]$Results,
    [switch]$Push
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Results) { $Results = Join-Path $root 'TestResults' }
$stats = Join-Path $Results 'stats'
$repo = $env:GITHUB_REPOSITORY
$history = Join-Path $Results 'stats-history.json'
New-Item -ItemType Directory -Force $stats | Out-Null

# A call whose failure leaves its part of the dashboard missing, never the job failed.
function Invoke-Gh {
    $output = gh @args 2>&1
    if ($LASTEXITCODE -eq 0) { return $output }
    Write-Host "::warning::gh $($args[0..1] -join ' ') failed, so the dashboard shows its part as missing: $output"
}

# The stats branch's tip into a folder, or an empty folder when the branch is not there yet; its stats.json as the history.
function Get-StatsBranch([string]$Into) {
    Remove-Item $Into -Recurse -Force -ErrorAction Ignore
    git init --quiet $Into
    git -C $Into fetch --quiet --depth 1 "https://github.com/$repo.git" stats 2>$null
    if ($LASTEXITCODE -eq 0) { git -C $Into checkout --quiet -B stats FETCH_HEAD } else { git -C $Into checkout --quiet --orphan stats }
    Remove-Item $history -ErrorAction Ignore
    if (Test-Path (Join-Path $Into 'stats.json')) { Copy-Item (Join-Path $Into 'stats.json') $history }
}

# The jobs that measured, without this one and the gate, which wait for it: each one's time and result, and how long
# the run took from the first one's start to the last one's end.
function Get-Jobs {
    $listed = Invoke-Gh api "repos/$repo/actions/runs/$env:GITHUB_RUN_ID/attempts/$env:GITHUB_RUN_ATTEMPT/jobs?per_page=100" | Out-String | ConvertFrom-Json
    $done = @($listed.jobs | Where-Object { $_.status -eq 'completed' -and $_.name -notin '📊 Stats dashboard', 'CI gate' -and $_.started_at })
    $jobs = foreach ($job in $done) {
        @{ name = $job.name; seconds = ([DateTime]$job.completed_at - [DateTime]$job.started_at).TotalSeconds; result = $job.conclusion }
    }
    $wall = if ($done) {
        $first = ($done | ForEach-Object { [DateTime]$_.started_at } | Sort-Object)[0]
        (($done | ForEach-Object { [DateTime]$_.completed_at } | Sort-Object)[-1] - $first).TotalSeconds
    }
    @{ Jobs = @($jobs); Wall = $wall }
}

function Get-Count([string]$Query) {
    $count = Invoke-Gh api -X GET search/issues -f "q=repo:$repo $Query" --jq .total_count
    if ($count) { [int]"$count" }
}

# The last Bug hunt that left its results: the branch's own, run by hand, else main's nightly.
function Get-BugHunt {
    $branches = @($env:GITHUB_HEAD_REF, 'main') | Where-Object { $_ } | Select-Object -Unique
    foreach ($branch in $branches) {
        $runs = Invoke-Gh run list -R $repo --workflow mutation.yml --branch $branch --status completed --limit 5 --json 'databaseId,url,conclusion' |
            Out-String | ConvertFrom-Json
        foreach ($run in $runs | Where-Object conclusion -in 'success', 'failure') {
            $into = Join-Path ([IO.Path]::GetTempPath()) "bughunt-$($run.databaseId)"
            Invoke-Gh run download $run.databaseId -R $repo -p 'stats-bughunt-*' -D $into | Out-Null
            $found = @(Get-ChildItem $into -Recurse -Filter 'bughunt-*.json' -ErrorAction SilentlyContinue)
            if (-not $found) { continue }
            $found | Copy-Item -Destination $stats
            return $run.url
        }
    }
}

if ($Push) {
    # A push to main writes it; another run's push between this one's fetch and its own is fetched, and this run added again.
    $branch = Join-Path ([IO.Path]::GetTempPath()) 'stats-branch'
    $bot = 'github-actions[bot]'
    $auth = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("x-access-token:$env:GH_TOKEN"))
    foreach ($attempt in 1..3) {
        Get-StatsBranch $branch
        pwsh -NoProfile -File (Join-Path $PSScriptRoot 'stats.ps1') -Results $Results -History $history -Out (Join-Path $Results 'pushed.md') -Publish $branch
        if ($LASTEXITCODE -ne 0) { exit 1 }
        git -C $branch add --all
        git -C $branch -c user.name=$bot -c user.email="41898099+$bot@users.noreply.github.com" commit --quiet -m "stats: $env:GITHUB_SHA"
        git -C $branch -c "http.extraheader=AUTHORIZATION: basic $auth" push --quiet "https://github.com/$repo.git" stats
        if ($LASTEXITCODE -eq 0) { exit 0 }
        Write-Host "The stats branch moved; attempt $attempt of 3."
    }
    exit 1
}

$needs = $env:NEEDS | ConvertFrom-Json -AsHashtable
$outcomes = @{}
foreach ($job in $needs.Keys) { $outcomes[$job] = $needs[$job].result }
$timed = Get-Jobs
Get-StatsBranch (Join-Path ([IO.Path]::GetTempPath()) 'stats-branch')
@{
    commit = "$env:HEAD_SHA".Substring(0, 7)
    runUrl = "$env:GITHUB_SERVER_URL/$repo/actions/runs/$env:GITHUB_RUN_ID"
    jobs = $timed.Jobs
    wallSeconds = $timed.Wall
    results = $outcomes
    passed = -not ($outcomes.Values | Where-Object { $_ -notin 'success', 'skipped' })
    openBugs = Get-Count 'is:issue is:open label:bug'
    updates = Get-Count 'is:pr is:open author:app/renovate'
    bugHuntUrl = Get-BugHunt
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $stats 'github.json') -Encoding utf8
