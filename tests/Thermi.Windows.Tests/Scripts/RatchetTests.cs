using Thermi.Windows.Tests.Processes;

namespace Thermi.Windows.Tests.Scripts;

/// <summary>
/// The test-case ratchet's rule that its baseline is raised in a commit of its own, and its -Raise that makes that commit,
/// run by PowerShell 7 on a git repository of the test's own.
/// </summary>
public sealed class RatchetTests : IDisposable
{
    // The side branch: the baseline raised alone, then work.
    private const string SideRaisedAlone = "git switch -q -c side; Save 2 ''; Save 0 'code'; git switch -q main; Save 0 'docs'; ";

    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "ratchet-repos", Guid.NewGuid().ToString("N"));

    public RatchetTests()
    {
        Directory.CreateDirectory(_folder);
    }

    // A merge brings in its other side's work along with the raise made there, so the raise passes when it landed there
    // in a commit of its own, made or under way; a merge holding more than either parent raised it itself, on either side.
    // GitHub's squash of a pull request holds all its commits, which its own run checked. -Raise commits the baseline alone
    // and leaves the work in the tree; work already staged would look committed, so it refuses that.
    // Rows: a commit, then each way a merge brings a raise in, then the squash, then -Raise and its refusal.
    [Theory]
    [InlineData("raised alone", false)]
    [InlineData("raised with other work", true)]
    [InlineData("merged, raised alone on its side", false)]
    [InlineData("merging, raised alone on its side", false)]
    [InlineData("merged, raised with other work on its side", true)]
    [InlineData("merged, raised by the merge", true)]
    [InlineData("merged, raised by a merge on its side", true)]
    [InlineData("squashed by GitHub", false)]
    [InlineData("raised by -Raise, the work unstaged", false)]
    [InlineData("raised by -Raise, the work staged", true)]
    public async Task TheBaselineIsRaisedOnlyInACommitOfItsOwn(string history, bool refused)
    {
        var made = history switch
        {
            "raised alone" => "Save 2 ''",
            "raised with other work" => "Save 2 'code'",
            "merged, raised alone on its side" => SideRaisedAlone + "git merge -q --no-ff side -m merge",
            "merging, raised alone on its side" => SideRaisedAlone + "git merge -q --no-ff --no-commit side",
            "merged, raised with other work on its side" =>
                "git switch -q -c side; Save 2 'code'; git switch -q main; Save 0 'docs'; git merge -q --no-ff side -m merge",
            "merged, raised by the merge" => SideRaisedAlone + "git merge -q --no-ff --no-commit side; Save 3 ''",
            "merged, raised by a merge on its side" => "git switch -q -c side; Save 2 ''; git switch -q -c other; Save 0 'more'; git switch -q side; "
                + "git merge -q --no-ff --no-commit other; Save 3 ''; git switch -q main; Save 0 'docs'; git merge -q --no-ff side -m merge",
            "raised by -Raise, the work unstaged" => "Set-Content base.txt more; $refused = Save-Raise (Get-Location).Path @{ dotnet = 2 }; git stash -q",
            "raised by -Raise, the work staged" => "Set-Content base.txt more; git add base.txt; $refused = Save-Raise (Get-Location).Path @{ dotnet = 2 }",
            _ => "$env:GIT_COMMITTER_NAME = 'GitHub'; $env:GIT_COMMITTER_EMAIL = 'noreply@github.com'; Save 2 'code' 'test: the work (#12)'",
        };

        // 3 is the refusal; anything else but 0, the script failing.
        Assert.Equal(refused ? 3 : 0, await RatchetAsync(made));
    }

    public void Dispose()
    {
        // git leaves its objects read-only, which a recursive delete refuses.
        foreach (var file in Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_folder, recursive: true);
    }

    // The script's functions are read from its syntax tree, so running them lists no tests. Save commits a baseline of
    // that many cases, none for the one before, and a file of work by that name, none for none, with the message given.
    // What -Raise refused counts as the rule's refusal, with the faults the rule then finds in the history.
    private async Task<int> RatchetAsync(string history)
    {
        var script = Path.Combine(TestRecordings.RepoRoot(), "scripts", "test-count.ps1");
        var load = $"$ErrorActionPreference = 'Stop'; $ast = [System.Management.Automation.Language.Parser]::ParseFile('{script}', [ref]$null, [ref]$null); "
            + "foreach ($function in $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) "
            + "{ . ([scriptblock]::Create($function.Extent.Text)) }; ";
        var repository = $"Set-Location '{_folder}'; git init -q -b main; git config user.name tests; git config user.email tests@thermi; "
            + "git config commit.gpgsign false; New-Item tests -ItemType Directory | Out-Null; "
            + "function Save([int]$count, [string]$work, [string]$message = \"$count $work\") { "
            + "if ($count) { Set-Content tests/test-count.json ('{\"dotnet\": ' + $count + '}') }; "
            + "if ($work) { Set-Content \"$work.txt\" $work }; git add -A; git commit -q -m $message }; "
            + "Save 1 'base'; ";
        var check = "; $faults = @(Get-RaiseFaults (Get-Location).Path (Get-Content tests/test-count.json -Raw | ConvertFrom-Json) @('dotnet')) "
            + "+ @($refused | Where-Object { $_ }); "
            + "$faults | Write-Host; exit ($faults ? 3 : 0)";
        var (code, said) = await DesktopProcess.RunAsync(
            "pwsh", ["-NoProfile", "-NonInteractive", "-Command", load + repository + history + check], TimeSpan.FromMinutes(2));
        TestContext.Current.TestOutputHelper?.WriteLine(said);
        return code;
    }
}
