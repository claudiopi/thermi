using System.CommandLine;

namespace Thermi.Cli.Commands;

/// <summary><c>thermi cpu-load &lt;seconds&gt;</c>: every core loaded, to record how the fans answer, and the fan finder's load.</summary>
internal sealed record CpuLoadRequest(int Seconds) : IThermiRequest;

/// <summary>
/// <c>thermi cpu-load</c>, which every OS's thermi parses beside the agents' commands, for the fan finder and for a person
/// who records how the fans answer. No agent runs it: the fan finder's command loads the CPU itself.
/// </summary>
internal static class CpuLoadCommand
{
    public const int MaxSeconds = 600;

    public static (Command Command, Func<ParseResult, IThermiRequest> Request) Create()
    {
        var seconds = new Argument<int>("seconds") { CustomParser = AgentCommandLine.Whole(1, MaxSeconds) };
        var command = new Command("cpu-load", "Loads every core at idle priority, to record how the fans answer.") { seconds };
        return (command, it => new CpuLoadRequest(it.GetValue(seconds)));
    }
}
