using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Temperature units, hidden units, an item's own color and click-through; the lock is the window's, in the Windows tests.
/// </summary>
public sealed class MoreControlTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    private static List<TextColor> ValueColors(ThermiConfig config)
    {
        var overlay = config.Overlays[0];
        var look = OverlayLook.Of(overlay.Style, lightTaskbar: false);
        return [.. OverlayLayout.Build(overlay, SensorMap.Read(Desktop, overlay), CultureInfo.InvariantCulture).Select(look.ValueColorOf)];
    }

    [Fact]
    public void TemperaturesShowInTheChosenUnitAndAreKeptInCelsius()
    {
        var fahrenheit = Done(ThermiEdits.SetTemperature(new ThermiConfig(), TemperatureUnit.Fahrenheit));
        var unit = fahrenheit.Temperature;
        var threshold = Item(fahrenheit, Metrics.CpuTemperature.Id).Threshold!;

        var shown = OverlayLayout.Build(fahrenheit.Overlays[0], TrayTextTests.Quiet, English, Texts.English, unit)[0];
        var set = Done(ItemEdits.SetThreshold(fahrenheit, Metrics.CpuTemperature.Id, Temperatures.Kept(150, unit), Temperatures.Kept(185, unit)));

        Assert.Equal(("144 °F", "212 °F"), (shown.Value, shown.Room));
        Assert.Equal([194.0, 203.0], new[] { threshold.Warning!.Value, threshold.Critical!.Value }.Select(value => Temperatures.Shown(value, unit)));
        Assert.Equal(new Threshold(90, 95), threshold);
        Assert.Contains("\"temperature\": \"f\"", File(fahrenheit), StringComparison.Ordinal);
        Assert.Equal(new Threshold(65.6, 85), Item(set, Metrics.CpuTemperature.Id).Threshold);
        Assert.Equal((32.0, 212.0, 1.0), Temperatures.Track(unit));
        Assert.Equal((0.0, 100.0, 1.0), Temperatures.Track(TemperatureUnit.Celsius));
        Assert.Equal((-9.0, -5.0), (Temperatures.ShownDifference(-5, unit), Temperatures.KeptDifference(-9, unit)));
        Assert.Equal(
            (-5.0, -5.0, 62.0),
            (
                Temperatures.ShownDifference(-5, TemperatureUnit.Celsius),
                Temperatures.KeptDifference(-5, TemperatureUnit.Celsius),
                Temperatures.Kept(62, TemperatureUnit.Celsius)));
    }

    // thermi's tooltip gives the hottest value as the overlay shows it, so without its unit; an item's icon keeps its own.
    // Labels off, each pill keeps its items and shows their values alone.
    [Fact]
    public void UnitsAndLabelsCanBeHidden()
    {
        var bare = WithOverlay(
            TrayTextTests.InTray(new ThermiConfig(), Metrics.CpuFan.Id),
            0,
            overlay => overlay with { Style = overlay.Style with { Units = false } });
        var readings = SensorMap.Read(Desktop, bare.Overlays[0]);

        var shown = OverlayLayout.Build(bare.Overlays[0], TrayTextTests.Quiet, English);
        var icons = TrayScene.Of(bare, TrayTextTests.Input());

        Assert.Equal(["62", "1,087", "13", "88", "3", "27.3"], shown.Select(item => item.Value));
        Assert.Equal(["CPU", null, null, null, "GPU", "RAM"], shown.Select(item => item.Label));
        Assert.Equal(["100", "8,888", "100", "888", "100", "888.8"], shown.Select(item => item.Room));
        Assert.Equal("4.6", ReadingFormatter.Format(new Reading(SensorKind.Clock, 4560), English, units: false));
        Assert.Contains(ReadingFormatter.Format(readings[Metrics.CpuTemperature.Id], English, units: false), icons[0].Tooltip, StringComparison.Ordinal);
        Assert.DoesNotContain(ReadingFormatter.Format(readings[Metrics.CpuTemperature.Id], English), icons[0].Tooltip, StringComparison.Ordinal);
        Assert.Contains(ReadingFormatter.Format(readings[Metrics.CpuFan.Id], English), icons[1].Tooltip, StringComparison.Ordinal);

        var unlabeled = Done(StyleEdits.Restyle(bare, 0, style => style with { NoLabels = true })).Overlays[0];
        var look = OverlayLook.Of(unlabeled.Style, lightTaskbar: false);
        var pills = OverlayLayout.Groups(OverlayLayout.Build(unlabeled, TrayTextTests.Quiet, English), look.Layout, look.Labels);
        Assert.Equal([4, 1, 1], pills.Select(pill => pill.Count));
        Assert.All(pills.SelectMany(pill => pill), item => Assert.Null(item.Label));
    }

    [Fact]
    public void AnItemsOwnColorOverridesTheStyle()
    {
        var styled = Done(StyleEdits.SetColor(new ThermiConfig(), 0, StyleColor.Value, "#E0E0E0"));
        var own = Done(ItemEdits.SetColor(styled, Metrics.CpuTemperature.Id, "#FFB000"));
        var tooClose = Done(ItemEdits.SetColor(own, Metrics.GpuLoad.Id, "#202020"));
        var back = Done(ItemEdits.SetColor(tooClose, Metrics.CpuTemperature.Id, null));

        var colors = ValueColors(tooClose);

        Assert.Equal(new TextColor(Rgb.Parse("#FFB000")), colors[0]);
        Assert.All(colors.Where((_, at) => at is not (0 or 4)), color => Assert.Equal(new TextColor(Rgb.Parse("#E0E0E0")), color));
        Assert.Equal(new TextColor(PillColors.Dark.Value, Unreadable: true), colors[4]);
        Assert.Equal(new TextColor(Rgb.Parse("#E0E0E0")), ValueColors(back)[0]);
        Assert.Equal(
            "overlays[0].items[0].color is \"orange\", not a color like #RRGGBB",
            ItemEdits.SetColor(back, Metrics.CpuTemperature.Id, "orange").Problem);
    }
}
