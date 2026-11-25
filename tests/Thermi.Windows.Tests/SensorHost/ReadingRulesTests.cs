extern alias sensors;

using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.SensorHost;
using Thermi.Core.Sensors;
using sensors::Windows.Win32;
using sensors::Windows.Win32.NetworkManagement.IpHelper;

namespace Thermi.Windows.Tests.SensorHost;

/// <summary>
/// The service's own rules over what the library reads: when hardware is updated and its speeds hold, when a drive has
/// sampled its counters twice, what a device is named, and what Windows says an adapter is.
/// </summary>
public sealed class ReadingRulesTests
{
    private const uint Ethernet = 6;

    private static readonly Dictionary<string, string> Auto = new(StringComparer.Ordinal);

    // Seconds between reads, the first at 0, and ! where the library made the devices anew, as at its start and each scan
    // for new hardware. Each read updates (u) with speeds that hold, updates after a gap (g) past the pace and the stale
    // limit of 3 s, as across sleep, or is not due (.): every read for the pace of 0, each 5 s for drives, again at once
    // after a gap, so speeds come back a second later, and at the two reads after the library made them.
    [Theory]
    [InlineData(0, "0 1 1 3 3.5 1", "uuuugu")]
    [InlineData(5, "0 1 1 1 1 1 1", "u....u.")]
    [InlineData(5, "0 8 9", "uug")]
    [InlineData(5, "0 3600 1 1", "ugu.")]
    [InlineData(5, "0! 1 1 1 1 1 1 1! 1 1", "uu....uuu.")]
    public void SpeedsHoldUnlessAnUpdateComesPastItsPaceAndTheStaleLimit(int every, string gaps, string expected)
    {
        var clock = new FakeTimeProvider();
        var pace = new ReadPace(clock, TimeSpan.FromSeconds(every));
        var seen = "";

        foreach (var gap in gaps.Split(' '))
        {
            clock.Advance(TimeSpan.FromSeconds(double.Parse(gap.TrimEnd('!'), CultureInfo.InvariantCulture)));
            if (gap.EndsWith('!'))
            {
                pace.Found();
            }

            var due = pace.Due;
            if (due)
            {
                pace.Updated();
            }

            seen += !due ? '.' : pace.SpeedsHold ? 'u' : 'g';
        }

        Assert.Equal(expected, seen);
    }

    // A second each across a scan for new hardware, as the pipe sent them before the service took a drive's first update
    // for what it is, its first sample: what the overlay shows of the drive's activity, a value (#) or missing (-), never
    // the near 100 % of its counters since Windows started.
    [Fact]
    public void ADrivesActivityIsMissingUntilItsCountersAreSampledTwice()
    {
        var shown = TestRecordings.Series(TestRecordings.DiskRescan)
            .Select(snapshot => SensorMap.Read(snapshot with { Sensors = ReadingRules.Sampled(snapshot.Sensors) }, Auto))
            .Select(readings => readings[Metrics.DiskActivity.Id].Value is null ? '-' : '#');

        Assert.Equal("###------###", string.Concat(shown));
    }

    // The library names a memory module by its chip's part number, here as two reads of one module came, its NULs and
    // other control characters written as their Unicode pictures, as ␀.
    [Theory]
    [InlineData("Patriot Memory (PDP Systems) - 6400Series␀␀␀␀␀␀␀␀␀␀␀␀␀␀␀␀␀} (#1)", "Patriot Memory (PDP Systems) - 6400Series (#1)")]
    [InlineData("Patriot Memory (PDP Systems) - 6400Ser␀␀␀e␁?␃r?␀␀␀␀,@,@,@␀} (#1)", "Patriot Memory (PDP Systems) - 6400Ser (#1)")]
    [InlineData("Patriot Memory (PDP Systems) - 6400Series      } (#1)", "Patriot Memory (PDP Systems) - 6400Series (#1)")]
    [InlineData("Patriot Memory (PDP Systems) - 6400Series (#3)", "Patriot Memory (PDP Systems) - 6400Series (#3)")]
    public void ADeviceIsNamedWithoutWhatItsChipLeftPastItsName(string library, string shown)
    {
        var read = string.Concat(library.Select(each => each is >= '␀' and < '␠' ? (char)(each - '␀') : each));

        Assert.Equal(shown, ReadingRules.NameOf(read));
    }

    // By Windows' flags, never by names: a filter's layer whatever else it says, hardware unless a loopback or a tunnel,
    // and anything without the hardware flag virtual.
    [Theory]
    [InlineData(true, true, Ethernet, nameof(AdapterKind.Layer))]
    [InlineData(false, true, Ethernet, nameof(AdapterKind.Physical))]
    [InlineData(false, true, SensorPInvoke.IF_TYPE_TUNNEL, nameof(AdapterKind.Virtual))]
    [InlineData(false, true, SensorPInvoke.IF_TYPE_SOFTWARE_LOOPBACK, nameof(AdapterKind.Virtual))]
    [InlineData(false, false, Ethernet, nameof(AdapterKind.Virtual))]
    public void AnAdapterIsWhatWindowsFlagsSay(bool filter, bool hardware, uint type, string kind)
    {
        var row = new MIB_IF_ROW2 { Type = type };
        row.InterfaceAndOperStatusFlags.FilterInterface = filter;
        row.InterfaceAndOperStatusFlags.HardwareInterface = hardware;

        Assert.Equal(Enum.Parse<AdapterKind>(kind), NetworkAdapters.KindOf(row));
    }
}
