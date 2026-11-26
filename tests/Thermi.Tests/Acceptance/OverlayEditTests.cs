using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Overlays are made, moved, merged and hidden by edits in Core, each one undo step, from the window or the desktop.
/// </summary>
public sealed class OverlayEditTests
{
    private static ThermiConfig One => new();

    private static ThermiConfig Two => Done(OverlayEdits.Snap(Done(OverlayEdits.Add(One)), 1, new SnapConfig(SnapSpot.TopRight)));

    private static ThermiConfig Solo =>
        WithOverlay(Two, 1, overlay => overlay with { Items = [new ItemConfig { Id = "solo", Metric = Metrics.GpuTemperature.Id }] });

    private static ThermiConfig Free => WithOverlay(One, 0, overlay => overlay with { Position = new Position(400, 300) });

    private static ThermiConfig Bar => WithOverlay(One, 0, overlay => overlay with { Snap = new SnapConfig(SnapSpot.Taskbar) { Offset = 1000 } });

    private static ThermiConfig Four => Done(OverlayEdits.Add(Done(OverlayEdits.Add(Two))));

    // The CPU fan moved first, as a person orders the default items.
    private static ThermiConfig Reordered => Done(ItemEdits.Move(One, Metrics.CpuFan.Id, 0));

    // The CPU pill, its items alone in their overlay.
    private static ThermiConfig Cpu => WithOverlay(One, 0, overlay => overlay with { Items = [.. overlay.Items.Take(4)] });

    private static ThermiConfig Restyled(Func<StyleConfig, StyleConfig> change) =>
        WithOverlay(One, 0, overlay => overlay with { Style = change(overlay.Style) });

    // The GPU's pill between two of the CPU's dragged off, which leaves the second CPU pill a start of its own.
    private static ThermiConfig Split =>
        Done(OverlayEdits.DragOff(Done(ItemEdits.Move(One, Metrics.GpuLoad.Id, 2)), Metrics.GpuLoad.Id, new Position(400, 300)));

    // Where the edit starts, the edit, the overlays after it as their place, their items' count, h when hidden, v when
    // vertical and their size when not 100 %, or the refusal; then the name an overlay without a typed one shows, as its
    // place and that name's text, or "same" when no edit wrote a name.
    [Theory] // Rows: each edit's own path and refusal, the hold's 500 ms boundary, the cap checked at each site, and the nudge's clamp and guards
    [InlineData("reordered", "new", "Taskbar:6 TopRight:6", "1=TopRight")]
    [InlineData("two", "duplicate 0", "Taskbar:6 BottomRight:6 TopRight:6", "1=BottomRight")]
    [InlineData("two", "merge 1 0", "Taskbar:12", "same")]
    [InlineData("two", "delete 1", "Taskbar:6", "same")]
    [InlineData("two", "hide 1", "Taskbar:6 TopRight:6h", "same")]
    [InlineData("one", "move cpu.load new", "Taskbar:5 TopRight:1", "1=TopRight")]
    [InlineData("solo", "move solo 0", "Taskbar:7", "same")]
    [InlineData("one", "drag cpu.load 400 300", "Taskbar:2 400,300:4", "")]
    [InlineData("solo", "drag solo 400 300", "Taskbar:6 400,300:1", "same")]
    [InlineData("free", "nudge 0 8 0", "408,300:6", "same")]
    [InlineData("free", "nudge 0 0 -1", "400,299:6", "same")]
    [InlineData("four", "new", nameof(Refusal.TooManyOverlays), "same")]
    [InlineData("four", "duplicate 0", nameof(Refusal.TooManyOverlays), "same")]
    [InlineData("four", "move cpu.load new", nameof(Refusal.TooManyOverlays), "same")]
    [InlineData("two", "rename 1 Desk", "Taskbar:6 TopRight:6", "")]
    [InlineData("two", "rename 1 ", nameof(Refusal.NameEmpty), "same")]
    [InlineData("two", "merge 5 0", nameof(Refusal.NotFound), "same")]
    [InlineData("two", "move none 1", nameof(Refusal.NotFound), "same")]
    [InlineData("one", "delete 0", nameof(Refusal.LastOverlay), "same")]
    [InlineData("four", "drag cpu.load 400 300", nameof(Refusal.TooManyOverlays), "same")]
    [InlineData("one", "drag none 400 300", nameof(Refusal.NotFound), "same")]
    [InlineData("one", "desk 0 hold500+gpu.load 400 300 5", nameof(Refusal.NotFound), "same")]
    [InlineData("free", "resize 0 4.01 vertical", nameof(Refusal.SizeOutOfRange), "same")]
    [InlineData("one", "nudge 0 8 0", nameof(Refusal.Snapped), "same")]
    [InlineData("bar", "nudge 0 8 0", "Taskbar@1008:6", "same")]
    [InlineData("bar", "nudge 0 -2000 0", "Taskbar@0:6", "same")]
    [InlineData("bar", "nudge 0 0 -1", nameof(Refusal.Snapped), "same")]
    [InlineData("solo", "desk 1 solo 400 300 0", "Taskbar:7", "same")]
    [InlineData("solo", "desk 1 solo 20 1306", "Taskbar:6 BottomLeft:1", "same")]
    [InlineData("one", "desk 0 hold500+gpu.load 400 300", "Taskbar:5 400,300:1", "")]
    [InlineData("one", "desk 0 hold499+gpu.load 400 300", "400,300:6", "same")]
    [InlineData("one", "desk 0 edge 400 300 0", "400,300:6", "same")]
    [InlineData("free", "desk 0 edge 1500 1380", "Taskbar:6", "same")]
    [InlineData("free", "corner 400 38 -300 200", "400,300:6v0.75", "same")]
    [InlineData("free", "corner 400 38 199 5", "400,300:61.5", "same")]
    [InlineData("free", "corner 400 38 2000 5", "400,300:64", "same")]
    [InlineData("one", "desk 0 hold500+cpu.load 400 300", "Taskbar:2 400,300:4", "")]
    [InlineData("one", "desk 0 hold500+value:cpu.load 1300 1375", "Taskbar:5 Taskbar@1300:1", "")]
    [InlineData("four", "desk 0 hold500+value:cpu.load 400 300", nameof(Refusal.TooManyOverlays), "same")]
    [InlineData("cpu", "desk 0 hold500+value:cpu.load 400 300", "Taskbar:3 400,300:1", "")]
    [InlineData("cpu", "desk 0 hold500+cpu.load 400 300", "400,300:4", "same")]
    [InlineData("unlabeled", "desk 0 hold500+value:cpu.load 400 300", "Taskbar:2 400,300:4", "")]
    [InlineData("onebar", "desk 0 hold500+value:cpu.load 400 300", "Taskbar:5 400,300:1", "")]
    [InlineData("onebar", "desk 0 hold500+cpu.load 400 300", "Taskbar:2 400,300:4", "")]
    public void OverlaysAreMadeMovedMergedAndHiddenAsOneStepEach(string start, string edit, string expected, string named)
    {
        var before = start switch
        {
            "two" => Two,
            "solo" => Solo,
            "free" => Free,
            "four" => Four,
            "reordered" => Reordered,
            "bar" => Bar,
            "cpu" => Cpu,
            "unlabeled" => Restyled(style => style with { NoLabels = true }),
            "onebar" => Restyled(style => style with { Layout = PillLayout.One }),
            _ => One,
        };
        var history = new UndoHistory();

        var result = Edit(before, edit.Split(' '));
        if (result.Refused is { } refused)
        {
            Assert.Equal((expected, File(before)), (refused.ToString(), File(result.Config)));
            return;
        }

        var after = Done(history, before, _ => result);
        var undone = history.Undo(after)!;

        Assert.Equal(expected, string.Join(' ', after.Overlays.Select((overlay, at) => Summary(after, overlay, at))));
        Assert.Equal((File(before), 0), (File(undone.Config), undone.Skipped));
        Assert.False(history.CanUndo);
        Assert.InRange(after.Overlays.Count, 1, ThermiConfig.MaxOverlays);
        // A new overlay's default items come in the order the first overlay gives them.
        Assert.True(edit != "new" || after.Overlays[^1].Items.Select(item => item.Metric).SequenceEqual(after.Overlays[0].Items.Select(item => item.Metric)));
        Assert.True(named switch
        {
            "same" => Names(after).All(Names(before).Contains),
            "" => true,
            _ => Names(after)[named[0] - '0'] is null
                && OverlayNames.Of(after, named[0] - '0', Texts.English) == Texts.English.Of(Enum.Parse<NameText>(named[2..])),
        });
    }

    private static EditResult Edit(ThermiConfig config, string[] words)
    {
        int At(int word)
        {
            return int.Parse(words[word], CultureInfo.InvariantCulture);
        }

        return words[0] switch
        {
            "new" => OverlayEdits.Add(config),
            "duplicate" => OverlayEdits.Duplicate(config, At(1)),
            "merge" => OverlayEdits.Merge(config, At(1), At(2)),
            "delete" => OverlayEdits.Delete(config, At(1)),
            "hide" => OverlayEdits.SetHidden(config, At(1), hidden: true),
            "rename" => OverlayEdits.Rename(config, At(1), words[2]),
            "move" => OverlayEdits.MoveItem(config, words[1], words[2] == "new" ? null : At(2)),
            "drag" => OverlayEdits.DragOff(config, words[1], new Position(At(2), At(3))),
            "resize" => OverlayEdits.Resize(
                config,
                At(1),
                double.Parse(words[2], CultureInfo.InvariantCulture),
                Enum.Parse<Orientation>(words[3], ignoreCase: true)),
            "desk" => Desk(config, words),
            "corner" => Corner(config, DesktopGestures.Corner(1, At(1), At(2), At(3), At(4))),
            _ => OverlayEdits.Nudge(config, At(1), At(2), At(3)),
        };
    }

    // The first overlay's corner dragged on the desktop, at 100 %.
    private static EditResult Corner(ThermiConfig config, (double Size, Orientation Orientation) dragged) =>
        OverlayEdits.Resize(config, 0, dragged.Size, dragged.Orientation);

    // An item's pill, pressed on its label, or the item, pressed on its value as "value:", held still for the milliseconds
    // its word says as "hold500+", by the injected clock, or the edge, of an overlay as the main monitor shows it, dropped
    // where the words say, near a snap mark or onto the overlay they name, before the pill of the item they name next ("-"
    // past the last), or with "in" last before that item of its own pill.
    private static EditResult Desk(ThermiConfig config, string[] words)
    {
        var dropped = new Position(int.Parse(words[3], CultureInfo.InvariantCulture), int.Parse(words[4], CultureInfo.InvariantCulture));
        var mark = Snaps.Near([SnapTests.Main], Orientation.Horizontal, 600, 57, dropped);
        int? onto = words.Length > 5 ? int.Parse(words[5], CultureInfo.InvariantCulture) : null;
        var overlay = int.Parse(words[1], CultureInfo.InvariantCulture);
        var plus = words[2].IndexOf('+', StringComparison.Ordinal);
        var time = new FakeTimeProvider();
        var pressed = time.GetTimestamp();
        time.Advance(TimeSpan.FromMilliseconds(plus < 0 ? 0 : int.Parse(words[2][4..plus], CultureInfo.InvariantCulture)));
        var on = words[2][(plus + 1)..];
        var value = on.StartsWith("value:", StringComparison.Ordinal);
        var grip = DesktopGestures.Grip(config, on == "edge" ? null : on.Replace("value:", "", StringComparison.Ordinal), onLabel: !value);
        var carried = DesktopGestures.Holds(config, overlay, grip, time.GetElapsedTime(pressed)) ? grip : null;
        var target = new DropTarget(onto, words.Length > 6 && words[6] != "-" ? words[6] : null, InPill: words.Length > 7 && words[7] == "in");
        return DesktopGestures.Release(config, overlay, carried, dropped, target, mark).Result;
    }

    // A lifted pill, or an overlay moved whole, dropped on an overlay joins it where it was let go, before the pill it was
    // dropped in front of, or at its end; a pill dropped back on its own overlay moves there, and stays put on its own place.
    // An item lifted alone moves within its own pill dropped on it, and elsewhere joins as a pill does.
    // Where the drop starts, its words as the desk edit's, and the items of the overlay it lands on, in their order.
    [Theory] // Rows: each place its steps state for a pill, an item and a whole overlay, two of them a QA bug's drops
    [InlineData("solo", "0 hold500+gpu.load 400 300 1 solo", "gpu.load solo")]
    [InlineData("solo", "0 hold500+gpu.load 400 300 1", "solo gpu.load")]
    [InlineData("solo", "1 solo 400 300 0 gpu.load", "cpu.temperature fan.cpu cpu.load cpu.power solo gpu.load ram.used")]
    [InlineData("one", "0 hold500+gpu.load 400 300 0 cpu.temperature", "gpu.load cpu.temperature fan.cpu cpu.load cpu.power ram.used")]
    [InlineData("one", "0 hold500+gpu.load 400 300 0 gpu.load", "cpu.temperature fan.cpu cpu.load cpu.power gpu.load ram.used")]
    [InlineData("one", "0 hold500+value:cpu.temperature 400 300 0 cpu.power in", "fan.cpu cpu.load cpu.temperature cpu.power gpu.load ram.used")]
    [InlineData("one", "0 hold500+value:cpu.temperature 400 300 0 - in", "fan.cpu cpu.load cpu.power cpu.temperature gpu.load ram.used")]
    [InlineData("one", "0 hold500+value:cpu.load 400 300 0 cpu.load in", "cpu.temperature fan.cpu cpu.load cpu.power gpu.load ram.used")]
    [InlineData("one", "0 hold500+value:cpu.load 400 300 0 ram.used", "cpu.temperature fan.cpu cpu.power gpu.load cpu.load ram.used")]
    [InlineData("solo", "0 hold500+value:cpu.load 400 300 1 solo", "cpu.load solo")]
    public void ADropOnAnOverlayJoinsItWhereItIsLetGo(string start, string drop, string items)
    {
        var before = start == "solo" ? Solo : One;
        var words = drop.Split(' ');

        var after = Done(Edit(before, ["desk", .. words]));

        Assert.Equal(items, string.Join(' ', after.Overlays[int.Parse(words[4], CultureInfo.InvariantCulture)].Items.Select(item => item.Id)));
    }

    // The list's moves: a pill's row by Alt+arrows among its overlay's pills, up to either end however far, and an
    // item's row dropped on an overlay's, before the pill or item it lands on or, on its heading, past its last, an item
    // apart above a pill starting one of its own; an overlay left empty goes, each move is one undo step, and one that moves
    // nothing, past an end or onto its own place, is refused, so the page that waits to say it lets go. Where it
    // starts, the move, then each overlay's items in order, an item that starts a pill of its own marked *, or the refusal.
    [Theory]
    [InlineData("one", "pill gpu.load -1", "gpu.load cpu.temperature fan.cpu cpu.load cpu.power ram.used")]
    [InlineData("one", "pill ram.used 1", nameof(Refusal.NotMoved))]
    [InlineData("one", "pill gpu.load 2147483647", "cpu.temperature fan.cpu cpu.load cpu.power ram.used gpu.load")]
    [InlineData("two", "itemto cpu.power 1 gpu.load-2", "cpu.temperature fan.cpu cpu.load gpu.load ram.used | "
        + "cpu.temperature-2 fan.cpu-2 cpu.load-2 cpu.power-2 cpu.power gpu.load-2 ram.used-2")]
    [InlineData("two", "itemto cpu.power 1 gpu.load-2 apart", "cpu.temperature fan.cpu cpu.load gpu.load ram.used | "
        + "cpu.temperature-2 fan.cpu-2 cpu.load-2 cpu.power-2 cpu.power* gpu.load-2 ram.used-2")]
    [InlineData("solo", "itemto solo 0 -", "cpu.temperature fan.cpu cpu.load cpu.power gpu.load ram.used solo")]
    [InlineData("one", "itemto cpu.load 0 cpu.load", nameof(Refusal.NotMoved))]
    [InlineData("one", "itemto cpu.load 0 nobody", nameof(Refusal.NotFound))]
    [InlineData("one", "pill nobody -1", nameof(Refusal.NotFound))]
    public void TheListMovesPillsAndItemsAmongOverlays(string start, string move, string expected)
    {
        var before = start switch
        {
            "two" => Two,
            "solo" => Solo,
            _ => One,
        };
        var words = move.Split(' ');
        PageEdit edit = words[0] switch
        {
            "pill" => new MovePillEdit(words[1], int.Parse(words[2], CultureInfo.InvariantCulture)),
            _ => new DropItemEdit(words[1], int.Parse(words[2], CultureInfo.InvariantCulture), words[3] == "-" ? null : words[3], words.Length > 4),
        };
        var history = new UndoHistory();

        var result = PageEdits.Of(edit, TestRecordings.Read(TestRecordings.Admin), Texts.English, () => new HashSet<int>())(before);
        if (result.Refused is { } refused)
        {
            Assert.Equal((expected, File(before)), (refused.ToString(), File(result.Config)));
            return;
        }

        var after = Done(history, before, _ => result);
        var moved = File(after) != File(before);
        var shown = after.Overlays.Select(overlay => string.Join(' ', overlay.Items.Select(item => item.Own ? $"{item.Id}*" : item.Id)));
        Assert.Equal(expected, string.Join(" | ", shown));
        Assert.Equal(moved, history.CanUndo);
        Assert.Equal(File(before), moved ? File(history.Undo(after)!.Config) : File(after));
    }

    // The window lists the pills the overlay labels, in every layout, and a join or a split leaves every pill a start of
    // its own: a file joined by hand with its single bar over GPU, CPU and RAM; the GPU pill between two of the CPU's
    // dragged off; an overlay of the RAM merged after the RAM's pill; an item lifted off its pill, before it or within it,
    // or off the second of two CPU pills, which stays apart. Where it starts, the edit, then the first overlay's pills after
    // it as each one's label and its items' count.
    [Theory] // Rows: each layout of the joined file and each join or split its steps state, one of them a fix's bug
    [InlineData("joined", "", "GPU:2 CPU:4 RAM:1")]
    [InlineData("joined pills", "", "GPU:2 CPU:3 CPU:1 RAM:1")]
    [InlineData("joined each", "", "GPU:1 GPU:1 CPU:1 CPU:1 CPU:1 CPU:1 RAM:1")]
    [InlineData("between", "drag gpu.load 400 300", "CPU:2 CPU:2 RAM:1")]
    [InlineData("ram", "merge 1 0", "CPU:4 GPU:1 RAM:1 RAM:1")]
    [InlineData("one", "desk 0 hold500+value:cpu.load 400 300 0 cpu.temperature", "CPU:1 CPU:3 GPU:1 RAM:1")]
    [InlineData("split", "desk 0 hold500+value:cpu.load 400 300", "CPU:2 CPU:1 RAM:1")]
    [InlineData("split", "desk 0 hold500+value:cpu.load 400 300 0 - in", "CPU:2 CPU:2 RAM:1")]
    public void TheWindowListsThePillsTheOverlayLabels(string start, string edit, string expected)
    {
        var joined = ThermiConfigJson.Read(TestFiles.Config("joined.json")).Config;
        var before = start.Split(' ') switch
        {
            ["joined", var layout] =>
                WithOverlay(joined, 0, overlay => overlay with { Style = overlay.Style with { Layout = Enum.Parse<PillLayout>(layout, true) } }),
            ["joined"] => joined,
            ["between"] => Done(ItemEdits.Move(One, Metrics.GpuLoad.Id, 2)),
            ["one"] => One,
            ["split"] => Split,
            _ => WithOverlay(Two, 1, overlay => overlay with { Items = [new ItemConfig { Id = "ram2", Metric = Metrics.RamUsed.Id }] }),
        };

        var after = edit.Length == 0 ? before : Done(Edit(before, edit.Split(' ')));
        var runs = OverlayLayout.Runs(after.Overlays[0], Texts.English);
        var drawn = OverlayLayout.Build(after.Overlays[0], TrayTextTests.Quiet, CultureInfo.InvariantCulture, Texts.English, windowOpen: true);

        Assert.Equal(expected, string.Join(' ', runs.Select(run => $"{run.Label}:{run.Last - run.First + 1}")));
        Assert.Equal(runs.Select(run => run.Label), drawn.Select(item => item.Label).OfType<string>());
    }

    [Theory] // Rows: what the page's own test leaves: the other number format with a unit, rounding, the low end, no finite number, a half
    [InlineData("size 137,5 %", "es", "size 1.38", "")]
    [InlineData("background -5", "en", "opacity 0 0.5", "range|0 %|100 %|0 %")]
    [InlineData("gap 7.5 px", "en", "gap 8", "")]
    [InlineData("background NaN", "en", "opacity 1 0.5", "kept|100 %")]
    [InlineData("valuesize 23.3 px", "en", "value 23.5 Custom", "")]
    public void ASliderBoxKeepsWhatIsTypedAsItsFieldKeepsIt(string typed, string language, string expected, string said)
    {
        var words = typed.Split(' ', 2);
        var field = Enum.Parse<TypedField>(words[0], ignoreCase: true);
        var before = Restyled(style => style with { Opacity = new OpacityConfig(Overlay: 0.5) });
        var format = new ValueFormat(Texts.Of(language), CultureInfo.GetCultureInfo(language == "es" ? "es-ES" : "en-US"));

        var answer = TypedNumbers.Read(before, new TypedEdit(0, field, words[1]), format);
        var after = answer.Edit is { } edit ? Done(edit(before)) : before;

        var (overlay, texts) = (after.Overlays[0], format.Texts);
        var (kept, label) = field switch
        {
            TypedField.Size => (string.Create(CultureInfo.InvariantCulture, $"size {overlay.Size}"), texts.Of(UiText.SizeField)),
            TypedField.Background => (
                string.Create(CultureInfo.InvariantCulture, $"opacity {overlay.Style.Opacity.Background} {overlay.Style.Opacity.Overlay}"),
                texts.Of(UiText.BackgroundField)),
            TypedField.Gap => (string.Create(CultureInfo.InvariantCulture, $"gap {overlay.Style.Gap}"), texts.Of(OverlayPageText.GapField)),
            TypedField.LabelSize or TypedField.ValueSize => (
                string.Create(CultureInfo.InvariantCulture, $"value {StylePresets.Text(overlay.Style).Value.Size} {overlay.Style.Preset}"),
                texts.Of(UiText.ValuesField)),
            _ => throw new ArgumentOutOfRangeException(nameof(typed)),
        };
        object?[] blanks = [label, .. said.Split('|').Skip(1)];
        Assert.Equal(
            (expected, said.Split('|')[0] switch
            {
                "range" => texts.Format(format.Numbers, OverlayPageText.TypedOutOfRange, blanks),
                "kept" => texts.Format(format.Numbers, OverlayPageText.TypedNotANumber, blanks),
                _ => null,
            }),
            (kept, answer.Said));
    }

    private static string Summary(ThermiConfig config, OverlayConfig overlay, int at)
    {
        var place = overlay.Snapped is { } snap ? $"{snap.Spot}{(snap.Offset is { } offset ? $"@{offset}" : "")}"
            : overlay.Position is { } position ? $"{position.X},{position.Y}" : "-";
        var hidden = Overlay(config, at)?["hidden"]?.GetValue<bool>() == true ? "h" : "";
        var vertical = overlay.Style.Orientation == Orientation.Vertical ? "v" : "";
        var size = Math.Abs(overlay.Size - 1) < 0.001 ? "" : overlay.Size.ToString(CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{place}:{overlay.Items.Count}{hidden}{vertical}{size}");
    }

    private static List<string?> Names(ThermiConfig config) => [.. config.Overlays.Select((_, at) => Overlay(config, at)?["name"]?.GetValue<string>())];

    // Read from the file, since names and the own Hide are fields of the file's new version.
    private static JsonNode? Overlay(ThermiConfig config, int at) => JsonNode.Parse(File(config))?["overlays"]?[at];
}
