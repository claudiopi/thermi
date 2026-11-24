using System.Text.RegularExpressions;

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

        Assert.NotEmpty(sensors);
        Assert.NotEmpty(elevated);
        Assert.Contains($"$service = '{sensors}'", Read("scripts", "install.ps1"), StringComparison.Ordinal);
        Assert.Contains($"public const string Service = \"{elevated}\";", Read("src", "Thermi", "Commands", "Elevated.cs"), StringComparison.Ordinal);
        Assert.Contains(
            $"public const string Elevated = \"{elevated}\";", Read("src", "Thermi.SensorHost", "Updates", "ServiceConfig.cs"), StringComparison.Ordinal);
        Assert.Contains("public const string Privileges = \"SeChangeNotifyPrivilege\";", installation, StringComparison.Ordinal);
        Assert.Contains("public const string YoungGeneration = \"DOTNET_GCgen0size=200000\";", installation, StringComparison.Ordinal);
        Assert.Contains("system.Environment(Sensors, [YoungGeneration]);", installation, StringComparison.Ordinal);
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([Root, .. path]));

    private static string Name(string code) =>
        Regex.Match(code, "public const string Name = \"(?<name>[^\"]+)\";", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups["name"].Value;
}
