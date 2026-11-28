using System.Runtime.InteropServices;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Native;

/// <summary>
/// A subscription to IOReport's channels the readers read: the energy each part of the chip used, and the time each CPU
/// core and the GPU spent in each performance state, each a count since the Mac started, which the readers turn into a
/// second's watts and clocks. A Mac with none of the groups, as a virtual machine, answers no channel. IOReport is the
/// library Apple's own powermetrics reads them through, which macOS ships with no header: its calls are declared by the
/// shapes Stats and macmon call them by, and any user may subscribe.
/// </summary>
internal sealed partial class Reports : IDisposable
{
    private const string Library = "/usr/lib/libIOReport.dylib";

    // IOReport's format of a channel of one number, and of one of states with their residencies.
    private const int Simple = 1;
    private const int State = 2;

    private static readonly (string Group, string? Subgroup)[] Groups =
        [("Energy Model", null), ("CPU Stats", "CPU Core Performance States"), ("GPU Stats", "GPU Performance States")];

    private readonly IntPtr _channels;
    private readonly IntPtr _subscription;
    private readonly IntPtr _subscribed;

    public Reports()
    {
        foreach (var (group, subgroup) in Groups)
        {
            var channels = Channels(group, subgroup);
            if (channels == IntPtr.Zero)
            {
                continue;
            }

            if (_channels == IntPtr.Zero)
            {
                _channels = CoreFoundation.MutableCopy(channels);
            }
            else
            {
                IOReportMergeChannels(_channels, channels, IntPtr.Zero);
            }

            CoreFoundation.Release(channels);
        }

        if (_channels != IntPtr.Zero)
        {
            _subscription = IOReportCreateSubscription(IntPtr.Zero, _channels, out _subscribed, 0, IntPtr.Zero);
        }
    }

    public IReadOnlyList<ReportChannel> Read()
    {
        var samples = _subscription == IntPtr.Zero ? IntPtr.Zero : IOReportCreateSamples(_subscription, _subscribed, IntPtr.Zero);
        if (samples == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            var channels = CoreFoundation.Get(samples, "IOReportChannels");
            return [.. CoreFoundation.Items(channels).Select(Channel)];
        }
        finally
        {
            CoreFoundation.Release(samples);
        }
    }

    public void Dispose()
    {
        CoreFoundation.Release(_subscribed);
        CoreFoundation.Release(_subscription);
        CoreFoundation.Release(_channels);
    }

    private static IntPtr Channels(string group, string? subgroup)
    {
        var named = CoreFoundation.String(group);
        var under = subgroup is null ? IntPtr.Zero : CoreFoundation.String(subgroup);
        try
        {
            return IOReportCopyChannelsInGroup(named, under, 0, 0, 0);
        }
        finally
        {
            CoreFoundation.Release(named);
            CoreFoundation.Release(under);
        }
    }

    private static ReportChannel Channel(IntPtr channel)
    {
        var read = new ReportChannel(
            CoreFoundation.Text(IOReportChannelGetGroup(channel)) ?? "",
            CoreFoundation.Text(IOReportChannelGetSubGroup(channel)),
            CoreFoundation.Text(IOReportChannelGetChannelName(channel)) ?? "",
            CoreFoundation.Text(IOReportChannelGetUnitLabel(channel)));
        return IOReportChannelGetFormat(channel) switch
        {
            Simple => read with { Value = IOReportSimpleGetIntegerValue(channel, 0) },
            State => read with
            {
                States = [.. Enumerable.Range(0, IOReportStateGetCount(channel)).Select(index => new ReportState(
                    CoreFoundation.Text(IOReportStateGetNameForIndex(channel, index)) ?? "",
                    IOReportStateGetResidency(channel, index)))],
            },
            _ => read,
        };
    }

    [LibraryImport(Library)]
    private static partial IntPtr IOReportCopyChannelsInGroup(IntPtr group, IntPtr subgroup, ulong a, ulong b, ulong c);

    [LibraryImport(Library)]
    private static partial void IOReportMergeChannels(IntPtr into, IntPtr channels, IntPtr none);

    [LibraryImport(Library)]
    private static partial IntPtr IOReportCreateSubscription(IntPtr none, IntPtr channels, out IntPtr subscribed, ulong a, IntPtr b);

    [LibraryImport(Library)]
    private static partial IntPtr IOReportCreateSamples(IntPtr subscription, IntPtr subscribed, IntPtr none);

    [LibraryImport(Library)]
    private static partial IntPtr IOReportChannelGetGroup(IntPtr channel);

    [LibraryImport(Library)]
    private static partial IntPtr IOReportChannelGetSubGroup(IntPtr channel);

    [LibraryImport(Library)]
    private static partial IntPtr IOReportChannelGetChannelName(IntPtr channel);

    [LibraryImport(Library)]
    private static partial IntPtr IOReportChannelGetUnitLabel(IntPtr channel);

    [LibraryImport(Library)]
    private static partial int IOReportChannelGetFormat(IntPtr channel);

    [LibraryImport(Library)]
    private static partial long IOReportSimpleGetIntegerValue(IntPtr channel, int index);

    [LibraryImport(Library)]
    private static partial int IOReportStateGetCount(IntPtr channel);

    [LibraryImport(Library)]
    private static partial IntPtr IOReportStateGetNameForIndex(IntPtr channel, int index);

    [LibraryImport(Library)]
    private static partial long IOReportStateGetResidency(IntPtr channel, int index);
}
