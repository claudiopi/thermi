# Dot-sourced by the check and the stats: what each test project measures, and the app's coverage from the Windows and
# Linux tests' reports, which each test process writes of its own; a line or branch that any of them ran counts once.

# The assemblies each test project measures, each with the project it is built from, as the dashboard names it: thermi
# and thermi-sensors are each OS's own. Thermi.Tests measures what it runs of the shared code and the tools, not the app,
# which the Windows and Linux tests measure where they run it.
$coverageMeasured = [ordered]@{
    'Thermi.Tests' = [ordered]@{
        'Thermi.Core' = 'Thermi.Core'; 'Thermi.Cli' = 'Thermi.Cli'; 'Thermi.Client' = 'Thermi.Client'; 'thermi-releases' = 'Thermi.Releases'
    }
    'Thermi.Windows.Tests' = [ordered]@{
        thermi = 'Thermi.Windows'; 'thermi-sensors' = 'Thermi.Sensors.Windows'; 'Thermi.App' = 'Thermi.App'; 'Thermi.Trust' = 'Thermi.Trust'
    }
    'Thermi.Linux.Tests' = [ordered]@{ thermi = 'Thermi.Linux'; 'thermi-sensors' = 'Thermi.Sensors.Linux'; 'Thermi.App' = 'Thermi.App' }
}

# The code no test process measures, and why, so the dashboard's coverage lists every project its code table does.
$coverageUnmeasured = [ordered]@{
    'scripts' = 'not measured: each runs as a program of its own, in the check, the CI and some tests'
    'Thermi.Gnome' = 'not measured: it runs inside GNOME Shell, a process of its own in the Linux tests'
    'Thermi.Settings.Types' = 'not measured: it runs in the build; the page type-checks what it writes, and ContractTests read its schemas'
}

# Each module's lines and branches, merged from the reports each test project wrote under $Folder, and the modules each
# project's reports measured.
function Read-AppCoverage([string]$Folder, [string[]]$Projects) {
    $modules = @{}
    $measured = @{}
    foreach ($tests in $Projects) {
        $names = @{}
        foreach ($report in Get-ChildItem $Folder -Filter "$tests.*coverage*.json" -ErrorAction SilentlyContinue) {
            $read = Get-Content $report.FullName -Raw | ConvertFrom-Json -AsHashtable
            foreach ($module in $read.GetEnumerator()) { $names[[IO.Path]::GetFileNameWithoutExtension($module.Key)] += @($module.Value) }
        }
        $measured[$tests] = @($names.Keys)
        foreach ($name in $names.Keys) {
            if (-not $modules[$name]) { $modules[$name] = @{ Lines = @{}; Branches = @{} } }
            foreach ($files in $names[$name]) { Add-Hits $modules[$name] $files }
        }
    }
    @{ Modules = $modules; Measured = $measured }
}

# A report holds file, class, method, then each method's lines and branches.
function Add-Hits($Into, $Files) {
    foreach ($file in $Files.GetEnumerator()) {
        foreach ($method in $file.Value.Values.ForEach({ $_.GetEnumerator() })) {
            foreach ($line in $method.Value.Lines.GetEnumerator()) {
                $key = "$($file.Key)|$($line.Key)"
                $Into.Lines[$key] = $Into.Lines[$key] -or $line.Value -gt 0
            }
            foreach ($branch in $method.Value.Branches) {
                $key = "$($method.Key)|$($branch.Line)|$($branch.Offset)|$($branch.EndOffset)|$($branch.Path)|$($branch.Ordinal)"
                $Into.Branches[$key] = $Into.Branches[$key] -or $branch.Hits -gt 0
            }
        }
    }
}

function Get-Rate([hashtable]$Hits) {
    if ($Hits.Count -eq 0) { return 100 }
    100 * @($Hits.Values | Where-Object { $_ }).Count / $Hits.Count
}
