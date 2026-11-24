using Thermi.Windows.Tests.Processes;

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
    // would run. A link the hash does not follow, there before the hash, still points where the user writes. A hidden
    // file is hashed, so it is copied too.
    [Theory]
    [InlineData("untouched", 0, true)]
    [InlineData("hidden", 0, true)]
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
            "hidden" => ($"(New-Item '{build}\\.hidden' -Value 'built').Attributes = 'Hidden'; ", ""),
            "changed" => ("", $"Set-Content '{build}\\thermi-sensors.exe' 'planted'; "),
            "added" => ("", $"Set-Content '{build}\\runtimes\\version.dll' 'planted'; "),
            "removed" => ("", $"Remove-Item '{build}\\runtimes\\thermi.dll'; "),
            "junction" => (
                $"New-Item '{elsewhere}' -ItemType Directory | Out-Null; Set-Content '{elsewhere}\\version.dll' 'planted'; "
                    + $"New-Item '{build}\\plugins' -ItemType Junction -Target '{elsewhere}' | Out-Null; ",
                ""),
            _ => ("", ""),
        };

        var exit = await PwshAsync($"{before}$hash = Get-TreeHash '{build}'; {after}Copy-Verified '{build}' $hash '{incoming}' | Out-Null");

        Assert.Equal((exitCode, installed), (exit, Directory.Exists(incoming)));
    }

    // The old thermi is set aside, not deleted, until the new one's service is registered: an install that fails there,
    // or an old folder held open that cannot be set aside, puts the old files back and registers them again, and leaves
    // nothing beside them. The service manager is faked, refusing the new build's files, so no service on this PC is
    // touched; a service registered last is one it took.
    [Theory]
    [InlineData(true, false, 0, "built")]
    [InlineData(false, false, 1, "old")]
    [InlineData(true, true, 1, "old")]
    public async Task AnInstallThatFailsPutsTheOldThermiBack(bool registers, bool held, int exitCode, string left)
    {
        var build = Path.Combine(_folder, "build");
        var target = Path.Combine(_folder, "thermi");
        var registered = Path.Combine(_folder, "registered");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "thermi-sensors.exe"), "old", TestContext.Current.CancellationToken);
        var refuse = registers ? "" : "if ((Get-Content \"$target\\thermi-sensors.exe\") -eq 'built') { throw 'Windows refused the service.' }";

        int exit;
        using (held ? new FileStream(Path.Combine(target, "thermi-sensors.exe"), FileMode.Open, FileAccess.Read, FileShare.Read) : null)
        {
            exit = await PwshAsync(
                $"$target = '{target}'; $service = 'thermi-tests-none'; function Remove-Service {{ }}; "
                + $"function Register-Service {{ {refuse}; Set-Content '{registered}' 'yes' }}; "
                + $"Install-Elevated '{build}' (Get-TreeHash '{build}') $true");
        }

        var beside = new[] { ".old", ".new" }.Any(end => Directory.Exists(target + end));
        var installed = File.Exists(Path.Combine(target, "thermi-sensors.exe"))
            ? await File.ReadAllTextAsync(Path.Combine(target, "thermi-sensors.exe"), TestContext.Current.CancellationToken)
            : "nothing";
        Assert.Equal((exitCode, left, false, true), (exit, installed.Trim(), beside, File.Exists(registered)));
    }

    // A service Windows registered without its restarts or with all of LocalSystem's privileges would run, but not as
    // docs/adr/0003-sensor-service.md says: each change the service manager refuses stops the registration there. The
    // service manager and New-Service are faked, so no service on this PC is touched.
    [Theory]
    [InlineData("failure")]
    [InlineData("failureflag")]
    [InlineData("privs")]
    public async Task AServiceManagerRefusalStopsTheRegistration(string refused)
    {
        var calls = Path.Combine(_folder, "calls.txt");

        var exit = await PwshAsync(
            $"$target = '{_folder}'; $service = 'thermi-tests-none'; $servicePrivileges = 'SeChangeNotifyPrivilege'; "
            + "function New-Service { }; function New-ItemProperty { }; function Start-Service { }; "
            + $"function sc.exe {{ Add-Content '{calls}' $args[0]; $global:LASTEXITCODE = $args[0] -eq '{refused}' ? 5 : 0 }}; "
            + "Register-Service");

        var called = await File.ReadAllLinesAsync(calls, TestContext.Current.CancellationToken);
        Assert.Equal((1, refused), (exit, called[^1]));
    }

    // The elevated half waits in a file the user can write while the UAC prompt is up: the administrator runs it only as
    // it was when its hash was taken, before the prompt.
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 4)]
    public async Task TheAdministratorStepRunsOnlyTheScriptHashedBeforeThePrompt(bool changed, int exitCode)
    {
        var script = Path.Combine(_folder, "elevated.ps1");
        var ran = Path.Combine(_folder, "ran.txt");
        var tamper = changed ? $"Add-Content '{script}' 'exit 0'; " : "";

        var exit = await PwshAsync(
            $"function Install-Elevated {{ Set-Content '{ran}' 'yes' }}; $check = Get-ElevatedCommand '' '' $true '{script}'; {tamper}"
            + "pwsh -NoProfile -NonInteractive -EncodedCommand $check; exit $LASTEXITCODE");

        Assert.Equal((exitCode, !changed), (exit, File.Exists(ran)));
    }

    // The elevated half runs as administrator from the PowerShell that started the installer, so only one where only
    // administrators write, and signed as it shipped, is trusted with it.
    [Theory]
    [InlineData("installed", 0)]
    [InlineData("copied", 1)]
    public async Task OnlyAPowerShellUnderProgramFilesRunsTheAdministratorStep(string where, int exitCode)
    {
        var copy = Path.Combine(_folder, "pwsh.exe");
        var path = where == "installed" ? "(Join-Path $PSHOME 'pwsh.exe')" : $"'{copy}'";

        var exit = await PwshAsync($"Copy-Item (Join-Path $PSHOME 'pwsh.exe') '{copy}'; Get-TrustedPwsh {path} | Out-Null");

        Assert.Equal(exitCode, exit);
    }

    // The elevated pwsh takes its environment from the user's registry, and .NET loads a profiler, a hook or more
    // assemblies named there before PowerShell runs a line: with one named, nothing is asked. A variable that loads
    // nothing is no reason to refuse.
    [Theory]
    [InlineData("CORECLR_ENABLE_PROFILING", 1)]
    [InlineData("COR_PROFILER", 1)]
    [InlineData("DOTNET_STARTUP_HOOKS", 1)]
    [InlineData("DOTNET_CLI_TELEMETRY_OPTOUT", 0)]
    public async Task NoAdministratorStepIsAskedForWhileTheEnvironmentLoadsCode(string name, int exitCode)
    {
        Assert.Equal(exitCode, await PwshAsync($"Assert-NoCodeLoadingVariable @('Path', '{name}')"));
    }

    public void Dispose()
    {
        // A recursive delete refuses a junction, so the links go first, on their own.
        var links = Directory.GetDirectories(_folder, "*", SearchOption.AllDirectories)
            .Where(folder => File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint));
        foreach (var link in links)
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
            + "foreach ($function in $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) "
            + "{ . ([scriptblock]::Create($function.Extent.Text)) }; ";
        var (code, _) = await DesktopProcess.RunAsync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", load + command], TimeSpan.FromMinutes(2));
        return code;
    }
}
