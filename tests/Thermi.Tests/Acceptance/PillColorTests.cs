using System.Text.Json;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>Pills in a color of their own, on which every text and level stays readable (the owner, 2026-11-25).</summary>
public sealed class PillColorTests
{
    private static readonly HighContrastColors HighContrast = new(Rgb.Parse("#000000"), Rgb.Parse("#FFFFFF"), Rgb.Parse("#1AEBFF"), Rgb.Parse("#FFFF00"));

    // The pills' own color, and the theme it is chosen over; choosing a theme again, in the window or by an agent, gives
    // the pills back the theme's color.
    [Theory] // Rows: black, the gray where neither kind of Auto value reaches 4.5:1 in its own color, and a bright hue the default levels near
    [InlineData("#000000", PillTheme.Light)]
    [InlineData("#808080", PillTheme.Dark)]
    [InlineData("#FEC84B", PillTheme.System)]
    public void APillColorOfItsOwnKeepsEveryTextAndLevelReadable(string background, PillTheme theme)
    {
        var themed = Done(StyleEdits.Restyle(new ThermiConfig(), 0, style => style with { Pills = theme, Layout = PillLayout.One }));
        var colored = Done(StyleEdits.SetColor(themed, 0, StyleColor.Background, background));
        var style = colored.Overlays[0].Style;
        var big = Done(StyleEdits.SetText(colored, 0, TextPart.Value, new TextStyle(36, TextWeight.Bold))).Overlays[0].Style;

        var look = OverlayLook.Of(style, lightTaskbar: false);
        var pills = look.Pills;
        var tooClose = OverlayLook.Of(style with { Colors = style.Colors! with { Label = background } }, lightTaskbar: false);
        Rgb[] drawn = [pills.Border, pills.MeterOff, pills.Inner];
        TextColor[] texts = [look.LabelColor, look.ValueColor];
        Rgb[] levels = [look.Normal, look.Warning, look.Critical];

        Assert.Equal(Rgb.Parse(background), pills.Outer);
        Assert.DoesNotContain(pills.Outer, drawn);
        Assert.InRange(pills.Inner.ContrastWith(pills.Outer), 1.5, 2);
        Assert.All(texts, text => Assert.InRange(text.Color.ContrastWith(pills.Outer), 4.5, 21));
        Assert.All(levels, level => Assert.InRange(Math.Min(level.ContrastWith(pills.Outer), level.ContrastWith(pills.MeterOff)), 3, 21));
        Assert.Equal(new TextColor(look.LabelColor.Color, Unreadable: true), tooClose.LabelColor);
        Assert.Equal((OverlayLook.PillHeight, 1.9 * 36), (look.Height, OverlayLook.Of(big, lightTaskbar: false).Height));
        Assert.Equal(HighContrast.Window, OverlayLook.Of(style, lightTaskbar: false, HighContrast).Pills.Outer);
        Assert.Equal(themed.Overlays[0].Style, Done(StyleEdits.SetColor(colored, 0, StyleColor.Background, null)).Overlays[0].Style);
        Assert.Equal(themed.Overlays[0].Style with { Pills = PillTheme.Dark }, Themed(colored, "dark").Overlays[0].Style);
    }

    private static ThermiConfig Themed(ThermiConfig config, string theme)
    {
        var edit = new StyleFieldEdit(0, "pills", JsonSerializer.SerializeToElement(theme));
        return Done(PageEdits.Of(edit, TestRecordings.Read(TestRecordings.Admin), Texts.English, () => new HashSet<int>())(config));
    }
}
