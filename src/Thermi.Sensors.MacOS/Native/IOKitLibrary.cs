using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Native;

/// <summary>
/// IOKit's registry, declared by hand as this project is plain .NET: its services by their class, and of each only the
/// properties asked (<see cref="IMac.Registry"/>), each copied alone, as copying all of an accelerator's takes milliseconds.
/// A key <see cref="MacJson.Parent"/> is searched up the service's parents, one <see cref="MacJson.Child"/> down its
/// children, and a path, as <c>Device Characteristics/Product Name</c>, takes one value of a dictionary, so the rest of it,
/// its serial number among it, is never read. Any user may read it.
/// </summary>
internal static partial class IOKitLibrary
{
    private const string Library = "/System/Library/Frameworks/IOKit.framework/IOKit";

    // kIORegistryIterateRecursively and kIORegistryIterateParents, as a search of the registry takes them.
    private const uint Recursively = 1;
    private const uint Parents = 2;

    // kIOMainPortDefault, which IOKit reads as the main port.
    private const uint MainPort = 0;

    public static IReadOnlyList<JsonObject> Registry(string className, IReadOnlyList<string> keys)
    {
        var entries = new List<JsonObject>();
        foreach (var service in Services(className))
        {
            var entry = new JsonObject();
            foreach (var key in keys)
            {
                if (Property(service, key) is { } value)
                {
                    entry[key] = value;
                }
            }

            if (entry.Count > 0)
            {
                entry[MacJson.EntryName] = NameOf(service);
                entries.Add(entry);
            }

            Release(service);
        }

        return entries;
    }

    private static JsonNode? Property(uint service, string key)
    {
        var (options, path) = key.StartsWith(MacJson.Parent, StringComparison.Ordinal) ? (Recursively | Parents, key[MacJson.Parent.Length..])
            : key.StartsWith(MacJson.Child, StringComparison.Ordinal) ? (Recursively, key[MacJson.Child.Length..])
            : (0u, key);
        var names = path.Split('/');
        var property = Search(service, names[0], options);
        try
        {
            var value = property;
            foreach (var name in names.Skip(1))
            {
                value = CoreFoundation.Get(value, name);
            }

            return CoreFoundation.ToJson(value);
        }
        finally
        {
            CoreFoundation.Release(property);
        }
    }

    // Each service of the registry of the class, which its caller releases.
    private static List<uint> Services(string className)
    {
        var services = new List<uint>();
        // The matching dictionary is the call's to release.
        if (IOServiceGetMatchingServices(MainPort, IOServiceMatching(className), out var iterator) != 0)
        {
            return services;
        }

        for (var service = IOIteratorNext(iterator); service != 0; service = IOIteratorNext(iterator))
        {
            services.Add(service);
        }

        Release(iterator);
        return services;
    }

    /// <summary>The first service of <paramref name="className"/>, or zero, which its caller releases.</summary>
    public static uint Service(string className) => IOServiceGetMatchingService(MainPort, IOServiceMatching(className));

    /// <summary>Lets go of a service or an iterator; zero is nothing, and a release that fails leaves nothing to do.</summary>
    public static void Release(uint service)
    {
        if (service != 0)
        {
            _ = IOObjectRelease(service);
        }
    }

    // The service's name in the registry, as pmgr.
    private static unsafe string? NameOf(uint service)
    {
        // io_name_t, 128 characters.
        var name = stackalloc byte[128];
        return IORegistryEntryGetName(service, name) == 0 ? Marshal.PtrToStringUTF8((IntPtr)name) : null;
    }

    // A property of the service, or with options of its parents' or children's, which its caller releases.
    private static IntPtr Search(uint service, string key, uint options)
    {
        var name = CoreFoundation.String(key);
        try
        {
            return options == 0
                ? IORegistryEntryCreateCFProperty(service, name, IntPtr.Zero, 0)
                : IORegistryEntrySearchCFProperty(service, "IOService", name, IntPtr.Zero, options);
        }
        finally
        {
            CoreFoundation.Release(name);
        }
    }

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr IOServiceMatching(string className);

    [LibraryImport(Library)]
    private static partial int IOServiceGetMatchingServices(uint mainPort, IntPtr matching, out uint iterator);

    [LibraryImport(Library)]
    private static partial uint IOServiceGetMatchingService(uint mainPort, IntPtr matching);

    [LibraryImport(Library)]
    private static partial uint IOIteratorNext(uint iterator);

    [LibraryImport(Library)]
    private static partial int IOObjectRelease(uint service);

    [LibraryImport(Library)]
    private static unsafe partial int IORegistryEntryGetName(uint service, byte* name);

    [LibraryImport(Library)]
    private static partial IntPtr IORegistryEntryCreateCFProperty(uint service, IntPtr key, IntPtr allocator, uint options);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr IORegistryEntrySearchCFProperty(uint service, string plane, IntPtr key, IntPtr allocator, uint options);
}
