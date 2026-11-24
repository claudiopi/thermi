using Thermi.Core.Sensors;

namespace Thermi.Core.Bridge;

/// <summary>
/// What an item's Sensor dropdown lists, by the ids items name sensors by: <see cref="First"/>, the sensors of its type on
/// the device it reads now, and <see cref="More"/>, those of its type on every other device, which it lists only once asked
/// to show all.
/// </summary>
public sealed record SensorOffer(IReadOnlyList<string> First, IReadOnlyList<string> More)
{
    public static readonly SensorOffer None = new([], []);

    /// <summary>
    /// The offer for an item of the library's type <paramref name="type"/> that reads <paramref name="read"/> now, among the
    /// catalog's <paramref name="devices"/>: what <see cref="SensorCatalog.Unoffered"/> leaves out stays out, but the
    /// sensor the item names, <paramref name="own"/>. An amount of data means another thing on each kind of device, the RAM
    /// in use or what a drive read in all, so other devices offer it only from the item's own kind. An item whose sensor
    /// is not read lists every sensor of its type first, since no device is its own.
    /// </summary>
    public static SensorOffer Of(IReadOnlyList<CatalogDevice> devices, string type, string? read, string? own)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var unoffered = SensorCatalog.Unoffered(devices).ToHashSet(StringComparer.Ordinal);
        var rows = devices
            .SelectMany(device => device.Sensors.Select(row => (Device: device.Id, Row: row)))
            .Where(each => each.Row.Sensor.Type == type)
            .ToList();
        var offered = rows.Where(each => each.Row.Id == own || !unoffered.Contains(each.Row.Id)).ToList();
        if (rows.Find(each => each.Row.Id == read) is not { Row: not null } reading)
        {
            return new([.. offered.Select(each => each.Row.Id)], []);
        }

        return new(
            [.. offered.Where(each => each.Device == reading.Device).Select(each => each.Row.Id)],
            [
                .. offered
                    .Where(each => each.Device != reading.Device && (type != "Data" || each.Row.Sensor.HardwareType == reading.Row.Sensor.HardwareType))
                    .Select(each => each.Row.Id),
            ]);
    }
}
