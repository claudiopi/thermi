using System.Globalization;
using System.Text.Json.Nodes;
using Thermi.Core.Editing;
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

    private static ThermiConfig Four => Done(OverlayEdits.Add(Done(OverlayEdits.Add(Two))));

    // The CPU fan moved first, as a person orders the default items.
    private static ThermiConfig Reordered => Done(ItemEdits.Move(One, Metrics.CpuFan.Id, 0));

    // Where the edit starts, the edit, the overlays after it as their place, their items' count, h when hidden, v when
    // vertical and their size when not 100 %, or the refusal; then the name an overlay without a typed one shows, as its
    // place and that name's text, or "same" when no edit wrote a name.
    [Theory]
    [InlineData("one", "new", "TaskbarRight:6 TopRight:6", "1=TopRight")]
    [InlineData("reordered", "new", "TaskbarRight:6 TopRight:6", "1=TopRight")]
    [InlineData("two", "duplicate 0", "TaskbarRight:6 BottomRight:6 TopRight:6", "1=BottomRight")]
    [InlineData("two", "merge 1 0", "TaskbarRight:12", "same")]
    [InlineData("two", "merge 0 1", "TopRight:12", "same")]
    [InlineData("two", "delete 1", "TaskbarRight:6", "same")]
    [InlineData("two", "hide 1", "TaskbarRight:6 TopRight:6h", "same")]
    [InlineData("two", "move cpu.load 1", "TaskbarRight:5 TopRight:7", "same")]
    [InlineData("one", "move cpu.load new", "TaskbarRight:5 TopRight:1", "1=TopRight")]
    [InlineData("solo", "move solo 0", "TaskbarRight:7", "same")]
    [InlineData("one", "drag gpu.load 400 300", "TaskbarRight:5 400,300:1", "")]
    [InlineData("one", "drag cpu.load 400 300", "TaskbarRight:2 400,300:4", "")]
    [InlineData("free", "resize 0 1.5 vertical", "400,300:6v1.5", "same")]
    [InlineData("free", "nudge 0 8 0", "408,300:6", "same")]
    [InlineData("free", "nudge 0 0 -1", "400,299:6", "same")]
    [InlineData("four", "new", nameof(Refusal.TooManyOverlays), "same")]
    [InlineData("four", "duplicate 0", nameof(Refusal.TooManyOverlays), "same")]
    [InlineData("four", "move cpu.load new", nameof(Refusal.TooManyOverlays), "same")]
    [InlineData("two", "rename 1 Desk", "TaskbarRight:6 TopRight:6", "")]
    [InlineData("two", "rename 1 ", nameof(Refusal.NameEmpty), "same")]
    [InlineData("two", "merge 5 0", nameof(Refusal.NotFound), "same")]
    [InlineData("two", "move none 1", nameof(Refusal.NotFound), "same")]
    [InlineData("one", "delete 0", nameof(Refusal.LastOverlay), "same")]
    [InlineData("four", "drag cpu.load 400 300", nameof(Refusal.TooManyOverlays), "same")]
    [InlineData("one", "drag none 400 300", nameof(Refusal.NotFound), "same")]
    [InlineData("free", "resize 0 3 vertical", nameof(Refusal.SizeOutOfRange), "same")]
    [InlineData("one", "nudge 0 8 0", nameof(Refusal.Snapped), "same")]
    [InlineData("solo", "desk 1 solo 400 300 0", "TaskbarRight:7", "same")]
    [InlineData("solo", "desk 1 solo 20 1306", "TaskbarRight:6 BottomLeft:1", "same")]
    [InlineData("solo", "desk 1 solo 400 300", "TaskbarRight:6 400,300:1", "same")]
    [InlineData("one", "desk 0 ctrl+gpu.load 400 300", "TaskbarRight:5 400,300:1", "")]
    [InlineData("two", "desk 0 ctrl+gpu.load 400 300 1", "TaskbarRight:5 TopRight:7", "same")]
    [InlineData("one", "desk 0 cpu.load 400 300", "400,300:6", "same")]
    [InlineData("one", "desk 0 edge 400 300 0", "400,300:6", "same")]
    [InlineData("one", "desk 0 edge 1300 1375", "TaskbarRight:6", "same")]
    [InlineData("free", "desk 0 edge 1000 1380", "TaskbarCenter:6", "same")]
    [InlineData("four", "desk 0 ctrl+cpu.load 400 300", "400,300:6 TopRight:6 BottomRight:6 BottomLeft:6", "same")]
    [InlineData("free", "corner 400 38 -300 200", "400,300:6v0.75", "same")]
    [InlineData("free", "corner 400 38 199 5", "400,300:61.5", "same")]
    public void OverlaysAreMadeMovedMergedAndHiddenAsOneStepEach(string start, string edit, string expected, string named)
    {
        var before = start switch { "two" => Two, "solo" => Solo, "free" => Free, "four" => Four, "reordered" => Reordered, _ => One };
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

    // A pill, held with Ctrl when its word says "ctrl+", or the edge, of an overlay as the main monitor shows it, dropped
    // where the words say, near a snap mark or onto the overlay they name.
    private static EditResult Desk(ThermiConfig config, string[] words)
    {
        var dropped = new Position(int.Parse(words[3], CultureInfo.InvariantCulture), int.Parse(words[4], CultureInfo.InvariantCulture));
        var mark = Snaps.Near([SnapTests.Main], Orientation.Horizontal, 600, 57, dropped);
        int? onto = words.Length > 5 ? int.Parse(words[5], CultureInfo.InvariantCulture) : null;
        var overlay = int.Parse(words[1], CultureInfo.InvariantCulture);
        var ctrl = words[2].StartsWith("ctrl+", StringComparison.Ordinal);
        var pill = ctrl ? words[2]["ctrl+".Length..] : words[2];
        var takenOff = DesktopGestures.TakesOff(config, overlay, pill == "edge" ? null : pill, ctrl) ? pill : null;
        return DesktopGestures.Release(config, overlay, takenOff, dropped, onto, mark).Result;
    }

    private static string Summary(ThermiConfig config, OverlayConfig overlay, int at)
    {
        var place = overlay.Snapped is { } snap ? snap.Spot.ToString() : overlay.Position is { } position ? $"{position.X},{position.Y}" : "-";
        var hidden = Overlay(config, at)?["hidden"]?.GetValue<bool>() == true ? "h" : "";
        var vertical = overlay.Style.Orientation == Orientation.Vertical ? "v" : "";
        var size = Math.Abs(overlay.Size - 1) < 0.001 ? "" : overlay.Size.ToString(CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{place}:{overlay.Items.Count}{hidden}{vertical}{size}");
    }

    private static List<string?> Names(ThermiConfig config) => [.. config.Overlays.Select((_, at) => Overlay(config, at)?["name"]?.GetValue<string>())];

    // Read from the file, since names and the own Hide are fields of the file's new version.
    private static JsonNode? Overlay(ThermiConfig config, int at) => JsonNode.Parse(File(config))?["overlays"]?[at];
}
