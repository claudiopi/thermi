using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Where a dragged overlay lands (the owner, 2026-11-24): anywhere along a taskbar, centered across it, with its marks a
/// help within reach and never a constraint, or at a corner's mark, or free where it was let go.
/// </summary>
public sealed class TaskbarDropTests
{
    private const int Width = 550;
    private const int Height = 38;

    // The QA PC: 2560 × 1440 at 100 %, a 48 px taskbar at the bottom whose notification area starts at 2210, so the mark
    // beside it lies at 2210 - 8 - 550 = 1652 and the one at the taskbar's start at 8, both at 1397, centered across it.
    private static readonly Screen Pc = new("main", true, new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 1)
    {
        Taskbar = new PixelRect(0, 1392, 2560, 1440),
        Tray = new PixelRect(2210, 1392, 2560, 1440),
    };

    // A second monitor at its right, its taskbar at the top, its notification area starting at 4200.
    private static readonly Screen Side = new("side", false, new PixelRect(2560, 0, 4480, 1080), new PixelRect(2560, 40, 4480, 1080), 1)
    {
        Taskbar = new PixelRect(2560, 0, 4480, 40),
        Tray = new PixelRect(4200, 0, 4480, 40),
    };

    // The QA PC with a 96 px taskbar, on which an overlay shows at its size up to 250 %, so a row's widths are what its size asks for.
    private static readonly Screen Tall = Pc with
    {
        Work = Pc.Work with { Bottom = 1344 },
        Taskbar = new PixelRect(0, 1344, 2560, 1440),
        Tray = new PixelRect(2210, 1344, 2560, 1440),
    };

    // An overlay 550 × 38 dragged and dropped with its top-left corner at `drop`, with Alt held, or vertical, when it says
    // so. Where it lands: its snap and where it shows, or "free" where it is kept.
    [Theory]
    // On the taskbar, anywhere along it, over the app buttons or the notification area too, kept whole on it.
    [InlineData("1200,1397", "Taskbar 1200,1397")]
    [InlineData("300,1420", "Taskbar 300,1397")]
    [InlineData("2200,1397", "Taskbar 2010,1397")]
    // Its marks pull within 96 px along it, and no farther.
    [InlineData("1556,1397", "Taskbar 1652,1397")]
    [InlineData("1555,1397", "Taskbar 1555,1397")]
    [InlineData("60,1397", "Taskbar 8,1397")]
    // Its edge facing the taskbar within 96 px of it brings it onto the taskbar, and no farther.
    [InlineData("1200,1258", "Taskbar 1200,1397")]
    [InlineData("1200,1257", "free 1200,1257")]
    // The corners: within reach of a mark, its edge 96 and 97 px from it, and between a top corner's mark and the top.
    [InlineData("18,1336", "BottomLeft 8,1346")]
    [InlineData("1906,64", "TopRight 2002,64")]
    [InlineData("1905,64", "free 1905,64")]
    [InlineData("1992,18", "TopRight 2002,64")]
    // Pushed past the monitor's edge, reach counts from where it would show, kept inside it.
    [InlineData("2300,1320", "BottomRight 2002,1346")]
    // Alt held: no mark pulls it, and off the taskbar no reach either; on it, it stays on it where it was let go.
    [InlineData("alt 1600,1397", "Taskbar 1600,1397")]
    [InlineData("alt 18,1336", "free 18,1336")]
    [InlineData("alt 1200,1300", "free 1200,1300")]
    // A taskbar at the top of another monitor, from its edge 96 px below it too; a vertical overlay never goes on one.
    [InlineData("3000,0", "Taskbar 3000,1")]
    [InlineData("3000,136", "Taskbar 3000,1")]
    [InlineData("vertical 1200,1397", "free 1200,1354")]
    public void ADropLandsWhereItIsLetGoOrOnAMarkInReach(string drop, string lands)
    {
        var words = drop.Split(' ');
        var at = words[^1].Split(',').Select(each => int.Parse(each, CultureInfo.InvariantCulture)).ToArray();
        var dropped = new Position(at[0], at[1]);
        var orientation = words[0] == "vertical" ? Orientation.Vertical : Orientation.Horizontal;

        var snap = Snaps.Near([Pc, Side], orientation, Width, Height, dropped, magnets: words[0] != "alt");

        var landed = snap is null
            ? Snaps.Inside(dropped, [Pc, Side], Width, Height)
            : Snaps.Locate(new OverlayConfig { Snap = snap }, [Pc, Side], Width, Height)!;
        Assert.Equal(lands, string.Create(CultureInfo.InvariantCulture, $"{snap?.Spot.ToString() ?? "free"} {landed.X},{landed.Y}"));
    }

    // The owner's taskbar after a pill was dragged off (the owner, 2026-11-24): the CPU overlay 662 × 38 beside the
    // notification area, the "CPU 141 W" overlay 103 × 38 to its left, a hidden third overlay, and a fourth 100 × 38 between
    // the two shown, 34 px from the first and 72 px from the second. A drag held as its words
    // say, `self` the dragged overlay's place (-1 for a lifted pill, whose overlay stays), its box `x,y,width` 38 px high
    // and the cursor, with Alt held when it says so. What it joins, and the point of it the drop joins at, or none.
    [Theory]
    // The cursor on the other overlay, or off it with the dragged overlay over it by half of either.
    [InlineData("1 1700,1397,103 1750,1416", "0 1750,1416")]
    [InlineData("1 1700,1380,103 1750,1394", "0 1750,1397")]
    [InlineData("1 1521,1397,103 1550,1416", "0 1572,1416")]
    [InlineData("1 1520,1397,103 1550,1416", "none")]
    [InlineData("1 1469,1397,103 1500,1416", "none")]
    // The big overlay over the small one, and a lifted pill over another overlay or its own.
    [InlineData("0 900,1397,662 1200,1416", "1 1263,1416")]
    [InlineData("-1 1300,1397,220 1380,1416", "3 1400,1416")]
    [InlineData("-1 1280,1393,103 1300,1412", "1 1300,1412")]
    [InlineData("-1 1900,1393,103 1950,1412", "0 1950,1412")]
    // Alt held drops it free.
    [InlineData("alt 1 1700,1397,103 1750,1416", "none")]
    public void ADragJoinsTheOverlayItIsOver(string drag, string joins)
    {
        var words = drag.Split(' ');
        var (box, cursor) = (Numbers(words[^2]), Numbers(words[^1]));
        PixelRect?[] shown = [new PixelRect(1572, 1397, 2234, 1435), new PixelRect(1263, 1397, 1366, 1435), null, new PixelRect(1400, 1397, 1500, 1435)];

        var target = DesktopGestures.JoinTarget(
            shown,
            int.Parse(words[^3], CultureInfo.InvariantCulture),
            new PixelRect(box[0], box[1], box[0] + box[2], box[1] + Height),
            new Position(cursor[0], cursor[1]),
            free: words[0] == "alt");

        Assert.Equal(joins, target is var (onto, at) ? string.Create(CultureInfo.InvariantCulture, $"{onto} {at.X},{at.Y}") : "none");
    }

    // "Separate" on the GPU's pill of the default overlay shown at `shown`, on the taskbar or free where it says, the pill
    // an overlay of `made` pixels once on its own, as a drag carries it: 100 × 38, or 108 × 38 out of one bar, whose pill
    // is drawn 4 px inside the bar. It is dropped beside it, 8 px away and level with it, as a drag with Alt held drops it,
    // and becomes an overlay of its own there. Its snap, where it shows and its items.
    [Theory]
    // On the taskbar it lands on the taskbar, before the overlay, centered across it; before the overlay as it sits once
    // the pill has left, 450 px wide, shrunk toward the notification area.
    [InlineData("taskbar 1652,1397,2202,1435 100x38", "Taskbar 1544,1397 gpu.load")]
    [InlineData("taskbar 1652,1397,2202,1435 108x38", "Taskbar 1536,1397 gpu.load")]
    [InlineData("taskbar 1652,1397,2202,1435 100x38 450x38", "Taskbar 1644,1397 gpu.load")]
    // Free, before it, or after it where before is off the monitor, level with its middle.
    [InlineData("free 1000,500,1550,538 100x38", "free 892,500 gpu.load")]
    [InlineData("free 1000,500,1550,538 108x38", "free 884,500 gpu.load")]
    [InlineData("free 1000,500,1550,538 108x30", "free 884,504 gpu.load")]
    [InlineData("free 0,500,550,538 100x38", "free 558,500 gpu.load")]
    // Vertical, below it, or above it where below is off the work area, in line with its left edge.
    [InlineData("vertical 1000,500,1100,800 100x50", "free 1000,808 gpu.load")]
    [InlineData("vertical 1000,1100,1100,1380 100x50", "free 1000,1042 gpu.load")]
    // With as many overlays as there may be, no other fits, and the refusal says so.
    [InlineData("full 1000,500,1550,538 100x38", "TooManyOverlays")]
    public void SeparateDropsThePillBesideItsOverlay(string separated, string lands)
    {
        var words = separated.Split(' ');
        var shown = Numbers(words[1]);
        var size = words[2].Split('x').Select(each => int.Parse(each, CultureInfo.InvariantCulture)).ToArray();
        int[] rest = words.Length > 3
            ? [.. words[3].Split('x').Select(each => int.Parse(each, CultureInfo.InvariantCulture))]
            : [shown[2] - shown[0], shown[3] - shown[1]];
        var others = words[0] == "full" ? ThermiConfig.MaxOverlays - 1 : 0;
        var vertical = words[0] == "vertical";
        var overlay = new OverlayConfig
        {
            Position = words[0] == "taskbar" ? null : new Position(shown[0], shown[1]),
            Style = new StyleConfig { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal },
        };
        var (width, height) = (size[0], size[1]);

        var drop = DesktopGestures.Separate(
            new ThermiConfig { Overlays = [overlay, .. Enumerable.Repeat(new OverlayConfig { Items = [] }, others)] },
            0,
            "gpu.load",
            new PixelRect(shown[0], shown[1], shown[2], shown[3]),
            (width, height),
            (rest[0], rest[1]),
            [Pc]);

        var made = drop.Result.Config.Overlays[^1];
        var landed = Snaps.Locate(made, [Pc], width, height)!;
        var items = string.Join(',', made.Items.Select(item => item.Id));
        var where = string.Create(CultureInfo.InvariantCulture, $"{made.Snap?.Spot.ToString() ?? "free"} {landed.X},{landed.Y} {items}");
        Assert.Equal(lands, drop.Result.Refused?.ToString() ?? where);
    }

    // Night QA: at 200 % an overlay on the 48 px taskbar was 76 px high and cut. On a taskbar it shows at most as high as
    // the taskbar, at 100 % and on a 72 px taskbar at 150 %; free, or vertical above the taskbar, at its own size.
    [Theory]
    [InlineData("taskbar 1", 38)]
    [InlineData("taskbar 2", 48)]
    [InlineData("taskbar 2 150%", 72)]
    [InlineData("free 2", 76)]
    [InlineData("vertical 2", 76)]
    public void AnOverlayOnTheTaskbarNeverOverflowsIt(string overlay, int high)
    {
        var words = overlay.Split(' ');
        var scaled = words.Length > 2;
        var screen = scaled ? Pc with { Scale = 1.5, Work = Pc.Work with { Bottom = 1368 }, Taskbar = new PixelRect(0, 1368, 2560, 1440) } : Pc;
        var shown = new OverlayConfig(double.Parse(words[1], CultureInfo.InvariantCulture))
        {
            Position = words[0] == "free" ? new Position(100, 100) : null,
            Style = new StyleConfig { Orientation = words[0] == "vertical" ? Orientation.Vertical : Orientation.Horizontal },
        };

        var size = Snaps.SizeShown(shown, [screen]);

        Assert.Equal(high, (int)Math.Ceiling(OverlayLook.PillHeight * screen.Scale * size));
    }

    // Night QA: two overlays side by side on the taskbar, and one raised to 200 % covered its neighbour. Each overlay as
    // `offset:width` along the taskbar, or `tray:width` beside the notification area, 38 px high, at size 1 or as `@size`
    // says; where each shows. One that grows pushes the next along, toward the notification area only, and the last keeps
    // to its edge; one grown past the room the others leave there shows at that room, never smaller than at 100 %, so none
    // leaves the taskbar, covers another or goes back over the app buttons. Where the room is less than that (QA round 6,
    // a taskbar nearly full), none shows smaller: the one before is held back over the app buttons. Each, in order, shows at
    // the width it has room for, beside the others as they show.
    [Theory]
    [InlineData("8:308 tray:319", "8 1883")]
    [InlineData("1567:432@2 tray:319", "1567 1883")]
    [InlineData("100:600 650:300", "100 708")]
    [InlineData("1500:600@2 2000:500", "1500 2000")]
    [InlineData("100:1800@2 tray:500", "100 1702")]
    [InlineData("1700:300 tray:600", "1294 1602")]
    public void OverlaysOnOneTaskbarNeverCoverEachOther(string overlays, string shown)
    {
        var each = overlays.Split(' ').Select(overlay => overlay.Replace('@', ':').Split(':')).ToList();
        var configs = each.Select(words => new OverlayConfig(words.Length > 2 ? double.Parse(words[2], CultureInfo.InvariantCulture) : 1)
        {
            Snap = new SnapConfig(SnapSpot.Taskbar) { Offset = words[0] == "tray" ? null : int.Parse(words[0], CultureInfo.InvariantCulture) },
        }).ToList();
        var natural = each.Select(words => int.Parse(words[1], CultureInfo.InvariantCulture)).ToList();
        var boxes = TaskbarRow.Fitted(configs, [Tall], [], (at, room) => Within(natural[at], room)).Fitted;
        var widths = boxes.Select(box => box!.Value.Width).ToList();

        var places = Enumerable.Range(0, configs.Count).Select(index => TaskbarRow.Locate(configs, index, [Tall], (widths[index], Height), boxes)!).ToList();

        Assert.Equal(shown, string.Join(' ', places.Select(place => place.X.ToString(CultureInfo.InvariantCulture))));
        Assert.All(places, place => Assert.Equal(1373, place.Y));
        Assert.All(places.Skip(1), (place, at) => Assert.True(place.X >= places[at].X + widths[at], "Apart."));
    }

    // QA round 5: two overlays on one taskbar, and hiding the one beside the notification area moved the other 631 px; after
    // another grew, the two swapped places and one covered the app buttons. An overlay moves only when the person moves it:
    // a push goes toward the notification area only, and is stored as the pushed one's own place, as one undo step with
    // what pushed it. Overlays 300 px wide at size 1 at an offset, `tray` or `free` at 100 100, 38 px high, then edits, each settled as the
    // window and the overlay settle theirs: `size <i> <size>`, `move <i> <offset or tray>`, `hide <i>`, `show <i>`. Where each shows
    // after them, `-` for hidden, then each edit refused, and one undo takes the last edit back whole.
    // QA round 6, a taskbar nearly full: a move onto it without room for the overlay at its own size is refused, and the
    // others keep their size and place; one shown again there squeezes none, the one before it held back over the app
    // buttons only while it shows, so hiding it again puts that one back where it was. Touching is no overlap, so a move
    // that ends where the next begins fits, as one a pixel over would not.
    [Theory]
    [InlineData("1000 1400", "size 0 2", "1000 1608")]
    [InlineData("1000 1400 free", "size 0 2,hide 0", "- 1608 100")]
    [InlineData("1000 1400", "size 0 2,size 0 1", "1000 1608")]
    [InlineData("1000 1400", "hide 1,size 0 2,show 1", "1000 1608")]
    [InlineData("1000 1400", "move 0 1300,hide 0", "- 1608")]
    [InlineData("1000 tray", "size 1 5", "1000 1308")]
    [InlineData("1500 free", "size 1 2,move 1 tray", "1500 100 NoRoomOnTaskbar")]
    [InlineData("1000 tray", "move 0 1602", "1602 1902")]
    [InlineData("1000 tray", "move 0 1500,move 0 1603", "1500 1902 NoRoomOnTaskbar")]
    [InlineData("1700 tray", "hide 1,size 1 2,show 1", "1594 1902")]
    [InlineData("1700 tray", "hide 1,size 1 2,show 1,hide 1", "1700 -")]
    public void AnOverlayMovesOnlyWhenThePersonMovesIt(string overlays, string edits, string shown)
    {
        var configs = overlays.Split(' ').Select(offset => offset == "free" ? new OverlayConfig { Position = new Position(100, 100) } : new OverlayConfig
        {
            Snap = new SnapConfig(SnapSpot.Taskbar) { Offset = offset == "tray" ? null : int.Parse(offset, CultureInfo.InvariantCulture) },
        }).ToList();
        var history = new UndoHistory();
        var before = configs;
        var boxes = Fitted(configs, []);
        var refused = new List<Refusal>();
        foreach (var words in edits.Split(',').Select(edit => edit.Split(' ')))
        {
            var edited = Applied(configs, words);
            var settled = TaskbarRow.Settled(new ThermiConfig { Overlays = configs }, new ThermiConfig { Overlays = edited }, [Tall], boxes, Fit(edited));
            if (settled.Refused is { } refusal)
            {
                refused.Add(refusal);
                continue;
            }

            (before, configs) = (configs, [.. settled.Config.Overlays]);
            boxes = Fitted(configs, boxes);
            history.Record(new ThermiConfig { Overlays = before }, new ThermiConfig { Overlays = configs });
        }

        var places = configs.Select((_, at) => boxes[at] is { } box
            ? TaskbarRow.Locate(configs, at, [Tall], (box.Width, box.Height), boxes)!.X.ToString(CultureInfo.InvariantCulture)
            : "-");
        Assert.Equal(shown, string.Join(' ', [.. places, .. refused.Select(refusal => refusal.ToString())]));
        Assert.Equal(Edited.File(new ThermiConfig { Overlays = before }), Edited.File(history.Undo(new ThermiConfig { Overlays = configs })!.Config));
    }

    private static List<OverlayConfig> Applied(List<OverlayConfig> configs, string[] words)
    {
        var at = int.Parse(words[1], CultureInfo.InvariantCulture);
        int? value = words.Length > 2 && words[2] != "tray" ? int.Parse(words[2], CultureInfo.InvariantCulture) : null;
        var edited = configs.ToList();
        edited[at] = words[0] switch
        {
            "size" => edited[at] with { Size = value!.Value },
            "move" => edited[at] with { Snap = new SnapConfig(SnapSpot.Taskbar) { Offset = value }, Position = null },
            _ => edited[at] with { Hidden = words[0] == "hide" },
        };
        return edited;
    }

    private static List<PixelRect?> Fitted(List<OverlayConfig> configs, List<PixelRect?> before) =>
        TaskbarRow.Fitted(configs, [Tall], before, Fit(configs)).Fitted;

    // An overlay as its painter measures it: 300 px wide at size 1, or narrower within a room.
    private static OverlayFit Fit(List<OverlayConfig> configs) => (at, room) => Within((int)(300 * configs[at].Size), room);

    private static PixelRect Within(int width, Func<(int Width, int Height), int?>? room) =>
        new(0, 0, Math.Min(width, room?.Invoke((width, Height)) ?? width), Height);

    private static int[] Numbers(string word) => [.. word.Split(',').Select(each => int.Parse(each, CultureInfo.InvariantCulture))];
}
