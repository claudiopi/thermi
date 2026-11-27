using Thermi.Core.Placement;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Overlay;

/// <summary>
/// What the running overlay tells agents: whether its overlays can show on this desktop now, and its monitors as Core's
/// <see cref="Screen"/>s, in thermi's pixels, as it places the overlays on them. It is the answer of the
/// <see cref="OverlayObject"/>'s <see cref="OverlayObject.State"/>, which Gio alone asks: so <c>thermi-cli</c> asks it
/// with no GTK and no display, as over SSH, and every desktop answers the same way.
/// </summary>
internal sealed record OverlayState(bool CanShow, IReadOnlyList<Screen> Screens)
{
    // A screen: its id, whether it is the primary one, its bounds, its work area and scale, then its taskbar, tray, centre
    // and end, each a list of none or one.
    private const string ScreenShape = "(sb(iiii)(iiii)da(iiii)a(iiii)a(iiii)a(iiii))";

    /// <summary>The answer's arguments: whether the overlays can show, and the screens.</summary>
    public const string Arguments = $"ba{ScreenShape}";

    /// <summary>What this session's overlay tells, or null where none runs, or one of another shape answers.</summary>
    public static OverlayState? Ask() =>
        SessionBus.Call(ShellLink.Name, OverlayObject.Path, OverlayObject.Name, OverlayObject.State) is { } answer
        && answer.GetTypeString() == $"({Arguments})"
            ? Of(answer)
            : null;

    public GLib.Variant Variant() => GLib.Variant.NewTuple(
    [
        GLib.Variant.NewBoolean(CanShow),
        GLib.Variant.NewArray(GLib.VariantType.New(ScreenShape), [.. Screens.Select(ScreenVariant)]),
    ]);

    private static GLib.Variant ScreenVariant(Screen screen) => GLib.Variant.NewTuple(
    [
        GLib.Variant.NewString(screen.Id),
        GLib.Variant.NewBoolean(screen.Primary),
        RectVariant(screen.Bounds),
        RectVariant(screen.Work),
        GLib.Variant.NewDouble(screen.Scale),
        .. new[] { screen.Taskbar, screen.Tray, screen.Centre, screen.End }.Select(rect =>
            GLib.Variant.NewArray(GLib.VariantType.New("(iiii)"), rect is { } each ? [RectVariant(each)] : [])),
    ]);

    private static GLib.Variant RectVariant(PixelRect rect) => GLib.Variant.NewTuple(
        [GLib.Variant.NewInt32(rect.Left), GLib.Variant.NewInt32(rect.Top), GLib.Variant.NewInt32(rect.Right), GLib.Variant.NewInt32(rect.Bottom)]);

    private static OverlayState Of(GLib.Variant state)
    {
        var screens = state.GetChildValue(1);
        return new OverlayState(
            state.GetChildValue(0).GetBoolean(),
            [.. Enumerable.Range(0, (int)screens.NChildren()).Select(at => ScreenOf(screens.GetChildValue((nuint)at)))]);
    }

    private static Screen ScreenOf(GLib.Variant screen)
    {
        PixelRect? Optional(nuint at)
        {
            var list = screen.GetChildValue(at);
            return list.NChildren() == 1 ? RectOf(list.GetChildValue(0)) : null;
        }

        return new Screen(
            screen.GetChildValue(0).GetString(out _),
            screen.GetChildValue(1).GetBoolean(),
            RectOf(screen.GetChildValue(2)),
            RectOf(screen.GetChildValue(3)),
            screen.GetChildValue(4).GetDouble())
        {
            Taskbar = Optional(5),
            Tray = Optional(6),
            Centre = Optional(7),
            End = Optional(8),
        };
    }

    private static PixelRect RectOf(GLib.Variant rect) => new(
        rect.GetChildValue(0).GetInt32(), rect.GetChildValue(1).GetInt32(), rect.GetChildValue(2).GetInt32(), rect.GetChildValue(3).GetInt32());
}
