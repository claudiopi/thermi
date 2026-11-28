using Thermi.App.Gestures;
using Thermi.App.SettingsFile;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Linux.Gnome;
using Thermi.Linux.Overlay;
using Thermi.Linux.Painting;

namespace Thermi.Linux.Tray;

/// <summary>What the tray reaches in its host: the settings, the overlays' visibility, and the menus and what they open.</summary>
internal sealed record TrayHost(Settings Settings, OverlayVisibility Visibility, Action RequestPaint)
{
    /// <summary>The rows of thermi's menu, or, with an overlay, that overlay's opened from the tray on an item.</summary>
    public required Func<int?, string?, ShownMenu> Menu { get; init; }

    /// <summary>Does the row at a place of a menu shown.</summary>
    public required Action<ShownMenu, int> Choose { get; init; }

    /// <summary>Opens the settings window on an overlay, by its place, and an item of it.</summary>
    public required Action<int, string?> OpenSettings { get; init; }
}

/// <summary>
/// thermi's icon and the items' tray icons on Linux, as Windows' tray shows them: Core's <see cref="TrayScene"/> painted
/// by the <see cref="TrayPainter"/>, each painted again only when <see cref="TrayRefresh"/> says it changed, and a press
/// on one doing what <see cref="TrayPresses"/> says, as on Windows. Each icon is known by its name, <see cref="TrayIcon.Name"/>.
/// </summary>
internal sealed class DesktopTray : IDisposable
{
    private readonly ITrayIcons _icons;
    private readonly TrayHost _host;
    private readonly Dictionary<Guid, Picture> _pictures = [];
    private readonly Dictionary<string, ShownMenu> _menus = new(StringComparer.Ordinal);
    private TrayRefresh _refresh = new();
    private IReadOnlyList<TrayIcon> _shown = [];
    private double _scale;
    private bool _clickIsMenu;

    public DesktopTray(ITrayIcons icons, TrayHost host)
    {
        (_icons, _host) = (icons, host);
        icons.Pressed = Pressed;
        icons.MenuOf = MenuOf;
        icons.Chosen = Chosen;
    }

    /// <summary>
    /// Shows the icons of <paramref name="config"/> for <paramref name="input"/>, painted for the primary monitor's
    /// <paramref name="scale"/>, where the tray is, once something of them changed; done once the desktop has them.
    /// </summary>
    public Task Refresh(ThermiConfig config, TrayInput input, double scale)
    {
        var icons = TrayScene.Of(config, input);
        var update = _refresh.Next(icons);
        var rescaled = Math.Abs(scale - _scale) > 0.001;
        var clickIsMenu = !_host.Visibility.HideOverlays;
        if (!rescaled && clickIsMenu == _clickIsMenu && update.Redrawn.Count == 0 && update.Retipped.Count == 0 && update.Removed.Count == 0)
        {
            return Task.CompletedTask;
        }

        (_scale, _clickIsMenu) = (scale, clickIsMenu);
        var redrawn = rescaled ? icons : update.Redrawn;
        foreach (var icon in redrawn)
        {
            Drop(icon.Id);
            _pictures[icon.Id] = TrayPainter.Paint(icon, TrayPainter.PixelsAt(scale));
        }

        update.Removed.ToList().ForEach(Drop);
        var tips = _shown.ToDictionary(icon => icon.Id, icon => icon.Tooltip);
        _shown = icons;
        return _icons.Show([.. icons.Select(icon => new TrayPicture(icon.Name, icon.Tooltip, _pictures[icon.Id], scale)
        {
            Redrawn = redrawn.Contains(icon),
            Retipped = !tips.TryGetValue(icon.Id, out var tip) || tip != icon.Tooltip,
            ClickIsMenu = icon.ItemId is null && clickIsMenu,
        })]);
    }

    /// <summary>The desktop forgot the icons, as the Shell's extension does as it starts again: the next refresh shows them all.</summary>
    public void Forget()
    {
        _refresh = new TrayRefresh();
        _scale = 0;
    }

    public void Dispose()
    {
        _pictures.Values.ToList().ForEach(picture => picture.Dispose());
        _icons.Dispose();
    }

    private void Drop(Guid id)
    {
        if (_pictures.Remove(id, out var picture))
        {
            picture.Dispose();
        }
    }

    private TrayIcon? Shown(string key) => _shown.FirstOrDefault(icon => icon.Name == key);

    private void Pressed(string key, TrayPress press)
    {
        if (Shown(key) is not { } icon)
        {
            return;
        }

        switch (TrayPresses.Of(icon.ItemId, press, _host.Visibility))
        {
            case TrayAnswer.Paint:
                _host.RequestPaint();
                break;
            case TrayAnswer.Menu:
                _icons.OpenMenu(key);
                break;
            case TrayAnswer.Settings:
                _host.OpenSettings(TrayScene.OverlayOf(_host.Settings.Config, icon.ItemId), icon.ItemId);
                break;
            case TrayAnswer.None:
            default:
                break;
        }
    }

    private IReadOnlyList<ShellRow> MenuOf(string key)
    {
        var item = Shown(key)?.ItemId;
        var menu = _host.Menu(item is null ? null : TrayScene.OverlayOf(_host.Settings.Config, item), item);
        _menus[key] = menu;
        return menu.Rows;
    }

    private void Chosen(string key, int index)
    {
        if (_menus.TryGetValue(key, out var menu))
        {
            _host.Choose(menu, index);
        }
    }
}
