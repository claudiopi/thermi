using System.Text.RegularExpressions;

namespace Thermi.Tests.Rules;

public sealed partial class RepoRulesTests
{
    // The service writes its failures to the Application log under its own name, a source the installer registers as it
    // registers the service, and removes with it. The name is read from the service's own code, which this project cannot
    // reference. The service's small young generation is read only from its environment, which the installer writes in
    // the service's own key, so deleting the service removes it too. Of LocalSystem's privileges it keeps one alone, as
    // docs/adr/0003-sensor-service.md measured.
    [Fact]
    public void TheInstallerRegistersAndRemovesTheServicesEventSourceAndEnvironment()
    {
        var program = File.ReadAllText(Path.Combine(Root, "src", "Thermi.SensorHost", "Program.cs"));
        var script = File.ReadAllText(Path.Combine(Root, "scripts", "install.ps1"));
        var name = Regex.Match(program, "public const string Name = \"(?<name>[^\"]+)\";", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups["name"].Value;

        Assert.NotEmpty(name);
        Assert.Contains($"$service = '{name}'", script, StringComparison.Ordinal);
        Assert.Contains("[System.Diagnostics.EventLog]::CreateEventSource($service, 'Application')", script, StringComparison.Ordinal);
        Assert.Contains("[System.Diagnostics.EventLog]::DeleteEventSource($service)", script, StringComparison.Ordinal);
        Assert.Contains("$serviceKey = \"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\$service\"", script, StringComparison.Ordinal);
        Assert.Contains(
            "New-ItemProperty $serviceKey -Name Environment -PropertyType MultiString -Value @('DOTNET_GCgen0size=200000')",
            script,
            StringComparison.Ordinal);
        Assert.Contains("sc.exe delete $service", script, StringComparison.Ordinal);
        Assert.Contains("$servicePrivileges = 'SeChangeNotifyPrivilege'", script, StringComparison.Ordinal);
        Assert.Contains("sc.exe privs $service $servicePrivileges", script, StringComparison.Ordinal);

        // The fixes' service, by the name both its own code and the window's know it by, is registered and removed too.
        var fixes = File.ReadAllText(Path.Combine(Root, "src", "Thermi.SensorHost", "ElevatedService.cs"));
        var asks = File.ReadAllText(Path.Combine(Root, "src", "Thermi", "Commands", "Elevated.cs"));
        var elevated = Regex.Match(fixes, "public const string Name = \"(?<name>[^\"]+)\";", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups["name"].Value;
        Assert.NotEmpty(elevated);
        Assert.Contains($"public const string Service = \"{elevated}\";", asks, StringComparison.Ordinal);
        Assert.Contains($"$elevatedService = '{elevated}'", script, StringComparison.Ordinal);
        Assert.Contains("New-Service $elevatedService -BinaryPathName \"`\"$sensors`\" --elevated\"", script, StringComparison.Ordinal);
        Assert.Contains("sc.exe delete $elevatedService", script, StringComparison.Ordinal);

        // thermi-cli reads what was installed from the value the installer writes, and runs from any shell through the
        // machine's PATH, which an install sets and an uninstall takes back, with the value.
        var agents = File.ReadAllText(Path.Combine(Root, "src", "Thermi", "Agents", "AgentEnvironment.cs"));
        Assert.Contains("private const string InstallKey = @\"SOFTWARE\\thermi\";", agents, StringComparison.Ordinal);
        Assert.Contains("public const string SensorsOnlyValue = \"SensorsOnly\";", agents, StringComparison.Ordinal);
        Assert.Contains("$installKey = 'HKLM:\\SOFTWARE\\thermi'", script, StringComparison.Ordinal);
        Assert.Contains("Set-ItemProperty $installKey -Name SensorsOnly -Value ([int]$SensorsOnly) -Type DWord", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item $installKey -Recurse", script, StringComparison.Ordinal);
        Assert.Contains("Set-MachinePath ([bool]$From)", script, StringComparison.Ordinal);
    }
}
