using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Thermi.Sensors.MacOS.Native;

/// <summary>
/// The System Management Controller, through the user client of IOKit's <c>AppleSMC</c>, which any user may open to read:
/// each key, four characters, as <c>Tp01</c>, holds a value of a type of its own, which the SMC tells first. A read sends
/// an 80-byte <c>SMCKeyData_t</c>, as Stats and macmon send it, and a key the SMC lacks, or a read that fails, is null.
/// A Mac with no SMC, as a virtual machine, has no keys.
/// </summary>
internal sealed partial class Smc : IDisposable
{
    private const string Library = "/System/Library/Frameworks/IOKit.framework/IOKit";

    // The struct's size, and where its fields are: the key, its info (size and type), the SMC's result, the command and
    // its argument, and the value's bytes.
    private const int Size = 80;
    private const int KeyAt = 0;
    private const int DataSizeAt = 28;
    private const int DataTypeAt = 32;
    private const int ResultAt = 40;
    private const int CommandAt = 42;
    private const int IndexAt = 44;
    private const int BytesAt = 48;

    // The user client's one method, and its commands: read a value, a key by its index, and a key's info.
    private const uint Method = 2;
    private const byte ReadBytes = 5;
    private const byte ReadIndex = 8;
    private const byte ReadInfo = 9;

    private readonly uint _connection;
    private readonly Dictionary<string, (uint Size, string Type)?> _info = new(StringComparer.Ordinal);

    public Smc()
    {
        var service = IOKitLibrary.Service("AppleSMC");
        if (service == 0)
        {
            return;
        }

        if (IOServiceOpen(service, SystemLibrary.OwnTask, 0, out var connection) == 0)
        {
            _connection = connection;
        }

        IOKitLibrary.Release(service);
    }

    /// <summary>Every key the SMC holds, in its order, as it counts them under <c>#KEY</c>.</summary>
    public IReadOnlyList<string> Keys()
    {
        if (_connection == 0 || Read("#KEY") is not { } count)
        {
            return [];
        }

        var keys = new List<string>();
        Span<byte> input = stackalloc byte[Size];
        for (var index = 0u; index < (uint)count; index++)
        {
            input.Clear();
            input[CommandAt] = ReadIndex;
            BinaryPrimitives.WriteUInt32LittleEndian(input[IndexAt..], index);
            if (Call(input) is { } output)
            {
                keys.Add(KeyText(BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(KeyAt))));
            }
        }

        return keys;
    }

    /// <summary>A key's value as a number, or null when the SMC lacks the key, the read fails, or its type is no number.</summary>
    public double? Read(string key)
    {
        if (_connection == 0 || key.Length != 4 || Info(key) is not var (size, type))
        {
            return null;
        }

        Span<byte> input = stackalloc byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(input[KeyAt..], KeyNumber(key));
        BinaryPrimitives.WriteUInt32LittleEndian(input[DataSizeAt..], size);
        input[CommandAt] = ReadBytes;
        return Call(input) is { } output ? Decode(type, output.AsSpan(BytesAt, (int)Math.Min(size, 32))) : null;
    }

    public void Dispose()
    {
        if (_connection != 0)
        {
            _ = IOServiceClose(_connection);
        }
    }

    /// <summary>A value's bytes as the number its type says, or null for a type that is no number.</summary>
    internal static double? Decode(string type, ReadOnlySpan<byte> bytes) => type switch
    {
        "flt " when bytes.Length >= 4 => BinaryPrimitives.ReadSingleLittleEndian(bytes),
        "ui8 " or "flag" when bytes.Length >= 1 => bytes[0],
        "ui16" when bytes.Length >= 2 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
        "ui32" when bytes.Length >= 4 => BinaryPrimitives.ReadUInt32BigEndian(bytes),
        "si8 " when bytes.Length >= 1 => (sbyte)bytes[0],
        "si16" when bytes.Length >= 2 => BinaryPrimitives.ReadInt16BigEndian(bytes),
        "sp78" when bytes.Length >= 2 => BinaryPrimitives.ReadInt16BigEndian(bytes) / 256d,
        "fpe2" when bytes.Length >= 2 => BinaryPrimitives.ReadUInt16BigEndian(bytes) / 4d,
        _ => null,
    };

    // The SMC's info of a key is the same for as long as it runs, so it is asked once; a key it lacks is remembered too.
    private (uint Size, string Type)? Info(string key)
    {
        if (_info.TryGetValue(key, out var known))
        {
            return known;
        }

        Span<byte> input = stackalloc byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(input[KeyAt..], KeyNumber(key));
        input[CommandAt] = ReadInfo;
        var info = Call(input) is { } output
            ? (BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(DataSizeAt)), KeyText(BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(DataTypeAt))))
            : ((uint, string)?)null;
        _info[key] = info;
        return info;
    }

    private unsafe byte[]? Call(ReadOnlySpan<byte> input)
    {
        var output = new byte[Size];
        nuint length = Size;
        fixed (byte* sent = input)
        fixed (byte* received = output)
        {
            return IOConnectCallStructMethod(_connection, Method, sent, Size, received, ref length) == 0 && output[ResultAt] == 0
                ? output
                : null;
        }
    }

    // A key's four characters, the first highest, as the SMC numbers it; and back.
    private static uint KeyNumber(string key) => BinaryPrimitives.ReadUInt32BigEndian(Encoding.ASCII.GetBytes(key));

    private static string KeyText(uint number)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, number);
        return Encoding.ASCII.GetString(bytes);
    }

    [LibraryImport(Library)]
    private static partial int IOServiceOpen(uint service, uint task, uint type, out uint connection);

    [LibraryImport(Library)]
    private static partial int IOServiceClose(uint connection);

    [LibraryImport(Library)]
    private static unsafe partial int IOConnectCallStructMethod(
        uint connection, uint selector, byte* input, nuint inputSize, byte* output, ref nuint outputSize);
}
