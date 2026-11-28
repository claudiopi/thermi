using System.Globalization;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.Tests;

/// <summary>How a snap names its monitor, by the ids of the monitors' EDIDs, and where on a bar with parts an overlay sits.</summary>
public sealed class PlacementTests
{
    // GNOME's top bar on Debian 13 and Ubuntu 24.04 at 1280 × 800, as their VMs' Shells told it (2026-11-28): Activities
    // with what sits beside it, the clock, and the system menu, which is the bar's notification area.
    private static readonly Screen Debian = TopBar((1280, 800), 1, start: 72, clock: (553, 727), menu: 1130);
    private static readonly Screen Ubuntu = TopBar((1280, 800), 1, start: 72, clock: (603, 744), menu: 1130);

    // The landing page's case: GNOME at 150 % on 1920 × 1080, the clock to the system menu about 650 px apart.
    private static readonly Screen Landing = TopBar((1920, 1080), 1.5, start: 108, clock: (829, 1091), menu: 1741);

    // Windows' taskbar along the bottom of 1280 × 800, its notification area from 1000.
    private static readonly Screen Taskbar = new("main", true, new PixelRect(0, 0, 1280, 800), new PixelRect(0, 0, 1280, 752), 1)
    {
        Taskbar = new PixelRect(0, 752, 1280, 800),
        Tray = new PixelRect(1000, 752, 1280, 800),
    };

    // Overlays on a top bar, each `offset:width` along it or `tray:width` with none, 24 px high, `moved` when the first was
    // just moved there from the desktop: where each shows, and whether the move is refused. Each sits in the room between
    // the bar's parts that holds it, nearest where it would go, and never covers one, its row's pushes kept within that
    // room; where none holds it, it shows below the top bar, as a vertical one does, and a move there is refused, as onto
    // a full taskbar. The settings window is closed.
    [Theory] // Rows: Debian's, Ubuntu's and the landing page's reproductions, each gap kept or changed, a row's push, the refusal
    [InlineData("debian moved tray:545", "727,40 refused")]
    [InlineData("ubuntu tray:558", "714,40")]
    [InlineData("landing tray:813", "1095,60")]
    [InlineData("debian tray:380", "742,4")]
    [InlineData("debian 0:300", "80,4")]
    [InlineData("debian 800:420", "125,4")]
    [InlineData("debian 300:150 400:100", "287,4 445,4")]
    public void AnOverlayOnABarNeverCoversItsParts(string overlays, string shown)
    {
        var words = overlays.Split(' ');
        var screen = words[0] switch { "debian" => Debian, "ubuntu" => Ubuntu, _ => Landing };
        var each = words.Skip(1).Where(word => word != "moved").Select(word => word.Split(':')).ToList();
        List<OverlayConfig> configs = [.. each.Select(word => new OverlayConfig
        {
            Snap = new SnapConfig(SnapSpot.Taskbar) { Offset = word[0] == "tray" ? null : int.Parse(word[0], CultureInfo.InvariantCulture) },
        })];
        List<PixelRect?> boxes = [.. each.Select(word => (PixelRect?)new PixelRect(0, 0, int.Parse(word[1], CultureInfo.InvariantCulture), 24))];
        List<OverlayConfig> before = words.Contains("moved") ? [new OverlayConfig { Position = new Position(100, 100) }, .. configs.Skip(1)] : configs;

        var places = configs.Select((_, at) => TaskbarRow.Locate(configs, at, [screen], (boxes[at]!.Value.Width, 24), boxes)!)
            .Select(at => string.Create(CultureInfo.InvariantCulture, $"{at.X},{at.Y}"));
        var refused = TaskbarRow.Crowded(before, configs, [screen], boxes, boxes) ? " refused" : "";

        Assert.Equal(shown, string.Join(' ', places) + refused);
    }

    // While the settings window is open, whose alert-only items widen an overlay as any edit may: overlays on a bar, each
    // `offset:closed/open` or `tray:closed/open` wide, its width with the window closed and open, 24 px high, fitted in
    // turn as their painters fit them. Where each shows, at what width, and whether beside its bar. On a bar with parts,
    // one whose size with the window closed sits in a room between them keeps to that room, shrinking to fit it beside the
    // others there, smaller than at 100 % too; one that fits in none even then shows beside the bar, as with the window
    // closed. Windows' taskbar, which tells no parts, holds the one before back over the app buttons, as ever.
    [Theory] // Rows: alone, alone where it slides to fit, shared, beside the bar, and Windows' taskbar
    [InlineData("debian 300:200/600", "300,4@245")]
    [InlineData("debian 300:200/250", "295,4@250")]
    [InlineData("debian 100:200/400 400:100/100", "100,4@337 445,4@100")]
    [InlineData("landing tray:813/900", "1008,60@900 beside")]
    [InlineData("taskbar 600:300/300 tray:500/500", "184,764@300 492,764@500")]
    public void WhileTheWindowIsOpenAnOverlayKeepsToItsRoomOnTheBar(string overlays, string shown)
    {
        var words = overlays.Split(' ');
        var screen = words[0] switch { "debian" => Debian, "landing" => Landing, _ => Taskbar };
        var each = words.Skip(1).Select(word => word.Replace('/', ':').Split(':')).ToList();
        List<OverlayConfig> configs = [.. each.Select(word => new OverlayConfig
        {
            Snap = new SnapConfig(SnapSpot.Taskbar) { Offset = word[0] == "tray" ? null : int.Parse(word[0], CultureInfo.InvariantCulture) },
        })];
        int Wide(int at, int word)
        {
            return int.Parse(each[at][word], CultureInfo.InvariantCulture);
        }

        var fitted = TaskbarRow.Fitted(
            configs,
            [screen],
            [],
            (at, room) => new PixelRect(0, 0, Math.Min(Wide(at, 2), room?.Invoke((Wide(at, 2), 24)) ?? Wide(at, 2)), 24),
            (at, _) => new PixelRect(0, 0, Wide(at, 1), 24)).Fitted;
        var places = fitted.Select((box, at) => (Box: box!.Value, At: TaskbarRow.Locate(configs, at, [screen], (box.Value.Width, 24), fitted)!))
            .Select(one => new PixelRect(one.At.X, one.At.Y, one.At.X + one.Box.Width, one.At.Y + 24))
            .Select((box, at) => string.Create(
                CultureInfo.InvariantCulture, $"{box.Left},{box.Top}@{box.Width}{(TaskbarRow.Beside(configs[at], [screen], box) ? " beside" : "")}"));

        Assert.Equal(shown, string.Join(' ', places));
    }

    private static Screen TopBar((int Width, int Height) size, double scale, int start, (int Left, int Right) clock, int menu)
    {
        var bar = new PixelRect(0, 0, size.Width, (int)(32 * scale));
        var end = bar with { Left = menu };
        return new Screen("main", true, new PixelRect(0, 0, size.Width, size.Height), new PixelRect(0, bar.Bottom, size.Width, size.Height), scale)
        {
            Taskbar = bar,
            Tray = end,
            Start = bar with { Right = start },
            Centre = bar with { Left = clock.Left, Right = clock.Right },
            End = end,
        };
    }

    // R35: the monitors, each as its EDID's serial number and the serial's own text, "blank" for a text of spaces, "none"
    // for no EDID and "zeros" for one of zeros, then @ and its connection; and their ids: maker, product and the serial's
    // own text, else the serial number, else the connection, two that report the same serial told apart by the connection.
    [Theory] // Rows: each a fallback R35 states, in its order.
    [InlineData("12345 7XQ9K13@5&2f5d3e1a&0&UID4357", "DEL-A1B2-7XQ9K13")]
    [InlineData("12345@UID1", "DEL-A1B2-12345")]
    [InlineData("0 blank@UID1", "DEL-A1B2@UID1")]
    [InlineData("none@UID1", "UID1")]
    [InlineData("zeros@UID1", "UID1")]
    [InlineData("1@UID1;1@UID2;9@UID3", "DEL-A1B2-1;DEL-A1B2-1@UID2;DEL-A1B2-9")]
    public void AMonitorIsKnownByItsEdidAndWithoutASerialByItsConnection(string monitors, string ids)
    {
        var read = monitors.Split(';').Select(monitor => monitor.Split('@')).Select(parts => (MonitorIds.Of(Edid(parts[0]), parts[1]), parts[1]));

        Assert.Equal(ids.Split(';'), MonitorIds.Unique([.. read]));
    }

    // Off GNOME, a Linux desktop names a monitor from its EDID as Mutter does, which GNOME's snaps saved: the product's
    // name with its spaces, else its code, and the serial's text, else its number, each code in hex; no EDID, no name,
    // which the desktop's own then gives.
    [Theory] // Rows: each a fallback of Mutter's, in its order.
    [InlineData("12345 7XQ9K13", "DEL-DELL U2723QE-7XQ9K13")]
    [InlineData("12345 blank", "DEL-DELL U2723QE-0x00003039")]
    [InlineData("12345 7XQ9K13 unnamed", "DEL-0xa1b2-7XQ9K13")]
    [InlineData("none", null)]
    public void OffGnomeAMonitorIsNamedAsGnomeNamesIt(string edid, string? id) => Assert.Equal(id, MonitorIds.OfGnome(Edid(edid), "DP-1"));

    private static byte[] Edid(string written) => written.Split(' ') switch
    {
        ["none"] => [],
        ["zeros"] => new byte[128],
        [var number] => Edid(uint.Parse(number, System.Globalization.CultureInfo.InvariantCulture), serial: null),
        [var number, var serial] => Edid(uint.Parse(number, System.Globalization.CultureInfo.InvariantCulture), serial == "blank" ? " \n" : serial),
        [var number, var serial, "unnamed"] => Edid(uint.Parse(number, System.Globalization.CultureInfo.InvariantCulture), serial, named: false),
        _ => throw new ArgumentException("An EDID is a number, a serial and \"unnamed\" at most.", nameof(written)),
    };

    // A base EDID block of 128 bytes: its header, "DEL" as the maker, 0xA1B2 as the product, a serial number, and the
    // serial's text in the second display descriptor, after a name in the first unless it is unnamed.
    private static byte[] Edid(uint number, string? serial, bool named = true)
    {
        var edid = new byte[128];
        byte[] header = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
        header.CopyTo(edid, 0);
        var maker = (('D' - '@') << 10) | (('E' - '@') << 5) | ('L' - '@');
        (edid[8], edid[9]) = ((byte)(maker >> 8), (byte)maker);
        (edid[10], edid[11]) = (0xB2, 0xA1);
        BitConverter.GetBytes(number).CopyTo(edid, 12);
        if (named)
        {
            Descriptor(edid, 54, 0xFC, "DELL U2723QE");
        }
        if (serial is not null)
        {
            Descriptor(edid, 72, 0xFF, serial);
        }

        return edid;
    }

    private static void Descriptor(byte[] edid, int at, byte tag, string text)
    {
        edid[at + 3] = tag;
        var field = (text + "\n").PadRight(13);
        for (var index = 0; index < 13; index++)
        {
            edid[at + 5 + index] = (byte)field[index];
        }
    }
}
