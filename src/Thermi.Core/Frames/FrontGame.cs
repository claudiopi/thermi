namespace Thermi.Core.Frames;

/// <summary>
/// Which app's frames FPS shows: the game, not the focus. The game is an app that presents with a window that fills its
/// monitor (full screen, borderless or maximized), and stays it only while it does both. With no game, the app that
/// presents at <see cref="TakesOverAt"/> or more with such a window is the game at once, one that presents from its own
/// window first, then the one in front, then the fastest: a browser's or a terminal's frames come from a child, a game's
/// from itself. An app in front takes over from the game after <see cref="HeldFor"/> seconds in a row of doing so, when
/// it beats the game the same way and no window of the game's shows on another monitor.
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
    /// The process whose frames to show this second. Null <paramref name="rates"/> means frames cannot be read;
    /// <paramref name="fills"/> is the monitor the front window fills, null when it fills none.
    /// </summary>
    public int? Next(IReadOnlyList<FrameRate>? rates, int? front, nint? fills, Func<FrameRate, IReadOnlyList<AppWindow>> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (rates is null)
        {
            return _game;
        }

        var candidates = new Candidates(windows, front);
        var game = Kept(rates, candidates);
        if (game is null)
        {
            _game = null;
        }

        var taking = game is null ? Best(rates, candidates.Of) : Challenger(rates, front, fills, game, candidates.Of);
        (_taking, _held) = taking is { } next ? (next, next == _taking ? _held + 1 : 1) : ((int?)null, 0);
        // With no game, the service's own 3 s of steady frames are wait enough.
        if (_held >= (game is null ? 1 : HeldFor))
        {
            (_game, _taking, _held) = (_taking, null, 0);
        }

        return _game;
    }

    private Candidate? Kept(IReadOnlyList<FrameRate> rates, Candidates candidates) =>
        _game is { } current && GameSensors.RateOf(rates, current) is { } playing && candidates.Of(playing) is { Fills: true } kept ? kept : null;

    // As when thermi starts with a game running behind a terminal maximized in front on the other monitor.
    private static int? Best(IReadOnlyList<FrameRate> rates, Func<FrameRate, Candidate> of) =>
        rates.Where(rate => rate.Fps >= TakesOverAt).Select(of).Where(candidate => candidate.Fills)
            .OrderByDescending(candidate => candidate.Own).ThenByDescending(candidate => candidate.InFront).ThenByDescending(candidate => candidate.Rate.Fps)
            .FirstOrDefault() is { Rate.Process: > 0 } best
            ? best.Rate.Process
            : null;

    private static int? Challenger(IReadOnlyList<FrameRate> rates, int? front, nint? fills, Candidate game, Func<FrameRate, Candidate> of) =>
        front is { } process && fills is { } monitor && GameSensors.RateOf(rates, process) is { Fps: >= TakesOverAt } rate
            && !game.InFront && of(rate).Beats(game)
            && !game.Windows.Any(window => window.Monitor != monitor)
            ? process
            : null;

    // Each app's windows, asked of once a second at most, as each asking lists every window on the desktop.
    private sealed class Candidates(Func<FrameRate, IReadOnlyList<AppWindow>> windows, int? front)
    {
        private readonly Dictionary<FrameRate, Candidate> _asked = [];

        public Candidate Of(FrameRate rate)
        {
            if (!_asked.TryGetValue(rate, out var candidate))
            {
                candidate = new Candidate(rate, windows(rate), front);
                _asked[rate] = candidate;
            }

            return candidate;
        }
    }

    private sealed record Candidate(FrameRate Rate, IReadOnlyList<AppWindow> Windows, int? Front)
    {
        public bool Fills => Windows.Any(window => window.Fills);

        public bool Own => Windows.Any(window => window.Fills && window.Own);

        public bool InFront => Front is { } front && (Rate.Process == front || Rate.Parent == front);

        public bool Beats(Candidate game) => Own != game.Own ? Own : Rate.Fps > game.Rate.Fps;
    }
}
