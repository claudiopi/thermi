using System.Buffers.Binary;
using System.Text;

namespace Thermi.Core.Messages;

/// <summary>How a value or an overlay's edge was reached on the desktop.</summary>
public enum SelectHow
{
    Click,
    DoubleClick,
    PointIn,
    PointOut,
}

/// <summary>The desktop gestures, each of which the window takes as a step of its own history.</summary>
public enum GestureKind
{
    Move,
    Resize,
    Nudge,
    DragOff,
    Merge,
}

/// <summary>
/// What the overlay and the window tell each other beyond the settings file. An overlay is counted from 1; an item is
/// named by its id, which the receiver ignores when it does not know it.
/// </summary>
public abstract record WindowMessage;

/// <summary>The window, as it opens, asks the overlay for the last ten minutes.</summary>
public sealed record HistoryAsked : WindowMessage;

/// <summary>An item's ten minutes: the time of its newest slot on the monotonic clock, and one slot a second, NaN for a gap.</summary>
public sealed record ItemHistory(string ItemId, long Newest, IReadOnlyList<float> Slots);

public sealed record History(IReadOnlyList<ItemHistory> Items) : WindowMessage;

/// <summary>A value, or the overlay's edge when no item is named, reached on the desktop.</summary>
public sealed record Select(int Overlay, string? ItemId, SelectHow How) : WindowMessage;

/// <summary>The overlay being edited, the item selected and the item pointed at, each or none.</summary>
public sealed record Edited(int? Overlay, string? Selected, string? Pointed) : WindowMessage;

/// <summary>A desktop gesture, with the file as the overlay read it and as it wrote it.</summary>
public sealed record Gesture(GestureKind Kind, string Before, string After) : WindowMessage;

/// <summary>A second thermi asks the open window to come to the front, on an overlay or 0 for its own, and an item to select.</summary>
public sealed record Raise(int Overlay, string? ItemId) : WindowMessage;

/// <summary>
/// Encodes and decodes every message's payload, a version byte first and under a size cap; a payload that fails decodes
/// to null. Each kind has the name its message is registered by, so the receiver tells the kinds apart before it decodes.
/// </summary>
public static class WindowMessages
{
    /// <summary>The most a payload holds: ten minutes of 20 items are 48 KB, and two settings files a few KB each.</summary>
    public const int MaxLength = 1 << 20;

    private const byte Version = 1;

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The name a kind's message is registered by, the same in both processes.</summary>
    public static string NameOf(WindowMessage message) => message switch
    {
        HistoryAsked or History => "thermi-history",
        Select => "thermi-select",
        Edited => "thermi-edited",
        Gesture => "thermi-gesture",
        Raise => "thermi-raise",
        _ => throw new ArgumentException("Not a message thermi sends.", nameof(message)),
    };

    /// <summary>Every name a message is registered by.</summary>
    public static IReadOnlyList<string> Names { get; } = ["thermi-history", "thermi-select", "thermi-edited", "thermi-gesture", "thermi-raise"];

    public static byte[] Encode(WindowMessage message)
    {
        var writer = new Writer();
        writer.Byte(Version);
        writer.Byte(KindOf(message));
        switch (message)
        {
            case History history:
                writer.Int(history.Items.Count);
                foreach (var item in history.Items)
                {
                    writer.Text(item.ItemId);
                    writer.Long(item.Newest);
                    writer.Floats(item.Slots);
                }

                break;
            case Select select:
                writer.Int(select.Overlay);
                writer.Text(select.ItemId);
                writer.Byte((byte)select.How);
                break;
            case Edited edited:
                writer.Int(edited.Overlay ?? -1);
                writer.Text(edited.Selected);
                writer.Text(edited.Pointed);
                break;
            case Gesture gesture:
                writer.Byte((byte)gesture.Kind);
                writer.Text(gesture.Before);
                writer.Text(gesture.After);
                break;
            case Raise raise:
                writer.Int(raise.Overlay);
                writer.Text(raise.ItemId);
                break;
            default:
                break;
        }

        return writer.ToArray();
    }

    public static WindowMessage? Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxLength || payload.Length < 2 || payload[0] != Version)
        {
            return null;
        }

        try
        {
            var reader = new Reader(payload[2..]);
            var message = Read(payload[1], ref reader);
            return reader.Done ? message : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static byte KindOf(WindowMessage message) => message switch
    {
        HistoryAsked => 0,
        History => 1,
        Select => 2,
        Edited => 3,
        Gesture => 4,
        Raise => 5,
        _ => throw new ArgumentException("Not a message thermi sends.", nameof(message)),
    };

    private static WindowMessage? Read(byte kind, ref Reader reader) => kind switch
    {
        0 => new HistoryAsked(),
        1 => ReadHistory(ref reader),
        2 => new Select(reader.Int(), reader.Text(), reader.Enum<SelectHow>()),
        3 => new Edited(reader.Int() is var overlay and >= 0 ? overlay : null, reader.Text(), reader.Text()),
        4 => new Gesture(reader.Enum<GestureKind>(), reader.Text() ?? throw new InvalidDataException(), reader.Text() ?? throw new InvalidDataException()),
        5 => new Raise(reader.Int(), reader.Text()),
        _ => null,
    };

    private static History ReadHistory(ref Reader reader)
    {
        var count = reader.Int();
        var items = new List<ItemHistory>();
        for (var at = 0; at < count; at++)
        {
            items.Add(new ItemHistory(reader.Text() ?? throw new InvalidDataException(), reader.Long(), reader.Floats()));
        }

        return new History(items);
    }

    private sealed class Writer
    {
        private readonly List<byte> _bytes = [];

        public void Byte(byte value) => _bytes.Add(value);

        public void Int(int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            _bytes.AddRange(bytes);
        }

        public void Long(long value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
            _bytes.AddRange(bytes);
        }

        // A length of -1 is no text.
        public void Text(string? text)
        {
            if (text is null)
            {
                Int(-1);
                return;
            }

            var bytes = Strict.GetBytes(text);
            Int(bytes.Length);
            _bytes.AddRange(bytes);
        }

        public void Floats(IReadOnlyList<float> values)
        {
            Int(values.Count);
            Span<byte> bytes = stackalloc byte[sizeof(float)];
            foreach (var value in values)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
                _bytes.AddRange(bytes);
            }
        }

        public byte[] ToArray() => [.. _bytes];
    }

    // Every read past the end, or of a length that cannot be, is invalid data.
    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _bytes = bytes;

        public readonly bool Done => _bytes.IsEmpty;

        public int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(sizeof(int)));

        public long Long() => BinaryPrimitives.ReadInt64LittleEndian(Take(sizeof(long)));

        public T Enum<T>()
            where T : struct, Enum
        {
            var value = (T)System.Enum.ToObject(typeof(T), Take(1)[0]);
            return System.Enum.IsDefined(value) ? value : throw new InvalidDataException();
        }

        public string? Text()
        {
            var length = Int();
            return length == -1 ? null : Strict.GetString(Take(length));
        }

        public float[] Floats()
        {
            var count = Int();
            var values = new float[count < 0 || count > _bytes.Length / sizeof(float) ? throw new InvalidDataException() : count];
            for (var at = 0; at < count; at++)
            {
                values[at] = BinaryPrimitives.ReadSingleLittleEndian(Take(sizeof(float)));
            }

            return values;
        }

        private ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || length > _bytes.Length)
            {
                throw new InvalidDataException();
            }

            var taken = _bytes[..length];
            _bytes = _bytes[length..];
            return taken;
        }
    }
}
