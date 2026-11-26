#Requires -Version 7
<#
.SYNOPSIS
    Draws the program's icon, src/Thermi.Windows/thermi.ico, from thermi's mark, with the tray's own painter, at every size
    Windows shows a program's icon at, and the README's logo, docs/images/thermi-light.png and thermi-dark.png. Run it
    after changing the mark: the build embeds the icon as thermi.exe's, and ProgramIconTests.TheProgramsIconIsTheMark
    fails while a file is not the mark.
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
Write-Host "ok   $(Join-Path $root 'src' 'Thermi.Windows' 'thermi.ico'), $(Join-Path $root 'docs' 'images')"
