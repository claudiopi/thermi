namespace Thermi.Core.Frames;

/// <summary>
/// Which app's frames FPS shows: the game, not the focus. An app takes over as the game after <see cref="HeldFor"/>
/// seconds in a row in front with a window that fills its monitor (full screen, borderless or maximized) while presenting
/// at <see cref="TakesOverAt"/> or more, and faster than the game while that still presents; with no game yet, the
/// fastest app that does so in front or not. The game stays it while it presents, even as another window, as a terminal
/// on another monitor, takes the focus: a terminal or a browser that repaints fast for a moment, or slower than the game,
/// never takes over.
/// </summary>
public sealed class FrontGame
{
    /// <summary>The frames a second an app in front presents at to take over; a busy terminal presents some 20.</summary>
    public const double TakesOverAt = 30;

    /// <summary>How many seconds in a row an app meets the rule in front before it takes over.</summary>
    public const int HeldFor = 3;

    private int? _game;
    private int? _taking;
    private int _held;

    /// <summary>
    /// The process whose frames to show this second, from the slots' <paramref name="rates"/>, null while frames cannot be
    /// read, the process in front, whether its window fills its monitor, and whether a process that presents has a window,
    /// or its parent one, that fills its monitor, asked only while there is no game.
    /// </summary>
    public int? Next(IReadOnlyList<FrameRate>? rates, int? front, bool fills, Func<FrameRate, bool> filling)
    {
        ArgumentNullException.ThrowIfNull(filling);
        if (rates is null)
        {
            return _game;
        }

        var game = _game is { } current ? GameSensors.RateOf(rates, current) : null;
        if (game is null)
        {
            _game = null;
        }

        var inFront = front is { } process && process != _game && fills && GameSensors.RateOf(rates, process) is { } rate
            && rate.Fps >= TakesOverAt && rate.Fps > (game?.Fps ?? 0);
        // With no game yet, as when thermi starts with one running behind, the fastest that fills its monitor is one.
        var taking = inFront ? front : _game is null ? Fastest(rates, filling) : null;
        (_taking, _held) = taking is { } next ? (next, next == _taking ? _held + 1 : 1) : ((int?)null, 0);
        if (_held >= HeldFor)
        {
            (_game, _taking, _held) = (_taking, null, 0);
        }

        return _game;
    }

    private static int? Fastest(IReadOnlyList<FrameRate> rates, Func<FrameRate, bool> filling) =>
        rates.Where(rate => rate.Fps >= TakesOverAt).OrderByDescending(rate => rate.Fps).FirstOrDefault(filling) is { Process: > 0 } fastest
            ? fastest.Process
            : null;
}
