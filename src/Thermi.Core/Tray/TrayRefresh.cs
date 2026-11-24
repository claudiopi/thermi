namespace Thermi.Core.Tray;

/// <summary>
/// What changed since the last refresh: the icons to draw again, new ones included, those whose tooltip alone changed,
/// and the ids of those gone.
/// </summary>
public sealed record TrayUpdate(IReadOnlyList<TrayIcon> Redrawn, IReadOnlyList<TrayIcon> Retipped, IReadOnlyList<Guid> Removed);

/// <summary>
/// Remembers what each tray icon showed, so the overlay draws an icon again only when its text or color changes: a
/// redraw makes a new icon handle, and the once-a-second refresh mostly finds the same values.
/// </summary>
public sealed class TrayRefresh
{
    private Dictionary<Guid, TrayIcon> _shown = [];

    public TrayUpdate Next(IReadOnlyList<TrayIcon> icons)
    {
        var redrawn = icons.Where(icon => !_shown.TryGetValue(icon.Id, out var was) || was.Text != icon.Text || was.Color != icon.Color || was.Mark != icon.Mark).ToList();
        var retipped = icons.Where(icon => _shown.TryGetValue(icon.Id, out var was) && was.Tooltip != icon.Tooltip && !redrawn.Contains(icon)).ToList();
        var removed = _shown.Keys.Where(id => icons.All(icon => icon.Id != id)).ToList();
        _shown = icons.ToDictionary(icon => icon.Id);
        return new TrayUpdate(redrawn, retipped, removed);
    }
}
