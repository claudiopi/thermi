using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Thermi.Cli.Agents;
using Thermi.Cli.Commands;
using Thermi.Core.Pipe;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;

namespace Thermi.Tests.Agents;

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
/// <c>thermi-cli</c> run in the test's process as a host runs it, on a settings file of the test's own, a recorded service,
/// one monitor, no overlay and a start at sign-in of its own, on the folder's fake clock.
/// </summary>
internal sealed class AgentRig : IDisposable
{
    public SettingsFolder Folder { get; } = new();

    public Func<ISensorFeed> Sensors { get; set; } = () => new RecordedFeed([TestRecordings.Read(TestRecordings.Admin)]);

    public bool Overlay { get; set; }

    public bool SensorsOnly { get; set; }

    public bool Installed { get; set; } = true;

    public bool PerUser { get; set; }

    public bool Raised { get; set; }

    /// <summary>Where the commands log, the folder's own list when null.</summary>
    public ILogger? Log { get; set; }

    public IReadOnlyList<Screen> Screens { get; set; } =
        [new Screen("DISPLAY1", Primary: true, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), Scale: 1)];

    /// <summary>When each run of the fan finder's load started and ended, on the folder's clock.</summary>
    public List<(long Started, long? Ended)> Loads { get; } = [];

    public IAgentEnvironment Environment => new RigEnvironment(this);

    public StartAtSignIn Startup { get; } = new();

    /// <summary>Parses the arguments as a host does, and runs the agent's command they make, stepping the clock until it ends.</summary>
    public async Task<AgentEnd> RunAsync(params string[] args)
    {
        if (AgentCommandLine.Parse(new RootCommand(), [], args, _ => [], _ => null, out var errors) is not AgentRequest request)
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

    /// <summary>
    /// The rig's picture of an overlay: in place of pixels, which each OS's painter makes, what it was asked to paint, and
    /// a size that grows with the overlay's own and the scale.
    /// </summary>
    public static OverlayPng Painted(ThermiConfig settings, int overlay, Snapshot snapshot, float scale)
    {
        var size = settings.Overlays[overlay].Size * scale;
        var asked = string.Create(CultureInfo.InvariantCulture, $"overlay {overlay} of {settings.Overlays.Count}, {snapshot.Sensors.Count} sensors, {size}");
        return new OverlayPng(Encoding.UTF8.GetBytes(asked), (int)Math.Round(100 * size), (int)Math.Round(20 * size));
    }

    public void Dispose() => Folder.Dispose();

    /// <summary>The start at sign-in as the OS would hold it, here a switch of the rig's own.</summary>
    internal sealed class StartAtSignIn : IStartAtSignIn
    {
        public bool On { get; private set; }

        public void Set(bool on) => On = on;
    }

    // What the rig says, read at each call, so a test changes it between commands.
    private sealed class RigEnvironment(AgentRig rig) : IAgentEnvironment
    {
        public string SettingsPath => rig.Folder.File;

        public ILogger Log => rig.Log ?? rig.Folder.Log;

        public IStartAtSignIn Startup => rig.Startup;

        public string FinderRan => Path.Combine(Path.GetDirectoryName(rig.Folder.File)!, "fan-finder-ran.txt");

        public ISensorFeed Sensors() => rig.Sensors();

        public bool Installed() => rig.Installed;

        public bool PerUser() => rig.PerUser;

        public bool Overlay() => rig.Overlay;

        public bool SensorsOnly() => rig.SensorsOnly;

        public bool Raised() => rig.Raised;

        public bool RunsInstalled() => true;

        public IReadOnlyList<Screen> Screens() => rig.Screens;

        public IReadOnlySet<int> AltGrKeys() => new HashSet<int>();

        public void Load(CancellationToken stop) => rig.Loaded(stop);

        public OverlayPng Paint(ThermiConfig settings, int overlay, Snapshot snapshot, float scale) => Painted(settings, overlay, snapshot, scale);

        // A test holds the file the finder claims, and nothing else fails to open it here.
        public bool HeldElsewhere(IOException error) => true;
    }
}
