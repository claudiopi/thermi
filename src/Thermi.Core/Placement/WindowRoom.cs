using System.Globalization;
using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>The list's width, or whether it folds into a button of the title row, and its width then, over the pane.</summary>
public readonly record struct ListLayout(int Width, bool Folded);

/// <summary>The pane's columns, one or two, and the widest a column may be.</summary>
public readonly record struct PaneColumns(int Count, int MaxWidth);

/// <summary>The settings window's size and place, and its list's and pane's widths, as plain geometry.</summary>
public static class WindowRoom
{
    private const double FirstShare = 0.9;
    private const int FirstWidth = 1500;
    private const int FirstHeight = 900;
    private const int WholeWidthUnder = 860;
    private const int MinWidth = 760;
    private const int MinHeight = 560;
    private const int WideWindow = 1500;
    private const int WideList = 344;
    private const double ListShare = 0.32;
    private const int NarrowestList = 240;
    private const int WidestList = 296;
    private const int FoldedList = 320;
    private const double FoldedShare = 0.85;
    private const int TwoColumnsFrom = 864;
    private const int Column = 720;
    private const int WideColumn = 880;
    private const int WideColumnPast = 2000;

    /// <summary>
    /// The first size and place: 90 % of the first overlay's monitor's work area, at most 1500 × 900, centered, or its whole
    /// width where 90 % of it is too narrow for the list and the pane and the whole is not. Each free overlay it covers
    /// shortens it on the side that loses the least, as long as it keeps its minimum. There is at least one monitor.
    /// </summary>
    public static PixelRect First(ThermiConfig config, IReadOnlyList<Screen> screens, IReadOnlyList<PixelRect> freeOverlays)
    {
        var work = Snaps.ScreenOf(config.Overlays[0], screens)!.Work;
        var minimum = Minimum(work);
        var share = (int)(work.Width * FirstShare);
        var width = Math.Max(minimum.Width, share < WholeWidthUnder && work.Width >= WholeWidthUnder ? work.Width : Math.Min(FirstWidth, share));
        var height = Math.Max(minimum.Height, Math.Min(FirstHeight, (int)(work.Height * FirstShare)));
        var left = work.Left + ((work.Width - width) / 2);
        var top = work.Top + ((work.Height - height) / 2);
        return freeOverlays.Aggregate(new PixelRect(left, top, left + width, top + height), (window, overlay) => Off(window, overlay, minimum));
    }

    /// <summary>The smallest the window may be, or the work area when that is smaller.</summary>
    public static (int Width, int Height) Minimum(PixelRect work) => (Math.Min(MinWidth, work.Width), Math.Min(MinHeight, work.Height));

    /// <summary>What a remembered size is kept under: the same for the same monitors, wherever they are listed.</summary>
    public static string Setup(IReadOnlyList<Screen> screens) =>
        string.Join(';', screens.Select(screen => string.Create(CultureInfo.InvariantCulture, $"{screen.Id}@{screen.Bounds.Left},{screen.Bounds.Top},{screen.Bounds.Width}x{screen.Bounds.Height}")).Order(StringComparer.Ordinal));

    public static ListLayout List(int windowWidth) =>
        windowWidth >= WideWindow ? new ListLayout(WideList, false)
        : windowWidth >= WholeWidthUnder ? new ListLayout(Math.Clamp((int)(windowWidth * ListShare), NarrowestList, WidestList), false)
        : new ListLayout(Math.Min(FoldedList, (int)(windowWidth * FoldedShare)), true);

    public static PaneColumns Columns(int paneWidth) =>
        new(paneWidth >= TwoColumnsFrom ? 2 : 1, paneWidth > WideColumnPast ? WideColumn : Column);

    // The cut that keeps the most of the window; none when every cut would take it under its minimum.
    private static PixelRect Off(PixelRect window, PixelRect overlay, (int Width, int Height) minimum)
    {
        if (!window.Overlaps(overlay))
        {
            return window;
        }

        PixelRect[] cuts =
        [
            window with { Left = overlay.Right },
            window with { Right = overlay.Left },
            window with { Top = overlay.Bottom },
            window with { Bottom = overlay.Top },
        ];
        return cuts.Where(cut => cut.Width >= minimum.Width && cut.Height >= minimum.Height)
            .OrderByDescending(cut => (long)cut.Width * cut.Height)
            .DefaultIfEmpty(window)
            .First();
    }
}
