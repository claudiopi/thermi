using Thermi.App.Logging;
using Thermi.Core.Agents;
using Thermi.Core.Look;
using Thermi.Windows.Updates;

namespace Thermi.Windows.Ui.Views;

/// <summary>The AI agents section as Windows answers it: thermi-cli beside the launcher, the PATH, and the agents' log.</summary>
internal static class WindowAgents
{
    // Beside the launcher, whose path an update never changes, by its name where a client finds it on the PATH.
    public static AgentsView View(ValueFormat format, TimeZoneInfo zone) => AgentActivity.View(
        AgentSetup.Command(Path.Combine(Path.GetDirectoryName(ThermiPaths.Launcher)!, AgentSetup.Program + ".exe"), PathFolders()),
        AgentActivity.Changes(AgentsLog.Lines(ThermiLog.AgentsPath)),
        format,
        zone);

    // The machine's and the user's PATH as a client started now reads them, and this process's own.
    private static IEnumerable<string> PathFolders() =>
        new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Process }
            .SelectMany(target => Environment.ExpandEnvironmentVariables(Environment.GetEnvironmentVariable("PATH", target) ?? "").Split(Path.PathSeparator));
}
