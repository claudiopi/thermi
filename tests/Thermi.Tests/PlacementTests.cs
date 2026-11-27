using Thermi.Core.Placement;

namespace Thermi.Tests;

/// <summary>How a snap names its monitor: by the ids of the monitors' EDIDs.</summary>
public sealed class PlacementTests
{
    // R35: the monitors, each as its EDID's serial number and the serial's own text, "blank" for a text of spaces, "none"
    // for no EDID and "zeros" for one of zeros, then @ and its connection; and their ids: maker, product and the serial's
    // own text, else the serial number, else the connection, two that report the same serial told apart by the connection.
    [Theory] // Rows: each a fallback R35 states, in its order.
    [InlineData("12345 7XQ9K13@5&2f5d3e1a&0&UID4357", "DEL-A1B2-7XQ9K13")]
    [InlineData("12345@UID1", "DEL-A1B2-12345")]
    [InlineData("0 blank@UID1", "DEL-A1B2@UID1")]
    [InlineData("none@UID1", "UID1")]
    [InlineData("zeros@UID1", "UID1")]
    [InlineData("1@UID1;1@UID2;9@UID3", "DEL-A1B2-1;DEL-A1B2-1@UID2;DEL-A1B2-9")]
    public void AMonitorIsKnownByItsEdidAndWithoutASerialByItsConnection(string monitors, string ids)
    {
        var read = monitors.Split(';').Select(monitor => monitor.Split('@')).Select(parts => (MonitorIds.Of(Edid(parts[0]), parts[1]), parts[1]));

        Assert.Equal(ids.Split(';'), MonitorIds.Unique([.. read]));
    }

    // Off GNOME, a Linux desktop names a monitor from its EDID as Mutter does, which GNOME's snaps saved: the product's
    // name with its spaces, else its code, and the serial's text, else its number, each code in hex; no EDID, no name,
    // which the desktop's own then gives.
    [Theory] // Rows: each a fallback of Mutter's, in its order.
    [InlineData("12345 7XQ9K13", "DEL-DELL U2723QE-7XQ9K13")]
    [InlineData("12345 blank", "DEL-DELL U2723QE-0x00003039")]
    [InlineData("12345 7XQ9K13 unnamed", "DEL-0xa1b2-7XQ9K13")]
    [InlineData("none", null)]
    public void OffGnomeAMonitorIsNamedAsGnomeNamesIt(string edid, string? id) => Assert.Equal(id, MonitorIds.OfGnome(Edid(edid), "DP-1"));

    private static byte[] Edid(string written) => written.Split(' ') switch
    {
        ["none"] => [],
        ["zeros"] => new byte[128],
        [var number] => Edid(uint.Parse(number, System.Globalization.CultureInfo.InvariantCulture), serial: null),
        [var number, var serial] => Edid(uint.Parse(number, System.Globalization.CultureInfo.InvariantCulture), serial == "blank" ? " \n" : serial),
        [var number, var serial, "unnamed"] => Edid(uint.Parse(number, System.Globalization.CultureInfo.InvariantCulture), serial, named: false),
        _ => throw new ArgumentException("An EDID is a number, a serial and \"unnamed\" at most.", nameof(written)),
    };

    // A base EDID block of 128 bytes: its header, "DEL" as the maker, 0xA1B2 as the product, a serial number, and the
    // serial's text in the second display descriptor, after a name in the first unless it is unnamed.
    private static byte[] Edid(uint number, string? serial, bool named = true)
    {
        var edid = new byte[128];
        byte[] header = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
        header.CopyTo(edid, 0);
        var maker = (('D' - '@') << 10) | (('E' - '@') << 5) | ('L' - '@');
        (edid[8], edid[9]) = ((byte)(maker >> 8), (byte)maker);
        (edid[10], edid[11]) = (0xB2, 0xA1);
        BitConverter.GetBytes(number).CopyTo(edid, 12);
        if (named)
        {
            Descriptor(edid, 54, 0xFC, "DELL U2723QE");
        }
        if (serial is not null)
        {
            Descriptor(edid, 72, 0xFF, serial);
        }

        return edid;
    }

    private static void Descriptor(byte[] edid, int at, byte tag, string text)
    {
        edid[at + 3] = tag;
        var field = (text + "\n").PadRight(13);
        for (var index = 0; index < 13; index++)
        {
            edid[at + 5 + index] = (byte)field[index];
        }
    }
}
