using Thermi.Windows.Overlay;
using Windows.Win32.UI.Shell;

namespace Thermi.Windows.Tests;

/// <summary>
/// A notification area that keeps icons as Windows does, so a host under test never reaches the user's: an add of an id
/// it holds fails, a change or a delete of one it lacks fails, and it can refuse every add, as before the taskbar is
/// ready at sign-in, or lose every icon, as when Explorer restarts. Without <paramref name="record"/>, it keeps no calls,
/// so a test that counts the process's memory counts only the host's.
/// </summary>
internal sealed class FakeTray(bool record = true)
{
    /// <summary>The tooltip of an icon <see cref="Leave"/> put there.</summary>
    public const string Leftover = "leftover";

    /// <summary>A path no real thermi runs from, which the icons' ids are tied to.</summary>
    public static string ExePath { get; } = Path.Combine(AppContext.BaseDirectory, "thermi-under-test.exe");

    private readonly Dictionary<Guid, string> _shown = [];

    /// <summary>A call, with the notice it shows as its title, a line break and its text, or empty for none.</summary>
    public sealed record Call(NOTIFY_ICON_MESSAGE Message, Guid Id, NOTIFY_ICON_DATA_FLAGS Flags, string Tip, bool WithIcon, uint Callback, string Notice);

    public List<Call> Calls { get; } = [];

    /// <summary>How many times an icon was changed with a new picture.</summary>
    public int Redrawn { get; private set; }

    /// <summary>The icons it shows, by id, with their tooltips.</summary>
    public IReadOnlyDictionary<Guid, string> Shown => _shown;

    public bool Refusing { get; set; }

    /// <summary>How many changes to come fail without changing anything, as when a busy Explorer lets the call time out.</summary>
    public int Busy { get; set; }

    /// <summary>Every icon it showed is gone, as when Explorer restarts.</summary>
    public void Restart() => _shown.Clear();

    /// <summary>An icon under <paramref name="id"/> that a thermi which ended without removing it left behind.</summary>
    public void Leave(Guid id) => _shown[id] = Leftover;

    public HostEnvironment Environment(string windowMutex, Action<int>? openSettings = null) =>
        new(windowMutex, ExePath, Notify) { OpenSettings = openSettings };

    private bool Notify(NOTIFY_ICON_MESSAGE message, in NOTIFYICONDATAW data)
    {
        var text = data.szTip.ToString();
        if (record)
        {
            var notice = data.uFlags.HasFlag(NOTIFY_ICON_DATA_FLAGS.NIF_INFO) ? data.szInfoTitle.ToString() + "\n" + data.szInfo.ToString() : "";
            Calls.Add(new Call(message, data.guidItem, data.uFlags, text, !data.hIcon.IsNull, data.uCallbackMessage, notice));
        }

        Redrawn += message == NOTIFY_ICON_MESSAGE.NIM_MODIFY && data.uFlags.HasFlag(NOTIFY_ICON_DATA_FLAGS.NIF_ICON) ? 1 : 0;
        return message switch
        {
            NOTIFY_ICON_MESSAGE.NIM_ADD => !Refusing && _shown.TryAdd(data.guidItem, text),
            NOTIFY_ICON_MESSAGE.NIM_MODIFY => Modified(data.guidItem, data.uFlags.HasFlag(NOTIFY_ICON_DATA_FLAGS.NIF_TIP) ? text : null),
            NOTIFY_ICON_MESSAGE.NIM_DELETE => _shown.Remove(data.guidItem),
            NOTIFY_ICON_MESSAGE.NIM_SETVERSION => _shown.ContainsKey(data.guidItem),
            NOTIFY_ICON_MESSAGE.NIM_SETFOCUS => false,
            _ => false,
        };
    }

    // A change of an icon it lacks fails; one without a tooltip keeps the one it had.
    private bool Modified(Guid id, string? tip)
    {
        if (!_shown.TryGetValue(id, out var kept) || (Busy > 0 && Busy-- > 0))
        {
            return false;
        }

        _shown[id] = tip ?? kept;
        return true;
    }
}
