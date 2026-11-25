using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Core.Look;

/// <summary>
/// What a look's tile in the window shows: three pills of the default overlay's items, the CPU's temperature and load, the
/// GPU's load and the RAM in use, each with its bars, side by side in the look. A look holds no layout or direction, and
/// bars stay narrow where lines would need a minute of readings.
/// </summary>
public static class LookSample
{
    private static readonly HashSet<string> Shown =
        new([Metrics.CpuTemperature.Id, Metrics.CpuLoad.Id, Metrics.GpuLoad.Id, Metrics.RamUsed.Id], StringComparer.Ordinal);

    public static OverlayConfig Of(StyleConfig look) => new()
    {
        Style = look with { Layout = PillLayout.Pills, Orientation = Orientation.Horizontal },
        Graph = GraphStyle.Bars,
        Items = [.. OverlayConfig.DefaultItems.Where(item => Shown.Contains(item.Id))],
    };
}
