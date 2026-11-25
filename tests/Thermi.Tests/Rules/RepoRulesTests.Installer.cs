using System.Text.RegularExpressions;
using Thermi.Core.Install;

namespace Thermi.Tests.Rules;

public sealed partial class RepoRulesTests
{
    // The services are registered by the service's own code and asked for by the window's and the install script, each by
    // the name the service's own code gives it, which this project cannot reference. What the registration sets is what
    // docs/adr/0003-sensor-service.md measured: one privilege of LocalSystem's, and a small young generation read only
    // from the service's environment, in its own key, so deleting the service removes it too.
    [Fact]
    public void EveryoneNamesTheServicesAsTheirOwnCodeDoes()
    {
        var sensors = Name(Read("src", "Thermi.SensorHost", "Program.cs"));
        var elevated = Name(Read("src", "Thermi.SensorHost", "ElevatedService.cs"));
        var installation = Read("src", "Thermi.SensorHost", "Install", "Installation.cs");
        var script = Read("scripts", "install.ps1");

        Assert.NotEmpty(sensors);
        Assert.NotEmpty(elevated);
        Assert.Contains($"$service = '{sensors}'", script, StringComparison.Ordinal);
        Assert.Contains($"$elevatedService = '{elevated}'", script, StringComparison.Ordinal);
        Assert.Contains($"public const string Service = \"{elevated}\";", Read("src", "Thermi", "Commands", "Elevated.cs"), StringComparison.Ordinal);
        Assert.Contains(
            $"public const string Elevated = \"{elevated}\";", Read("src", "Thermi.SensorHost", "Updates", "ServiceConfig.cs"), StringComparison.Ordinal);
        Assert.Contains("public const string Privileges = \"SeChangeNotifyPrivilege\";", installation, StringComparison.Ordinal);
        Assert.Contains("public const string YoungGeneration = \"DOTNET_GCgen0size=200000\";", installation, StringComparison.Ordinal);
        Assert.Contains("system.Environment(Sensors, [YoungGeneration]);", installation, StringComparison.Ordinal);

        // thermi-cli reads what was installed from the value the installer writes, and runs from any shell through the
        // machine's PATH, which an install sets and an uninstall takes back, with the value.
        var agents = Read("src", "Thermi", "Agents", "AgentEnvironment.cs");
        Assert.Contains("private const string InstallKey = @\"SOFTWARE\\thermi\";", agents, StringComparison.Ordinal);
        Assert.Contains("public const string SensorsOnlyValue = \"SensorsOnly\";", agents, StringComparison.Ordinal);
        Assert.Contains("$installKey = 'HKLM:\\SOFTWARE\\thermi'", script, StringComparison.Ordinal);
        Assert.Contains("Set-ItemProperty $installKey -Name SensorsOnly -Value ([int]$SensorsOnly) -Type DWord", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item $installKey -Recurse", script, StringComparison.Ordinal);
        Assert.Contains("Set-MachinePath ([bool]$From)", script, StringComparison.Ordinal);
    }

    // thermi's programs, thermi-cli among them, are listed once, by the layout: what an install ends, an update requires
    // and a publish checks, so none is left running from a folder being replaced or missing from a version.
    [Fact]
    public void EveryoneNamesThermisProgramsAsTheLayoutDoes()
    {
        var programs = string.Join(", ", InstallLayout.Programs.Select(program => $"'{program}'"));
        string[] listed = [.. Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("GetProcessesByName(\"thermi", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(Root, file))];

        Assert.Equal(["thermi", "thermi-cli", "thermi-sensors"], InstallLayout.Programs);
        Assert.Contains($"$programs = {programs}", Read("scripts", "install.ps1"), StringComparison.Ordinal);
        Assert.Contains("\"`$programs = ", Read("scripts", "install.ps1"), StringComparison.Ordinal);
        Assert.Contains($"foreach ($program in {programs})", Read("scripts", "publish.ps1"), StringComparison.Ordinal);
        Assert.Empty(listed);
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([Root, .. path]));

    private static string Name(string code) =>
        Regex.Match(code, "public const string Name = \"(?<name>[^\"]+)\";", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups["name"].Value;
}
