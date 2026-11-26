using System.Globalization;
using System.Text.RegularExpressions;
using Thermi.Core.Look;
using Thermi.Core.Translations;

namespace Thermi.Core.Agents;

/// <summary>What an agent changed, as <c>thermi-cli</c> logs it and the AI agents section names it.</summary>
[Translated]
public enum AgentDid
{
    Edit,
    Look,
    FindCpuFan,
    StartWithWindows,
    Undo,
    Redo,
}

public sealed record AgentChange(DateTimeOffset At, AgentDid Did, string What);

/// <summary>The AI agents section: how to add thermi to an agent, and the last changes agents made, newest first.</summary>
public sealed record AgentsView(string Command, string File, IReadOnlyList<AgentChangeView> Changes);

/// <summary>A change as the section shows it: its time and its kind in the window's words, and what it changed as logged.</summary>
public sealed record AgentChangeView(string When, string Did, string What);

/// <summary>
/// The agents' changes read back from their log, <c>thermi-agents.log</c>, whose message <c>thermi-cli</c> writes with
/// <see cref="Message"/>, so a change in its words cannot leave the window reading nothing.
/// </summary>
public static partial class AgentActivity
{
    public const int Shown = 5;

    /// <summary>The message of each change an agent made, with its <see cref="AgentDid"/> and what it changed.</summary>
    public const string Message = Opening + "{Did}" + Between + "{What}";

    private const string Opening = "An agent's ";

    private const string Between = " changed: ";

    private const string Stamp = "yyyy-MM-dd HH:mm:ss.fff zzz";

    /// <summary>The last <see cref="Shown"/> changes, newest first, of the log's <paramref name="lines"/> as they come, oldest first.</summary>
    public static IReadOnlyList<AgentChange> Changes(IEnumerable<string> lines) =>
        [.. lines.Select(Of).OfType<AgentChange>().Reverse().Take(Shown)];

    public static AgentsView View(string command, IReadOnlyList<AgentChange> changes, ValueFormat format, TimeZoneInfo zone) =>
        new(
            AgentSetup.ClaudeCode(command),
            AgentSetup.File(command),
            [.. changes.Select(change => new AgentChangeView(
                TimeZoneInfo.ConvertTime(change.At, zone).ToString("g", format.Numbers),
                format.Texts.Of(change.Did),
                change.What))]);

    private static AgentChange? Of(string line)
    {
        var match = Line().Match(line);
        return match.Success
            && Enum.TryParse<AgentDid>(match.Groups["did"].Value, out var did)
            && DateTimeOffset.TryParseExact(match.Groups["at"].Value, Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                ? new AgentChange(at, did, match.Groups["what"].Value.TrimEnd())
                : null;
    }

    // A line as thermi's logs write it: the time, the counter, the process, the level and the source, then the message,
    // whose kind Serilog quotes as it quotes any value that is not text.
    [GeneratedRegex(
        @"^(?<at>[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{3} [+-][0-9]{2}:[0-9]{2}) \S+ [0-9]+ INF \S+: "
            + Opening + @"""?(?<did>[A-Za-z]+)""?" + Between + "(?<what>.*)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex Line();
}
