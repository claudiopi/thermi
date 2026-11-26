extern alias sensors;

using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.Sensors.Windows.Frames;
using Thermi.Core.Frames;

namespace Thermi.Windows.Tests.Sensors;

/// <summary>The service's frames over a trace's life, with a trace that fails, starts and is stopped from outside as asked.</summary>
public sealed class FramedSourceTests
{
    // A trace that cannot start reads no frames, says so once, and is tried a minute later, not at each read; one stopped
    // from outside is started again at the next read; and with no read for 5 s, as when the last client leaves, it stops.
    [Fact]
    public async Task TheTraceRunsWhileReadsComeAndIsTriedAgainAMinuteLater()
    {
        var clock = new FakeTimeProvider();
        var traces = new List<Trace>();
        var said = new List<string>();
        var starts = new Queue<bool>([false, true, true]);
        using var sensors = new NoSensors();
        using var processes = new NoProcesses();
        using var source = new FramedSource(sensors, Open, processes, clock, said.Add);
        Trace Open()
        {
            var trace = new Trace(starts.Dequeue());
            traces.Add(trace);
            return trace;
        }

        async Task<bool> Reads()
        {
            return FrameSlots.Read(await source.ReadAsync(TestContext.Current.CancellationToken)) is not null;
        }

        var failed = (await Reads(), await Reads());
        clock.Advance(FramedSource.Retry);
        var started = await Reads();
        traces[^1].Running = false;
        var restarted = await Reads();
        clock.Advance(FramedSource.Idle);

        Assert.Equal((false, false), failed);
        Assert.Single(said);
        Assert.True(started);
        Assert.True(restarted);
        Assert.Equal([true, true, true], traces.Select(trace => trace.Disposed));
    }

    // Zwift's presents, delivered as late as the real trace delivered them, up to 0.74 s after they happened: each second
    // the service closes, from the third, as Zwift counts as presenting, holds every present of it, none lost to coming after
    // its second closed.
    [Fact]
    public async Task NoPresentIsLostToTheTracesDelay()
    {
        var lines = await File.ReadAllLinesAsync(TestRecordings.Path(TestRecordings.ZwiftArrived), TestContext.Current.CancellationToken);
        var presents = lines.Select(line => PresentJson.Read(line)!.Value).ToList();
        var clock = new FakeTimeProvider();
        var start = clock.GetTimestamp() * 1000.0 / clock.TimestampFrequency;
        using var trace = new Trace(true);
        using var sensors = new NoSensors();
        using var processes = new NoProcesses();
        using var source = new FramedSource(sensors, () => trace, processes, clock, _ => { });
        await source.ReadAsync(TestContext.Current.CancellationToken);
        var counted = new List<double?>();
        var expected = new List<double?>();

        for (var now = 1000.0; now <= 8000; now += 1000)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            foreach (var present in presents.Where(present => present.Arrived > now - 1000 && present.Arrived <= now))
            {
                trace.Sink?.Invoke(new TracedPresent(present.Process, start + present.At, present.Source));
            }

            var closed = now - FramedSource.Lag.TotalMilliseconds;
            var slots = FrameSlots.Read(await source.ReadAsync(TestContext.Current.CancellationToken));
            counted.Add(slots is [var busiest, ..] ? Math.Round(busiest.Fps) : null);
            expected.Add(presents.Count(present => present.Source == PresentSource.KernelDetailed && present.At > closed - 1000 && present.At <= closed));
        }

        Assert.Equal(expected.Skip(2), counted.Skip(2));
    }

    private sealed class Trace(bool starts) : IPresentTrace
    {
        public bool Running { get; set; } = starts;

        public bool Disposed { get; private set; }

        public Action<TracedPresent>? Sink { get; private set; }

        public bool Start(Action<TracedPresent> sink)
        {
            Sink = sink;
            return Running;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class NoProcesses : IProcessTable
    {
        public ProcessFacts? Of(int process) => null;

        public void Dispose()
        {
        }
    }
}
