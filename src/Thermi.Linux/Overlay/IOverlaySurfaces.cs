using Thermi.Linux.Gnome;

namespace Thermi.Linux.Overlay;

/// <summary>
/// Where a desktop shows thermi's pictures, each its own way (docs/adr/0019-linux.md): GNOME Shell's extension, layer
/// surfaces, or X11 windows. Each shows a picture where thermi says, in the stage's logical pixels, takes the pointer only
/// on its boxes, shows a menu of the rows thermi gives, and tells the pointer, Esc, the monitors and the row chosen back; it
/// decides nothing.
/// </summary>
internal interface IOverlaySurfaces : IDisposable
{
    /// <summary>Whether pictures show now; until they do, thermi paints nothing.</summary>
    bool Ready { get; }

    /// <summary>Whether <see cref="Pointer"/> tells the pointer anywhere on the screen, past the pictures' boxes, as a fade needs it.</summary>
    bool PointerAnywhere { get; }

    /// <summary>The surfaces came, or came again: what showed before shows no more, and the monitors may be others.</summary>
    Action Arrived { get; set; }

    Action<IReadOnlyList<ShellMonitor>> MonitorsChanged { get; set; }

    Action<ShellPointer> Pointer { get; set; }

    /// <summary>A key on a picture a press holds, by its name: only Esc.</summary>
    Action<uint, string> Key { get; set; }

    /// <summary>A row of the menu last shown was chosen: its overlay, and the row's place among the rows shown.</summary>
    Action<uint, int> Chosen { get; set; }

    /// <summary>Starts the surfaces, as thermi starts; <see cref="Arrived"/> follows once they show.</summary>
    void Start();

    Task<IReadOnlyList<ShellMonitor>> MonitorsAsync();

    /// <summary>The monitors as the surfaces know them now, none where only a call tells them, as GNOME's extension's.</summary>
    IReadOnlyList<ShellMonitor> Monitors();

    /// <summary>Shows a picture with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>) of the stage; done once it shows.</summary>
    Task Show(uint id, ShellPicture picture, double x, double y);

    Task Move(uint id, double x, double y);

    Task Hide(uint id);

    /// <summary>Shows the picture at <paramref name="opacity"/>, from 0 to 1, as it is, without a new one: a fade near the pointer.</summary>
    Task Fade(uint id, double opacity);

    /// <summary>
    /// Shows a menu of <paramref name="rows"/> on picture <paramref name="id"/> at a point of the stage, as the desktop's own;
    /// done once it was sent.
    /// </summary>
    Task Menu(uint id, double x, double y, IReadOnlyList<ShellRow> rows);

    /// <summary>Takes Esc to the picture while <paramref name="held"/>, as a press holds it, and gives it back after.</summary>
    void HoldKeys(uint id, bool held);

    /// <summary>Tells the pointer anywhere while <paramref name="on"/>, as an overlay that fades near it shows.</summary>
    void Watch(bool on);

    /// <summary>
    /// The desktop's token for a program thermi starts on a click at <paramref name="time"/>, the click's, or now for 0, or
    /// empty: the window that program shows with it comes in front.
    /// </summary>
    Task<string> ActivationAsync(uint time);
}
