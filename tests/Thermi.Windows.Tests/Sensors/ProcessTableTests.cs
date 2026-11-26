extern alias sensors;

using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.Sensors.Windows.Frames;

namespace Thermi.Windows.Tests.Sensors;

/// <summary>The processes that present, each read once and held while it runs, as the service's frames ask of them.</summary>
public sealed class ProcessTableTests
{
    // A process is opened once however often it presents; once it ends it keeps its facts, as its id is still held, until the
    // second's sweep lets it go, and a process that then takes its id is read anew.
    [Fact]
    public void AProcessIsReadOnceAndItsIdAnewOnceItEnded()
    {
        var clock = new FakeTimeProvider();
        using var first = new Held(new ProcessFacts(1, Compositor: false));
        using var second = new Held(new ProcessFacts(2, Compositor: true));
        var opens = new Queue<Held>([first, second]);
        using var table = new ProcessTable(clock, _ => opens.Dequeue(), () => []);

        var asked = (table.Of(7), table.Of(7));
        first.Exited = true;
        var ended = table.Of(7);
        clock.Advance(TimeSpan.FromSeconds(1));
        var taken = table.Of(7);

        Assert.Equal((first.Facts, first.Facts), asked);
        Assert.Equal(first.Facts, ended);
        Assert.True(first.Disposed);
        Assert.Equal(second.Facts, taken);
    }

    private sealed class Held(ProcessFacts facts) : IHeldProcess
    {
        public ProcessFacts Facts { get; } = facts;

        public bool Exited { get; set; }

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
