# Dot-sourced by scripts/check.ps1. Here, a check runs only what the change can affect: each half takes the files that
# differ from the last tree this worktree checked green in that half, or else from where the branch left its base. What
# a file can affect comes from the projects' references and from what Settings.Types writes; a file this does not know
# runs everything. The CI never asks it: on GitHub every step runs, so nothing merges red.

# What shapes every build or every step: a change to one runs the whole check.
$wholeCheck = @(
    'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'nuget.config', 'thermi.slnx',
    'Thermi.globalconfig', 'SonarLint.xml', 'BannedSymbols.txt', '.editorconfig', '.gitattributes', 'src/*.targets', 'src/*.props',
    'scripts/check.ps1', 'scripts/check-*.ps1', 'scripts/test-count.ps1'
)
# Read by the Windows tests as files, beside their projects' own.
$windowsReads = @('scripts/install.ps1')
# What only the repo rules of Thermi.Tests read: docs, agent briefs, workflows, tools and the scripts no step runs.
$rulesOnly = @('*.md', 'docs/*', '.claude/*', '.github/*', '.config/*', 'scripts/*', 'LICENSE', '.gitignore', 'renovate.json')
$notices = @('THIRD-PARTY-NOTICES.md', 'scripts/notices.ps1', 'scripts/licenses/*', '*packages.lock.json', 'src/Thermi.Settings.Web/package*.json')
$page = 'src/Thermi.Settings.Web/'
# What the build of Settings.Types writes into the repo: the page's bridge, and the contract's schemas.
$generated = @("$($page)src/bridge/*", 'docs/agents/schema/*')
# The tests of the settings window, the only ones that load the page, which thermi.exe embeds.
$pageTests = 'Thermi.Windows.Tests.Window'

# The working tree as a git tree, untracked files too, written through an index of its own so git's is never touched.
function Get-WorkTree {
    $index = git rev-parse --path-format=absolute --git-path thermi-check.index
    Copy-Item (git rev-parse --path-format=absolute --git-path index) $index
    $env:GIT_INDEX_FILE = $index
    try {
        git add --all 2>$null
        git write-tree
    }
    finally {
        Remove-Item Env:GIT_INDEX_FILE
        Remove-Item $index -ErrorAction SilentlyContinue
    }
}

function Get-GreenFile([string]$Half) { git rev-parse --path-format=absolute --git-path "thermi-check-green-$Half" }

# A half that passed records the tree it checked, in this worktree's own git folder.
function Save-Green([string]$Half, [string]$Tree) { Set-Content (Get-GreenFile $Half) $Tree -NoNewline }

# The files that differ from what the half last checked green, or from where HEAD left $Base; $null when git cannot tell.
function Get-ChangedFiles([string]$Half, [string]$Tree, [string]$Base) {
    $green = Get-GreenFile $Half
    $from = if (Test-Path $green) { (Get-Content $green -Raw).Trim() }
    if ($from) { git cat-file -e "$from^{tree}" 2>$null }
    if (-not $from -or $LASTEXITCODE -ne 0) {
        $from = git merge-base HEAD $Base 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $from) { return $null }
    }
    , @(git diff --name-only --no-renames $from $Tree)
}

# Every project, the projects it references, through the files it imports too, and the files outside its folder it
# compiles or copies.
function Get-Projects([string]$Root) {
    $projects = @{}
    foreach ($file in git -C $Root ls-files '*.csproj') {
        $folder = Split-Path (Join-Path $Root $file) -Parent
        $text = Get-ProjectText (Join-Path $Root $file)
        $properties = @{}
        foreach ($m in [regex]::Matches($text, '<(\w+)(?: [^>]*)?>([^<]*)</\1>')) { $properties[$m.Groups[1].Value] = $m.Groups[2].Value }
        $references = foreach ($m in [regex]::Matches($text, '<ProjectReference Include="([^"]+)"')) {
            $path = [regex]::Replace($m.Groups[1].Value, '\$\((\w+)\)', { param($p) $properties[$p.Groups[1].Value] })
            [IO.Path]::GetFileNameWithoutExtension($path)
        }
        $links = foreach ($m in [regex]::Matches($text, '<(?:Compile|None|Content|EmbeddedResource|AdditionalFiles) Include="(\.\.[^"$]+)"')) {
            foreach ($each in $m.Groups[1].Value -split ';') {
                [IO.Path]::GetRelativePath($Root, [IO.Path]::GetFullPath((Join-Path $folder $each))).Replace('\', '/').Replace('**', '*')
            }
        }
        $projects[[IO.Path]::GetFileNameWithoutExtension($file)] = @{
            Folder = [IO.Path]::GetRelativePath($Root, $folder).Replace('\', '/') + '/'
            References = @($references)
            Links = @($links)
        }
    }
    $projects
}

# A project's text with that of the files it imports by a relative path.
function Get-ProjectText([string]$Path) {
    $text = Get-Content $Path -Raw
    $imports = foreach ($m in [regex]::Matches($text, '<Import Project="([^"$]+)"')) {
        $imported = Join-Path (Split-Path $Path -Parent) $m.Groups[1].Value
        if (Test-Path $imported) { Get-ProjectText $imported }
    }
    (@($text) + $imports) -join "`n"
}

function Test-Like([string]$Path, [string[]]$Patterns) { @($Patterns | Where-Object { $Path -like $_ }).Count -gt 0 }

# What the changed files can affect; everything when git cannot tell, or a file says so.
function Get-Scope([string]$Root, [string[]]$Changed) {
    $scope = [pscustomobject]@{
        All = $true; Files = @($Changed).Count; Restore = $true; Notices = $true; Pack = $true; TestCount = $true
        CoreTests = $true; WindowsTests = $true; WindowsFilter = $null; Web = $true
    }
    if ($null -eq $Changed) { return $scope }
    $scope.All = $false
    $scope.Restore = $scope.Notices = $scope.Pack = $scope.TestCount = $scope.CoreTests = $scope.WindowsTests = $scope.Web = $false
    if ($scope.Files -eq 0) { return $scope }

    $projects = Get-Projects $Root
    $touched = [Collections.Generic.HashSet[string]]::new()
    $pageChanged = $false
    foreach ($file in $Changed) {
        if (Test-Like $file $wholeCheck) { return Get-Scope $Root $null }
        if (Test-Like $file $notices) { $scope.Notices = $true }
        if ($file -like '*.md') { continue }
        if ($file -like '*.csproj' -or $file -like '*packages.lock.json') { $scope.Restore = $true }
        if ($file -like 'tests/*') { $scope.TestCount = $true }
        $owners = @($projects.Keys | Where-Object {
                $file.StartsWith($projects[$_].Folder, [StringComparison]::OrdinalIgnoreCase) -or (Test-Like $file $projects[$_].Links)
            })
        if ($owners) { foreach ($owner in $owners) { [void]$touched.Add($owner) } }
        elseif ($file.StartsWith($page, [StringComparison]::OrdinalIgnoreCase)) { $scope.Web = $pageChanged = $true }
        elseif (Test-Like $file $windowsReads) { [void]$touched.Add('Thermi.Windows.Tests') }
        elseif ($file -like 'src/Thermi.Installer/*') { $scope.Restore = $scope.Notices = $true }
        elseif ($file -eq 'tests/test-count.json') { $scope.Web = $true }
        elseif (-not (Test-Like $file ($rulesOnly + $notices))) { return Get-Scope $Root $null }
    }

    # What each touched project reaches through the projects that reference it.
    $affected = [Collections.Generic.HashSet[string]]::new($touched)
    do {
        $grew = $false
        foreach ($name in @($projects.Keys)) {
            $reaches = @($projects[$name].References | Where-Object { $affected.Contains($_) }).Count -gt 0
            if ($reaches -and $affected.Add($name)) { $grew = $true }
        }
    } while ($grew)

    # The repo rules read every file, so Thermi.Tests runs for any change.
    $scope.CoreTests = $true
    $scope.WindowsTests = $affected.Contains('Thermi.Windows.Tests') -or $pageChanged
    if (-not $affected.Contains('Thermi.Windows.Tests') -and $pageChanged) { $scope.WindowsFilter = $pageTests }
    $scope.Pack = $affected.Contains('Thermi.Client')
    $scope
}

# What the half's record holds once it passes: the tree it began from, or the one after the build when the build changed
# only what Settings.Types writes, which the tests then ran with.
function Get-CheckedTree([string]$Before, [string]$After) {
    if ($Before -eq $After) { return $Before }
    $built = @(git diff --name-only --no-renames $Before $After)
    if ($built | Where-Object { -not (Test-Like $_ $generated) }) { $Before } else { $After }
}

function Format-Scope($Scope, [string]$Half) {
    $name = if ($Half -eq 'web') { "the page's half" } else { 'the .NET half' }
    if ($Scope.All) { return "$($name): everything" }
    if ($Scope.Files -eq 0) { return "$($name): nothing changed since its last green check" }
    if ($Half -eq 'web') { return "$($name): $(Format-Files $Scope.Files), $(if ($Scope.Web) { 'the page among them' } else { 'none the page reads' })" }
    $parts = @('Thermi.Tests')
    if ($Scope.WindowsTests) { $parts += if ($Scope.WindowsFilter) { "the Windows tests of $($Scope.WindowsFilter)" } else { 'the Windows tests' } }
    if ($Scope.Notices) { $parts += 'the notices' }
    "$($name): $(Format-Files $Scope.Files), for $($parts -join ', ')"
}

function Format-Files([int]$Count) { if ($Count -eq 1) { '1 file changed' } else { "$Count files changed" } }
