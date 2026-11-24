using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Thermi.Core.Tray;
using Thermi.Logging;
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

    // An icon Windows no longer knows is added again on this refresh.
    private void Modify(Slot slot, NOTIFY_ICON_DATA_FLAGS flags)
    {
        if (slot.Added && !shell(NOTIFY_ICON_MESSAGE.NIM_MODIFY, Data(slot, flags)))
        {
            slot.Added = false;
            slot.Tried = null;
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
        if (shell(NOTIFY_ICON_MESSAGE.NIM_ADD, Data(slot, NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP)))
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
            uFlags = flags | NOTIFY_ICON_DATA_FLAGS.NIF_GUID,
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
    }
}
