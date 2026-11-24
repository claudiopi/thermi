extern alias sensors;

using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.SensorHost;
using sensors::Windows.Win32;
using sensors::Windows.Win32.NetworkManagement.IpHelper;

namespace Thermi.Windows.Tests.SensorHost;

/// <summary>The service's own rules over what the library reads: when hardware is updated and its speeds hold, and what Windows says an adapter is.</summary>
public sealed class ReadingRulesTests
{
    private const uint Ethernet = 6;

    // Seconds between reads, the first at 0. Each read updates (u) with speeds that hold, updates after a gap (g) past the
    // pace and the stale limit of 3 s, as across sleep, or is not due (.): every read for the pace of 0, each 5 s for
    // drives, and again at once after a gap, so speeds come back a second later.
    [Theory]
    [InlineData(0, "0 1 1 3 3.5 1", "uuuugu")]
    [InlineData(5, "0 1 1 1 1 1 1", "u....u.")]
    [InlineData(5, "0 8 9", "uug")]
    [InlineData(5, "0 3600 1 1", "ugu.")]
    public void SpeedsHoldUnlessAnUpdateComesPastItsPaceAndTheStaleLimit(int every, string gaps, string expected)
    {
        var clock = new FakeTimeProvider();
        var pace = new ReadPace(clock, TimeSpan.FromSeconds(every));
        var seen = "";

        foreach (var gap in gaps.Split(' ').Select(gap => double.Parse(gap, CultureInfo.InvariantCulture)))
        {
            clock.Advance(TimeSpan.FromSeconds(gap));
            var due = pace.Due;
            if (due)
            {
                pace.Updated();
            }

            seen += !due ? '.' : pace.SpeedsHold ? 'u' : 'g';
        }

        Assert.Equal(expected, seen);
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
