# Dot-sourced by scripts/test-count.ps1 and scripts/redundant.ps1. A table may say why each of its rows is a behavior of
# its own, in a "// Rows: ..." comment: on a .NET test's [Theory] line, or on or just above the line that opens a page's
# loop or .each. Such a table may pass the cap of rows, and its rows are not held to the share of cases that hold
# nothing of their own: Stryker makes no mutant of an operand of && or ||, a number at a boundary or a static table's
# entry, so its rows that prove them look redundant to it.
function Test-RowsSayWhy([string[]]$Lines, [int]$At) {
    # A .NET test starts at its first attribute.
    for ($line = $At; $line -lt [Math]::Min($Lines.Count, $At + 3); $line++) {
        if ($Lines[$line] -match '^\s*\[Theory\b') { return $Lines[$line] -match '//\s*Rows:' }
    }
    # A page's case is where its title is, under the table that opens its loop or .each.
    for ($line = $At; $line -ge 0; $line--) {
        if ($Lines[$line] -match '\.each\(|^\s*for\s*\(') {
            return $Lines[$line] -match '//\s*Rows:' -or ($line -gt 0 -and $Lines[$line - 1] -match '^\s*//\s*Rows:')
        }
    }
    $false
}
