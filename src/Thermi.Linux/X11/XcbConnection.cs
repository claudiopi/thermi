using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Thermi.Linux.Desktop;
using Thermi.Linux.Logging;

namespace Thermi.Linux.X11;

/// <summary>
/// A connection of thermi's own to the X server, besides GDK's, which GLib's main loop reads: each event is handed to
/// <see cref="Event"/> on the main loop's thread, as a pointer to xcb's copy, freed once it returns. Its own connection lets
/// thermi ask the server for what GDK 4 does not, as passive grabs and the root window's properties, without GDK seeing
/// events it never asked for.
/// </summary>
internal sealed partial class XcbConnection : IDisposable
{
    private const string Xcb = "libxcb.so.1";

    // xcb_change_window_attributes' value of the window's event mask.
    private const uint EventMask = 0x800;

    private readonly GLib.IOChannel _channel;
    private readonly uint _watch;
    private readonly ILogger _log;
    private readonly string _what;

    /// <summary>A connection whose events are <paramref name="what"/>, as a failure to handle one is logged.</summary>
    public XcbConnection(string what, ILogger log)
    {
        (_what, _log) = (what, log);
        Handle = Connect(null, out var screen);
        if (HasError(Handle) != 0)
        {
            Disconnect(Handle);
            throw new InvalidOperationException($"No X server took thermi's connection for {what}.");
        }

        Root = RootOf(screen);
        _channel = GLib.IOChannel.UnixNew(FileDescriptor(Handle));
        _watch = GLib.Functions.IoAddWatch(_channel, 0, GLib.IOCondition.In, (_, _, _) =>
        {
            Events();
            return true;
        });
    }

    public nint Handle { get; }

    public uint Root { get; }

    /// <summary>xcb's <c>xcb_setup_t</c>, the server's setup, which the connection owns.</summary>
    public nint Setup => GetSetup(Handle);

    /// <summary>Each event the server sends, as xcb's <c>xcb_generic_event_t</c>.</summary>
    public Action<nint> Event { get; set; } = _ => { };

    /// <summary>Sends what was asked, and reads the events the replies waited for, which the socket no longer tells.</summary>
    public void Flush()
    {
        _ = FlushRequests(Handle);
        Events();
    }

    /// <summary>The atom of <paramref name="name"/>, or 0 where the server answers none.</summary>
    public uint Atom(string name)
    {
        var reply = InternAtomReply(Handle, InternAtom(Handle, 0, (ushort)name.Length, name), 0);
        if (reply == 0)
        {
            return 0;
        }

        // The atom, at its place in xcb_intern_atom_reply_t.
        var atom = (uint)Marshal.ReadInt32(reply, 8);
        Marshal.FreeHGlobal(reply);
        return atom;
    }

    /// <summary>Sets the events <paramref name="window"/> tells this connection, as X11's event masks.</summary>
    public void Listen(uint window, uint events)
    {
        _ = ChangeAttributes(Handle, window, EventMask, [events]);
    }

    public void Dispose()
    {
        GLib.Functions.SourceRemove(_watch);
        _channel.Dispose();
        Disconnect(Handle);
    }

    // xcb's replies and events are malloc's, which FreeHGlobal frees on Linux.
    private void Events()
    {
        for (var each = PollForEvent(Handle); each != 0; each = PollForEvent(Handle))
        {
            var heard = each;
            // GLib calls the watch from native code, which one failed event would end.
            Callbacks.Safely(() => Event(heard), ex => _log.SignalFailed(_what, ex));
            Marshal.FreeHGlobal(each);
        }
    }

    private uint RootOf(int screen)
    {
        var roots = RootsIterator(GetSetup(Handle));
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

    [LibraryImport(Xcb, EntryPoint = "xcb_intern_atom", StringMarshalling = StringMarshalling.Utf8)]
    private static partial uint InternAtom(nint connection, byte onlyIfExists, ushort length, string name);

    [LibraryImport(Xcb, EntryPoint = "xcb_intern_atom_reply")]
    private static partial nint InternAtomReply(nint connection, uint cookie, nint error);

    [LibraryImport(Xcb, EntryPoint = "xcb_change_window_attributes")]
    private static partial uint ChangeAttributes(nint connection, uint window, uint mask, ReadOnlySpan<uint> values);

    [LibraryImport(Xcb, EntryPoint = "xcb_flush")]
    private static partial int FlushRequests(nint connection);

    [LibraryImport(Xcb, EntryPoint = "xcb_poll_for_event")]
    private static partial nint PollForEvent(nint connection);
}
