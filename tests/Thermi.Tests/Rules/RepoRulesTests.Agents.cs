using System.Text.RegularExpressions;

namespace Thermi.Tests.Rules;

public sealed partial class RepoRulesTests
{
    // An agent runs as the user and asks a person to fix what needs an administrator (docs/adr/0013-agents-cli-and-mcp.md):
    // no code of its commands or of the MCP server reaches the elevated mode, a UAC prompt, the service manager or the
    // driver's install, and the pipe it reads never starts a host; on Linux, what thermi-cli runs there, its environment and
    // what that reads, never reaches pkexec, sudo, systemd-run or a unit's start. The service's name, which status looks up,
    // is all it may take of them.
    [Fact]
    public void AgentsNeverElevate()
    {
        string[] agents =
        [
            Path.Combine("src", "Thermi.Windows", "Agents"),
            Path.Combine("src", "Thermi.Core", "Agents"),
            Path.Combine("src", "Thermi.Cli") + Path.DirectorySeparatorChar,
            Path.Combine("src", "Thermi.Linux", "Agents"),
            Path.Combine("src", "Thermi.Linux", "Commands"),
            Path.Combine("src", "Thermi.Linux", "Sensors"),
            Path.Combine("src", "Thermi.Linux", "Desktop"),
            Path.Combine("src", "Thermi.Linux", "Gnome"),
            Path.Combine("src", "Thermi.Linux", "Program.cs"),
        ];
        var reached = RepoFiles([".cs"])
            .Where(path => agents.Any(part => Relative(path).StartsWith(part, StringComparison.Ordinal)))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (line, index))
                .Where(each => Elevating().IsMatch(each.line))
                .Select(each => $"{Relative(path)}:{each.index + 1} reaches {Elevating().Match(each.line).Value}"))
            .ToList();

        Assert.Contains(RepoFiles([".cs"]), path => Relative(path).StartsWith(agents[0], StringComparison.Ordinal));
        var environment = File.ReadAllText(Path.Combine(Root, agents[0], "AgentEnvironment.cs"));
        Assert.Contains("new SensorPipes(time, logs.CreateLogger<SensorPipes>(), starts: false)", environment, StringComparison.Ordinal);
        AssertNone(reached, "an agent asks the person to fix what needs an administrator, as docs/adr/0013-agents-cli-and-mcp.md says");
    }

    // The agents' README is how an agent learns the CLI: its Commands table names each command thermi-cli parses and no
    // other, but mcp, which has a section of its own.
    [Fact]
    public void TheAgentsReadmeNamesEveryCommand()
    {
        var parsed = CliCommand().Matches(File.ReadAllText(Path.Combine(Root, "src", "Thermi.Cli", "Commands", "AgentCommandLine.cs")))
            .Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        var readme = File.ReadAllLines(Path.Combine(Root, "docs", "agents", "README.md"));
        var table = readme.SkipWhile(line => line != "## Commands")
            .SkipWhile(line => !line.StartsWith('|'))
            .TakeWhile(line => line.StartsWith('|'))
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Replace("\\|", "/", StringComparison.Ordinal).Split('|')[1])
            .SelectMany(cell => CodeSpan().Matches(cell).Select(span => span.Groups["span"].Value.Split(' ')[0]))
            .Append("mcp")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(readme, line => line.Contains("thermi-cli mcp", StringComparison.Ordinal));
        AssertNone(parsed.Except(table).Select(command => $"docs/agents/README.md has no row for thermi-cli {command}")
            .Concat(table.Except(parsed).Select(command => $"docs/agents/README.md names thermi-cli {command}, which it does not parse")));
    }

    [GeneratedRegex(@"new Command\(""(?<name>[\w-]+)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CliCommand();

    [GeneratedRegex(
        @"\bElevated\w*|runas|ServiceController|IServiceManager|WindowsServices|DriverParts|InstallDriver|StartSensors\.(?!Service\b)\w+|\bShellExecute"
            + @"|starts:\s*true|pkexec|\bsudo\b|systemd-run|SystemdRun|LinuxFixes|""(?:re)?start""",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex Elevating();
}
