using System.Runtime.InteropServices;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Native;

/// <summary>
/// The HID event system's temperature sensors, the ones of Apple's vendor page, <c>0xff00</c>, of usage 5, as Stats and
/// macmon match them: the PMU's dies, the SSD's NAND and, on some chips, the cores. Each is asked for its event each read;
/// one that answers none reads missing.
/// </summary>
internal sealed partial class HidSensors : IDisposable
{
    private const string Library = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const int AppleVendorPage = 0xff00;
    private const int TemperatureUsage = 5;

    // kIOHIDEventTypeTemperature, and the field of its value.
    private const long TemperatureEvent = 15;
    private const int TemperatureField = 15 << 16;

    private readonly IntPtr _client;

    public HidSensors()
    {
        _client = IOHIDEventSystemClientCreate(IntPtr.Zero);
        if (_client == IntPtr.Zero)
        {
            return;
        }

        var matching = CoreFoundation.NumbersByName(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["PrimaryUsagePage"] = AppleVendorPage,
            ["PrimaryUsage"] = TemperatureUsage,
        });
        // A client that matches nothing lists no sensor, as a Mac with none.
        _ = IOHIDEventSystemClientSetMatching(_client, matching);
        CoreFoundation.Release(matching);
    }

    public IReadOnlyList<HidReading> Read()
    {
        var services = _client == IntPtr.Zero ? IntPtr.Zero : IOHIDEventSystemClientCopyServices(_client);
        if (services == IntPtr.Zero)
        {
            return [];
        }

        var product = CoreFoundation.String("Product");
        try
        {
            var readings = new List<HidReading>();
            foreach (var service in CoreFoundation.Items(services))
            {
                var name = IOHIDServiceClientCopyProperty(service, product);
                var hidEvent = IOHIDServiceClientCopyEvent(service, TemperatureEvent, 0, 0);
                if (CoreFoundation.Text(name) is { } named)
                {
                    readings.Add(new HidReading(named, hidEvent == IntPtr.Zero ? null : IOHIDEventGetFloatValue(hidEvent, TemperatureField)));
                }

                CoreFoundation.Release(name);
                CoreFoundation.Release(hidEvent);
            }

            return readings;
        }
        finally
        {
            CoreFoundation.Release(product);
            CoreFoundation.Release(services);
        }
    }

    public void Dispose() => CoreFoundation.Release(_client);

    [LibraryImport(Library)]
    private static partial IntPtr IOHIDEventSystemClientCreate(IntPtr allocator);

    [LibraryImport(Library)]
    private static partial int IOHIDEventSystemClientSetMatching(IntPtr client, IntPtr matching);

    [LibraryImport(Library)]
    private static partial IntPtr IOHIDEventSystemClientCopyServices(IntPtr client);

    [LibraryImport(Library)]
    private static partial IntPtr IOHIDServiceClientCopyProperty(IntPtr service, IntPtr property);

    [LibraryImport(Library)]
    private static partial IntPtr IOHIDServiceClientCopyEvent(IntPtr service, long type, int options, long timestamp);

    [LibraryImport(Library)]
    private static partial double IOHIDEventGetFloatValue(IntPtr hidEvent, int field);
}
