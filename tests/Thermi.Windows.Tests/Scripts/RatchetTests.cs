using Thermi.Windows.Tests.Processes;

namespace Thermi.Windows.Tests.Scripts;

/// <summary>
/// The test-case ratchet's rule that its baseline is raised in a commit of its own, run by PowerShell 7 on a git
/// repository of the test's own.
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
    // in a commit of its own, made or under way; a merge holding more than either parent raised it itself.
    // Rows: a commit, then each way a merge brings a raise in.
    [Theory]
    [InlineData("raised alone", false)]
    [InlineData("raised with other work", true)]
    [InlineData("merged, raised alone on its side", false)]
    [InlineData("merging, raised alone on its side", false)]
    [InlineData("merged, raised with other work on its side", true)]
    [InlineData("merged, raised by the merge", true)]
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
            _ => SideRaisedAlone + "git merge -q --no-ff --no-commit side; Save 3 ''",
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
    // that many cases, none for the one before, and a file of work by that name, none for none.
    private async Task<int> RatchetAsync(string history)
    {
        var script = Path.Combine(TestRecordings.RepoRoot(), "scripts", "test-count.ps1");
        var load = $"$ErrorActionPreference = 'Stop'; $ast = [System.Management.Automation.Language.Parser]::ParseFile('{script}', [ref]$null, [ref]$null); "
            + "foreach ($function in $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) "
            + "{ . ([scriptblock]::Create($function.Extent.Text)) }; ";
        var repository = $"Set-Location '{_folder}'; git init -q -b main; git config user.name tests; git config user.email tests@thermi; "
            + "git config commit.gpgsign false; New-Item tests -ItemType Directory | Out-Null; "
            + "function Save([int]$count, [string]$work) { if ($count) { Set-Content tests/test-count.json ('{\"dotnet\": ' + $count + '}') }; "
            + "if ($work) { Set-Content \"$work.txt\" $work }; git add -A; git commit -q -m \"$count $work\" }; "
            + "Save 1 'base'; ";
        var check = "; $faults = @(Get-RaiseFaults (Get-Location).Path (Get-Content tests/test-count.json -Raw | ConvertFrom-Json) @('dotnet')); "
            + "$faults | Write-Host; exit ($faults ? 3 : 0)";
        var (code, said) = await DesktopProcess.RunAsync(
            "pwsh", ["-NoProfile", "-NonInteractive", "-Command", load + repository + history + check], TimeSpan.FromMinutes(2));
        TestContext.Current.TestOutputHelper?.WriteLine(said);
        return code;
    }
}
