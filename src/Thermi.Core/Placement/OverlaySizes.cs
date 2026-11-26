using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>
/// The sizes the overlay's menu offers and Ctrl and the wheel turn to: a step of the window's Size slider either way, from
/// the size the overlay shows, and a few presets, within 75 % to 400 % and, on a taskbar, the most it shows there, so none
/// would change nothing.
/// </summary>
public static class OverlaySizes
{
    /// <summary>A step of the window's Size slider, of the menu's "Bigger" and "Smaller", and of each notch of the wheel.</summary>
    public const double Step = 0.05;

    /// <summary>A notch of the wheel, as Windows counts it; a precision touchpad sends parts of one.</summary>
    public const int Notch = 120;

    private static readonly double[] Presets = [0.75, 1, 1.25, 1.5, 2];

    /// <summary>The most the overlay shows at where it sits: 400 %, or on a taskbar what keeps it within its height.</summary>
    public static double Most(OverlayConfig overlay, IReadOnlyList<Screen> screens) =>
        Snaps.SizeShown(overlay with { Size = OverlayConfig.MaxSize }, screens);

    /// <summary>The presets that show at their own size where the overlay sits.</summary>
    public static IEnumerable<double> Offered(OverlayConfig overlay, IReadOnlyList<Screen> screens)
    {
        var most = Most(overlay, screens);
        return Presets.Where(size => size <= most);
    }

    /// <summary>A step above the size the overlay shows, or null where it shows no bigger.</summary>
    public static double? Bigger(OverlayConfig overlay, IReadOnlyList<Screen> screens) =>
        Snaps.SizeShown(overlay, screens) is var shown && shown < Most(overlay, screens)
            ? Tidy(Math.Min((Math.Floor(Math.Round(shown / Step, 6)) + 1) * Step, OverlayConfig.MaxSize))
            : null;

    /// <summary>A step below the size the overlay shows, or null at the smallest.</summary>
    public static double? Smaller(OverlayConfig overlay, IReadOnlyList<Screen> screens) =>
        Snaps.SizeShown(overlay, screens) is var shown && shown > OverlayConfig.MinSize
            ? Tidy(Math.Max((Math.Ceiling(Math.Round(shown / Step, 6)) - 1) * Step, OverlayConfig.MinSize))
            : null;

    /// <summary>
    /// A turn of the wheel by <paramref name="delta"/>, added to what earlier turns <paramref name="carried"/> short of a
    /// notch, or dropped when it turns the other way: the size its whole notches reach, null for none, and what it leaves.
    /// </summary>
    public static (double? Size, int Carried) Wheel(OverlayConfig overlay, IReadOnlyList<Screen> screens, int carried, int delta)
    {
        var turned = Math.Sign(carried) == -Math.Sign(delta) ? delta : carried + delta;
        double? size = null;
        for (var notches = Math.Abs(turned / Notch); notches > 0; notches--)
        {
            var at = size is { } reached ? overlay with { Size = reached } : overlay;
            if ((turned > 0 ? Bigger(at, screens) : Smaller(at, screens)) is not { } next)
            {
                break;
            }

            size = next;
        }

        return (size, turned % Notch);
    }

    // A step's size as the file and the slider write it, free of what binary fractions add.
    private static double Tidy(double size) => Math.Round(size, 2);
}
