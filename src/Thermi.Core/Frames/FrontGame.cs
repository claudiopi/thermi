namespace Thermi.Core.Frames;

/// <summary>
/// Which app's frames FPS shows: the game, not the focus. An app becomes the game when it is in front with a window that
/// fills its monitor (full screen, borderless or maximized) while presenting at <see cref="TakesOverAt"/> or more, and
/// stays it while it presents, even as another window, as a terminal on another monitor, takes the focus. A window that
/// does not fill its monitor, or presents slower, never takes over, so a terminal or a browser at work shows no FPS.
/// </summary>
public sealed class FrontGame
{
    /// <summary>The frames a second an app in front presents at to become the game; a busy terminal presents some 20.</summary>
    public const double TakesOverAt = 30;

    private int? _game;

    /// <summary>
    /// The process whose frames to show this second, from the slots' <paramref name="rates"/>, null while frames cannot be
    /// read, the process in front, and whether its window fills its monitor.
    /// </summary>
    public int? Next(IReadOnlyList<FrameRate>? rates, int? front, bool fills)
    {
        if (rates is null)
        {
            return _game;
        }

        if (front is { } process && fills && GameSensors.RateOf(rates, process) is { Fps: >= TakesOverAt })
        {
            _game = process;
        }

        if (_game is { } game && GameSensors.RateOf(rates, game) is null)
        {
            _game = null;
        }

        return _game;
    }
}
