using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Thermi.Core.Pipe;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;
using Thermi.Windows.Agents;
using Thermi.Windows.Commands;
using Thermi.Windows.Ui;

namespace Thermi.Windows.Tests.Agents;

/// <summary>
/// The service as a recording plays it: each connection gets the snapshots in order, at once or, with <paramref name="pace"/>,
/// one a second on that clock as the pipe sends them, then waits; with none, it reads nothing, in the state it is given.
/// </summary>
internal sealed class RecordedFeed(
    IReadOnlyList<Snapshot> snapshots,
    ServiceState state = ServiceState.Running,
    bool taken = false,
    TimeProvider? pace = null) : ISensorFeed
{
    public event EventHandler? Received;

    public Snapshot? Latest { get; private set; }

    public ServiceState State => Latest is null ? state : ServiceState.Running;

    public bool Taken => taken;

    public async Task RunAsync(CancellationToken stop)
    {
        try
        {
            foreach (var snapshot in snapshots)
            {
                Latest = snapshot;
                Received?.Invoke(this, EventArgs.Empty);
                if (pace is not null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), pace, stop);
                }
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, stop);
        }
        catch (OperationCanceledException)
        {
            // The connection ends as its reader hangs up.
        }
    }
}

/// <summary>
/// A stdin whoever started the command keeps open and never writes, as an agent's tool keeps it, whose read Windows cannot
/// cancel once begun, so nothing but the command's own end ends a watch.
/// </summary>
internal sealed class HeldStdin : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Only read asynchronously.");

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(Never());

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Never();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private static async Task<int> Never()
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, CancellationToken.None);
        return 0;
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
    // A key of each rig's own, since tests run at once.
    private readonly string _keys = @"Software\thermi-tests\agents\" + Guid.NewGuid().ToString("N") + @"\";

    public SettingsFolder Folder { get; } = new();

    public Func<ISensorFeed> Sensors { get; set; } = () => new RecordedFeed([TestRecordings.Read(TestRecordings.Admin)]);

    public bool Overlay { get; set; }

    public bool SensorsOnly { get; set; }

    public bool Installed { get; set; } = true;

    public bool PerUser { get; set; }

    public bool Raised { get; set; }

    public IReadOnlyList<Screen> Screens { get; set; } =
        [new Screen("DISPLAY1", Primary: true, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), Scale: 1)];

    /// <summary>When each run of the fan finder's load started and ended, on the folder's clock.</summary>
    public List<(long Started, long? Ended)> Loads { get; } = [];

    public AgentEnvironment Environment => new(
        Folder.File,
        () => Sensors(),
        Startup,
        Folder.Log)
    {
        FinderRan = Path.Combine(Path.GetDirectoryName(Folder.File)!, "fan-finder-ran.txt"),
        Installed = () => Installed,
        PerUser = () => PerUser,
        Raised = () => Raised,
        Overlay = () => Overlay,
        SensorsOnly = () => SensorsOnly,
        Screens = () => Screens,
        AltGrKeys = () => new HashSet<int>(),
        Load = Loaded,
    };

    public StartWithWindows Startup => new(_keys + "Run", _keys + "Approved", "thermi.exe");

    /// <summary>Parses the arguments as thermi does, and runs the agent's command they make, stepping the clock until it ends.</summary>
    public async Task<AgentEnd> RunAsync(params string[] args)
    {
        if (ThermiCommands.Parse(args, out var errors) is not AgentRequest request)
        {
            return new AgentEnd(2, "", string.Join('\n', errors));
        }

        await using var input = new HeldStdin();
        using var output = new MemoryStream();
        await using var error = new StringWriter();
        var run = AgentRun.RunAsync(request, Environment, new AgentConsole(input, output, error), NullLoggerFactory.Instance, Folder.Clock);
        await Folder.StepWhileAsync(run);
        return new AgentEnd(await run, Encoding.UTF8.GetString(output.ToArray()), error.ToString());
    }

    private void Loaded(CancellationToken stop)
    {
        lock (Loads)
        {
            var at = Loads.Count;
            Loads.Add((Folder.Clock.GetTimestamp(), null));
            stop.Register(() =>
            {
                lock (Loads)
                {
                    Loads[at] = (Loads[at].Started, Folder.Clock.GetTimestamp());
                }
            });
        }
    }

    public void Dispose()
    {
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_keys.TrimEnd('\\'), throwOnMissingSubKey: false);
        Folder.Dispose();
    }
}
