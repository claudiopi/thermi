using Thermi.App.Preview;
using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Settings;
using Thermi.Linux.Desktop;
using Thermi.Linux.Painting;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>The desktop gestures a person reported doing nothing on GNOME 50, with the overlay of their own settings.</summary>
public sealed partial class GestureTests
{
    // The first overlay of the person's settings: a GPU pill of two values, a RAM pill, and FPS, which takes no room, at 100 %.
    private const string Persons = """
        {"version":6,"overlays":[{"size":1,"style":{"gap":8,"units":true,"opacity":{"background":1,"overlay":1}},"items":[
        {"id":"gpu.load","metric":"gpu.load"},
        {"id":"/gpu-nvidia/0/temperature/0","sensor":"/gpu-nvidia/0/temperature/0","label":"GPU","threshold":{"warning":80,"critical":88}},
        {"id":"ram.used","metric":"ram.used","warnBy":{"metric":"ram.load"}},
        {"id":"game.fps","metric":"game.fps"}]}]}
        """;

    // Ctrl and the wheel at 100 %, floating below the top bar at the top right, where they had it, and on the top bar, which
    // caps it below 100 %; the temperature lifted and dropped on the load, before it in its own pill; and the
    // floating overlay's corner, pressed where its round pill draws nothing, dragged wider.
    private static async Task PersonsOwnAsync(CancellationToken cancel)
    {
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [TwoMonitors[0]], cancel);
        var persons = ThermiConfigJson.Read(Persons).Config.Overlays[0];
        var feed = await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var (width, height, places) = await PaintedAsync(persons, feed.Snapshot);
        var at = new Position(TwoMonitors[0].Width - width - 60, 60);
        var onBar = persons with { Snap = new SnapConfig(SnapSpot.Taskbar) { Offset = Scene.OnPanel.X } };
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [persons with { Position = at }, onBar] }, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);

        Assert.Equal((1.05, 1.0), await WheelAsync(desktop, thermi, (at.X + (width / 2), at.Y + (height / 2)), 0, cancel));
        var bar = await Scene.PanelTopAsync(desktop, 0, cancel);
        var capped = await WheelAsync(desktop, thermi, (Scene.OnPanel.X + 20, bar), 1, cancel, down: true);
        Assert.True(capped.First < 1 && capped.Then > capped.First, $"On the top bar the wheel turned it to {capped}.");

        // Dropped on the first third of the load's meter and value, the part of it a hold lifts alone, well before its middle.
        var (load, temperature) = (places[0].Alone, places[1].Value);
        await HoldAndDropAsync(desktop, thermi, Middle(at, temperature), (at.X + (int)(load.Left + (load.Width / 3)), Middle(at, load).Y), cancel);
        var moved = await Settings.ReadAsync(desktop.Session, cancel);
        Assert.Equal(["/gpu-nvidia/0/temperature/0", "gpu.load", "ram.used", "game.fps"], moved.Overlays[0].Items.Select(item => item.Id));

        await DragAsync(desktop, thermi, (at.X + width - 2, at.Y + height - 2), (at.X + width + 40, at.Y + height - 2), cancel);
        var resized = await Settings.ReadAsync(desktop.Session, cancel);
        Assert.True(resized.Overlays[0].Size > 1, $"The corner left it at {resized.Overlays[0].Size}.");
    }

    // A notch one way and then one the other over a point, with Control held: the overlay's size after each.
    private static async Task<(double First, double Then)> WheelAsync(
        TestDesktop desktop, SessionProcess thermi, (int X, int Y) over, int overlay, CancellationToken cancel, bool down = false)
    {
        var sizes = new List<double>();
        foreach (var notches in down ? new[] { -1, 1 } : [1, -1])
        {
            var applied = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
            await desktop.Input.WheelAsync(over.X, over.Y, notches, "Control_L", cancel);
            await applied;
            sizes.Add((await Settings.ReadAsync(desktop.Session, cancel)).Overlays[overlay].Size);
        }

        return (sizes[0], sizes[1]);
    }

    // The overlay as thermi paints it at 100 % on a monitor at 100 %, and where it draws each item.
    private static async Task<(int Width, int Height, IReadOnlyList<PaintedItem> Places)> PaintedAsync(OverlayConfig overlay, string snapshot)
    {
        var config = ThermiConfig.New with { Overlays = [overlay] };
        var series = await SnapshotPreview.ReadAsync(snapshot, TextWriter.Null) ?? throw new InvalidOperationException($"{snapshot} is no snapshot.");
        var shown = SnapshotPreview.Shown(config, series, DesktopLooks.BaseClock(), DesktopLooks.LightPanels);
        PangoCairo.Module.Initialize();
        using var painter = new Painter();
        using var picture = painter.Paint(shown.Items, OverlayLook.Of(overlay.Style, DesktopLooks.LightPanels, null), overlay.Size);
        return (picture.Width, picture.Height, [.. painter.Places]);
    }

    private static (int X, int Y) Middle(Position at, PillBox box) =>
        (at.X + (int)(box.Left + (box.Width / 2)), at.Y + (int)(box.Top + (box.Height / 2)));
}
