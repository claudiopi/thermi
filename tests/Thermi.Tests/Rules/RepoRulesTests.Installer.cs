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
    }
}
