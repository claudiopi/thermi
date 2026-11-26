<#
.SYNOPSIS
    Install the WebView2 runtime when it is missing, as on a CI runner whose image lacks it: the window's tests host the
    settings page in it, and without it they wait until their timeout instead of failing at once.
#>
$ErrorActionPreference = 'Stop'

# WebView2's own registration, per machine and per user, as Microsoft documents for detecting the Evergreen runtime.
$client = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
$keys = "HKLM:\SOFTWARE\WOW6432Node\$client", "HKLM:\SOFTWARE\$client", "HKCU:\SOFTWARE\$client"
$version = $keys | ForEach-Object { (Get-ItemProperty -Path $_ -Name pv -ErrorAction SilentlyContinue).pv } |
    Where-Object { $_ -and $_ -ne '0.0.0.0' } | Select-Object -First 1
if ($version) {
    Write-Host "WebView2 $version is installed."
    return
}

$setup = Join-Path ([IO.Path]::GetTempPath()) 'MicrosoftEdgeWebview2Setup.exe'
Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $setup
$install = Start-Process -FilePath $setup -ArgumentList '/silent', '/install' -Wait -PassThru
if ($install.ExitCode -ne 0) {
    throw "WebView2's setup ended with $($install.ExitCode)."
}
Write-Host 'WebView2 is installed.'
