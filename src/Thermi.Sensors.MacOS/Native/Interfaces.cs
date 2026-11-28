using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Native;

/// <summary>
/// The network adapters and their counters, as <c>netstat -ib</c> reads them, from sysctl's interface MIB: each adapter's
/// <c>struct ifmibdata</c>, by its index from 1, with its BSD name, its flags, its type and its bytes in and out, as
/// 64-bit counts.
/// </summary>
internal static partial class Interfaces
{
    private const string Library = "/usr/lib/libSystem.B.dylib";

    // CTL_NET, PF_LINK, NETLINK_GENERIC; IFMIB_SYSTEM with IFMIB_IFCOUNT, or IFMIB_IFDATA with an index and IFDATA_GENERAL.
    private const int Net = 4;
    private const int Link = 18;
    private const int Generic = 0;
    private const int SystemMib = 1;
    private const int IfCount = 1;
    private const int Data = 2;
    private const int General = 1;

    // The struct's size and where its fields are: the name, the flags, and in its if_data64 the type and the bytes.
    private const int MibSize = 256;
    private const int NameLength = 16;
    private const int FlagsAt = 20;
    private const int TypeAt = 52;
    private const int ReceivedAt = 116;
    private const int SentAt = 124;
    private const int LoopbackFlag = 0x8;

    // Indexes may have gaps where adapters went, so the search goes past the count, as far as a few left.
    private const int Gaps = 16;

    public static IReadOnlyList<Adapter> Read()
    {
        if (Sysctl([Net, Link, Generic, SystemMib, IfCount], sizeof(int)) is not { Length: sizeof(int) } counted)
        {
            return [];
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(counted);
        var adapters = new List<Adapter>();
        for (var index = 1; adapters.Count < count && index <= count + Gaps; index++)
        {
            if (Sysctl([Net, Link, Generic, Data, index, General], MibSize) is { Length: > SentAt + sizeof(long) } mib)
            {
                adapters.Add(Of(mib));
            }
        }

        return adapters;
    }

    private static Adapter Of(ReadOnlySpan<byte> mib)
    {
        var name = mib[..NameLength];
        var end = name.IndexOf((byte)0);
        return new Adapter(
            Encoding.ASCII.GetString(end < 0 ? name : name[..end]),
            mib[TypeAt],
            (BinaryPrimitives.ReadUInt32LittleEndian(mib[FlagsAt..]) & LoopbackFlag) != 0,
            (long)BinaryPrimitives.ReadUInt64LittleEndian(mib[ReceivedAt..]),
            (long)BinaryPrimitives.ReadUInt64LittleEndian(mib[SentAt..]));
    }

    // What sysctl answers by the numbers of its name, as bytes, or null where it has none.
    private static unsafe byte[]? Sysctl(ReadOnlySpan<int> name, int size)
    {
        var value = new byte[size];
        var length = (nuint)size;
        fixed (int* names = name)
        fixed (byte* buffer = value)
        {
            return SysctlByNumbers(names, (uint)name.Length, buffer, ref length, IntPtr.Zero, 0) == 0 ? value[..(int)length] : null;
        }
    }

    [LibraryImport(Library, EntryPoint = "sysctl")]
    private static unsafe partial int SysctlByNumbers(int* name, uint length, byte* value, ref nuint valueLength, IntPtr newValue, nuint newLength);
}
