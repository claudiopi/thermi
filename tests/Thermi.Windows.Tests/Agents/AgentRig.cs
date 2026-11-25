using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Thermi.Agents;
using Thermi.Commands;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;
using Thermi.Overlay;
using Thermi.Ui;

namespace Thermi.Windows.Tests.Agents;

/// <summary>
/// The service as a recording plays it: each connection gets the snapshots in order, then waits, as the pipe does between
/// seconds; with none, it reads nothing, in the state it is given.
/// </summary>
internal sealed class RecordedFeed(IReadOnlyList<Snapshot> snapshots, ServiceState state = ServiceState.Running, bool taken = false) : ISensorFeed
{
    public event EventHandler? Received;

    public Snapshot? Latest { get; private set; }

    public ServiceState State => Latest is null ? state : ServiceState.Running;

    public bool Taken => taken;

    public Task RunAsync(CancellationToken stop)
    {
        foreach (var snapshot in snapshots)
        {
            Latest = snapshot;
            Received?.Invoke(this, EventArgs.Empty);
        }

        return Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, stop).ContinueWith(_ => { }, TaskScheduler.Default);
    }
}

/// <summary>A command's end: its exit code, what it printed on stdout, and on stderr.</summary>
internal sealed record AgentEnd(int Exit, string Out, string Error)
{
    public JsonObject Json => JsonNode.Parse(Out)!.AsObject();
}

/// <summary>
/// <c>thermi-cli</c> run in the test's process as <c>Program</c> runs it, on a settings file of the test's own, a recorded
/// service, one monitor, no overlay and the start at sign-in under a key of the tests', on the folder's fake clock.
/// </summary>
internal sealed class AgentRig : IDisposable
{
    private const string Keys = @"Software\thermi-tests\agents\";

    public SettingsFolder Folder { get; } = new();

    public Func<ISensorFeed> Sensors { get; set; } = () => new RecordedFeed([TestRecordings.Read(TestRecordings.Admin)]);

    public bool Overlay { get; set; }

    public AgentEnvironment Environment => new(
        Folder.File,
        () => Sensors(),
        new StartWithWindows(Keys + "Run", Keys + "Approved", "thermi.exe"),
        NullLogger.Instance)
    {
        Installed = () => true,
        Overlay = () => Overlay,
        SensorsOnly = () => false,
        Screens = () => [new Screen("DISPLAY1", Primary: true, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), Scale: 1)],
        AltGrKeys = () => new HashSet<int>(),
        Load = _ => { },
    };

    /// <summary>Parses the arguments as thermi does, and runs the agent's command they make, stepping the clock until it ends.</summary>
    public async Task<AgentEnd> RunAsync(params string[] args)
    {
        if (ThermiCommands.Parse(args, out var errors) is not AgentRequest request)
        {
            return new AgentEnd(2, "", string.Join('\n', errors));
        }

        using var input = new MemoryStream();
        using var output = new MemoryStream();
        await using var error = new StringWriter();
        var run = AgentRun.RunAsync(request, Environment, new AgentConsole(input, output, error), NullLoggerFactory.Instance, Folder.Clock);
        for (var step = 0; !run.IsCompleted && step < 2000; step++)
        {
            Folder.Clock.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(TimeSpan.FromMilliseconds(2), TimeProvider.System);
        }

        return new AgentEnd(await run, Encoding.UTF8.GetString(output.ToArray()), error.ToString());
    }

    public void Dispose()
    {
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Keys.TrimEnd('\\'), throwOnMissingSubKey: false);
        Folder.Dispose();
    }
}
