#Requires -Version 7
<#
.SYNOPSIS
    Some tests while you work, one run at a time on the machine and below normal priority, like scripts/check.ps1:
    pwsh scripts/test.ps1 --project tests/Thermi.Tests --filter-method "*Threshold*" passes what follows to dotnet test;
    pwsh scripts/test.ps1 -Web tests/Quality.spec.ts -g "fits" builds the page and passes what follows to Playwright.
#>
# No param block: PowerShell would take the "--" that dotnet test's own arguments may hold, and -Web with it.

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$web = $args.Count -gt 0 -and $args[0] -eq '-Web'
$passed = if ($web) { $args | Select-Object -Skip 1 } else { $args }
. (Join-Path $PSScriptRoot 'check-lock.ps1')

try {
    Enter-Check
    if ($web) {
        $page = Join-Path $root 'src' 'Thermi.Settings.Web'
        npm run build --prefix $page
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Push-Location $page
        try { npx --no-install playwright test @passed }
        finally { Pop-Location }
    }
    else {
        Push-Location $root
        try { dotnet test @passed }
        finally { Pop-Location }
    }
    exit $LASTEXITCODE
}
finally {
    Exit-Check
}
