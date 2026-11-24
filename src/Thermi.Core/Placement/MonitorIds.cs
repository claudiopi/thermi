using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Thermi.Core.Placement;

/// <summary>
/// How a snap names its monitor: by the maker, model and serial in its EDID, which survive restarts and a change of port.
/// A monitor without a serial is known by its connection instead, and one whose EDID cannot be read by its connection alone.
/// </summary>
public static class MonitorIds
{
    private const int EdidLength = 128;
    private const int FirstDescriptor = 54;
    private const int DescriptorLength = 18;
    private const int Descriptors = 4;
    private const byte SerialTag = 0xFF;

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
        for (var index = 0; index < Descriptors; index++)
        {
            var descriptor = edid.Slice(FirstDescriptor + (index * DescriptorLength), DescriptorLength);
            if (descriptor[0] == 0 && descriptor[1] == 0 && descriptor[3] == SerialTag && Text(descriptor[5..]) is { Length: > 0 } text)
            {
                return text;
            }
        }

        var number = BinaryPrimitives.ReadUInt32LittleEndian(edid[12..]);
        return number == 0 ? null : number.ToString(CultureInfo.InvariantCulture);
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
