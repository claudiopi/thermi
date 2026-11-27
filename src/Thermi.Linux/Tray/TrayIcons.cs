using Thermi.App.Gestures;
using Thermi.Linux.Gnome;
using Thermi.Linux.Painting;

namespace Thermi.Linux.Tray;

/// <summary>
/// A tray icon as painted: its key, the icon's name, the same on every start, its tooltip, which a screen reader reads too,
/// and its picture at the scale it was painted for; whether this refresh redrew the picture or changed the tooltip, which
/// is all a tray is told again; and whether a left click on it opens its menu, as thermi's own does but while the
/// overlays are hidden, which that click shows.
/// </summary>
internal sealed record TrayPicture(string Key, string Tooltip, Picture Picture, double Scale)
{
    public bool Redrawn { get; init; } = true;

    public bool Retipped { get; init; } = true;

    public bool ClickIsMenu { get; init; }
}

/// <summary>
/// Where a desktop shows thermi's icons: GNOME's top bar through thermi's extension, or the tray every other desktop serves
/// to StatusNotifierItems. Each says what is pressed and chosen, by the icon's key, on the GLib main loop's thread.
/// </summary>
internal interface ITrayIcons : IDisposable
{
    Action<string, TrayPress> Pressed { get; set; }

    /// <summary>The rows of an icon's menu now, which a tray asks for as the menu opens.</summary>
    Func<string, IReadOnlyList<ShellRow>> MenuOf { get; set; }

    /// <summary>A row chosen in an icon's menu, by its place in the rows it opened with.</summary>
    Action<string, int> Chosen { get; set; }

    /// <summary>Shows the icons, in their order, in place of those shown; done once the desktop has them.</summary>
    Task Show(IReadOnlyList<TrayPicture> icons);

    /// <summary>Opens an icon's menu, where the desktop lets a program open it; a tray that opens it itself ignores it.</summary>
    void OpenMenu(string key);
}
