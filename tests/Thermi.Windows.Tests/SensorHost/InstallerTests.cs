using System.Diagnostics;

namespace Thermi.Windows.Tests.SensorHost;

/// <summary>
/// The installer's own copy-and-check, run by PowerShell 7 on folders of the test's own: never its elevated half, which
/// would stop and replace the thermi installed on this PC.
/// </summary>
public sealed class InstallerTests : IDisposable
{
    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "installer-files", Guid.NewGuid().ToString("N"));

    public InstallerTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "build", "runtimes"));
        File.WriteAllText(Path.Combine(_folder, "build", "thermi-sensors.exe"), "built");
        File.WriteAllText(Path.Combine(_folder, "build", "runtimes", "thermi.dll"), "built");
    }

    // The build waits in a folder the user can write while the UAC prompt is up, and the service runs what it holds as
    // SYSTEM: a file changed, added or removed after the build's hash refuses the install, and nothing is left where it
    // would run. A link the hash does not follow, there before the hash, still points where the user writes.
    [Theory]
    [InlineData("untouched", 0, true)]
    [InlineData("changed", 1, false)]
    [InlineData("added", 1, false)]
    [InlineData("removed", 1, false)]
    [InlineData("junction", 1, false)]
    public async Task OnlyTheFilesHashedBeforeThePromptAreInstalled(string tampering, int exitCode, bool installed)
    {
        var build = Path.Combine(_folder, "build");
        var incoming = Path.Combine(_folder, "incoming");
        var elsewhere = Path.Combine(_folder, "elsewhere");
        var (before, after) = tampering switch
        {
            "changed" => ("", $"Set-Content '{build}\\thermi-sensors.exe' 'planted'; "),
            "added" => ("", $"Set-Content '{build}\\runtimes\\version.dll' 'planted'; "),
            "removed" => ("", $"Remove-Item '{build}\\runtimes\\thermi.dll'; "),
            "junction" => ($"New-Item '{elsewhere}' -ItemType Directory | Out-Null; Set-Content '{elsewhere}\\version.dll' 'planted'; New-Item '{build}\\plugins' -ItemType Junction -Target '{elsewhere}' | Out-Null; ", ""),
            _ => ("", ""),
        };

        var exit = await PwshAsync($"{before}$hash = Get-TreeHash '{build}'; {after}Copy-Verified '{build}' $hash '{incoming}' | Out-Null");

        Assert.Equal((exitCode, installed), (exit, Directory.Exists(incoming)));
    }

    public void Dispose()
    {
        // A recursive delete refuses a junction, so the links go first, on their own.
        foreach (var link in Directory.GetDirectories(_folder, "*", SearchOption.AllDirectories).Where(folder => File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint)))
        {
            Directory.Delete(link);
        }

        Directory.Delete(_folder, recursive: true);
    }

    // The script's functions are read from its syntax tree, so running them runs nothing else of it.
    private static async Task<int> PwshAsync(string command)
    {
        var script = Path.Combine(TestRecordings.RepoRoot(), "scripts", "install.ps1");
        var load = $"$ErrorActionPreference = 'Stop'; $ast = [System.Management.Automation.Language.Parser]::ParseFile('{script}', [ref]$null, [ref]$null); "
            + "foreach ($function in $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) { . ([scriptblock]::Create($function.Extent.Text)) }; ";
        var start = new ProcessStartInfo("pwsh") { ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", load + command }, UseShellExecute = false, CreateNoWindow = true };
        using var pwsh = Process.Start(start)!;
        await pwsh.WaitForExitAsync(TestContext.Current.CancellationToken);
        return pwsh.ExitCode;
    }
}
