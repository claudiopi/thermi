using Microsoft.Extensions.Logging;
using Thermi.App.Finder;
using Thermi.App.Logging;
using Thermi.App.SettingsFile;
using Thermi.Cli.Agents;
using Thermi.Core.Agents;
using Thermi.Core.Pipe;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.MacOS.Cli.Native;
using Thermi.MacOS.Cli.Sensors;

namespace Thermi.MacOS.Cli.Agents;

/// <summary>
/// What an agent's command reaches on a Mac: the sensors from the person's service's socket, the service installed when its
/// agent's plist is in the person's LaunchAgents, Open at login as the overlay's agent's, and the load of a
/// <c>thermi-cli cpu-load</c>. The overlay's monitors and its state come with the overlay's own host; until it answers,
/// there is no overlay to ask. It loads no AppKit, so it answers over SSH, where no one signed in graphically.
/// </summary>
internal sealed class MacAgentEnvironment(ILogger log, TimeProvider time) : IAgentEnvironment
{
    // EWOULDBLOCK, which .NET gives as the error's code when another process holds the file.
    private const int WouldBlock = 35;

    public string SettingsPath => ConfigStore.DefaultPath;

    public ILogger Log => log;

    public IStartAtSignIn Startup { get; } = new OpenAtLogin();

    public string FinderRan => Path.Combine(UserFolders.Local, "thermi", "fan-finder-ran.txt");

    public ISensorFeed Sensors() => new MacSocketFeed(time, log);

    public bool Installed() => File.Exists(LaunchAgents.PlistOf(LaunchAgents.ServiceLabel));

    public bool PerUser() => false;

    public bool Overlay() => false;

    public bool SensorsOnly() => false;

    // Root writes into the person's folders would leave files the person cannot change, so as root it only reads.
    public bool Raised() => User.Id == 0;

    public bool RunsInstalled() => false;

    public IReadOnlyList<Screen> Screens() => [];

    public AgentProblem? WhyNoScreens() => AgentProblem.OverlayNotRunning;

    public IReadOnlySet<int> AltGrKeys() => new HashSet<int>();

    public void Load(CancellationToken stop) =>
        CpuLoad.Start(Path.Combine(AppContext.BaseDirectory, "thermi-cli"), CpuLoad.Arguments((int)FanFinder.MaxRun.TotalSeconds), stop);

    public bool HeldElsewhere(IOException error) => error.HResult == WouldBlock;

    // Open at login is whether launchd starts the overlay's agent at login, as its plist says.
    private sealed class OpenAtLogin : IStartAtSignIn
    {
        public Task<bool> OnAsync(CancellationToken cancel) => Task.FromResult(LaunchAgents.OpensAtLogin());

        public Task SetAsync(bool on, CancellationToken cancel)
        {
            LaunchAgents.SetOpenAtLogin(on);
            return Task.CompletedTask;
        }
    }
}
