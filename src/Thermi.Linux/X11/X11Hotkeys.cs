using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Thermi.Core;
using Thermi.Linux.Desktop;
using Thermi.Linux.Hotkeys;
using Thermi.Linux.Logging;

namespace Thermi.Linux.X11;

/// <summary>
/// thermi's hotkeys on X11, as a passive grab of each combination on the root window, through a connection to the X server
/// of their own, which GLib's main loop reads: the X server refuses a combination another program grabbed first. Each is
/// grabbed with Caps Lock and Num Lock on and off as well, as the person's locks should not matter, and a key the
/// keyboard repeats while held counts once.
/// </summary>
internal sealed partial class X11Hotkeys : IHotkeyGrab
{
    private const string Xcb = "libxcb.so.1";

    private const ushort Shift = 1;
    private const ushort Lock = 2;
    private const ushort Control = 4;
    private const ushort Mod1 = 8;
    private const ushort Mod2 = 16;
    private const ushort Mod4 = 64;
    private const byte KeyPress = 2;
    private const byte KeyRelease = 3;
    private const byte Async = 1;

    private static readonly ushort[] Locks = [0, Lock, Mod2, Lock | Mod2];

    private readonly nint _connection;
    private readonly uint _root;
    private readonly GLib.IOChannel _channel;
    private readonly uint _watch;
    private readonly ILogger _log;
    private List<(byte Code, ushort Modifiers)?> _taken = [];
    private (byte Code, uint Time)? _released;

    public X11Hotkeys(ILogger log)
    {
        _log = log;
        _connection = Connect(null, out var screen);
        if (HasError(_connection) != 0)
        {
            Disconnect(_connection);
            throw new InvalidOperationException("No X server took thermi's connection for its hotkeys.");
        }

        _root = RootOf(screen);
        _channel = GLib.IOChannel.UnixNew(FileDescriptor(_connection));
        _watch = GLib.Functions.IoAddWatch(_channel, 0, GLib.IOCondition.In, (_, _, _) =>
        {
            Events();
            return true;
        });
    }

    public Action<int> Pressed { get; set; } = _ => { };

    public Task<IReadOnlyList<bool>> Take(IReadOnlyList<Hotkey?> hotkeys)
    {
        foreach (var (code, modifiers) in _taken.OfType<(byte, ushort)>())
        {
            Array.ForEach(Locks, locks => _ = UngrabKey(_connection, code, _root, (ushort)(modifiers | locks)));
        }

        var codes = Keycodes();
        _taken = [.. hotkeys.Select(hotkey => hotkey is { } wanted && codes.TryGetValue(KeyNames.Keysym(wanted.Key), out var code)
            ? Grab(code, ModifiersOf(wanted.Modifiers))
            : null)];
        _ = Flush(_connection);
        // The replies the grabs waited for may have read events too, which the socket no longer tells.
        Events();
        return Task.FromResult<IReadOnlyList<bool>>([.. hotkeys.Select((hotkey, at) => hotkey is null || _taken[at] is not null)]);
    }

    public void Dispose()
    {
        GLib.Functions.SourceRemove(_watch);
        _channel.Dispose();
        Disconnect(_connection);
    }

    private static ushort ModifiersOf(KeyModifiers modifiers) => (ushort)(
        (modifiers.HasFlag(KeyModifiers.Shift) ? Shift : 0) | (modifiers.HasFlag(KeyModifiers.Ctrl) ? Control : 0)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? Mod1 : 0) | (modifiers.HasFlag(KeyModifiers.Win) ? Mod4 : 0));

    // Every lock's variant, or none: one the server refuses lets go of those it took.
    private (byte, ushort)? Grab(byte code, ushort modifiers)
    {
        var taken = new List<ushort>();
        foreach (var locks in Locks)
        {
            var error = RequestCheck(_connection, GrabKey(_connection, 1, _root, (ushort)(modifiers | locks), code, Async, Async));
            if (error != 0)
            {
                Marshal.FreeHGlobal(error);
                taken.ForEach(each => _ = UngrabKey(_connection, code, _root, each));
                return null;
            }

            taken.Add((ushort)(modifiers | locks));
        }

        return (code, modifiers);
    }

    // The first keycode of the keyboard that gives each keysym, unshifted or shifted.
    private Dictionary<uint, byte> Keycodes()
    {
        // The setup's lowest and highest keycodes, at their places in xcb_setup_t.
        var setup = GetSetup(_connection);
        var (first, last) = (Marshal.ReadByte(setup, 34), Marshal.ReadByte(setup, 35));
        var reply = KeyboardMappingReply(_connection, KeyboardMapping(_connection, first, (byte)(last - first + 1)), 0);
        var codes = new Dictionary<uint, byte>();
        if (reply == 0)
        {
            return codes;
        }

        var perCode = Marshal.ReadByte(reply, 1);
        var keysyms = KeyboardMappingKeysyms(reply);
        var count = KeyboardMappingKeysymsLength(reply);
        for (var at = 0; at < count; at++)
        {
            var keysym = (uint)Marshal.ReadInt32(keysyms, at * 4);
            if (at % perCode < 2 && keysym != 0)
            {
                codes.TryAdd(keysym, (byte)(first + (at / perCode)));
            }
        }

        Marshal.FreeHGlobal(reply);
        return codes;
    }

    // xcb's replies and events are malloc's, which FreeHGlobal frees on Linux.
    private void Events()
    {
        for (var each = PollForEvent(_connection); each != 0; each = PollForEvent(_connection))
        {
            var kind = (byte)(Marshal.ReadByte(each) & 0x7F);
            var (code, time) = (Marshal.ReadByte(each, 1), (uint)Marshal.ReadInt32(each, 4));
            var modifiers = (ushort)(Marshal.ReadInt16(each, 28) & ~(Lock | Mod2));
            Marshal.FreeHGlobal(each);
            if (kind == KeyRelease)
            {
                _released = (code, time);
            }
            else if (kind == KeyPress && _released != (code, time) && _taken.IndexOf((code, modifiers)) is >= 0 and var index)
            {
                // GLib calls the watch from native code, which one failed press would end.
                Callbacks.Safely(() => Pressed(index), ex => _log.SignalFailed("an X11 key press", ex));
            }
        }
    }

    private uint RootOf(int screen)
    {
        var roots = RootsIterator(GetSetup(_connection));
        for (var at = 0; at < screen && roots.Remaining > 1; at++)
        {
            ScreenNext(ref roots);
        }

        return (uint)Marshal.ReadInt32(roots.Data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenIterator
    {
        public nint Data;
        public int Remaining;
        public int Index;
    }

    [LibraryImport(Xcb, EntryPoint = "xcb_connect", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Connect(string? display, out int screen);

    [LibraryImport(Xcb, EntryPoint = "xcb_connection_has_error")]
    private static partial int HasError(nint connection);

    [LibraryImport(Xcb, EntryPoint = "xcb_disconnect")]
    private static partial void Disconnect(nint connection);

    [LibraryImport(Xcb, EntryPoint = "xcb_get_file_descriptor")]
    private static partial int FileDescriptor(nint connection);

    [LibraryImport(Xcb, EntryPoint = "xcb_get_setup")]
    private static partial nint GetSetup(nint connection);

    [LibraryImport(Xcb, EntryPoint = "xcb_setup_roots_iterator")]
    private static partial ScreenIterator RootsIterator(nint setup);

    [LibraryImport(Xcb, EntryPoint = "xcb_screen_next")]
    private static partial void ScreenNext(ref ScreenIterator iterator);

    [LibraryImport(Xcb, EntryPoint = "xcb_get_keyboard_mapping")]
    private static partial uint KeyboardMapping(nint connection, byte first, byte count);

    [LibraryImport(Xcb, EntryPoint = "xcb_get_keyboard_mapping_reply")]
    private static partial nint KeyboardMappingReply(nint connection, uint cookie, nint error);

    [LibraryImport(Xcb, EntryPoint = "xcb_get_keyboard_mapping_keysyms")]
    private static partial nint KeyboardMappingKeysyms(nint reply);

    [LibraryImport(Xcb, EntryPoint = "xcb_get_keyboard_mapping_keysyms_length")]
    private static partial int KeyboardMappingKeysymsLength(nint reply);

    [LibraryImport(Xcb, EntryPoint = "xcb_grab_key_checked")]
    private static partial uint GrabKey(nint connection, byte ownerEvents, uint window, ushort modifiers, byte key, byte pointerMode, byte keyboardMode);

    [LibraryImport(Xcb, EntryPoint = "xcb_ungrab_key")]
    private static partial uint UngrabKey(nint connection, byte key, uint window, ushort modifiers);

    [LibraryImport(Xcb, EntryPoint = "xcb_request_check")]
    private static partial nint RequestCheck(nint connection, uint cookie);

    [LibraryImport(Xcb, EntryPoint = "xcb_flush")]
    private static partial int Flush(nint connection);

    [LibraryImport(Xcb, EntryPoint = "xcb_poll_for_event")]
    private static partial nint PollForEvent(nint connection);
}
