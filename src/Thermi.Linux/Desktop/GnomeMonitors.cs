using Thermi.Core.Placement;

namespace Thermi.Linux.Desktop;

/// <summary>
/// The monitors as GNOME's Mutter lays them out, from <c>org.gnome.Mutter.DisplayConfig</c>'s state: each logical monitor
/// in the stage's pixels, with its scale, and GNOME's top bar as the taskbar along the top of the primary one. Another
/// desktop, or none, gives none.
/// </summary>
internal static class GnomeMonitors
{
    /// <summary>The top bar's height in the stage's pixels at a scale of 1, as GNOME draws it since GNOME 40.</summary>
    public const int TopBar = 32;

    // Mutter's layout modes: in a logical layout a monitor's place and size are in the stage's pixels, its mode's over its scale.
    private const uint Logical = 1;

    public static IReadOnlyList<Screen> Read()
    {
        var state = SessionBus.Call("org.gnome.Mutter.DisplayConfig", "/org/gnome/Mutter/DisplayConfig", "org.gnome.Mutter.DisplayConfig", "GetCurrentState");
        if (state is null)
        {
            return [];
        }

        var modes = CurrentModes(state.GetChildValue(1));
        var logical = SessionBus.Lookup(state.GetChildValue(3), "layout-mode")?.GetUint32() is null or Logical;
        var screens = new List<Screen>();
        var placed = state.GetChildValue(2);
        for (nuint index = 0; index < placed.NChildren(); index++)
        {
            if (ScreenOf(placed.GetChildValue(index), modes, logical) is { } screen)
            {
                screens.Add(screen);
            }
        }

        return screens;
    }

    // A logical monitor: (x, y, scale, transform, primary, its monitors' (connector, vendor, product, serial), properties).
    private static Screen? ScreenOf(GLib.Variant monitor, Dictionary<string, (int Width, int Height)> modes, bool logical)
    {
        var x = monitor.GetChildValue(0).GetInt32();
        var y = monitor.GetChildValue(1).GetInt32();
        var scale = monitor.GetChildValue(2).GetDouble();
        // A quarter or three quarters turn lays the mode on its side.
        var turned = monitor.GetChildValue(3).GetUint32() % 2 == 1;
        var primary = monitor.GetChildValue(4).GetBoolean();
        var shown = monitor.GetChildValue(5);
        if (shown.NChildren() == 0 || !modes.TryGetValue(shown.GetChildValue(0).GetChildValue(0).GetString(out _), out var size))
        {
            return null;
        }

        var (width, height) = turned ? (size.Height, size.Width) : size;
        var bounds = logical
            ? new PixelRect(x, y, x + (int)Math.Round(width / scale), y + (int)Math.Round(height / scale))
            : new PixelRect(x, y, x + width, y + height);
        var bar = (int)Math.Round(TopBar * (logical ? 1 : scale));
        var id = shown.GetChildValue(0).GetChildValue(0).GetString(out _);
        return primary
            ? new Screen(id, Primary: true, bounds, bounds with { Top = bounds.Top + bar }, scale) { Taskbar = bounds with { Bottom = bounds.Top + bar } }
            : new Screen(id, Primary: false, bounds, bounds, scale);
    }

    // Each monitor's connector and the size of its current mode: (spec (connector, ...), modes (id, width, height, ..., properties), ...).
    private static Dictionary<string, (int Width, int Height)> CurrentModes(GLib.Variant monitors)
    {
        var current = new Dictionary<string, (int Width, int Height)>(StringComparer.Ordinal);
        for (nuint index = 0; index < monitors.NChildren(); index++)
        {
            var monitor = monitors.GetChildValue(index);
            var connector = monitor.GetChildValue(0).GetChildValue(0).GetString(out _);
            var modes = monitor.GetChildValue(1);
            for (nuint each = 0; each < modes.NChildren(); each++)
            {
                var mode = modes.GetChildValue(each);
                if (SessionBus.Lookup(mode.GetChildValue(6), "is-current")?.GetBoolean() == true)
                {
                    current[connector] = (mode.GetChildValue(1).GetInt32(), mode.GetChildValue(2).GetInt32());
                }
            }
        }

        return current;
    }
}
