using Thermi.Linux.Desktop;

namespace Thermi.Linux.LayerShell;

/// <summary>
/// Where a monitor's panels leave room, as layer surfaces learn it: a surface anchored to every edge and kept off the
/// panels' zones is given the rest of the monitor, whose size alone it is told, never its place: the room the panels keep
/// is taken to be along the top. It shows nothing and takes no pointer, and goes once it is measured, as a surface the
/// monitor's size holds a picture that size.
/// </summary>
internal sealed class LayerProbe : IDisposable
{
    private readonly Gtk.Window _window = Gtk.Window.New();
    private readonly Gtk.DrawingArea _area = Gtk.DrawingArea.New();

    public LayerProbe(Gdk.Monitor monitor, Action<LayerProbe, int> measured)
    {
        Monitor = monitor;
        LayerShellLibrary.Init(_window);
        LayerShellLibrary.SetNamespace(_window, "thermi-probe");
        LayerShellLibrary.SetLayer(_window, Layer.Background);
        LayerShellLibrary.SetMonitor(_window, monitor);
        foreach (var edge in Enum.GetValues<Edge>())
        {
            LayerShellLibrary.SetAnchor(_window, edge, anchored: true);
        }

        LayerShellLibrary.SetExclusiveZone(_window, 0);
        PictureWindow.Unpainted(_window);
        _window.SetChild(_area);
        _area.OnResize += (_, size) =>
        {
            if (size.Width > 0 && size.Height > 0)
            {
                measured(this, size.Height);
            }
        };
        _window.OnRealize += (_, _) => PictureWindow.TakeNoPointer(_window);
        _window.Present();
    }

    public Gdk.Monitor Monitor { get; }

    public void Dispose()
    {
        _window.Destroy();
        _window.Dispose();
        _area.Dispose();
    }
}
