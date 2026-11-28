using System.Drawing;
using Thermi.App.Gestures;
using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Tray;

namespace Thermi.App.Overlay;

/// <summary>A value's tooltip to show: its text, and its item's place in the overlay's pixels.</summary>
internal sealed record ShownTip(string Text, PillBox Item);

/// <summary>What an overlay's tooltips reach of their host: its clock, its loop, and the tooltip it draws its own way.</summary>
internal interface ITipsHost
{
    TimeProvider Time { get; }

    /// <summary>Runs <paramref name="action"/> on the host's loop, as a timer of <see cref="Time"/> calls back from another thread.</summary>
    void Post(Action action);

    /// <summary>Shows <paramref name="tip"/> in place of the one before, or none for null.</summary>
    void Show(ShownTip? tip);
}

/// <summary>
/// An overlay's value tooltips, for a desktop that has no tooltips of its own an overlay can use: what Windows' tooltip
/// control does for the overlay there. The pointer resting on a value shows its tooltip after the desktop's double-click
/// time, the next value's after a fifth of it while one shows, for ten times it at most, its text Core's and following the
/// value; none while a press, a menu or a drop's quiet holds them, nor on a click-through overlay, which takes no pointer.
/// </summary>
internal sealed class OverlayTips(ITipsHost host, TimeSpan doubleClick) : IDisposable
{
    /// <summary>Between the tooltip and the overlay, in pixels, so it reads as the overlay's and covers none of its pills.</summary>
    public const int Gap = 6;

    private readonly TipQuiet _quiet = new();
    private IReadOnlyList<PillBox> _places = [];
    private Func<int, string> _textOf = _ => "";
    private bool _through;

    // The value the pointer is on and the one whose tooltip shows, by their place among the tooltips, with its text.
    private int? _on;
    private int? _shown;
    private string? _text;

    // A tooltip that showed its while comes again only for another value, as Windows' does.
    private bool _popped;
    private Wake? _wait;
    private Wake? _dropWait;

    public TimeSpan Initial => doubleClick;

    public TimeSpan Reshow => doubleClick / 5;

    public TimeSpan Showing => doubleClick * 10;

    /// <summary>Whether a tooltip may show now.</summary>
    public bool Speaks => _quiet.Speaks && !_through;

    /// <summary>
    /// Overlay <paramref name="overlay"/> painted <paramref name="shown"/>, its items at <paramref name="painted"/>: one
    /// tooltip over each of the first, as Core's <see cref="ValueTooltips"/> says it, the "Sensors stopped" pill's over that
    /// pill. A tooltip open over a value takes its new text.
    /// </summary>
    public void Update(ThermiConfig config, int overlay, TrayInput input, OverlayScene shown, IReadOnlyList<PaintedItem> painted)
    {
        _places = [.. painted.Take(shown.Tooltips.Count).Select(place => place.Item)];
        _textOf = at => ValueTooltips.Text(config, overlay, input, shown.Tooltips[at]);
        _through = overlay < config.Overlays.Count && config.Overlays[overlay].ClickThrough;
        if (_through || _shown >= _places.Count)
        {
            Hide();
            return;
        }

        if (_shown is { } on && _textOf(on) is var text && text != _text)
        {
            _text = text;
            host.Show(new ShownTip(text, _places[on]));
        }
    }

    /// <summary>The pointer over the overlay, at a point of its picture.</summary>
    public void Point(double x, double y)
    {
        int? item = null;
        for (var at = 0; at < _places.Count && item is null; at++)
        {
            item = _places[at].Contains(x, y) ? at : null;
        }

        if (item == _on)
        {
            if (_shown is null && _wait is null && !_popped)
            {
                Wait(Initial);
            }

            return;
        }

        var showing = _shown is not null;
        Hide();
        (_on, _popped) = (item, false);
        Wait(showing ? Reshow : Initial);
    }

    /// <summary>The pointer left the overlay: no tooltip shows, and a drop's quiet ends.</summary>
    public void Left()
    {
        (_on, _popped) = (null, false);
        Hide();
        var spoke = Speaks;
        if (_quiet.Left())
        {
            Stop(ref _dropWait);
            Heard(spoke);
        }
    }

    /// <summary>
    /// Holds the tooltips quiet for <paramref name="why"/>, or lets them go: a press, a drag or the menu. Held, they forget
    /// the value the pointer was on, as a drag or a menu takes it where the overlay hears it not: once free, the value the
    /// pointer next moves on shows its own, as Windows' tooltips wait for a move after a press.
    /// </summary>
    public void Quiet(Hush why, bool quiet)
    {
        var spoke = Speaks;
        _quiet.Quiet(why, quiet);
        if (quiet)
        {
            (_on, _popped) = (null, false);
        }

        Heard(spoke);
    }

    /// <summary>
    /// The overlay shows after <paramref name="drop"/>: one not yet heard of holds the tooltips until the pointer leaves
    /// the overlay or its quiet is over.
    /// </summary>
    public void After(DropQuiet drop)
    {
        var spoke = Speaks;
        if (_quiet.After(drop) is { } wait)
        {
            Heard(spoke);
            Stop(ref _dropWait);
            _dropWait = new Wake(host, wait, Calmed);
        }
    }

    /// <summary>
    /// Where a tooltip <paramref name="size"/> big shows for the overlay at <paramref name="overlay"/> on
    /// <paramref name="screen"/>: from the pointer's x, kept on the screen, whole above the overlay or below it as
    /// <see cref="Top"/> says.
    /// </summary>
    public static Position Place(PixelRect overlay, Size size, int pointerX, PixelRect screen) =>
        new(Math.Clamp(pointerX, screen.Left, Math.Max(screen.Left, screen.Right - size.Width)), Top(overlay, size.Height, screen.Top));

    /// <summary>
    /// The top of a tooltip <paramref name="height"/> tall: whole above the overlay, <see cref="Gap"/> apart, or below it
    /// where its screen, from <paramref name="screenTop"/> down, has no room above, as on a taskbar at the top.
    /// </summary>
    public static int Top(PixelRect overlay, int height, int screenTop) =>
        overlay.Top - Gap - height is var above && above >= screenTop ? above : overlay.Bottom + Gap;

    public void Dispose()
    {
        Stop(ref _wait);
        Stop(ref _dropWait);
    }

    // The drop's quiet may be over, or lasts a while yet.
    private void Calmed()
    {
        var spoke = Speaks;
        _dropWait = _quiet.Woke() is { } left ? new Wake(host, left, Calmed) : null;
        Heard(spoke);
    }

    // Quiet, the tooltip hides; free again, the value the pointer is known to be on shows its own after the usual wait.
    private void Heard(bool spoke)
    {
        if (!Speaks)
        {
            Hide();
        }
        else if (!spoke)
        {
            Wait(Initial);
        }
    }

    private void Wait(TimeSpan after)
    {
        Stop(ref _wait);
        if (_on is not null && Speaks)
        {
            _wait = new Wake(host, after, Showed);
        }
    }

    private void Showed()
    {
        _wait = null;
        if (_on is not { } on || on >= _places.Count || !Speaks)
        {
            return;
        }

        (_shown, _text) = (on, _textOf(on));
        host.Show(new ShownTip(_text, _places[on]));
        _wait = new Wake(host, Showing, Popped);
    }

    private void Popped()
    {
        Hide();
        _popped = true;
    }

    private void Hide()
    {
        Stop(ref _wait);
        if (_shown is not null)
        {
            (_shown, _text) = (null, null);
            host.Show(null);
        }
    }

    private static void Stop(ref Wake? wake)
    {
        wake?.Dispose();
        wake = null;
    }

    // One wait on the injected clock, which does nothing once stopped, even when its timer already fired; one that ran
    // let its timer go, so it is dropped as it is.
    private sealed class Wake : IDisposable
    {
        private readonly ITimer _timer;
        private bool _off;

        public Wake(ITipsHost host, TimeSpan after, Action then)
        {
            _timer = host.Time.CreateTimer(_ => host.Post(() => Run(then)), null, after, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            _off = true;
            _timer.Dispose();
        }

        private void Run(Action then)
        {
            if (!_off)
            {
                Dispose();
                then();
            }
        }
    }
}
