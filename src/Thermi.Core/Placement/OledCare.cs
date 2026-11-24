using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>
/// OLED care: a picture that never moves can burn into an OLED panel, so an overlay with it on moves 1 px every 3 minutes
/// through the four corners of a 1 px square and back. It follows from the time alone and is never written to the file. On
/// each axis the square lies inward of a snapped edge, so the overlay never leaves the edge it keeps, and up or to the left
/// where there is none.
/// </summary>
public static class OledCare
{
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(3);

    private const int Spots = 4;

    /// <summary>
    /// How far, in physical pixels, an overlay shown at <paramref name="spot"/>, or at a free position when it is null,
    /// moves after running for <paramref name="running"/>.
    /// </summary>
    public static (int X, int Y) Shift(SnapSpot? spot, TimeSpan running)
    {
        var (x, y) = Inward(spot);
        return (Math.Max(0, (long)(running / Step)) % Spots) switch
        {
            0 => (0, 0),
            1 => (x, 0),
            2 => (x, y),
            _ => (0, y),
        };
    }

    // An overlay on the taskbar keeps no edge: it sits where it was dropped along it, in its middle across it.
    private static (int X, int Y) Inward(SnapSpot? spot) => spot switch
    {
        SnapSpot.TopLeft => (1, 1),
        SnapSpot.TopRight => (-1, 1),
        SnapSpot.BottomLeft => (1, -1),
        SnapSpot.BottomRight or SnapSpot.Taskbar or null => (-1, -1),
        _ => (-1, -1),
    };
}
