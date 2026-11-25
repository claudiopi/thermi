using System.Text.RegularExpressions;

namespace Thermi.Tests.Rules;

public sealed partial class RepoRulesTests
{
    // The only code that writes to Windows' Event Log, each the sink its program's Main hands down.
    private static readonly string[] EventLogSinks =
    [
        Path.Combine("src", "Thermi.Sensors.Windows", "SensorHostEnvironment.cs"),
        Path.Combine("src", "Thermi.Windows", "Logging", "EventLogger.cs"),
    ];

    // What reaches those sinks from a test: the sinks themselves, and the environment that holds the service's.
    private static readonly string[] EventLogSinkNames = ["ApplicationLog", "EventLogger", "SensorHostEnvironment.Default"];

    // A test's write lands in the user's Application log, where a test's failure reads as one of their PC's. So only the
    // sinks write there, every other class is handed one, and no test names a sink: a test hands its own.
    [Fact]
    public void NoTestReachesTheEventLog()
    {
        var code = RepoFiles([".cs"])
            .Where(path => !EventLogSinks.Contains(Relative(path)) && Path.GetFileName(path) != "RepoRulesTests.EventLog.cs")
            .Select(path => (Path: Relative(path), Text: File.ReadAllText(path)))
            .ToList();
        var reaching = code
            .Where(file => EventLogType().IsMatch(file.Text))
            .Select(file => $"{file.Path} reaches the Event Log: take a sink its Main hands down");
        var naming = code
            .Where(file => file.Path.StartsWith("tests", StringComparison.Ordinal)
                && EventLogSinkNames.Any(name => file.Text.Contains(name, StringComparison.Ordinal)))
            .Select(file => $"{file.Path} names a sink of the Event Log: hand the code a fake");

        AssertNone(reaching.Concat(naming));
    }

    // The type, or ServiceBase's property of it; a PowerShell type literal, as the installer's test quotes, is not C#.
    [GeneratedRegex(@"\bEventLog\b(?!\]::)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EventLogType();
}
