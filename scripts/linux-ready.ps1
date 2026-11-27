#Requires -Version 7
<#
.SYNOPSIS
Whether the release workflow's Linux jobs publish (docs/RELEASES.md), as the output ready: with the repositories' key
and the AUR's both set up. With none of their variables set, the Windows release goes on and these jobs publish nothing,
with a notice in the run's summary; with only some, the run fails, as the Windows release's own check does, rather than
let a channel's Release files expire behind green runs.
#>
$names = 'THERMI_REPOSITORY_KEY', 'THERMI_REPOSITORY_PUB', 'AUR_SSH_KEY', 'AUR_HOST_KEY'
$missing = @($names | Where-Object { -not [Environment]::GetEnvironmentVariable($_) })
"ready=$($missing.Count -eq 0)".ToLowerInvariant() >> $env:GITHUB_OUTPUT
if (-not $missing) { exit 0 }
$why = "$($missing -join ', ') not set, so this run publishes nothing for Linux: docs/RELEASES.md says how to set them up."
if ($missing.Count -lt $names.Count) {
    "::error::$why"
    exit 1
}
"::notice::Skipped. $why"
"Skipped. $why" >> $env:GITHUB_STEP_SUMMARY
