using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Thermi.Core.Tray;
using Thermi.Logging;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Tray;

internal delegate bool NotifyShell(NOTIFY_ICON_MESSAGE message, in NOTIFYICONDATAW data);

/// <summary>
/// The tray icons in the notification area, owned by the host window, each known by its id from Core. An icon is drawn
/// again only when its text, its color or the icon size changes, and its old handle destroyed then. An icon Windows does
/// not take, as at sign-in before the taskbar is ready, is tried again every 5 s and logged once; after Explorer restarts,
/// every icon is added again.
/// </summary>
internal sealed class TrayIcons(HWND hwnd, NotifyShell shell, TimeProvider time, ILogger log) : IDisposable
{
    /// <summary>Each icon tells its clicks with a message of its own, so a click is never taken for another icon's.</summary>
    public const uint FirstMessage = PInvoke.WM_APP + 0x100;

    public const int MaxIcons = 0x100;

    // Refreshes in a row whose change of an icon failed before it counts as gone.
    private const int GivesUpAfter = 3;

    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    private readonly Dictionary<Guid, Slot> _slots = [];
    private TrayRefresh _refresh = new();
    private int _size;

    /// <summary>The size of a tray icon at a DPI, which for the notification area is the primary monitor's.</summary>
    public static int SizeAt(uint dpi) => PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi);

    public static bool IsCallback(uint message) => message is >= FirstMessage and < FirstMessage + MaxIcons;

    /// <summary>The icon a callback message is about, or null for one that is gone.</summary>
    public TrayIcon? Of(uint message) => _slots.Values.FirstOrDefault(slot => slot.Message == message)?.Icon;

    /// <summary>Shows these icons and no others, at this size, and tries again those Windows has not taken.</summary>
    public void Refresh(IReadOnlyList<TrayIcon> icons, int size)
    {
        // A new refresh draws every icon again, and knows nothing of those shown before, so it leaves out none of them.
        if (size != _size)
        {
            _size = size;
            _refresh = new TrayRefresh();
            foreach (var id in _slots.Keys.Except(icons.Select(icon => icon.Id)).ToList())
            {
                Remove(id);
            }
        }

        var update = _refresh.Next(icons);
        foreach (var id in update.Removed)
        {
            Remove(id);
        }

        foreach (var icon in update.Redrawn)
        {
            Redraw(icon);
        }

        foreach (var icon in update.Retipped)
        {
            Retip(icon);
        }

        foreach (var slot in _slots.Values.Where(slot => slot.Added && slot.Pending != default).ToList())
        {
            Modify(slot, slot.Pending);
        }

        foreach (var slot in _slots.Values.Where(slot => !slot.Added && (slot.Tried is not { } tried || time.GetElapsedTime(tried) >= Retry)))
        {
            Add(slot);
        }
    }

    /// <summary>Explorer made a new notification area, which holds none of the icons: each is added again.</summary>
    public void Restore()
    {
        foreach (var slot in _slots.Values)
        {
            slot.Added = false;
            Add(slot);
        }
    }

    /// <summary>
    /// Shows a Windows notice from an icon Windows holds, whose click comes back as that icon's
    /// <see cref="PInvoke.NIN_BALLOONUSERCLICK"/>; without the icon there is nowhere to show it, and it is dropped.
    /// </summary>
    public void Notify(Guid id, string title, string text)
    {
        if (!_slots.TryGetValue(id, out var slot) || !slot.Added)
        {
            return;
        }

        var data = Data(slot, NOTIFY_ICON_DATA_FLAGS.NIF_INFO);
        data.dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_INFO;
        Copy(title, data.szInfoTitle.AsSpan());
        Copy(text, data.szInfo.AsSpan());
        shell(NOTIFY_ICON_MESSAGE.NIM_MODIFY, data);
    }

    public void Dispose()
    {
        foreach (var id in _slots.Keys.ToList())
        {
            Remove(id);
        }
    }

    private void Redraw(TrayIcon icon)
    {
        if (!_slots.TryGetValue(icon.Id, out var slot))
        {
            if (FreeMessage() is not { } message)
            {
                return;
            }

            slot = new Slot(message);
            _slots[icon.Id] = slot;
        }

        var old = slot.Handle;
        slot.Icon = icon;
        slot.Handle = TrayPainter.HandleOf(icon, _size);
        Modify(slot, NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        if (!old.IsNull)
        {
            PInvoke.DestroyIcon(old);
        }
    }

    private void Retip(TrayIcon icon)
    {
        if (_slots.TryGetValue(icon.Id, out var slot))
        {
            slot.Icon = icon;
            Modify(slot, NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        }
    }

    // A change that fails is tried again at the next refresh: Explorer, busy, lets the call time out while it keeps the
    // icon, so taking the icon off and adding it anew would only make it blink and lose its place. Only a change that
    // keeps failing means Windows no longer knows the icon, which is then added again.
    private void Modify(Slot slot, NOTIFY_ICON_DATA_FLAGS flags)
    {
        if (!slot.Added)
        {
            return;
        }

        // A new tooltip brings Windows' own back over a menu that shows, so it waits for the next refresh after the menu.
        if (Topmost.MenuShowing)
        {
            slot.Pending |= flags;
            return;
        }

        if (shell(NOTIFY_ICON_MESSAGE.NIM_MODIFY, Data(slot, flags | slot.Pending)))
        {
            (slot.Pending, slot.Failed) = (default, 0);
            return;
        }

        slot.Pending |= flags;
        if (++slot.Failed >= GivesUpAfter)
        {
            (slot.Added, slot.Tried, slot.Pending, slot.Failed) = (false, null, default, 0);
        }
    }

    private void Remove(Guid id)
    {
        if (!_slots.Remove(id, out var slot))
        {
            return;
        }

        if (slot.Added)
        {
            shell(NOTIFY_ICON_MESSAGE.NIM_DELETE, Data(slot, default));
        }

        PInvoke.DestroyIcon(slot.Handle);
    }

    // An icon left with the same id, as after a crash, would make the add fail: it goes first.
    private void Add(Slot slot)
    {
        slot.Tried = time.GetTimestamp();
        shell(NOTIFY_ICON_MESSAGE.NIM_DELETE, Data(slot, default));
        var flags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE
            | NOTIFY_ICON_DATA_FLAGS.NIF_ICON
            | NOTIFY_ICON_DATA_FLAGS.NIF_TIP
            | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        // An add that timed out may have taken the icon all the same, which a change then finds.
        if (shell(NOTIFY_ICON_MESSAGE.NIM_ADD, Data(slot, flags)) || shell(NOTIFY_ICON_MESSAGE.NIM_MODIFY, Data(slot, flags)))
        {
            var version = Data(slot, default);
            version.uVersion = PInvoke.NOTIFYICON_VERSION_4;
            shell(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, version);
            slot.Added = true;
            slot.Logged = false;
        }
        else if (!slot.Logged)
        {
            log.TrayIconRefused(slot.Icon!.ItemId ?? "thermi", (int)Retry.TotalSeconds);
            slot.Logged = true;
        }
    }

    private uint? FreeMessage()
    {
        var used = _slots.Values.Select(slot => slot.Message).ToHashSet();
        for (var message = FirstMessage; message < FirstMessage + MaxIcons; message++)
        {
            if (!used.Contains(message))
            {
                return message;
            }
        }

        return null;
    }

    private NOTIFYICONDATAW Data(Slot slot, NOTIFY_ICON_DATA_FLAGS flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = hwnd,
            // Every call says the tooltip again and keeps Windows' own on: a call without NIF_SHOWTIP, as a notice's, turns
            // it off for an icon of version 4.
            uFlags = flags | NOTIFY_ICON_DATA_FLAGS.NIF_GUID | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP,
            uCallbackMessage = slot.Message,
            hIcon = slot.Handle,
            guidItem = slot.Icon!.Id,
        };
        Copy(slot.Icon.Tooltip, data.szTip.AsSpan());
        return data;
    }

    // Windows reads each text up to its zero, so one that would fill its field is cut a character short.
    private static void Copy(string text, Span<char> field) => text.AsSpan(0, Math.Min(text.Length, field.Length - 1)).CopyTo(field);

    private sealed class Slot(uint message)
    {
        public uint Message { get; } = message;

        public TrayIcon? Icon { get; set; }

        public HICON Handle { get; set; }

        public bool Added { get; set; }

        public long? Tried { get; set; }

        public bool Logged { get; set; }

        /// <summary>What a change that failed was to change, tried again at the next refresh.</summary>
        public NOTIFY_ICON_DATA_FLAGS Pending { get; set; }

        public int Failed { get; set; }
    }
}
