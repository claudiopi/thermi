extern alias sensors;

using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.SensorHost;
using sensors::Thermi.SensorHost.Frames;
using Thermi.Core.Frames;
using Thermi.Core.Sensors;
using Thermi.Windows.Tests.SensorHost;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>
/// <c>thermi-sensors</c> as its command line runs it, with a trace that tells of this test's own process presenting, and
/// Windows' real list of processes, which holds its name.
/// </summary>
public sealed class FrameWindowsTests : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "frames", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new();
    private readonly string _pipeName = $"thermi-test-{Guid.NewGuid():N}";
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task<int>> _runs = [];
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public FrameWindowsTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        Task runs = Task.WhenAll(_runs);
        if (SpinWait.SpinUntil(() =>
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            return runs.IsCompleted;
        }, Patience))
        {
            await runs.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        await _out.DisposeAsync();
        await _error.DisposeAsync();
        _stop.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task FramesLeaveTheServiceWithoutNames()
    {
        using var current = Process.GetCurrentProcess();
        var file = Path.Combine(_folder, "frames.jsonl");

        var recording = Run(["--frames", file, "--seconds", "1"]);
        Tick(recording);
        var exit = await recording;
        var recorded = await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken);

        _ = Run(["--console"]);
        await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.In, PipeOptions.Asynchronous);
        await client.ConnectAsync(Patience, TestContext.Current.CancellationToken);
        var served = await PipeReading.NextSnapshotAsync(client, _clock, Patience);
        for (var read = 0; read < FrameTally.SteadyFor + 2 && FrameSlots.Read(served) is not [_, ..]; read++)
        {
            served = await PipeReading.NextSnapshotAsync(client, _clock, Patience);
        }

        Assert.Equal(0, exit);
        Assert.All(recorded, line => Assert.Equal(current.Id, PresentJson.Read(line)?.Process));
        Assert.All(recorded, line => Assert.True(PresentJson.Read(line) is { Arrived: { } arrived } present && arrived >= present.At));
        Assert.NotEmpty(recorded);
        Assert.Equal<int?>(current.Id, FrameSlots.Read(served) is [var busiest, ..] ? busiest.Process : null);
        Assert.DoesNotContain(
            [.. recorded, SnapshotJson.Write(served with { Sensors = [.. served.Sensors.Where(FrameSlots.Is)] })],
            line => line.Contains(current.ProcessName, StringComparison.OrdinalIgnoreCase));
    }

    private Task<int> Run(string[] args)
    {
        var environment = new SensorHostEnvironment((_, _) => new NoSensors(), _clock, _out, _error)
        {
            PipeName = _pipeName,
            Trace = () => new OwnPresents(_clock),
        };
        var run = Task.Run(() => sensors::Thermi.SensorHost.Program.RunAsync(args, environment, _stop.Token), CancellationToken.None);
        _runs.Add(run);
        return run;
    }

    // A second at a time until it ends; the real clock only bounds a run that hangs.
    private void Tick(Task<int> run) =>
        Assert.True(SpinWait.SpinUntil(() =>
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            return run.IsCompleted;
        }, Patience));

    // Each second, as the clock moves, 15 presents of this test's process in the last 0.4 s, which the service has not
    // closed yet, so it counts as presenting from the third second it closes, and one of the compositor's, which neither
    // the recording nor the slots hold.
    private sealed class OwnPresents(FakeTimeProvider clock) : IPresentTrace
    {
        private ITimer? _timer;

        public bool Start(Action<TracedPresent> sink)
        {
            _timer = clock.CreateTimer(_ => Present(sink), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            return true;
        }

        public bool Running => true;

        public void Dispose() => _timer?.Dispose();

        private void Present(Action<TracedPresent> sink)
        {
            var now = clock.GetTimestamp() * 1000.0 / clock.TimestampFrequency;
            for (var at = now - 375; at < now; at += 25)
            {
                sink(new TracedPresent(Environment.ProcessId, at, PresentSource.Kernel));
            }

            foreach (var compositor in Process.GetProcessesByName("dwm"))
            {
                using (compositor)
                {
                    sink(new TracedPresent(compositor.Id, now - 200, PresentSource.Kernel));
                }
            }
        }
    }
}
