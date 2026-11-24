using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Overlay;
using Thermi.Painting;
using Thermi.Tray;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Thermi.Windows.Tests;

/// <summary>
/// The tray icons in a notification area of the test's own: their ids, retries, Explorer's restarts, redraws, language,
/// clicks and pictures.
/// </summary>
[Collection(nameof(HostRun))]
public sealed class TrayIconsTests : IDisposable
{
    // Set to a folder, the picture test also saves the icons there, to look at them.
    private const string PicturesFolder = "THERMI_TRAY_PNG";

    // Set to 1, the program's icon test draws the icon into its file first: scripts/mark-icon.ps1.
    private const string IconWriting = "THERMI_WRITE_ICON";

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    // Every size Windows shows a program's icon at, from 100 % to 250 % and Explorer's largest view.
    private static readonly int[] ProgramIconSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    // The line under the mark sits on light and dark alike, in Explorer, Start and the taskbar, so it is a middle gray.
    private static readonly Rgb ProgramIconInk = Rgb.Parse("#8A8A8A");

    private readonly SettingsFolder _folder = new();
    private readonly FakeTray _tray = new();
    private readonly List<int> _opened = [];

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task EachIconIsAddedUnderItsIdOnceALeftoverIsGone()
    {
        var settings = await LoadedAsync(WithTray(new ThermiConfig(), 0, 0, 2));
        var items = settings.Config.Overlays[0].Items;
        Guid[] ids = [TrayScene.IdOf(null, FakeTray.ExePath), TrayScene.IdOf(items[0].Id, FakeTray.ExePath), TrayScene.IdOf(items[2].Id, FakeTray.ExePath)];
        var shown = new Dictionary<Guid, string>();

        // A thermi that ended without removing its icons left them, under the same ids.
        foreach (var id in ids)
        {
            _tray.Leave(id);
        }

        Run(settings, _ => shown = new Dictionary<Guid, string>(_tray.Shown));

        Assert.Equal(ids.Order(), shown.Keys.Order());
        Assert.DoesNotContain(FakeTray.Leftover, shown.Values);
        Assert.Empty(_tray.Shown);
    }

    [Fact]
    public async Task AnIconWindowsRefusesIsTriedAgainEveryFiveSecondsAndLoggedOnce()
    {
        var settings = await LoadedAsync(new ThermiConfig());
        _tray.Refusing = true;
        var (whileRefused, after) = (0, 0);

        Run(settings, host =>
        {
            Seconds(host, 11);
            whileRefused = Adds();
            _tray.Refusing = false;
            Seconds(host, 5);
            after = _tray.Shown.Count;
        });

        Assert.Equal(3, whileRefused);
        Assert.Equal(4, Adds());
        Assert.Single(_folder.Log.Entries);
        Assert.Equal(1, after);
    }

    [Fact]
    public async Task IconsComeBackWhenExplorerRestarts()
    {
        var settings = await LoadedAsync(WithTray(new ThermiConfig(), 0, 1));
        var (before, lost, back) = (0, 0, 0);

        Run(settings, host =>
        {
            before = _tray.Shown.Count;
            _tray.Restart();
            lost = _tray.Shown.Count;
            PInvoke.PostMessage(host.Hwnd, Topmost.TaskbarCreated, 0, 0);
            HostRun.Pump(host);
            back = _tray.Shown.Count;
        });

        Assert.Equal((2, 0, 2), (before, lost, back));
    }

    [Fact]
    public async Task TheIconsFollowTheLanguageWithoutARestart()
    {
        // The download's label is one that Spanish translates, as the default items' are not.
        var download = new ItemConfig { Id = Metrics.NetDownload.Id, Metric = Metrics.NetDownload.Id };
        var settings = await LoadedAsync(WithTray(new ThermiConfig() with { Overlays = [new OverlayConfig { Items = [download] }] }, 0, 0));
        var icon = TrayScene.IdOf(settings.Config.Overlays[0].Items[0].Id, FakeTray.ExePath);
        var label = Metrics.Find(settings.Config.Overlays[0].Items[0].Metric)!.Label;
        var tooltips = new List<string>();

        Run(settings, host =>
        {
            tooltips.Add(_tray.Shown[icon]);
            _folder.Write(ThermiConfigJson.Write(settings.Config with { Language = "es" }));
            settings.FileChanged();
            HostRun.Until(host, _folder.Clock, () => _tray.Shown[icon] != tooltips[0]);
            tooltips.Add(_tray.Shown[icon]);
        });

        Assert.StartsWith(Texts.Of("en").Of(label) + " ", tooltips[0], StringComparison.Ordinal);
        Assert.StartsWith(Texts.Of("es").Of(label) + " ", tooltips[1], StringComparison.Ordinal);
    }

    // A double-click on thermi's own icon opens nothing: its click already opens its menu.
    [Fact]
    public async Task ADoubleClickOnAnItemsIconOpensTheWindowOnItsOverlay()
    {
        var two = OverlayEdits.Add(new ThermiConfig()).Config;
        var settings = await LoadedAsync(WithTray(two, 1, 3));
        var item = TrayScene.IdOf(settings.Config.Overlays[1].Items[3].Id, FakeTray.ExePath);

        Run(settings, host =>
        {
            foreach (var id in new[] { item, TrayScene.IdOf(null, FakeTray.ExePath) })
            {
                var callback = _tray.Calls.First(call => call.Id == id && call.Message == NOTIFY_ICON_MESSAGE.NIM_ADD).Callback;
                PInvoke.PostMessage(host.Hwnd, callback, 0, (nint)PInvoke.WM_LBUTTONDBLCLK);
                HostRun.Pump(host);
            }
        });

        Assert.Equal([1], _opened);
    }

    // Every icon is drawn again at a new size, and one that leaves the scene in that same refresh must leave the tray too.
    [Fact]
    public void AnIconThatLeavesAsTheSizeChangesIsRemoved()
    {
        TrayIcon[] icons = [Icon("62", Taskbar.Dark.Normal), Icon("41", Taskbar.Dark.Warning)];
        var shown = new List<Guid[]>();

        using (var tray = new TrayIcons(HWND.Null, _tray.Environment(HostRun.WindowMutex()).Shell, _folder.Clock, _folder.Log))
        {
            tray.Refresh(icons, 16);
            tray.Refresh([icons[0]], 24);
            shown.Add([.. _tray.Shown.Keys]);
            _tray.Restart();
            tray.Restore();
            shown.Add([.. _tray.Shown.Keys]);
        }

        Assert.Equal([[icons[0].Id], [icons[0].Id]], shown);
    }

    // A change a busy Explorer lets time out is tried again, the icon kept where it was, never taken off and added anew; and
    // every call keeps Windows' own tooltip on, which a call without it, as a notice's, would turn off.
    [Fact]
    public void AChangeExplorerDropsIsTriedAgainAndTheTooltipStays()
    {
        TrayIcon[] warm = [Icon("62", Taskbar.Dark.Normal) with { Tooltip = "CPU 62 °C" }];
        TrayIcon[] hot = [warm[0] with { Text = "91", Color = Taskbar.Dark.Critical, Tooltip = "CPU 91 °C, critical" }];
        var tips = new List<string>();

        using (var tray = new TrayIcons(HWND.Null, _tray.Environment(HostRun.WindowMutex()).Shell, _folder.Clock, _folder.Log))
        {
            tray.Refresh(warm, 16);
            tray.Notify(warm[0].Id, "title", "text");
            _tray.Busy = 1;
            tray.Refresh(hot, 16);
            tray.Refresh(hot, 16);
            tips.Add(_tray.Shown[warm[0].Id]);
        }

        Assert.Equal([hot[0].Tooltip], tips);
        Assert.Single(_tray.Calls, call => call.Message == NOTIFY_ICON_MESSAGE.NIM_ADD);
        Assert.All(
            _tray.Calls.Where(call => call.Message != NOTIFY_ICON_MESSAGE.NIM_DELETE),
            call => Assert.True(call.Flags.HasFlag(NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP), call.ToString()));
    }

    [Theory]
    [InlineData(96, 16)]
    [InlineData(144, 24)]
    [InlineData(192, 32)]
    public void AnIconIsTheSmallIconSizeAtThePrimaryMonitorsScale(uint dpi, int size) => Assert.Equal(size, TrayIcons.SizeAt(dpi));

    // Each icon's ink is all of its color, its faint edges aside, and an item's characters fill it on its tighter axis;
    // thermi's mark as the one-color glyph of stopped sensors too.
    [Fact]
    public void AnIconsCharactersFillItInTheirColor()
    {
        foreach (var (name, taskbar) in new[] { ("dark", Taskbar.Dark), ("light", Taskbar.Light) })
        {
            TrayIcon[] icons =
            [
                Icon("62", taskbar.Normal),
                Icon("1087", taskbar.Warning),
                Icon("27.3", taskbar.Critical),
                Icon("12k", taskbar.Text),
                Icon(ReadingFormatter.Missing, taskbar.Text),
                Icon(null, taskbar.Text) with { Mark = MarkState.Stopped },
            ];
            foreach (var scale in new[] { 100, 150, 200 })
            {
                var size = TrayIcons.SizeAt((uint)(96 * scale / 100));
                var pictures = icons.Select(icon => TrayPainter.Paint(icon, size)).ToList();
                Save(pictures, taskbar, $"tray-t24-{name}-{scale}.png");
                foreach (var (icon, picture) in icons.Zip(pictures))
                {
                    var ink = Ink(picture);
                    Assert.NotEmpty(ink);
                    Assert.All(
                        ink.Where(pixel => pixel.Color.A == 255),
                        pixel => Assert.Equal(icon.Color, new Rgb(pixel.Color.R, pixel.Color.G, pixel.Color.B)));
                    Assert.True(
                        icon.Text is null or ReadingFormatter.Missing
                            || Math.Max(Span(ink, pixel => pixel.X), Span(ink, pixel => pixel.Y)) >= size - 1,
                        $"{icon.Text} at {size} px");
                }

                pictures.ForEach(picture => picture.Dispose());
            }
        }
    }

    // The program's icon is the mark in its app form, drawn by the tray's painter at each size Windows asks of an icon,
    // hand-placed pixels included; `pwsh scripts/mark-icon.ps1` draws it again into the file after the mark changes.
    // Another Windows may smooth the vector's edges a little differently, so a channel may differ by a little.
    [Fact]
    public void TheProgramsIconIsTheMark()
    {
        var file = Path.Combine(TestRecordings.RepoRoot(), "src", "Thermi", "thermi.ico");
        var painted = ProgramIconSizes
            .Select(size => TrayPainter.Paint(new TrayIcon(Guid.Empty, null, null, ProgramIconInk, "") { Mark = MarkState.App }, size))
            .ToList();
        if (Environment.GetEnvironmentVariable(IconWriting) == "1")
        {
            File.WriteAllBytes(file, IconFile(painted));
        }

        var icon = File.ReadAllBytes(file);
        Assert.Equal(ProgramIconSizes.Length, BitConverter.ToUInt16(icon, 4));
        foreach (var (picture, at) in painted.Select((picture, at) => (picture, at)))
        {
            var entry = 6 + (16 * at);
            using var stream = new MemoryStream(icon, BitConverter.ToInt32(icon, entry + 12), BitConverter.ToInt32(icon, entry + 8));
            using var frame = new Bitmap(stream);
            Assert.Equal(picture.Size, frame.Size);
            var far = Enumerable.Range(0, frame.Height)
                .SelectMany(y => Enumerable.Range(0, frame.Width).Where(x => Differs(picture.GetPixel(x, y), frame.GetPixel(x, y))))
                .Count();
            Assert.True(far == 0, $"{far} pixels of the {picture.Width} px icon are not the mark's");
        }

        painted.ForEach(picture => picture.Dispose());
    }

    // A faint edge's color says little, so colors are compared where both pixels are at least half opaque.
    private static bool Differs(Color painted, Color stored)
    {
        const int Near = 24;
        var color = new[] { painted.R - stored.R, painted.G - stored.G, painted.B - stored.B }.Max(Math.Abs);
        return Math.Abs(painted.A - stored.A) > Near || (Math.Min(painted.A, stored.A) >= 128 && color > Near);
    }

    // An .ico of PNG frames, which Windows reads at every size since Vista: the header, one entry per frame, then the frames.
    private static byte[] IconFile(List<Bitmap> pictures)
    {
        var frames = pictures.Select(picture =>
        {
            using var png = new MemoryStream();
            picture.Save(png, ImageFormat.Png);
            return png.ToArray();
        }).ToList();
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);
        var offset = 6 + (16 * frames.Count);
        foreach (var (picture, frame) in pictures.Zip(frames))
        {
            // A side of 256 is written as 0, the byte's only way to say it.
            writer.Write((byte)(picture.Width % 256));
            writer.Write((byte)(picture.Height % 256));
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(frame.Length);
            writer.Write(offset);
            offset += frame.Length;
        }

        frames.ForEach(writer.Write);
        writer.Flush();
        return file.ToArray();
    }

    private static TrayIcon Icon(string? text, Rgb color) => new(Guid.NewGuid(), text is null ? null : "item", text, color, "");

    private static List<(int X, int Y, Color Color)> Ink(Bitmap picture) =>
        [.. Enumerable.Range(0, picture.Height)
            .SelectMany(y => Enumerable.Range(0, picture.Width).Select(x => (x, y, picture.GetPixel(x, y))))
            .Where(pixel => pixel.Item3.A > 0)];

    private static int Span(List<(int X, int Y, Color Color)> ink, Func<(int X, int Y, Color Color), int> axis) => ink.Max(axis) - ink.Min(axis) + 1;

    // The icons side by side on the taskbar's color, as they show, and again four times as large, to see their pixels.
    private static void Save(List<Bitmap> pictures, Taskbar taskbar, string name)
    {
        if (Environment.GetEnvironmentVariable(PicturesFolder) is not { Length: > 0 } folder)
        {
            return;
        }

        var (size, cell) = (pictures[0].Width, (pictures[0].Width * 4) + 8);
        using var strip = new Bitmap((cell * pictures.Count) + 8, size + cell + 16);
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(PainterShapes.Drawn(taskbar.Background));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            for (var at = 0; at < pictures.Count; at++)
            {
                g.DrawImageUnscaled(pictures[at], 8 + (at * cell), 8);
                g.DrawImage(pictures[at], 8 + (at * cell), size + 16, size * 4, size * 4);
            }
        }

        strip.Save(Path.Combine(folder, name), ImageFormat.Png);
    }

    private static ThermiConfig WithTray(ThermiConfig config, int overlay, params int[] items) => config with
    {
        Overlays =
        [
            .. config.Overlays.Select((each, at) => at != overlay
                ? each
                : each with { Items = [.. each.Items.Select((item, index) => item with { Tray = items.Contains(index) })] }),
        ],
    };

    private Task<Settings> LoadedAsync(ThermiConfig config)
    {
        _folder.Write(ThermiConfigJson.Write(config));
        return _folder.LoadedAsync();
    }

    private int Adds() => _tray.Calls.Count(call => call.Message == NOTIFY_ICON_MESSAGE.NIM_ADD);

    private void Seconds(OverlayHost host, int seconds)
    {
        for (var second = 0; second < seconds; second++)
        {
            _folder.Clock.Advance(TimeSpan.FromSeconds(1));
            HostRun.Tick(host);
        }
    }

    private void Run(Settings settings, Action<OverlayHost> body) => TestWindows.OnOwnDesktop(() =>
    {
        using var host = new OverlayHost(
            settings,
            () => HostRun.Recorded,
            English,
            _folder.Clock,
            _folder.Log,
            _tray.Environment(HostRun.WindowMutex(), _opened.Add));
        host.Start();
        HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == settings.Config.Overlays.Count);
        body(host);
    });
}
