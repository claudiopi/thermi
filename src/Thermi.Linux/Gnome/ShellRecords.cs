using Thermi.Core.Look;

namespace Thermi.Linux.Gnome;

/// <summary>
/// A monitor as the extension tells it, in the Shell's logical pixels: its top bar's height, 0 for none, and whether it
/// shows a full-screen window. Another desktop's surfaces tell it the same way, with its name.
/// </summary>
internal sealed record ShellMonitor(double X, double Y, double Width, double Height, double Scale, double Bar, bool Primary, bool FullScreen)
{
    /// <summary>
    /// The monitor's name off GNOME, as GNOME would name it (<see cref="Desktop.GdkMonitors"/>); null on GNOME, whose
    /// extension does not tell it, and where <see cref="ShellScreens.Names"/> reads it from Mutter's display config.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>The room the panels keep along each of the monitor's edges, where a desktop tells them all; null for <see cref="Bar"/> alone.</summary>
    public PanelRoom? Room { get; init; }

    /// <summary>The top bar's centre box, the clock, from its left edge to its right, on the stage; (0, 0) off the top bar.</summary>
    public (double Left, double Right) Centre { get; init; }

    /// <summary>The top bar's right box, the system menu, as <see cref="Centre"/>.</summary>
    public (double Left, double Right) End { get; init; }
}

/// <summary>The logical pixels the panels keep along each edge of a monitor, off its work area.</summary>
internal readonly record struct PanelRoom(double Left, double Top, double Right, double Bottom);

/// <summary>
/// The pointer on an overlay's boxes, as the extension sends it: what it did, where on the stage, its button and keys
/// held, and when, in milliseconds. What it did is <c>press</c>, <c>motion</c>, <c>release</c>, <c>scroll</c>, or
/// <c>cancel</c> when another grab, as a modal dialog's or the lock screen's, took the pointer from a press, which ends it
/// as Esc does, or held the Shell already as a press came, which then holds nothing. The overview does not open while a
/// press holds, so it never takes one.
/// </summary>
internal sealed record ShellPointer(uint Overlay, string Kind, double X, double Y, uint Button, uint Modifiers, uint Time, double Dx, double Dy);

/// <summary>
/// A picture as the extension takes it: premultiplied ARGB in the machine's byte order, rows <see cref="Stride"/> bytes
/// apart, at <see cref="Scale"/> pixels to a logical one, taking the pointer only in <see cref="Clicks"/>, boxes of it in
/// logical pixels.
/// </summary>
internal sealed record ShellPicture(GLib.Bytes Pixels, int Width, int Height, int Stride, double Scale, IReadOnlyList<PillBox> Clicks);

/// <summary>A row of a menu as the extension shows it: the row it is under, -1 at the top, and what it reads.</summary>
internal sealed record ShellRow(int Parent, string Text, string? Note, bool Checked, bool Grayed, bool Starts)
{
    /// <summary>Whether the row is the menu's default, which shows bold, as Windows' menus show theirs.</summary>
    public bool Default { get; init; }

    /// <summary>Whether the row's check shows as a radio, one of a group of which one is chosen, as Windows shows Graph's and Size's.</summary>
    public bool Radio { get; init; }
}

/// <summary>Where the extension stands for thermi: running with thermi's protocol, of another, or not running.</summary>
internal enum ExtensionState
{
    Ours,
    OtherVersion,
    Missing,

    /// <summary>
    /// Its files are where the Shell looks, as a package put them, but the running Shell has not read them: it reads them at
    /// the next sign-in, unless it installs the extension from extensions.gnome.org first.
    /// </summary>
    Unread,
    Off,

    /// <summary>Installed and on, but not up yet, as while the Shell starts: its Ready signal says when.</summary>
    Starting,
}
