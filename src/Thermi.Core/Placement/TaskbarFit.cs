using Thermi.Core.Translations;

namespace Thermi.Core.Placement;

/// <summary>An item as the fit measures it, in pixels: its label's width, 0 when it shows none, and its value's widest form's.</summary>
public readonly record struct FitItem(double Label, double Widest);

/// <summary>What the window says of an overlay that does not fit beside the app buttons, and its way out.</summary>
[Translated]
public enum FitText
{
    DoesNotFit,
    HideLabels,
}

/// <summary>
/// Whether an overlay fits beside the taskbar's app buttons. It only decides the window's notice: the overlay keeps its
/// labels and items whatever the room, since labels follow the user's setting alone.
/// </summary>
public static class TaskbarFit
{
    /// <summary>Room each value keeps beyond its widest form.</summary>
    public const double Spare = 8;

    /// <summary>
    /// True when the items, as they show, fit <paramref name="room"/> with <paramref name="gap"/> between them. Each value
    /// is measured at its widest form, so the answer holds while the values change.
    /// </summary>
    public static bool Fits(IReadOnlyList<FitItem> items, double gap, double room) =>
        items.Sum(item => item.Label + item.Widest + Spare) + (gap * Math.Max(0, items.Count - 1)) <= room;
}
