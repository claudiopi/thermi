namespace Thermi.Core;

/// <summary>
/// Which overlay the window edits, from the one <c>thermi settings --overlay n</c> asks for, counted from 1, or 0 for none,
/// which keeps an open window on the overlay it shows.
/// </summary>
public static class WindowRequest
{
    /// <summary>
    /// The place of the overlay the window shows: the one asked for, or the first when the settings have no such overlay;
    /// with none asked for, the one it shows now, while the settings still have it.
    /// </summary>
    public static int OverlayOf(int requested, int current, int count) => requested switch
    {
        0 when current < count => current,
        > 0 when requested <= count => requested - 1,
        _ => 0,
    };
}
