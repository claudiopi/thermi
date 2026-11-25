using Thermi.Core.Look;
using Thermi.Core.Placement;

namespace Thermi.Windows.Win32;

/// <summary>
/// What the overlay reads from Windows at every paint that changes only when Windows says so, kept until it does: the
/// monitors, the taskbar's and the apps' modes and the accent color. The scale, the work areas and the taskbars are read each time, and
/// a monitor gone or a taskbar on one it does not hold lists the monitors again.
/// </summary>
internal sealed class DesktopReads
{
    private IReadOnlyList<ListedMonitor>? _monitors;
    private bool? _lightTaskbar;
    private bool? _lightApps;
    private Rgb? _accent;

    /// <summary>The monitors as they are now.</summary>
    public IReadOnlyList<Screen> Screens()
    {
        _monitors ??= Desktop.Monitors();
        var screens = Desktop.OnTaskbars(_monitors, out var stale);
        if (stale)
        {
            _monitors = Desktop.Monitors();
            screens = Desktop.OnTaskbars(_monitors, out _);
        }

        return screens;
    }

    public bool LightTaskbar() => _lightTaskbar ??= Desktop.LightTaskbar();

    public bool LightApps() => _lightApps ??= AppMode.LightApps();

    public Rgb Accent() => _accent ??= Desktop.Accent();

    /// <summary>The display, a setting or the accent color changed: each is read again when next asked for.</summary>
    public void Forget()
    {
        _monitors = null;
        _lightTaskbar = null;
        _lightApps = null;
        _accent = null;
    }
}
