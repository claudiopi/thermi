using System.Runtime.InteropServices;

namespace Thermi.Sensors.MacOS.Native;

/// <summary>
/// libSystem's few calls the service makes, which .NET does not wrap: Mach's counters of each CPU's time and of the
/// memory's pages, sysctl's values by their names, and launchd's sockets, which <c>launch_activate_socket</c> hands an agent.
/// </summary>
internal static partial class SystemLibrary
{
    public const string Library = "/usr/lib/libSystem.B.dylib";

    // PROCESSOR_CPU_LOAD_INFO, HOST_VM_INFO64 and HOST_VM_INFO64_COUNT, in 32-bit words.
    private const int CpuLoadInfo = 2;
    private const int VmInfo64 = 4;
    private const int VmInfo64Words = 38;

    // CPU_STATE_MAX: user, system, idle and nice.
    private const int CpuStates = 4;

    private static readonly Lazy<uint> Host = new(MachHostSelf);

    private static readonly Lazy<uint> Task = new(() => (uint)Marshal.ReadInt32(NativeLibrary.GetExport(NativeLibrary.Load(Library), "mach_task_self_")));

    /// <summary>This process's Mach task, as <c>mach_task_self()</c> reads it.</summary>
    public static uint OwnTask => Task.Value;

    /// <summary>Each CPU's ticks of user, system, idle and nice time since it started, or null where Mach refused.</summary>
    public static unsafe uint[][]? CpuTicks()
    {
        if (HostProcessorInfo(Host.Value, CpuLoadInfo, out var count, out var info, out var words) != 0)
        {
            return null;
        }

        try
        {
            var ticks = (uint*)info;
            return [.. Enumerable.Range(0, (int)count).Select(cpu => new ReadOnlySpan<uint>(ticks + (cpu * CpuStates), CpuStates).ToArray())];
        }
        finally
        {
            _ = VmDeallocate(OwnTask, (nuint)info, words * sizeof(int));
        }
    }

    /// <summary>The memory's counts of pages, as <c>vm_stat</c> shows them, or null where Mach refused.</summary>
    public static unsafe byte[]? MemoryPages()
    {
        var pages = new byte[VmInfo64Words * sizeof(int)];
        var words = (uint)VmInfo64Words;
        fixed (byte* buffer = pages)
        {
            return HostStatistics64(Host.Value, VmInfo64, buffer, ref words) == 0 ? pages : null;
        }
    }

    /// <summary>What sysctl answers by the name, as bytes, or null where it has none.</summary>
    public static unsafe byte[]? Sysctl(string name)
    {
        nuint length = 0;
        if (SysctlByName(name, null, ref length, IntPtr.Zero, 0) != 0 || length == 0)
        {
            return null;
        }

        var value = new byte[length];
        fixed (byte* buffer = value)
        {
            return SysctlByName(name, buffer, ref length, IntPtr.Zero, 0) == 0 ? value[..(int)length] : null;
        }
    }

    /// <summary>The sockets launchd holds for this agent under <paramref name="name"/>, or null where launchd started it with none.</summary>
    public static unsafe int[]? LaunchdSockets(string name)
    {
        if (LaunchActivateSocket(name, out var sockets, out var count) != 0)
        {
            return null;
        }

        try
        {
            return new ReadOnlySpan<int>((int*)sockets, (int)count).ToArray();
        }
        finally
        {
            Free(sockets);
        }
    }

    [LibraryImport(Library, EntryPoint = "mach_host_self")]
    private static partial uint MachHostSelf();

    [LibraryImport(Library, EntryPoint = "host_processor_info")]
    private static partial int HostProcessorInfo(uint host, int flavor, out uint count, out IntPtr info, out uint words);

    [LibraryImport(Library, EntryPoint = "host_statistics64")]
    private static unsafe partial int HostStatistics64(uint host, int flavor, byte* info, ref uint words);

    [LibraryImport(Library, EntryPoint = "vm_deallocate")]
    private static partial int VmDeallocate(uint task, nuint address, nuint size);

    [LibraryImport(Library, EntryPoint = "sysctlbyname", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial int SysctlByName(string name, byte* value, ref nuint length, IntPtr newValue, nuint newLength);

    [LibraryImport(Library, EntryPoint = "launch_activate_socket", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LaunchActivateSocket(string name, out IntPtr sockets, out nuint count);

    [LibraryImport(Library, EntryPoint = "free")]
    private static partial void Free(IntPtr memory);
}
