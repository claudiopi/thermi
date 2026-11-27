using Thermi.Core.Look;
using Thermi.Core.Placement;

namespace Thermi.Windows.Win32;

/// <summary>
/// What the overlay reads from Windows at every paint that changes only when Windows says so, kept until it does: the
/// monitors, the taskbar's and the apps' modes, the accent color, high contrast and the animation effects. The scale, the
/// work areas and the taskbars are read each time, and a monitor gone or a taskbar on one it does not hold lists the
/// monitors again.
/// </summary>
/// <param name="highContrast">High contrast as Windows answers it, unless a test answers for it.</param>
/// <param name="animations">Whether Windows' animation effects are on, as it answers, unless a test answers for it.</param>
internal sealed class DesktopReads(Func<HighContrastColors?>? highContrast = null, Func<bool>? animations = null)
{
    private IReadOnlyList<ListedMonitor>? _monitors;
    private bool? _lightTaskbar;
    private bool? _lightApps;
    private Rgb? _accent;
    private (HighContrastColors? Colors, bool Read) _highContrast;
    private bool? _animations;

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

    public HighContrastColors? HighContrast()
    {
        if (!_highContrast.Read)
        {
            _highContrast = ((highContrast ?? Desktop.HighContrast)(), true);
        }

        return _highContrast.Colors;
    }

    public bool Animations() => _animations ??= (animations ?? Desktop.Animations)();

    /// <summary>The display, a setting, a system color or the accent color changed: each is read again when next asked for.</summary>
    public void Forget()
    {
        _monitors = null;
        _lightTaskbar = null;
        _lightApps = null;
        _accent = null;
        _highContrast = default;
        _animations = null;
    }
}
