using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Thermi.Sensors.MacOS.Native;

/// <summary>
/// CoreFoundation's few calls the service makes, declared by hand as this project is plain .NET, with none of .NET for
/// macOS's bindings: IOKit, IOReport and the HID sensors answer in its strings, numbers, data, dictionaries and arrays,
/// which <see cref="ToJson"/> turns into JSON, as a recording keeps them.
/// </summary>
internal static partial class CoreFoundation
{
    public const string Library = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint Utf8 = 0x08000100;

    // CFNumberType's kCFNumberSInt32Type, kCFNumberSInt64Type and kCFNumberFloat64Type.
    private const int SInt32 = 3;
    private const int SInt64 = 4;
    private const int Float64 = 6;

    private static readonly Lazy<(IntPtr Keys, IntPtr Values)> Callbacks = new(() =>
    {
        var library = NativeLibrary.Load(Library);
        return (NativeLibrary.GetExport(library, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(library, "kCFTypeDictionaryValueCallBacks"));
    });

    private static readonly Lazy<(nuint String, nuint Dictionary)> TypeIds = new(() => (CFStringGetTypeID(), CFDictionaryGetTypeID()));

    // Each type's JSON, by the type's id, which CoreFoundation tells only once it runs.
    private static readonly Lazy<Dictionary<nuint, Func<IntPtr, JsonNode?>>> Converters = new(() => new()
    {
        [CFStringGetTypeID()] = value => JsonValue.Create(StringOf(value)),
        [CFNumberGetTypeID()] = NumberOf,
        [CFBooleanGetTypeID()] = value => JsonValue.Create(CFBooleanGetValue(value)),
        [CFDataGetTypeID()] = value => JsonValue.Create(Convert.ToBase64String(BytesOf(value))),
        [CFDictionaryGetTypeID()] = DictionaryOf,
        [CFArrayGetTypeID()] = ArrayOf,
    });

    /// <summary>A CFString of <paramref name="text"/>, which its caller releases.</summary>
    public static IntPtr String(string text) => CFStringCreateWithCString(IntPtr.Zero, text, Utf8);

    /// <summary>A CFDictionary of 32-bit numbers by their names, as IOKit's matching takes, which its caller releases.</summary>
    public static IntPtr NumbersByName(IReadOnlyDictionary<string, int> numbers)
    {
        var keys = numbers.Keys.Select(String).ToArray();
        var values = numbers.Values.Select(Number).ToArray();
        try
        {
            return CFDictionaryCreate(IntPtr.Zero, keys, values, keys.Length, Callbacks.Value.Keys, Callbacks.Value.Values);
        }
        finally
        {
            foreach (var each in keys.Concat(values))
            {
                CFRelease(each);
            }
        }
    }

    /// <summary>A CFDictionary's value by <paramref name="key"/>, which the dictionary keeps, or zero, as for what is no dictionary.</summary>
    public static IntPtr Get(IntPtr dictionary, string key)
    {
        if (dictionary == IntPtr.Zero || CFGetTypeID(dictionary) != TypeIds.Value.Dictionary)
        {
            return IntPtr.Zero;
        }

        var name = String(key);
        try
        {
            return CFDictionaryGetValue(dictionary, name);
        }
        finally
        {
            CFRelease(name);
        }
    }

    /// <summary>A CFArray's values, which the array keeps; none for zero.</summary>
    public static IntPtr[] Items(IntPtr array) =>
        array == IntPtr.Zero ? [] : [.. Enumerable.Range(0, (int)CFArrayGetCount(array)).Select(index => CFArrayGetValueAtIndex(array, index))];

    /// <summary>A mutable copy of a dictionary, which its caller releases.</summary>
    public static IntPtr MutableCopy(IntPtr dictionary) => CFDictionaryCreateMutableCopy(IntPtr.Zero, 0, dictionary);

    /// <summary>Lets go of what a Create or Copy call answered; zero is nothing.</summary>
    public static void Release(IntPtr value)
    {
        if (value != IntPtr.Zero)
        {
            CFRelease(value);
        }
    }

    /// <summary>A CFString's text, or null for zero or anything else.</summary>
    public static string? Text(IntPtr value) => value != IntPtr.Zero && CFGetTypeID(value) == TypeIds.Value.String ? StringOf(value) : null;

    /// <summary>
    /// What a CoreFoundation value holds, as JSON: a string, a number, a boolean, data as base64, and dictionaries and
    /// arrays of them; null for zero or another type.
    /// </summary>
    public static JsonNode? ToJson(IntPtr value)
    {
        if (value == IntPtr.Zero)
        {
            return null;
        }

        return Converters.Value.TryGetValue(CFGetTypeID(value), out var convert) ? convert(value) : null;
    }

    private static IntPtr Number(int value) => CFNumberCreate(IntPtr.Zero, SInt32, ref value);

    private static JsonValue NumberOf(IntPtr value)
    {
        if (CFNumberIsFloatType(value))
        {
            CFNumberGetValue(value, Float64, out double real);
            return JsonValue.Create(real);
        }

        CFNumberGetValue(value, SInt64, out long whole);
        return JsonValue.Create(whole);
    }

    private static unsafe string StringOf(IntPtr value)
    {
        var length = CFStringGetLength(value);
        var buffer = new char[length];
        fixed (char* start = buffer)
        {
            CFStringGetCharacters(value, new CFRange(0, length), start);
        }

        return new string(buffer);
    }

    private static byte[] BytesOf(IntPtr value)
    {
        var bytes = new byte[CFDataGetLength(value)];
        Marshal.Copy(CFDataGetBytePtr(value), bytes, 0, bytes.Length);
        return bytes;
    }

    private static JsonObject DictionaryOf(IntPtr value)
    {
        var count = (int)CFDictionaryGetCount(value);
        var keys = new IntPtr[count];
        var values = new IntPtr[count];
        CFDictionaryGetKeysAndValues(value, keys, values);
        var json = new JsonObject();
        foreach (var (key, each) in keys.Zip(values))
        {
            if (Text(key) is { } name)
            {
                json[name] = ToJson(each);
            }
        }

        return json;
    }

    private static JsonArray ArrayOf(IntPtr value) => [.. Items(value).Select(ToJson)];

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CFRange(nint Location, nint Length);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [LibraryImport(Library)]
    private static partial nint CFStringGetLength(IntPtr text);

    [LibraryImport(Library)]
    private static unsafe partial void CFStringGetCharacters(IntPtr text, CFRange range, char* buffer);

    [LibraryImport(Library)]
    private static partial IntPtr CFNumberCreate(IntPtr allocator, int type, ref int value);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFNumberIsFloatType(IntPtr number);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFNumberGetValue(IntPtr number, int type, out double value);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFNumberGetValue(IntPtr number, int type, out long value);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFBooleanGetValue(IntPtr boolean);

    [LibraryImport(Library)]
    private static partial nint CFDataGetLength(IntPtr data);

    [LibraryImport(Library)]
    private static partial IntPtr CFDataGetBytePtr(IntPtr data);

    [LibraryImport(Library)]
    private static partial IntPtr CFDictionaryCreate(
        IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallbacks, IntPtr valueCallbacks);

    [LibraryImport(Library)]
    private static partial IntPtr CFDictionaryCreateMutableCopy(IntPtr allocator, nint capacity, IntPtr dictionary);

    [LibraryImport(Library)]
    private static partial IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    [LibraryImport(Library)]
    private static partial nint CFDictionaryGetCount(IntPtr dictionary);

    [LibraryImport(Library)]
    private static partial void CFDictionaryGetKeysAndValues(IntPtr dictionary, [Out] IntPtr[] keys, [Out] IntPtr[] values);

    [LibraryImport(Library)]
    private static partial nint CFArrayGetCount(IntPtr array);

    [LibraryImport(Library)]
    private static partial IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [LibraryImport(Library)]
    private static partial nuint CFGetTypeID(IntPtr value);

    [LibraryImport(Library)]
    private static partial nuint CFStringGetTypeID();

    [LibraryImport(Library)]
    private static partial nuint CFNumberGetTypeID();

    [LibraryImport(Library)]
    private static partial nuint CFBooleanGetTypeID();

    [LibraryImport(Library)]
    private static partial nuint CFDataGetTypeID();

    [LibraryImport(Library)]
    private static partial nuint CFDictionaryGetTypeID();

    [LibraryImport(Library)]
    private static partial nuint CFArrayGetTypeID();

    [LibraryImport(Library)]
    private static partial void CFRelease(IntPtr value);
}
