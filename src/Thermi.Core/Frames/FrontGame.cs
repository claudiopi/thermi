namespace Thermi.Core.Frames;

/// <summary>
/// Which app's frames FPS shows: the game, not the focus. An app takes over from the game after <see cref="HeldFor"/>
/// seconds in a row in front with a window that fills its monitor (full screen, borderless or maximized) while presenting
/// at <see cref="TakesOverAt"/> or more, faster than the game while that still presents, and with no window of the game's
/// shown on another monitor; with no game, the app in front that does so, or else the fastest that does so behind, is the
/// game at once. The game stays it while it presents and shows on another monitor: a terminal that repaints at its
/// monitor's rate as it streams text is no game.
/// </summary>
public sealed class FrontGame
{
    /// <summary>The frames a second an app in front presents at to take over; a busy terminal presents some 20.</summary>
    public const double TakesOverAt = 30;

    /// <summary>How many seconds in a row an app meets the rule in front before it takes over from a game.</summary>
    public const int HeldFor = 3;

    private int? _game;
    private int? _taking;
    private int _held;

    /// <summary>
    /// The process whose frames to show this second, from the slots' <paramref name="rates"/>, null while frames cannot be
    /// read, the process in front, the monitor its window fills or null when it fills none, and the windows a process that
    /// presents, or its parent, shows, asked only when needed.
    /// </summary>
    public int? Next(IReadOnlyList<FrameRate>? rates, int? front, nint? fills, Func<FrameRate, IReadOnlyList<AppWindow>> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (rates is null)
        {
            return _game;
        }

        var game = _game is { } current ? GameSensors.RateOf(rates, current) : null;
        if (game is null)
        {
            _game = null;
        }

        var inFront = front is { } process && process != _game && fills is { } monitor && GameSensors.RateOf(rates, process) is { } rate
            && rate.Fps >= TakesOverAt && rate.Fps > (game?.Fps ?? 0)
            && (game is not { } playing || !windows(playing).Any(window => window.Monitor != monitor));
        // With no game, as when thermi starts with one running behind, the fastest that fills its monitor is one.
        var taking = inFront ? front : _game is null ? Fastest(rates, windows) : null;
        (_taking, _held) = taking is { } next ? (next, next == _taking ? _held + 1 : 1) : ((int?)null, 0);
        // With no game, the service's own 3 s of steady frames are wait enough.
        if (_held >= (game is null ? 1 : HeldFor))
        {
            (_game, _taking, _held) = (_taking, null, 0);
        }

        return _game;
    }

    private static int? Fastest(IReadOnlyList<FrameRate> rates, Func<FrameRate, IReadOnlyList<AppWindow>> windows) =>
        rates.Where(rate => rate.Fps >= TakesOverAt).OrderByDescending(rate => rate.Fps)
            .FirstOrDefault(rate => windows(rate).Any(window => window.Fills)) is { Process: > 0 } fastest
            ? fastest.Process
            : null;
}
