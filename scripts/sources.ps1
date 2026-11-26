# Dot-sourced by scripts/test-count.ps1 and scripts/check-steps.ps1.

# A project's C# files, never entering bin or obj: a walk through bin holds open the folders the Windows tests, run
# beside it, are moving there, so the move is refused.
function Get-SourceFiles([string]$Project) {
    Get-ChildItem $Project -File -Filter '*.cs'
    Get-ChildItem $Project -Directory | Where-Object Name -NotIn 'bin', 'obj' | Get-ChildItem -Recurse -File -Filter '*.cs'
}
