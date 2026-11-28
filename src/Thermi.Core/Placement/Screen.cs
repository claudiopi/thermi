using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>A rectangle in physical pixels of the whole desktop, as Windows gives it: its right and bottom edges lie just outside.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public long Area => (long)Width * Height;

    public bool Overlaps(PixelRect other) => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    public bool Contains(PixelRect other) => other.Left >= Left && other.Right <= Right && other.Top >= Top && other.Bottom <= Bottom;

    public bool Contains(Position point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>The pixels this and <paramref name="other"/> both cover.</summary>
    public long Shared(PixelRect other) =>
        (long)Math.Max(0, Math.Min(Right, other.Right) - Math.Max(Left, other.Left)) * Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top));
}

/// <summary>
/// A monitor as Windows reports it, <see cref="Scale"/> being its DPI over 96. <see cref="Taskbar"/> is null on a monitor without one,
/// and <see cref="Tray"/> is the notification area on the main taskbar or the clock on another, null when it cannot be found from its
/// window. <see cref="Start"/>, <see cref="Centre"/> and <see cref="End"/> are parts of a taskbar no overlay covers where it
/// fits beside them, GNOME's Activities with what sits beside it, its clock and its system menu, or KDE Plasma's launcher,
/// task manager and tray; one that fits beside a part on neither side sits beside the notification area.
/// </summary>
public sealed record Screen(string Id, bool Primary, PixelRect Bounds, PixelRect Work, double Scale)
{
    public PixelRect? Taskbar { get; init; }

    public PixelRect? Tray { get; init; }

    public PixelRect? Start { get; init; }

    public PixelRect? Centre { get; init; }

    public PixelRect? End { get; init; }
}
