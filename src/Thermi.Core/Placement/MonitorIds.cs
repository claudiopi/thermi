using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Thermi.Core.Placement;

/// <summary>
/// How a snap names its monitor: by the maker, model and serial in its EDID, which survive restarts and a change of port.
/// A monitor without a serial is known by its connection instead, and one whose EDID cannot be read by its connection alone.
/// On Linux every desktop names it as GNOME's display config does, so a snap survives a change of desktop.
/// </summary>
public static class MonitorIds
{
    private const int EdidLength = 128;
    private const int FirstDescriptor = 54;
    private const int DescriptorLength = 18;
    private const int Descriptors = 4;
    private const byte SerialTag = 0xFF;
    private const byte NameTag = 0xFC;

    private static readonly byte[] Header = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    /// <summary>
    /// The id of a monitor from its EDID: its maker and product code, then its serial, as "DEL-A1B2-7XQ9K13"; without a
    /// serial, its <paramref name="connection"/> in its place, as "DEL-A1B2@UID4357"; and <paramref name="connection"/>
    /// alone when the EDID cannot be read.
    /// </summary>
    public static string Of(ReadOnlySpan<byte> edid, string connection)
    {
        if (edid.Length < EdidLength || !edid[..Header.Length].SequenceEqual(Header))
        {
            return connection;
        }

        var model = string.Create(CultureInfo.InvariantCulture, $"{Maker(edid)}-{BinaryPrimitives.ReadUInt16LittleEndian(edid[10..]):X4}");
        return Serial(edid) is { } serial ? $"{model}-{serial}" : $"{model}@{connection}";
    }

    /// <summary>
    /// The id GNOME's snaps know a monitor by, from the maker, product and serial Mutter's display config names it by:
    /// "DEL-DELL U2720Q-7XQ9K13"; without a serial, its <paramref name="connector"/> in its place, as "DEL-DELL U2720Q@DP-1".
    /// </summary>
    public static string OfGnome(string vendor, string product, string serial, string connector) =>
        serial.Length > 0 ? $"{vendor}-{product}-{serial}" : $"{vendor}-{product}@{connector}";

    /// <summary>
    /// GNOME's id of a monitor from its EDID, read as Mutter reads it, so another desktop names a monitor as GNOME does:
    /// the maker's three letters, the product's name, else its code as "0xa1b2", and the serial's text, else its number as
    /// "0x00003039"; null when the EDID cannot be read.
    /// </summary>
    public static string? OfGnome(ReadOnlySpan<byte> edid, string connector)
    {
        if (edid.Length < EdidLength || !edid[..Header.Length].SequenceEqual(Header))
        {
            return null;
        }

        var product = MutterText(DescriptorText(edid, NameTag));
        var serial = MutterText(DescriptorText(edid, SerialTag));
        return OfGnome(
            Maker(edid),
            product.Length > 0 ? product : string.Create(CultureInfo.InvariantCulture, $"0x{BinaryPrimitives.ReadUInt16LittleEndian(edid[10..]):x4}"),
            serial.Length > 0 ? serial : string.Create(CultureInfo.InvariantCulture, $"0x{BinaryPrimitives.ReadUInt32LittleEndian(edid[12..]):x8}"),
            connector);
    }

    /// <summary>
    /// The ids of the monitors in Windows' order, from each one's <see cref="Of"/> and connection: a monitor whose id an
    /// earlier one already has, as two of one model that report the same serial, adds its connection to it.
    /// </summary>
    public static IReadOnlyList<string> Unique(IReadOnlyList<(string Id, string Connection)> monitors)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        return [.. monitors.Select(monitor => taken.Add(monitor.Id) ? monitor.Id : $"{monitor.Id}@{monitor.Connection}")];
    }

    // Three letters of five bits each, 1 being A, in a big-endian word.
    private static string Maker(ReadOnlySpan<byte> edid)
    {
        var word = BinaryPrimitives.ReadUInt16BigEndian(edid[8..]);
        return new string([Letter(word >> 10), Letter(word >> 5), Letter(word)]);
    }

    private static char Letter(int bits) => (char)('@' + (bits & 0x1F));

    // The serial's own descriptor, in text, is what makers fill in; the number in the header is often zero or the same on every unit.
    private static string? Serial(ReadOnlySpan<byte> edid)
    {
        if (Text(DescriptorText(edid, SerialTag)) is { Length: > 0 } text)
        {
            return text;
        }

        var number = BinaryPrimitives.ReadUInt32LittleEndian(edid[12..]);
        return number == 0 ? null : number.ToString(CultureInfo.InvariantCulture);
    }

    private static ReadOnlySpan<byte> DescriptorText(ReadOnlySpan<byte> edid, byte tag)
    {
        for (var index = 0; index < Descriptors; index++)
        {
            var descriptor = edid.Slice(FirstDescriptor + (index * DescriptorLength), DescriptorLength);
            if (descriptor[0] == 0 && descriptor[1] == 0 && descriptor[3] == tag)
            {
                return descriptor[5..];
            }
        }

        return [];
    }

    // As Mutter keeps a descriptor's text: up to its line feed, its inner spaces kept and its padding dropped.
    private static string MutterText(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0x0A) is var feed and >= 0 ? feed : field.Length;
        return Encoding.ASCII.GetString(field[..end]).TrimEnd(' ');
    }

    // Up to 13 characters, ended by a line feed and padded with spaces; anything not printable is left out.
    private static string Text(ReadOnlySpan<byte> field)
    {
        var text = new StringBuilder(field.Length);
        foreach (var letter in field)
        {
            if (letter == 0x0A)
            {
                break;
            }

            if (letter is >= 0x21 and <= 0x7E)
            {
                text.Append((char)letter);
            }
        }

        return text.ToString();
    }
}
