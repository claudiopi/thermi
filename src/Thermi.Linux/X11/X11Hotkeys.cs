using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Thermi.Core;
using Thermi.Linux.Hotkeys;

namespace Thermi.Linux.X11;

/// <summary>
/// thermi's hotkeys on X11, as a passive grab of each combination on the root window, through a connection to the X server
/// of their own (<see cref="XcbConnection"/>): the X server refuses a combination another program grabbed first. Each is
/// grabbed with Caps Lock and Num Lock on and off as well, as the person's locks should not matter, nor a mouse button held
/// as the keys go down; a key the keyboard repeats while held counts once. A new keyboard layout moves the keys, so each
/// combination is taken again where it went, as any take is.
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

    // The state's keys and their modifiers, below the mouse buttons' bits.
    private const ushort Keys = 0xFF;
    private const byte KeyPress = 2;
    private const byte KeyRelease = 3;
    private const byte MappingNotify = 34;

    // MappingNotify's request of a change to the modifiers or the keyboard, not the pointer's buttons.
    private const byte MappingPointer = 2;
    private const byte Async = 1;

    private static readonly ushort[] Locks = [0, Lock, Mod2, Lock | Mod2];

    private readonly XcbConnection _x;
    private List<(byte Code, ushort Modifiers)?> _taken = [];
    private (byte Code, uint Time)? _released;

    public X11Hotkeys(ILogger log)
    {
        _x = new XcbConnection("an X11 key press", log) { Event = Heard };
    }

    public Action<int> Pressed { get; set; } = _ => { };

    public Action Moved { get; set; } = () => { };

    public Task<IReadOnlyList<bool>> Take(IReadOnlyList<Hotkey?> hotkeys)
    {
        foreach (var (code, modifiers) in _taken.OfType<(byte, ushort)>())
        {
            Array.ForEach(Locks, locks => _ = UngrabKey(_x.Handle, code, _x.Root, (ushort)(modifiers | locks)));
        }

        var codes = Keycodes();
        _taken = [.. hotkeys.Select(hotkey => hotkey is { } wanted && codes.TryGetValue(KeyNames.Keysym(wanted.Key), out var code)
            ? Grab(code, ModifiersOf(wanted.Modifiers))
            : null)];
        _x.Flush();
        return Task.FromResult<IReadOnlyList<bool>>([.. hotkeys.Select((hotkey, at) => hotkey is null || _taken[at] is not null)]);
    }

    public void Dispose() => _x.Dispose();

    private static ushort ModifiersOf(KeyModifiers modifiers) => (ushort)(
        (modifiers.HasFlag(KeyModifiers.Shift) ? Shift : 0) | (modifiers.HasFlag(KeyModifiers.Ctrl) ? Control : 0)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? Mod1 : 0) | (modifiers.HasFlag(KeyModifiers.Win) ? Mod4 : 0));

    // Every lock's variant, or none: one the server refuses lets go of those it took. A grab with no button names none, so
    // it holds with any button down.
    private (byte, ushort)? Grab(byte code, ushort modifiers)
    {
        var taken = new List<ushort>();
        foreach (var locks in Locks)
        {
            var error = RequestCheck(_x.Handle, GrabKey(_x.Handle, 1, _x.Root, (ushort)(modifiers | locks), code, Async, Async));
            if (error != 0)
            {
                Marshal.FreeHGlobal(error);
                taken.ForEach(each => _ = UngrabKey(_x.Handle, code, _x.Root, each));
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
        var setup = _x.Setup;
        var (first, last) = (Marshal.ReadByte(setup, 34), Marshal.ReadByte(setup, 35));
        var reply = KeyboardMappingReply(_x.Handle, KeyboardMapping(_x.Handle, first, (byte)(last - first + 1)), 0);
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

    private void Heard(nint each)
    {
        var kind = (byte)(Marshal.ReadByte(each) & 0x7F);
        if (kind == MappingNotify)
        {
            // The request it tells, at its place in xcb_mapping_notify_event_t. They are taken again as any take is, so
            // what the window says of them follows.
            if (Marshal.ReadByte(each, 4) != MappingPointer)
            {
                Moved();
            }

            return;
        }

        var (code, time) = (Marshal.ReadByte(each, 1), (uint)Marshal.ReadInt32(each, 4));
        var modifiers = (ushort)(Marshal.ReadInt16(each, 28) & Keys & ~(Lock | Mod2));
        if (kind == KeyRelease)
        {
            _released = (code, time);
        }
        else if (kind == KeyPress && _released != (code, time) && _taken.IndexOf((code, modifiers)) is >= 0 and var index)
        {
            Pressed(index);
        }
    }

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
}
