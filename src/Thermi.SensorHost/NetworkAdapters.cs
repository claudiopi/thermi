using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.IpHelper;

namespace Thermi.SensorHost;

/// <summary>What Windows says a network adapter is.</summary>
internal enum AdapterKind
{
    Physical,

    /// <summary>A loopback or tunnel type, or no hardware interface flag: Hyper-V's and WSL's switches and most VPNs.</summary>
    Virtual,

    /// <summary>A filter driver's layer over an adapter, which counts that adapter's traffic again.</summary>
    Layer,
}

/// <summary>
/// What each of the library's network adapters is, by Windows' own flags from IP Helper, never by names, which vendors
/// choose and Windows translates. The library names an adapter "/nic/{guid}", escaped, after the interface's GUID. An
/// interface's type and flags never change, so Windows is asked again only when an adapter not seen before appears.
/// </summary>
internal sealed class NetworkAdapters
{
    private const string Prefix = "/nic/";

    private readonly Dictionary<string, AdapterKind> _kinds = new(StringComparer.Ordinal);

    /// <summary>The kind of each of <paramref name="adapters"/>, the library's device ids.</summary>
    public IReadOnlyDictionary<string, AdapterKind> Of(IReadOnlyCollection<string> adapters)
    {
        if (adapters.Any(adapter => !_kinds.ContainsKey(adapter)) && Windows() is { } windows)
        {
            foreach (var adapter in adapters)
            {
                // One Windows does not list it says nothing about, so it counts as physical, as before Windows was asked.
                _kinds[adapter] = GuidOf(adapter) is { } guid ? windows.GetValueOrDefault(guid) : AdapterKind.Physical;
            }
        }

        return adapters.Distinct(StringComparer.Ordinal).ToDictionary(adapter => adapter, adapter => _kinds.GetValueOrDefault(adapter), StringComparer.Ordinal);
    }

    private static Guid? GuidOf(string adapter) =>
        adapter.StartsWith(Prefix, StringComparison.Ordinal) && Guid.TryParse(Uri.UnescapeDataString(adapter[Prefix.Length..]), out var guid) ? guid : null;

    // Every interface's kind by its GUID; null when Windows cannot say, and then nothing is cached.
    private static unsafe Dictionary<Guid, AdapterKind>? Windows()
    {
        MIB_IF_TABLE2* table = null;
        if (SensorPInvoke.GetIfTable2(&table) != WIN32_ERROR.NO_ERROR)
        {
            return null;
        }

        try
        {
            var kinds = new Dictionary<Guid, AdapterKind>();
            foreach (ref readonly var row in table->Table.AsSpan((int)table->NumEntries))
            {
                kinds[row.InterfaceGuid] = KindOf(row);
            }

            return kinds;
        }
        finally
        {
            SensorPInvoke.FreeMibTable(table);
        }
    }

    internal static AdapterKind KindOf(in MIB_IF_ROW2 row) =>
        row.InterfaceAndOperStatusFlags.FilterInterface ? AdapterKind.Layer
        : row.InterfaceAndOperStatusFlags.HardwareInterface && row.Type is not SensorPInvoke.IF_TYPE_SOFTWARE_LOOPBACK and not SensorPInvoke.IF_TYPE_TUNNEL ? AdapterKind.Physical
        : AdapterKind.Virtual;
}
