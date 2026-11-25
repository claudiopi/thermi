extern alias sensors;

using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.SensorHost.Frames;
using Thermi.Core.Frames;

namespace Thermi.Windows.Tests.SensorHost;

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
        using var source = new FramedSource(sensors, Open, new NoProcesses(), clock, said.Add);
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

    private sealed class Trace(bool starts) : IPresentTrace
    {
        public bool Running { get; set; } = starts;

        public bool Disposed { get; private set; }

        public bool Start(Action<TracedPresent> sink) => Running;

        public void Dispose() => Disposed = true;
    }

    private sealed class NoProcesses : IProcessTable
    {
        public ProcessFacts? Of(int process) => null;
    }
}
