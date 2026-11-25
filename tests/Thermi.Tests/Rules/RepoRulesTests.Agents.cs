using System.Text.RegularExpressions;

namespace Thermi.Tests.Rules;

public sealed partial class RepoRulesTests
{
    // An agent runs as the user and asks a person to fix what needs an administrator (docs/adr/0013-agents-cli-and-mcp.md):
    // no code of its commands or of the MCP server reaches the elevated mode, a UAC prompt, the service manager or the
    // driver's install. The service's name, which status looks up, is all it may take of them.
    [Fact]
    public void AgentsNeverElevate()
    {
        string[] agents =
        [
            Path.Combine("src", "Thermi", "Agents"),
            Path.Combine("src", "Thermi.Core", "Agents"),
            Path.Combine("src", "Thermi", "Commands", "AgentCommandLine.cs"),
        ];
        var reached = RepoFiles([".cs"])
            .Where(path => agents.Any(part => Relative(path).StartsWith(part, StringComparison.Ordinal)))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (line, index))
                .Where(each => Elevating().IsMatch(each.line))
                .Select(each => $"{Relative(path)}:{each.index + 1} reaches {Elevating().Match(each.line).Value}"))
            .ToList();

        Assert.Contains(RepoFiles([".cs"]), path => Relative(path).StartsWith(agents[0], StringComparison.Ordinal));
        AssertNone(reached);
    }

    [GeneratedRegex(
        @"\bElevated\w*|runas|ServiceController|IServiceManager|WindowsServices|DriverParts|InstallDriver|StartSensors\.(?!Service\b)\w+|ShellExecute",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex Elevating();
}
