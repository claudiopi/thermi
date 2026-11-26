using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Pills and style are data in Core, so the stage and the overlay always agree.
/// </summary>
public sealed class StyleTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    private static IReadOnlyList<IReadOnlyList<OverlayItem>> Pills(ThermiConfig config)
    {
        var overlay = config.Overlays[0];
        // Without the loads' graphs, lists that records compare by reference.
        var shown = OverlayLayout.Build(overlay, SensorMap.Read(Desktop, overlay), English).Select(item => item with { Graph = null });
        return OverlayLayout.Groups([.. shown], overlay.Style.Layout);
    }

    private static List<string> Labels(IReadOnlyList<OverlayItem> pill) => [.. pill.Select(item => item.Label).OfType<string>()];

    private static List<string> Values(IReadOnlyList<OverlayItem> pill) => [.. pill.Select(item => item.Value)];

    private static OverlayLook Look(ThermiConfig config, bool lightTaskbar = false) => OverlayLook.Of(config.Overlays[0].Style, lightTaskbar);

    [Fact]
    public void AnItemCanStartItsOwnPillAndShowsItsLabel()
    {
        var split = Done(ItemEdits.SetOwn(new ThermiConfig(), Metrics.CpuPower.Id, true));

        var pills = Pills(split);

        Assert.Equal(["CPU", "CPU", "GPU", "RAM"], pills.Select(pill => pill[0].Label));
        Assert.Equal(["77 °C", "1,087 RPM", "9 %"], Values(pills[0]));
        Assert.Equal(["71 W"], Values(pills[1]));
        Assert.Equal(3, Pills(Done(ItemEdits.SetOwn(split, Metrics.CpuPower.Id, false))).Count);
        Assert.Equal(Refusal.NotFound, ItemEdits.SetOwn(split, "gone", true).Refused);
        var fromTheLoad = Pills(Done(ItemEdits.SetOwn(new ThermiConfig(), Metrics.CpuLoad.Id, true)));
        Assert.Equal(["77 °C", "1,087 RPM"], Values(fromTheLoad[0]));
        Assert.Equal(["9 %", "71 W"], Values(fromTheLoad[1]));
    }

    [Fact]
    public void EachLayoutMakesItsPills()
    {
        var split = Done(ItemEdits.SetOwn(new ThermiConfig(), Metrics.CpuPower.Id, true));

        var each = Done(StyleEdits.Restyle(split, 0, style => style with { Layout = PillLayout.Each }));
        var one = Done(StyleEdits.Restyle(each, 0, style => style with { Layout = PillLayout.One }));
        var back = Done(StyleEdits.Restyle(one, 0, style => style with { Layout = PillLayout.Pills }));

        Assert.Equal(4, Pills(split).Count);
        Assert.Equal(["CPU", "CPU", "CPU", "CPU", "GPU", "RAM"], Pills(each).Select(pill => Assert.Single(Labels(pill))));
        Assert.Equal(["CPU", "GPU", "RAM"], Labels(Assert.Single(Pills(one))));
        Assert.Equal(Pills(split), Pills(back));
        Assert.True(Item(back, Metrics.CpuPower.Id).Own);
    }

    [Fact]
    public void AVerticalOverlayStacksItsPills()
    {
        var vertical = Look(Done(StyleEdits.Restyle(new ThermiConfig(), 0, style => style with { Orientation = Orientation.Vertical })));
        var horizontal = Look(new ThermiConfig());
        List<double> widths = [.. Pills(new ThermiConfig()).Select(pill => pill.Sum(item => 10.0 * ((item.Label?.Length ?? 0) + item.Room.Length)))];

        var stacked = vertical.Arrange(widths, 38, 2);
        var inARow = horizontal.Arrange(widths, 38, 2);
        var standing = vertical.Arrange(widths, 38, 2, [38, 120, 38]);

        Assert.Equal(widths.Max(), stacked.Width);
        Assert.Equal((3 * 38) + (2 * 16), stacked.Height);
        Assert.All(stacked.Pills, pill => Assert.Equal(widths.Max(), pill.Width));
        Assert.Equal([0.0, 54, 108], stacked.Pills.Select(pill => pill.Top));
        Assert.Equal([(0.0, 38.0), (54, 120), (190, 38)], standing.Pills.Select(pill => (pill.Top, pill.Height)));
        Assert.Equal(38 + 120 + 38 + (2 * 16), standing.Height);
        Assert.Equal(widths.Sum() + (2 * 16), inARow.Width);
        Assert.Equal(38, inARow.Height);
        Assert.Equal(widths[0] + 16, inARow.Pills[1].Left);
    }

    private static OverlayConfig Snapped(SnapSpot spot, Orientation orientation) =>
        new() { Snap = new SnapConfig(spot), Style = new StyleConfig { Orientation = orientation } };

    [Fact]
    public void AVerticalOverlayLeavesTheTaskbarForItsCorner()
    {
        var beside = SnapTests.Placed(Snapped(SnapSpot.Taskbar, Orientation.Vertical), [SnapTests.Main], 200, 228);

        Assert.Equal(SnapTests.Placed(Snapped(SnapSpot.BottomRight, Orientation.Vertical), [SnapTests.Main], 200, 228), beside);
        Assert.DoesNotContain(Snaps.Marks([SnapTests.Main], Orientation.Vertical, 200, 228), mark => mark.Snap.Spot == SnapSpot.Taskbar);
    }

    [Fact]
    public void APresetSetsTheTextAndCustomizingKeepsTheRest()
    {
        var expected = new Dictionary<StylePreset, (double Label, double Value)>
        {
            [StylePreset.Compact] = (11, 12),
            [StylePreset.Standard] = (13, 13.5),
            [StylePreset.Large] = (16, 17),
        };

        foreach (var (preset, (label, value)) in expected)
        {
            var chosen = Done(StyleEdits.SetPreset(new ThermiConfig(), 0, preset));

            Assert.Equal(new TextStyle(label, TextWeight.Semibold), Look(chosen).Label);
            Assert.Equal(new TextStyle(value, TextWeight.Bold), Look(chosen).Value);
            Assert.Equal(preset, Done(StyleEdits.SetOpacity(chosen, 0, new OpacityConfig(0.5, 0.8))).Overlays[0].Style.Preset);

            var customized = Done(StyleEdits.SetText(chosen, 0, TextPart.Label, new TextStyle(14.5, TextWeight.Regular))).Overlays[0].Style;
            Assert.Equal(StylePreset.Custom, customized.Preset);
            Assert.Equal((new TextStyle(14.5, TextWeight.Regular), new TextStyle(value, TextWeight.Bold)), StylePresets.Text(customized));
            Assert.Equal(new TextStyle(label, TextWeight.Semibold), Done(StyleEdits.SetPreset(chosen, 0, StylePreset.Custom)).Overlays[0].Style.Label);
        }

        Assert.Equal(StylePresets.Of(StylePreset.Standard), StylePresets.Text(new StyleConfig()));
        Assert.Equal(StylePresets.Of(StylePreset.Standard), StylePresets.Text(new StyleConfig { Preset = StylePreset.Custom }));
        var large = Done(StyleEdits.SetPreset(
            Done(StyleEdits.SetText(new ThermiConfig(), 0, TextPart.Value, new TextStyle(20, TextWeight.Bold))),
            0,
            StylePreset.Large));
        Assert.Null(large.Overlays[0].Style.Value);
        Assert.Equal(Refusal.NotFound, StyleEdits.SetPreset(large, 1, StylePreset.Compact).Refused);
    }

    [Fact]
    public void TextSizesStayBetweenNineAndThirtySixInHalfSteps()
    {
        foreach (var part in new[] { TextPart.Label, TextPart.Value })
        {
            foreach (var weight in new[] { TextWeight.Regular, TextWeight.Semibold, TextWeight.Bold })
            {
                foreach (var size in new[] { 9, 36 })
                {
                    var (label, value) = StylePresets.Text(
                        Done(StyleEdits.SetText(new ThermiConfig(), 0, part, new TextStyle(size, weight))).Overlays[0].Style);
                    Assert.Equal(new TextStyle(size, weight), part == TextPart.Label ? label : value);
                }

                var field = part == TextPart.Label ? "label" : "value";
                Assert.Equal(
                    $"overlays[0].style.{field}.size is 8.5, not 9 to 36",
                    StyleEdits.SetText(new ThermiConfig(), 0, part, new TextStyle(8.5, weight)).Problem);
                Assert.Equal(
                    $"overlays[0].style.{field}.size is 12.25, not a step of 0.5",
                    StyleEdits.SetText(new ThermiConfig(), 0, part, new TextStyle(12.25, weight)).Problem);
                Assert.Equal(
                    $"overlays[0].style.{field}.size is 36.5, not 9 to 36",
                    StyleEdits.SetText(new ThermiConfig(), 0, part, new TextStyle(36.5, weight)).Problem);
            }
        }
    }

    [Fact]
    public void BelowThirtyPercentTheTextGetsAShadow()
    {
        var effects = new[] { 0, 0.29, 0.3, 1 }
            .Select(background => Look(Done(StyleEdits.SetOpacity(new ThermiConfig(), 0, new OpacityConfig(background)))))
            .Select(look => look.Effect);
        var outlined = Done(StyleEdits.Restyle(new ThermiConfig(), 0, style => style with { Effect = TextEffect.Outline, Opacity = new OpacityConfig(0) }));

        Assert.Equal([TextEffect.Shadow, TextEffect.Shadow, TextEffect.None, TextEffect.None], effects);
        Assert.Equal(TextEffect.Outline, Look(outlined).Effect);
        Assert.Equal((0.0, 1.0), (Look(outlined).Background, Look(outlined).Opacity));
    }

    [Fact]
    public void MeterColorsCanChangeAndGoBack()
    {
        var config = new ThermiConfig();
        config = Done(StyleEdits.SetColor(config, 0, StyleColor.Normal, "#0072B2"));
        config = Done(StyleEdits.SetColor(config, 0, StyleColor.Warning, "#E69F00"));
        var changed = Done(StyleEdits.SetColor(config, 0, StyleColor.Critical, "#D55E00"));

        var reset = changed;
        foreach (var level in new[] { StyleColor.Normal, StyleColor.Warning, StyleColor.Critical })
        {
            reset = Done(StyleEdits.SetColor(reset, 0, level, null));
        }

        Assert.Equal(
            ["#0072B2", "#E69F00", "#D55E00"],
            new[] { Level.Normal, Level.Warning, Level.Critical }.Select(level => Look(changed).MeterColor(level).ToString()));
        Assert.Equal(
            [Look(new ThermiConfig()).Normal, OverlayLook.DefaultWarning, OverlayLook.DefaultCritical],
            new[] { Level.Normal, Level.Warning, Level.Critical }.Select(Look(reset).MeterColor));
        Assert.Null(reset.Overlays[0].Style.Colors);
        Assert.Equal("overlays[0].style.colors.normal is \"red\", not a color like #RRGGBB", StyleEdits.SetColor(reset, 0, StyleColor.Normal, "red").Problem);
    }

    [Fact]
    public void TextIsAlwaysReadableOnItsPills()
    {
        var light = Done(StyleEdits.Restyle(new ThermiConfig(), 0, style => style with { Pills = PillTheme.Light }));
        var gray = Done(StyleEdits.SetColor(new ThermiConfig(), 0, StyleColor.Label, "#808080"));
        var grayOnLight = Done(StyleEdits.SetColor(light, 0, StyleColor.Label, "#808080"));
        var white = Done(StyleEdits.SetColor(light, 0, StyleColor.Value, "#FFFFFF"));

        Assert.Equal(
            (new TextColor(Rgb.Parse("#A9A9A7")), new TextColor(Rgb.Parse("#EFEFEE"))),
            (Look(new ThermiConfig()).LabelColor, Look(new ThermiConfig()).ValueColor));
        Assert.Equal((new TextColor(Rgb.Parse("#5C5C5A")), new TextColor(Rgb.Parse("#1A1A1A"))), (Look(light).LabelColor, Look(light).ValueColor));
        Assert.Equal(new TextColor(Rgb.Parse("#808080")), Look(gray).LabelColor);
        Assert.Equal(new TextColor(Rgb.Parse("#808080")), Look(grayOnLight).LabelColor);
        Assert.Equal(new TextColor(Rgb.Parse("#1A1A1A"), Unreadable: true), Look(white).ValueColor);
        Assert.Equal(21, Math.Round(Rgb.Parse("#000000").ContrastWith(Rgb.Parse("#FFFFFF")), 2));
    }

    // The pills' theme, none for the default, the taskbar's mode, which alone reaches the pills, and whether they are light.
    [Theory] // Rows: the default follows the taskbar each way, and each theme ignores it each way
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData(PillTheme.Dark, true, false)]
    [InlineData(PillTheme.Light, false, true)]
    public void TheDefaultLookIsStandardTextOnPillsThatMatchTheTaskbar(PillTheme? pills, bool lightTaskbar, bool lightPills)
    {
        var config = pills is { } theme ? Done(StyleEdits.Restyle(new ThermiConfig(), 0, style => style with { Pills = theme })) : new ThermiConfig();
        var style = config.Overlays[0].Style;

        var look = Look(config, lightTaskbar);
        var resized = Done(StyleEdits.SetText(config, 0, TextPart.Value, new TextStyle(15, TextWeight.Bold))).Overlays[0].Style;

        Assert.Equal((new TextStyle(13, TextWeight.Semibold), new TextStyle(13.5, TextWeight.Bold)), (look.Label, look.Value));
        Assert.Equal(lightPills ? PillColors.Light : PillColors.Dark, look.Pills);
        Level[] levels = [Level.Normal, Level.Warning, Level.Critical];
        Assert.Equal(
            lightPills ? ["#0F7B0F", "#9D5D00", "#C42B1C"] : ["#6CCB5F", OverlayLook.DefaultWarning.ToString(), OverlayLook.DefaultCritical.ToString()],
            levels.Select(level => look.MeterColor(level).ToString()));
        // Each level's color is a mark of its own on the pill, on a pill a single bar holds and on a graph's unlit track; the
        // pills a bar holds stand from it at 1.5:1, and no more than 2:1, so the runs read apart quietly.
        Rgb[] unders = [look.Pills.Outer, look.Pills.Inner, look.Pills.MeterOff];
        Assert.All(
            levels.SelectMany(level => unders.Select(under => look.MeterColor(level).ContrastWith(under))),
            contrast => Assert.True(contrast >= OverlayLook.MinContrast, $"{contrast:0.00}:1"));
        Assert.InRange(look.Pills.Inner.ContrastWith(look.Pills.Outer), 1.5, 2);
        Assert.Equal((StylePreset.Custom, new TextStyle(15, TextWeight.Bold)), (resized.Preset, StylePresets.Text(resized).Value));
        Assert.Equal(style, resized with { Preset = style.Preset, Label = style.Label, Value = style.Value });
        // Its tile's sample is three pills, the CPU's temperature and load, the GPU and the RAM, each value with its bars, in the look.
        var sample = LookSample.Of(style);
        var sampled = OverlayLayout.Groups(OverlayLayout.Build(sample, SensorMap.Read(Desktop, sample), English), sample.Style.Layout);
        Assert.Equal(
            [[Metrics.CpuTemperature.Id, Metrics.CpuLoad.Id], [Metrics.GpuLoad.Id], [Metrics.RamUsed.Id]],
            sampled.Select(pill => pill.Select(item => item.Id)));
        Assert.All(sampled.SelectMany(pill => pill), item => Assert.NotNull(item.Lit));
        Assert.Equal(style.Pills, sample.Style.Pills);
    }

    // Each level on dark, light and high contrast pills, and the mark its value carries; percentages held between 0 and
    // 100; and only a graph item holds a meter, since a pill draws no bar.
    [Fact]
    public void ALevelShowsOnTheValueNeverByColorAlone()
    {
        var contrast = new HighContrastColors(Rgb.Parse("#000000"), Rgb.Parse("#FFFFFF"), Rgb.Parse("#1AEBFF"), Rgb.Parse("#FFFF00"));
        (bool Light, HighContrastColors? HighContrast, Level Level, bool Metered, LevelMark Mark)[] marks =
        [
            (false, null, Level.Normal, false, new LevelMark(MarkKind.None, default, default)),
            (false, null, Level.Warning, false, new LevelMark(MarkKind.Underline, OverlayLook.DefaultWarning, default)),
            (false, null, Level.Critical, false, new LevelMark(MarkKind.Reversed, OverlayLook.DefaultCritical, Rgb.Parse("#000000"))),
            (true, null, Level.Normal, false, new LevelMark(MarkKind.None, default, default)),
            (true, null, Level.Warning, false, new LevelMark(MarkKind.Underline, Rgb.Parse("#9D5D00"), default)),
            (true, null, Level.Critical, false, new LevelMark(MarkKind.Reversed, Rgb.Parse("#C42B1C"), Rgb.Parse("#FFFFFF"))),
            (false, contrast, Level.Warning, false, new LevelMark(MarkKind.Underline, contrast.WindowText, default)),
            (false, contrast, Level.Critical, false, new LevelMark(MarkKind.Reversed, contrast.WindowText, contrast.Window)),
            // A meter or a graph shows a warning in its color, so its value is not underlined too; a critical one is still reversed.
            (false, null, Level.Warning, true, new LevelMark(MarkKind.None, default, default)),
            (true, null, Level.Critical, true, new LevelMark(MarkKind.Reversed, Rgb.Parse("#C42B1C"), Rgb.Parse("#FFFFFF"))),
        ];
        var graphed = Done(PowerEdits.SetGraph(new ThermiConfig(), Metrics.CpuTemperature.Id, GraphStyle.Line));
        var readings = new Dictionary<string, Reading>(TrayTextTests.Quiet, StringComparer.Ordinal)
        {
            [Metrics.CpuLoad.Id] = new(SensorKind.Load, -3),
            [Metrics.GpuLoad.Id] = new(SensorKind.Load, 104),
        };

        var shown = OverlayLayout.Build(graphed.Overlays[0], readings, English);

        Assert.All(
            marks,
            row => Assert.Equal(
                row.Mark,
                LevelMarks.Of(
                    OverlayLook.Of(new StyleConfig { Pills = row.Light ? PillTheme.Light : PillTheme.Dark }, row.Light, row.HighContrast),
                    row.Level,
                    row.Metered)));
        Assert.Equal(
            [ReadingFormatter.Format(new Reading(SensorKind.Load, 0), English), ReadingFormatter.Format(new Reading(SensorKind.Load, 100), English)],
            shown.Where((_, at) => at is 2 or 4).Select(item => item.Value));
        Assert.Equal([true, false, true, false, true, false], shown.Select(item => item.Graph is not null));
        // An item that draws no line lights its bars instead; the power, which lines cannot judge, draws neither.
        Assert.Equal([null, 1, null, null, null, 2], shown.Select(item => item.Lit));
        // Only an item with neither underlines its warning.
        var warm = new OverlayItem("CPU", "90 °C", Level.Warning, "100 °C");
        var dark = OverlayLook.Of(new StyleConfig { Pills = PillTheme.Dark }, lightTaskbar: false);
        Assert.Equal(
            [MarkKind.Underline, MarkKind.None, MarkKind.None],
            new[] { warm, warm with { Lit = 5 }, warm with { Graph = [] } }.Select(item => LevelMarks.Of(dark, item).Kind));
    }

    // The RAM's meter, its load: of five segments, those its value rounds to, at least one for any value above zero, a
    // half rounding up; none at zero or while it reads nothing.
    [Theory] // Rows: the rounding's boundaries at 0, 3 and 50 % and a value that reads nothing, as its step names them
    [InlineData(0, 0)]
    [InlineData(3, 1)]
    [InlineData(50, 3)]
    [InlineData(double.NaN, 0)]
    public void AMeterLightsTheSegmentsItsValueRoundsTo(double load, int lit)
    {
        var readings = new Dictionary<string, Reading>(TrayTextTests.Quiet, StringComparer.Ordinal)
        {
            [Metrics.RamLoad.Id] = new(SensorKind.Load, load),
        };

        var ram = OverlayLayout.Build(new OverlayConfig(), readings, English).Single(item => item.Id == Metrics.RamUsed.Id);

        Assert.Equal(lit, ram.Lit);
    }

    // An item going critical, back to normal and critical again; and another going critical while the first glows. With
    // animation effects off, nothing glows, then or after they are turned on.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APillGlowsOnceTheFirstTimeItsItemGoesCritical(bool animate)
    {
        var time = new FakeTimeProvider();
        var glow = new CriticalGlow(time);
        static OverlayItem Item(string id, Level level)
        {
            return new("CPU", "90 °C", level, "100 °C") { Id = id };
        }

        var strengths = new List<double>();
        void Step(int milliseconds, bool animations, params OverlayItem[] items)
        {
            time.Advance(TimeSpan.FromMilliseconds(milliseconds));
            glow.Update(items, animations);
            strengths.Add(glow.Of("cpu"));
        }

        Step(0, animate, Item("cpu", Level.Critical));
        Step(300, animate, Item("cpu", Level.Critical), Item("gpu", Level.Critical));
        Step(300, animate, Item("cpu", Level.Normal), Item("gpu", Level.Critical));
        Step(100, true, Item("cpu", Level.Critical), Item("gpu", Level.Critical));

        Assert.Equal(animate ? [0, 1, 0, 0] : [0, 0, 0, 0], strengths.Select(strength => Math.Round(strength, 3)));
        Assert.Equal(animate ? 0.866 : 0, Math.Round(glow.Of("gpu"), 3));
        Assert.Equal(animate, glow.Glowing);
    }

}
