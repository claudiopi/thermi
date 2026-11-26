# Dot-sourced by scripts/check.ps1 and scripts/test.ps1.
# Several checks at once, each in a worktree of its own, make the PC slow to type on. So a check or a test run goes to
# idle priority, and so does all it starts, since Windows gives a child its parent's idle class: below normal would still
# outrank the apps that drop to idle while they wait in the background, as a terminal's renderer, which then answers
# the keyboard a second late once it comes back to the front. And one .NET half and one page's half run at a time on the
# machine, each under a name that is the same in every worktree, so a check of both runs its halves side by side. The
# CI's machine runs nothing else.
function Enter-Check([ValidateSet('dotnet', 'web')][string]$Half) {
    $self = [Diagnostics.Process]::GetCurrentProcess()
    $script:priority = $self.PriorityClass
    $self.PriorityClass = 'Idle'
    $script:lock = [Threading.Mutex]::new($false, "Global\thermi-check-$Half")
    $script:holder = [IO.MemoryMappedFiles.MemoryMappedFile]::CreateOrOpen("Local\thermi-check-holder-$Half", 1024)
    if (-not (Wait-Check 0)) {
        $reader = [IO.BinaryReader]::new($holder.CreateViewStream())
        Write-Host "waiting for another check's $Half half ($($reader.ReadString()))"
        $reader.Dispose()
        # Short waits, so Ctrl+C still stops it.
        while (-not (Wait-Check 500)) { }
    }
    $script:held = $true
    $writer = [IO.BinaryWriter]::new($holder.CreateViewStream())
    $writer.Write("pid $PID, folder $(Split-Path $PSScriptRoot -Parent)")
    $writer.Dispose()
    Limit-BuildServers
}

# The compiler server and MSBuild's reused nodes serve every build after the one that started them, at that one's
# priority: one a build at normal priority left running would compile this check's code at normal priority. They are
# lowered as the check starts; those the check starts are idle already. Lowering keeps them, and builds fast.
function Limit-BuildServers {
    Get-CimInstance Win32_Process -Filter "Name = 'VBCSCompiler.exe' OR Name = 'dotnet.exe' OR Name = 'MSBuild.exe'" |
        Where-Object { $_.Name -eq 'VBCSCompiler.exe' -or $_.CommandLine -match 'VBCSCompiler\.dll|/nodemode' } |
        ForEach-Object {
            $server = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
            # Another user's server, or one that just ended, is left as it is.
            try { if ($server -and $server.PriorityClass -ne 'Idle') { $server.PriorityClass = 'Idle' } }
            catch [ComponentModel.Win32Exception], [InvalidOperationException] { }
        }
}

function Wait-Check([int]$Milliseconds) {
    try { $lock.WaitOne($Milliseconds) }
    # A check that died without letting go has ended all the same.
    catch [Threading.AbandonedMutexException] { $true }
}

function Exit-Check {
    if ($held) { $lock.ReleaseMutex() }
    if ($lock) { $lock.Dispose() }
    if ($holder) { $holder.Dispose() }
    if ($priority) { [Diagnostics.Process]::GetCurrentProcess().PriorityClass = $priority }
}
