using Thermi.App.Link;
using Thermi.App.Window;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using ItemHistory = Thermi.Core.Look.ItemHistory;
using WindowEdited = Thermi.Core.Messages.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// What the overlay and the settings window tell each other on every OS (docs/adr/0009-overlay-and-window-messages.md),
/// each end as App decides it, whatever carries it: Windows' hidden windows or Linux's session bus.
/// </summary>
public sealed class WindowLinkTests : IDisposable
{
    private readonly SettingsFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    // The desktop tells a closed window nothing, and a double-click starts it. Open, it takes what is clicked, pointed at and
    // done, and asks for its ten minutes; the overlay it edits marks its values and the others fade, their frame, or whole
    // where their pills are the opposite of the taskbar's, and one a drag holds shows whole over it all. Closed, it marks none.
    [Fact]
    public async Task AnOpenWindowAloneTakesWhatTheDesktopDoes()
    {
        await using var settings = await _folder.LoadedAsync(new ThermiConfig { Overlays = [new OverlayConfig(), new OverlayConfig()] });
        var channel = new Channel();
        var paints = 0;
        var end = new OverlayEnd(channel, settings, new ItemHistory(_folder.Clock), () => paints++);
        var light = new OverlayConfig { Style = new StyleConfig { Pills = PillTheme.Light } };
        var moved = new Gesture(GestureKind.Move, "before", "after");
        end.Start(firstStart: true);

        end.Selected(new Select(1, Metrics.CpuLoad.Id, SelectHow.Click));
        end.Point(0, Metrics.CpuLoad.Id);
        end.Gestured(moved);
        end.OpenWindow(0, Metrics.CpuLoad.Id);

        Assert.Equal((new Select(1, Metrics.CpuLoad.Id, SelectHow.DoubleClick), true), Assert.Single(channel.Sent));
        Assert.Equal((0, Metrics.CpuLoad.Id, false), Assert.Single(channel.Opened));
        Assert.Equal((false, null, WindowMarks.None), (end.OpenedOnce, end.Editing(2), end.Marks(0, end.Editing(2), light, lightTaskbar: false)));

        channel.Running = true;
        channel.Sent.Clear();
        Assert.False(end.Heard(new WindowEdited(2, Metrics.RamUsed.Id, Metrics.CpuLoad.Id)));
        Assert.True(end.Heard(new HistoryAsked()));
        end.Point(0, Metrics.GpuLoad.Id);
        end.Point(0, null);
        end.Selected(new Select(2, null, SelectHow.Click));
        end.Gestured(moved);
        end.OpenSize(1);

        (WindowMessage, bool)[] told =
        [
            (new Select(1, Metrics.CpuLoad.Id, SelectHow.PointOut), false),
            (new Select(1, Metrics.GpuLoad.Id, SelectHow.PointIn), false),
            (new Select(1, Metrics.GpuLoad.Id, SelectHow.PointOut), false),
            (new Select(2, null, SelectHow.Click), true),
            (moved, false),
            (new Select(2, null, SelectHow.Size), true),
        ];
        Assert.Equal(told, channel.Sent);
        Assert.Single(channel.Opened);
        var editing = end.Editing(2);
        WindowMarks[] marks =
        [
            new(EditFade.None, Metrics.RamUsed.Id, Metrics.CpuLoad.Id),
            new(EditFade.Whole, null, null),
            new(EditFade.Frame, null, null),
        ];
        WindowMarks[] shown = [end.Marks(1, editing, light, lightTaskbar: false), end.Marks(0, editing, light, false), end.Marks(0, editing, light, true)];
        Assert.Equal(marks, shown);
        var look = OverlayLook.Of(new StyleConfig(), lightTaskbar: false);
        var (frame, whole) = (EditLooks.Applied(look, EditFade.Frame), EditLooks.Applied(look, EditFade.Whole));
        var held = EditLooks.Applied(look, EditFade.Whole, held: true);
        Assert.Equal((look.Background * 0.3, TextEffect.Outline, look.Opacity * 0.5), (frame.Background, frame.Effect, whole.Opacity));
        Assert.Equal((1d, 1d), (held.Background, held.Opacity));

        channel.Running = false;
        end.LookForWindow();
        Assert.Equal((true, null, 1), (end.OpenedOnce, end.Editing(2), paints));
    }

    // QA round 5 (R5-07 and the owner): the window's marks guide the person while it shows: minimized, it tells the overlay
    // it edits none, so no value is marked nor overlay faded, and restored, the overlay and value it edits again.
    [Fact]
    public async Task AMinimizedWindowMarksNoOverlay()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig { Overlays = [new OverlayConfig(), new OverlayConfig()] }));
        await using var settings = await _folder.LoadedAsync();
        var sent = new List<WindowEdited>();
        using var link = new WindowEnd(
            new ChartHistory(_folder.Clock),
            new WindowEditor(settings, action => action(), _folder.Log, _folder.Clock),
            _ => { },
            () => { },
            _folder.Clock,
            sent.Add);

        link.Editing(1, Metrics.CpuLoad.Id, null);
        foreach (var shown in new[] { false, true })
        {
            _folder.Pass(TimeSpan.FromMilliseconds(200));
            link.Shown(shown);
        }

        Assert.Equal([new WindowEdited(2, Metrics.CpuLoad.Id, null), new WindowEdited(null, null, null), new WindowEdited(2, Metrics.CpuLoad.Id, null)], sent);
    }

    // A window that runs or not, and takes what is sent while it runs.
    private sealed class Channel : IWindowChannel
    {
        public bool Running { get; set; }

        public List<(WindowMessage Message, bool Front)> Sent { get; } = [];

        public List<(int Overlay, string? Item, bool SizeRow)> Opened { get; } = [];

        public bool WindowOpen() => Running;

        public bool Send(WindowMessage message, bool front)
        {
            Sent.Add((message, front));
            return Running;
        }

        public void Open(int overlay, string? item, bool sizeRow) => Opened.Add((overlay, item, sizeRow));
    }
}
