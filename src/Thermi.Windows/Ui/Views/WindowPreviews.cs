using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Pipe;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows.Gestures;
using Thermi.Windows.Overlay;
using Thermi.Windows.Ui.Editor;
using Thermi.Windows.Win32;

namespace Thermi.Windows.Ui.Views;

/// <summary>
/// Each overlay painted for the page's preview as its own window paints it, and each look's tile sample: Core's scene and
/// look, and the painter of "Copy as image", so neither can differ from what the overlay draws. A minimum, maximum or
/// average is the window's history's, which takes the overlay's, as the window's own values are. Used on the window's thread alone.
/// </summary>
/// <param name="editor">Whose overlays are painted.</param>
/// <param name="sensors">The readings now and whether the service runs.</param>
/// <param name="charts">The window's readings of each item, and the minute each line draws.</param>
/// <param name="screens">The monitors, whose scale an overlay paints at and whose taskbar it may sit on.</param>
internal sealed class WindowPreviews(WindowEditor editor, ISensorFeed sensors, WindowCharts charts, Func<IReadOnlyList<Screen>> screens)
{
    private readonly Lazy<double?> _baseClock = new(CpuWatch.ReadBaseClock);

    // The page asks for each preview every second; one that shows what it showed last is not painted and encoded again.
    private readonly Dictionary<int, (Drawn Drawn, OverlayPicture Picture)> _last = [];

    /// <summary>
    /// The overlay at its own monitor's scale, or at the smaller one at which it is <paramref name="room"/> pixels wide when
    /// that is more than 0 and it is wider; null for one the settings lack.
    /// </summary>
    public OverlayPicture? Preview(int overlay, int room, Texts texts)
    {
        var config = editor.Config;
        if (overlay < 0 || overlay >= config.Overlays.Count)
        {
            return null;
        }

        var one = config.Overlays[overlay];
        var light = Desktop.LightTaskbar();
        var scene = ThermiMark.Overlay(config, overlay, Input(texts, light), windowOpenedOnce: true);
        IReadOnlyList<OverlayItem> items =
            [.. scene.Items.Select(item => item.Graph is null ? item : item with { Graph = charts.Graph(one.Items.First(each => each.Id == item.Id)) })];
        var drawn = new Drawn(one, screens(), items, scene.SensorsStopped, OverlayLook.Of(one.Style, light, Desktop.HighContrast()), light, texts)
        {
            Room = room,
        };
        if (_last.TryGetValue(overlay, out var last) && last.Drawn.Shows(drawn))
        {
            return last.Picture;
        }

        var scale = OverlayWindow.ScaleOf(96, one, drawn.Screens);
        var bitmap = Painted(items, drawn.Stopped, drawn.Look, scale, texts);
        var taskbar = OnTaskbar(one, drawn.Screens, bitmap.Size) ? Taskbar.Of(light).Background.ToString() : null;

        // Painted again at the scale that fits, as the overlay paints itself smaller beside another; rounding up may leave a
        // pixel over, which another round takes off.
        for (var round = 0; room > 0 && bitmap.Width > room && round < 3; round++)
        {
            scale *= (float)room / bitmap.Width;
            bitmap.Dispose();
            bitmap = Painted(items, drawn.Stopped, drawn.Look, scale, texts);
        }

        using (bitmap)
        {
            var picture = PictureOf(bitmap, taskbar);
            _last[overlay] = (drawn, picture);
            return picture;
        }
    }

    public LookSamples Samples(IReadOnlyDictionary<string, StyleConfig> looks, Texts texts, double scale)
    {
        var light = Desktop.LightTaskbar();
        var contrast = Desktop.HighContrast();
        var input = Input(texts, light);
        OverlayPicture Sample(StyleConfig look)
        {
            var sample = LookSample.Of(look);
            var scene = ThermiMark.Overlay(editor.Config with { Overlays = [sample] }, 0, input, windowOpenedOnce: true);
            using var bitmap = Painted(scene.Items, scene.SensorsStopped, OverlayLook.Of(sample.Style, light, contrast), (float)scale, texts);
            return PictureOf(bitmap, taskbar: null);
        }

        return new LookSamples(Sample(new StyleConfig()), looks.ToDictionary(look => look.Key, look => Sample(look.Value), StringComparer.Ordinal));
    }

    private TrayInput Input(Texts texts, bool light) =>
        new(sensors.Latest, sensors.State, CultureInfo.CurrentCulture, texts, light, "")
        {
            History = charts.Own,
            WindowOpen = true,
            BaseClock = _baseClock.Value,
        };

    private static Bitmap Painted(IReadOnlyList<OverlayItem> items, bool stopped, OverlayLook look, float scale, Texts texts) =>
        new PaintedOverlay(items, look, scale, Size.Empty) { Stopped = stopped }.Picture(texts.Of(OverlayText.SensorsStopped));

    // By its middle, as a horizontal overlay snapped to a taskbar sits on it.
    private static bool OnTaskbar(OverlayConfig overlay, IReadOnlyList<Screen> screens, Size size) =>
        Snaps.Locate(overlay, screens, size.Width, size.Height) is { } at
        && Snaps.ScreenOf(overlay, screens)?.Taskbar is { } bar
        && bar.Contains(new PixelRect(at.X + (size.Width / 2), at.Y + (size.Height / 2), at.X + (size.Width / 2) + 1, at.Y + (size.Height / 2) + 1));

    private static OverlayPicture PictureOf(Bitmap bitmap, string? taskbar)
    {
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        var data = "data:image/png;base64," + Convert.ToBase64String(png.GetBuffer(), 0, (int)png.Length);
        return new OverlayPicture(data, bitmap.Width, bitmap.Height, taskbar);
    }

    // Everything a preview's picture is painted from; its lines are compared second by second, not as lists.
    private sealed record Drawn(
        OverlayConfig Overlay,
        IReadOnlyList<Screen> Screens,
        IReadOnlyList<OverlayItem> Items,
        bool Stopped,
        OverlayLook Look,
        bool Light,
        Texts Texts)
    {
        public int Room { get; init; }

        public bool Shows(Drawn other) =>
            Overlay == other.Overlay && Room == other.Room && Stopped == other.Stopped && Look == other.Look && Light == other.Light
            && ReferenceEquals(Texts, other.Texts) && Screens.SequenceEqual(other.Screens)
            && Items.Count == other.Items.Count && Items.Zip(other.Items).All(each => Same(each.First, each.Second));

        private static bool Same(OverlayItem one, OverlayItem other) =>
            one with { Graph = null } == other with { Graph = null } && (one.Graph ?? []).SequenceEqual(other.Graph ?? []);
    }
}
