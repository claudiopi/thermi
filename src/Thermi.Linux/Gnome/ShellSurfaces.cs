using Thermi.App.Gestures;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Linux.Overlay;

namespace Thermi.Linux.Gnome;

/// <summary>
/// GNOME's surfaces: the Shell's extension, once it runs thermi's protocol, as <see cref="ExtensionArrival"/> says. The
/// extension grabs the pointer and Esc itself on a press, tells a press another grab took as a <see cref="ShellPointer"/>
/// of kind <c>cancel</c>, so <see cref="IOverlaySurfaces.Lost"/> stays unheard, and tells the pointer anywhere while
/// thermi watches it.
/// </summary>
internal sealed class ShellSurfaces(ShellLink link, ExtensionArrival arrival) : IOverlaySurfaces
{
    public bool Ready => arrival.Ready;

    public bool PointerAnywhere => true;

    public Action Arrived { get; set; } = () => { };

    public Action<IReadOnlyList<ShellMonitor>> MonitorsChanged
    {
        get => link.MonitorsChanged;
        set => link.MonitorsChanged = value;
    }

    public Action<ShellPointer> Pointer
    {
        get => link.Pointer;
        set => link.Pointer = value;
    }

    public Action<uint, string> Key
    {
        get => link.Key;
        set => link.Key = value;
    }

    public Action<uint, int> Chosen
    {
        get => link.Chosen;
        set => link.Chosen = value;
    }

    public void Start()
    {
        link.Ready += protocol =>
        {
            if (arrival.Arrived(protocol))
            {
                Arrived();
            }
        };
        link.Gone += arrival.Left;
        arrival.Start();
        if (arrival.Ready)
        {
            Arrived();
        }
    }

    public Task<IReadOnlyList<ShellMonitor>> MonitorsAsync() => link.MonitorsAsync();

    public IReadOnlyList<ShellMonitor> Monitors() => [];

    public Task Show(uint id, ShellPicture picture, double x, double y) => link.Show(id, picture, x, y);

    public Task Move(uint id, double x, double y) => link.Move(id, x, y);

    public Task Hide(uint id) => link.Hide(id);

    public Task Fade(uint id, double opacity) => link.Fade(id, opacity);

    public Task Menu(uint id, IReadOnlyList<ShellRow> rows, PillBox anchor, MenuSide side) => link.Menu(id, anchor, side, rows);

    public void Cursor(uint id, PointerShape shape) => _ = link.Cursor(id, shape);

    public Task Copy(byte[] png) => link.Copy(png);

    public void HoldKeys(uint id, bool held)
    {
        // The extension takes Esc with the press's grab, and gives it back as the grab ends.
    }

    public void Watch(bool on) => _ = link.Watch(on);

    public Task<string> ActivationAsync(uint time) => link.ActivationAsync(time);

    public void Dispose()
    {
        // The link is the host's, which owns its name on the bus beyond GNOME's surfaces.
    }
}
