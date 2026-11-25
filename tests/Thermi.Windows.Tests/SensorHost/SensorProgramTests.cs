extern alias sensors;

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.SensorHost;
using sensors::Thermi.SensorHost.User;
using Thermi.Core.Sensors;

namespace Thermi.Windows.Tests.SensorHost;

/// <summary>
/// <c>thermi-sensors</c> as its command line runs it, on sensors that replay the recording without admin rights, a fake
/// clock, a pipe and files of the test's own: no LibreHardwareMonitor, no driver and no admin rights.
/// </summary>
public sealed class SensorProgramTests : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "dumps", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new();
    private readonly FakeSensors _sensors = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private readonly string _pipeName = $"thermi-test-{Guid.NewGuid():N}";
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task<int>> _runs = [];

    public SensorProgramTests()
    {
        Directory.CreateDirectory(_folder);
    }

    private string File => Path.Combine(_folder, "dump.json");

    // A test that failed midway leaves its run going, a series with its file open, and deleting the folder would then fail
    // over the test's own failure: the stop ends serving, and the clock the series, first.
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        Task runs = Task.WhenAll(_runs);
        // A second at a time until they end; the real clock only bounds a run that hangs.
        var ended = SpinWait.SpinUntil(() =>
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            return runs.IsCompleted;
        }, Patience);
        if (ended)
        {
            await runs.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        _sensors.Dispose();
        await _out.DisposeAsync();
        await _error.DisposeAsync();
        _stop.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task ADumpSavesWhatTheSensorsReadNow()
    {
        var exit = await RunAsync(["--dump", File]);

        Assert.Equal(0, exit);
        var saved = SnapshotJson.Read(await System.IO.File.ReadAllTextAsync(File, TestContext.Current.CancellationToken))!.Sensors;
        Assert.Equal(FakeSensors.Recorded.Sensors, saved.Where(sensor => sensor.Id != FakeSensors.Counter));
        Assert.Equal((1, 1, true), (_sensors.Opened, _sensors.Reads, _sensors.Closed));
    }

    // A snapshot a second of the clock, the first at once, each on its own line as the pipe sends them.
    [Fact]
    public async Task ASeriesSavesOneSnapshotPerSecond()
    {
        var running = RunAsync(["--dump", File, "--seconds", "3"]);
        var early = new List<bool>();
        for (var second = 1; second < 3; second++)
        {
            await _sensors.ReadCountAsync(second);
            early.Add(running.IsCompleted);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        var exit = await running.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);

        var lines = await System.IO.File.ReadAllLinesAsync(File, TestContext.Current.CancellationToken);
        Assert.Equal(0, exit);
        Assert.Equal([false, false], early);
        Assert.Equal([1d, 2d, 3d], lines.Select(line => SnapshotJson.Read(line)!.Sensors.Single(sensor => sensor.Id == FakeSensors.Counter).Value!.Value));
        Assert.True(_sensors.Closed);
    }

    // Which arguments make no sense is the command line's, in CommandLineTests; one proves none opens a sensor or a file.
    [Fact]
    public async Task ArgumentsThatMakeNoSenseExitWith2BeforeAnySensorOpens()
    {
        var exit = await RunAsync(["--dump", "dump.json", "--seconds", "0"]);

        Assert.Equal(2, exit);
        Assert.NotEmpty(_error.ToString().Trim());
        Assert.Equal(0, _sensors.Opened);
        Assert.False(System.IO.File.Exists(Path.Combine(_folder, "dump.json")));
    }

    // What System.CommandLine answers itself goes to the program's own output, as the rest of what it says does.
    [Theory]
    [InlineData("--help", "--dump")]
    [InlineData("--version", @"^\d+\.\d+")]
    public async Task HelpAndVersionAnswerInItsOwnOutput(string asked, string answer)
    {
        var exit = await RunAsync([asked]);

        Assert.Equal(0, exit);
        Assert.Matches(answer, _out.ToString());
        Assert.Empty(_error.ToString());
        Assert.Equal(0, _sensors.Opened);
    }

    [Fact]
    public async Task InATerminalItServesReadingsUntilStopped()
    {
        var serving = RunAsync(["--console"]);
        await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.In, PipeOptions.Asynchronous);
        await client.ConnectAsync(Patience, TestContext.Current.CancellationToken);

        var snapshot = await PipeReading.NextSnapshotAsync(client, _clock, Patience);
        var early = serving.IsCompleted;
        await _stop.CancelAsync();
        var exit = await serving.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal(FakeSensors.Recorded.Sensors, snapshot.Sensors.Where(sensor => sensor.Id != FakeSensors.Counter));
        Assert.False(early, "It stopped serving before it was asked to.");
        Assert.Equal(0, exit);
        Assert.NotEmpty(_out.ToString().Trim());
        Assert.True(_sensors.Closed);
    }

    // Spec 004, A21: the pipe named for the user, owned by the user and shut to the rest but SYSTEM and administrators, and
    // a host that ends once its last client left, or when none came within 30 s.
    [Fact]
    public async Task AUsersHostServesItsUserUntilTheLastClientLeaves()
    {
        using var user = WindowsIdentity.GetCurrent();
        var serving = RunAsync(["--user"]);
        await using var client = new NamedPipeClientStream(".", _pipeName + "-" + user.User!.Value, PipeDirection.In, PipeOptions.Asynchronous);
        await client.ConnectAsync(Patience, TestContext.Current.CancellationToken);
        var snapshot = await PipeReading.NextSnapshotAsync(client, _clock, Patience);
        var security = client.GetAccessControl();
        var readers = security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow).Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value).Order();
        await client.DisposeAsync();
        await PassAsync(UserMode.Linger + TimeSpan.FromSeconds(3), serving);
        var served = await serving.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);

        var lonely = RunAsync(["--user"]);
        var early = await PassAsync(UserMode.FirstClient - TimeSpan.FromSeconds(1), lonely);
        await PassAsync(TimeSpan.FromSeconds(3), lonely);
        var alone = await lonely.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal(FakeSensors.Recorded.Sensors, snapshot.Sensors.Where(sensor => sensor.Id != FakeSensors.Counter));
        Assert.Equal(user.User, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.Equal(new[] { "S-1-5-18", "S-1-5-32-544", user.User.Value }.Order(), readers);
        Assert.Equal((0, false, 0), (served, early, alone));
    }

    // Spec 004, A24: the host's stop event ends it while a client still reads, and an event someone made before the host
    // keeps it from running at all, so no other process steers it.
    [Fact]
    public async Task TheUsersHostIsSteeredOnlyByItsOwnEvents()
    {
        using var user = WindowsIdentity.GetCurrent();
        var pipe = _pipeName + "-" + user.User!.Value;
        var serving = RunAsync(["--user"]);
        await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.In, PipeOptions.Asynchronous);
        await client.ConnectAsync(Patience, TestContext.Current.CancellationToken);
        await PipeReading.NextSnapshotAsync(client, _clock, Patience);
        using (var stop = EventWaitHandle.OpenExisting(UserMode.EventOf(pipe, UserMode.StopEvent)))
        {
            stop.Set();
        }

        await PassAsync(TimeSpan.FromSeconds(2), serving);
        var stopped = await serving.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);
        using var first = new EventWaitHandle(false, EventResetMode.AutoReset, UserMode.EventOf(pipe, "129"));
        var refused = await RunAsync(["--user"]).WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal((0, UserMode.Taken), (stopped, refused));
    }

    // Moves the clock a second at a time, giving the run a moment at each; whether it had ended by then.
    private async Task<bool> PassAsync(TimeSpan time, Task run)
    {
        for (var passed = TimeSpan.Zero; passed < time; passed += TimeSpan.FromSeconds(1))
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(TimeSpan.FromMilliseconds(20), TimeProvider.System, TestContext.Current.CancellationToken);
        }

        return run.IsCompleted;
    }

    private Task<int> RunAsync(string[] args)
    {
        var environment = new SensorHostEnvironment(_ => _sensors.Open(), _clock, _out, _error)
        {
            PipeName = _pipeName,
            UserLog = Path.Combine(_folder, "thermi-sensors.log"),
        };
        var run = Task.Run(
            () => sensors::Thermi.SensorHost.Program.RunAsync(
                [.. args.Select(arg => arg == "dump.json" ? File : arg)],
                environment,
                _stop.Token),
            CancellationToken.None);
        _runs.Add(run);
        return run;
    }

    /// <summary>
    /// The sensors of the author's PC as a user without admin rights reads them, many of them missing, with one more that
    /// counts the reads.
    /// </summary>
    private sealed class FakeSensors : ISensorSource
    {
        public const string Counter = "/test/0/load/0";

        private readonly SemaphoreSlim _read = new(0);

        public static Snapshot Recorded { get; } = TestRecordings.Read(TestRecordings.NoAdmin);

        public int Opened { get; private set; }

        public int Reads { get; private set; }

        public bool Closed { get; private set; }

        public FakeSensors Open()
        {
            Opened++;
            return this;
        }

        public Task<Snapshot> ReadAsync(CancellationToken stop)
        {
            Reads++;
            _read.Release();
            return Task.FromResult(new Snapshot([.. Recorded.Sensors, new RawSensor(Counter, "Test", "Test", "Load", "Reads", Reads)]));
        }

        /// <summary>Waits for the read numbered <paramref name="count"/>, from 1.</summary>
        public async Task ReadCountAsync(int count)
        {
            while (Reads < count)
            {
                Assert.True(await _read.WaitAsync(Patience, TestContext.Current.CancellationToken), $"Read {count} never came: {Reads} so far.");
            }
        }

        public void Dispose()
        {
            Closed = true;
            _read.Dispose();
        }
    }
}
