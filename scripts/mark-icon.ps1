#Requires -Version 7
<#
.SYNOPSIS
    Draws the program's icon, src/Thermi.Windows/thermi.ico, from thermi's mark, with the tray's own painter, at every size
    Windows shows a program's icon at. Run it after changing the mark: the build embeds the file as thermi.exe's icon,
    and TrayIconsTests.TheProgramsIconIsTheMark fails while the file is not the mark.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:THERMI_WRITE_ICON = '1'
try {
    dotnet test --project (Join-Path $root 'tests' 'Thermi.Windows.Tests') -- --filter-method '*TheProgramsIconIsTheMark'
    if ($LASTEXITCODE -ne 0) { exit 1 }
}
finally {
    Remove-Item Env:THERMI_WRITE_ICON
}
Write-Host "ok   $(Join-Path $root 'src' 'Thermi.Windows' 'thermi.ico')"
