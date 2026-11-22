using System.Globalization;
using Thermi.Core;

namespace Thermi.Windows.Tests;

/// <summary>The overlay keeps its size while the numbers change, so it never jitters on the taskbar.</summary>
public sealed class PainterTests
{
    [Theory]
    [InlineData(2, 12)]
    [InlineData(9, 100)]
    [InlineData(double.NaN, 55)]
    public void ThePillsKeepTheirWidthWhateverTheNumbers(double before, double after)
    {
        using var painter = new Painter();

        using var first = painter.Paint(Layout(before), 1);
        using var second = painter.Paint(Layout(after), 1);

        Assert.Equal(first.Width, second.Width);
    }

    private static IReadOnlyList<OverlayItem> Layout(double value)
    {
        var readings = new Dictionary<string, Reading>(StringComparer.Ordinal)
        {
            [Metrics.CpuFan.Id] = new(SensorKind.Fan, value * 20),
            [Metrics.CpuTemperature.Id] = new(SensorKind.Temperature, value),
            [Metrics.CpuLoad.Id] = new(SensorKind.Load, value),
            [Metrics.CpuPower.Id] = new(SensorKind.Power, value * 3),
            [Metrics.GpuLoad.Id] = new(SensorKind.Load, value),
            [Metrics.RamUsed.Id] = new(SensorKind.Data, value / 3),
        };
        return OverlayLayout.Build(new OverlayConfig(), readings, CultureInfo.GetCultureInfo("en-US"));
    }
}
