using Thermi.Core.Look;
using Thermi.Core.Placement;

namespace Thermi.Windows;

/// <summary>
/// What the overlay reads from Windows at every paint that changes only when Windows says so, kept until it does: the
/// monitors, the taskbar's and the apps' modes and the accent color. The scale, the work areas and the taskbars are read each time, and
/// a monitor gone or a taskbar on one it does not hold lists the monitors again. The taskbar's buttons are read, through
/// UI Automation off the window's thread, only while an overlay sits on a taskbar's left or right, which they may crowd.
/// Disposed on the window's thread.
/// </summary>
/// <param name="time">The clock the buttons' answers are kept by.</param>
/// <param name="changed">Told, on any thread, when a taskbar's buttons moved.</param>
internal sealed class DesktopReads(TimeProvider time, Action changed) : IDisposable
{
    private readonly Lock _gate = new();
    private IReadOnlyList<ListedMonitor>? _monitors;
    private bool? _lightTaskbar;
    private bool? _lightApps;
    private Rgb? _accent;
    private TaskbarApps? _apps;

    /// <summary>The monitors as they are now, with how far each taskbar's buttons reach when <paramref name="buttons"/>.</summary>
    public IReadOnlyList<Screen> Screens(bool buttons = false)
    {
        _monitors ??= Desktop.Monitors();
        var screens = Desktop.OnTaskbars(_monitors, out var stale);
        if (stale)
        {
            _monitors = Desktop.Monitors();
            screens = Desktop.OnTaskbars(_monitors, out _);
        }

        return buttons ? [.. screens.Select(Buttoned)] : screens;
    }

    private Screen Buttoned(Screen screen)
    {
        if (screen.Taskbar is not { } bar)
        {
            return screen;
        }

        lock (_gate)
        {
            _apps ??= new TaskbarApps(time, Guarded, changed);
            return screen with { Buttons = _apps.LeftButtons(bar), Apps = _apps.Extent(bar) };
        }
    }

    // The answers come on the pool's threads, and are kept under the same lock the paints read them under.
    private void Guarded(Action keep)
    {
        lock (_gate)
        {
            keep();
        }
    }

    public bool LightTaskbar() => _lightTaskbar ??= Desktop.LightTaskbar();

    public bool LightApps() => _lightApps ??= AppMode.LightApps();

    public Rgb Accent() => _accent ??= Desktop.Accent();

    public void Dispose()
    {
        lock (_gate)
        {
            _apps?.Dispose();
            _apps = null;
        }
    }

    /// <summary>The display, a setting or the accent color changed: each is read again when next asked for.</summary>
    public void Forget()
    {
        _monitors = null;
        _lightTaskbar = null;
        _lightApps = null;
        _accent = null;
    }
}
