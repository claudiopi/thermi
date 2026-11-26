extern alias sensors;

using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.Sensors.Windows.Frames;

namespace Thermi.Windows.Tests.Sensors.Frames;

/// <summary>The processes that present, each read once and held while it runs, as the service's frames ask of them.</summary>
public sealed class ProcessTableTests
{
    // A process is opened once however often it presents; once it ends it keeps its facts, as its id is still held, until the
    // second's sweep lets it go, and a process that then takes its id is read anew.
    [Fact]
    public void AProcessIsReadOnceAndItsIdAnewOnceItEnded()
    {
        var clock = new FakeTimeProvider();
        using var first = new EndingProcess(new ProcessFacts(1, Compositor: false));
        using var second = new EndingProcess(new ProcessFacts(2, Compositor: true));
        var opens = new Queue<EndingProcess>([first, second]);
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

    // A process the service cannot open, as the compositor is to a run without admin rights, is not tried again at each
    // present: it is looked up in the list until the second's sweep, and only then opened anew.
    [Fact]
    public void AProcessThatCannotBeOpenedIsTriedAgainOnlyAfterTheSweep()
    {
        var clock = new FakeTimeProvider();
        using var opened = new EndingProcess(new ProcessFacts(1, Compositor: true));
        var opens = new Queue<EndingProcess?>([null, opened]);
        var listed = new ProcessFacts(1, Compositor: false);
        using var table = new ProcessTable(clock, _ => opens.Dequeue(), () => new() { [7] = listed });

        var within = (table.Of(7), table.Of(7));
        clock.Advance(TimeSpan.FromSeconds(1));
        var after = table.Of(7);

        Assert.Equal((listed, listed), within);
        Assert.Equal(opened.Facts, after);
    }
}
