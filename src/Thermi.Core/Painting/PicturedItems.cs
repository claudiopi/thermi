using Thermi.Core.Look;

namespace Thermi.Core.Painting;

/// <summary>
/// What a picture of an overlay shows, as the window's previews and "Copy as image" paint it alike: its items without what
/// only the open window or a broken file adds, under the "Sensors stopped" pill while they are, or when nothing else is left.
/// </summary>
public static class PicturedItems
{
    public static (IReadOnlyList<OverlayItem> Items, bool Stopped) Of(IReadOnlyList<OverlayItem> items, bool stopped)
    {
        IReadOnlyList<OverlayItem> shown = [.. items.Where(item => item.Id.Length > 0)];
        return (shown, stopped || shown.Count == 0);
    }
}
