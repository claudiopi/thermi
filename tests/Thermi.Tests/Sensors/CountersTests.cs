using Thermi.Core.Sensors.Rates;

namespace Thermi.Tests.Sensors;

/// <summary>How Linux's and macOS's services turn an OS's counters into what changed since the second before.</summary>
public sealed class CountersTests
{
    // One counter's seconds, as a service reads them: its first, a rise over 2 s, one missing, the first after it, one
    // across a wrap of 100, one in the same instant, and one with no clock; then a change that went back, as after a
    // reset, and one after its source was forgotten; and the kernel's texts, as a file holds them or not.
    [Fact]
    public void ACounterRisesOnlyWhereItsRiseCanBeTold()
    {
        var counters = new Counters();
        (double? Value, double? At)[] seconds = [(10, 0), (30, 2), (null, 3), (40, 4), (20, 5), (30, 5), (35, null)];
        string?[] texts = ["  42\n", "-3", "4.5", null];

        var rates = seconds.Select(second => counters.Rate("energy", second.Value, second.At, wrap: 100)).ToList();
        double?[] changes = [counters.Change("bytes", 5), counters.Change("bytes", 8), counters.Change("bytes", 2)];
        counters.Forget("bytes");
        var forgotten = counters.Change("bytes", 9);

        Assert.Equal<double?>([null, 10, null, null, 80, null, null], rates);
        Assert.Equal<double?>([null, 3, null], changes);
        Assert.Null(forgotten);
        Assert.Equal<double?>([42, -3, null, null], texts.Select(Counters.Number));
    }
}
