using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// What v1 adds to the overlay, from recorded sensor data to what it shows: Windows' own items, the CPU's and GPU's model,
/// what is busiest at critical, the driver's marker, throttling, and the fade near the pointer.
/// </summary>
public sealed class AdditionTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    // Windows' facts as uptime in minutes, the address or "none", and the local time; what the three items show then.
    [Theory]
    [InlineData(134, "192.168.1.23", "14:05:09", "en-US", "2 h 14 min|192.168.1.23|2:05:09 PM")]
    [InlineData(14, "10.0.0.7", "09:30:00", "es-UY", "14 min|10.0.0.7|09:30:00")]
    [InlineData(134, "none", "23:59:59", "en-US", "2 h 14 min|–|11:59:59 PM")]
    public void TheWindowsItemsReadWindowsItself(int minutes, string address, string time, string culture, string shown)
    {
        var at = TimeOnly.Parse(time, CultureInfo.InvariantCulture);
        var facts = new WindowsFacts(
            TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(29),
            address == "none" ? null : SystemSensors.AddressOf(System.Net.IPAddress.Parse(address).GetAddressBytes()),
            new DateTimeOffset(new DateOnly(2026, 11, 23).ToDateTime(at), TimeSpan.FromHours(-3)));
        var config = new ThermiConfig
        {
            Overlays =
            [
                new OverlayConfig
                {
                    Items = [.. new[] { Metrics.Uptime, Metrics.LocalIp, Metrics.Clock }
                        .Select(metric => new ItemConfig { Id = metric.Id, Metric = metric.Id })],
                },
            ],
        };
        var input = new TrayInput(
            SystemSensors.With(Desktop, facts),
            ServiceState.Running,
            CultureInfo.GetCultureInfo(culture),
            Texts.English,
            LightTaskbar: false,
            "");

        var scene = ThermiMark.Overlay(config, 0, input, windowOpenedOnce: true);

        Assert.Equal(["UP", "IP", "TIME"], scene.Items.Select(item => item.Label));
        Assert.Equal(shown, string.Join('|', scene.Items.Select(item => item.Value)));
        // An address seldom changes, so its pill keeps the room of the address it shows, not of the widest one.
        Assert.Equal(scene.Items[1].Value, scene.Items[1].Room);
    }

    // An item, the recording, its level and what is busiest; then the lines its tooltip adds: the model it is read from, and
    // at critical what keeps its device busy, by name and share.
    [Theory]
    [InlineData("cpu.temperature", "admin", "", "AMD Ryzen 9 9950X")]
    [InlineData("gpu.load", "admin", "", "NVIDIA GeForce RTX 5060 Ti")]
    [InlineData("ram.load", "admin", "", "")]
    [InlineData("ram.used", "admin", "", "=27.2|61.6 GB")]
    [InlineData("cpu.load", "admin critical", "chrome 23", "AMD Ryzen 9 9950X|chrome 23 %")]
    [InlineData("gpu.load", "admin critical", "chrome 23", "NVIDIA GeForce RTX 5060 Ti|game 61 %")]
    [InlineData("cpu.load", "admin warning", "chrome 23", "AMD Ryzen 9 9950X")]
    [InlineData("cpu.temperature", "no-admin", "", "driver")]
    [InlineData("fan.cpu", "no-admin", "", "driver")]
    [InlineData("/amdcpu/0/load/2", "admin", "chrome 23", "AMD Ryzen 9 9950X")]
    [InlineData("/lpc/it8689e/0/temperature/2", "admin", "", "")]
    [InlineData("+/amdcpu/0/load/2", "admin", "", "AMD Ryzen 9 9950X")]
    public void AValuesTooltipSaysWhatIsBehindIt(string metric, string state, string busiest, string adds)
    {
        var words = state.Split(' ');
        var threshold = words.Contains("critical") ? new Threshold(0, 0) : words.Contains("warning") ? new Threshold(0, 1000) : null;
        // A sensor's id is an item of that sensor, and after a plus the first of the sensors an item sums.
        var item = metric[0] == '/' ? new ItemConfig { Id = "sensor", Sensor = metric }
            : metric[0] == '+' ? new ItemConfig { Id = "sum", Sensors = [metric[1..], "/amdcpu/0/load/3"], Combine = Combine.Sum }
            : new ItemConfig { Id = metric, Metric = metric, Meter = threshold is null ? null : new MeterConfig { Metric = metric }, Threshold = threshold };
        var config = new ThermiConfig { Overlays = [new OverlayConfig { Items = [item] }] };
        var snapshot = TestRecordings.Read(words[0] == "admin" ? TestRecordings.Admin : TestRecordings.NoAdmin);
        var busy = busiest.Length == 0 ? null : new Busiest(new BusyProcess("chrome", 23), new BusyProcess("game", 61));
        var input = new TrayInput(snapshot, ServiceState.Running, English, Texts.English, LightTaskbar: false, "") { Busiest = busy };

        var scene = ThermiMark.Overlay(config, 0, input, windowOpenedOnce: true);
        var lines = ValueTooltips.Text(config, 0, input, scene.Tooltips[0]).Split('\n');

        if (adds == "driver")
        {
            Assert.Equal(Texts.English.Of(OverlayText.Driver), scene.Items[0].Value);
            Assert.Equal(scene.Items[0].Value, scene.Items[0].Room);
            Assert.Equal(ValueHint.NeedsDriver, scene.Tooltips[0].Hint);

            // Cold review 4 (G7): a pill says it once, at its first value the driver reads, its others missing as any.
            var defaults = ThermiMark.Overlay(new ThermiConfig(), 0, input, windowOpenedOnce: true).Items;
            Assert.Equal([1, 0, 0], OverlayLayout.Groups(defaults).Select(pill => pill.Count(each => each.Value == scene.Items[0].Value)));
            Assert.Contains(defaults, each => each is { Value: ReadingFormatter.Missing, Label: null });
            return;
        }

        // After an equals sign, the value and the total the first line ends with.
        if (adds.StartsWith('='))
        {
            Assert.EndsWith(Texts.English.Format(English, OverlayText.OfTotal, adds[1..].Split('|')), lines[0], StringComparison.Ordinal);
            return;
        }

        var expected = adds.Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3 + expected.Length, lines.Length);
        Assert.Equal(expected.Length > 0 ? expected[0] : lines[1], lines[1]);
        Assert.All(expected.Skip(1), part => Assert.Contains(lines, line => part.Split(' ').All(word => line.Contains(word, StringComparison.Ordinal))));
    }

    // The CPU's clock, its temperature and its base clock, or "unknown"; whether its clock and temperature are marked as
    // throttling, by metric, by auto or by a CPU sensor of theirs, never the GPU's, the board's or another CPU sensor's.
    // Its warning by default is 90 °C, 10 under AMD's limit.
    [Theory]
    [InlineData(3100, 92, "4300", true)]
    [InlineData(3100, 90, "4300", true)]
    [InlineData(3100, 89.9, "4300", false)]
    [InlineData(4428, 92, "4300", false)]
    [InlineData(4300, 92, "4300", false)]
    [InlineData(3100, 92, "unknown", false)]
    [InlineData(double.NaN, 92, "4300", false)]
    public void TheCpuThrottlesUnderItsBaseClockWhileHot(double clock, double temperature, string baseClock, bool throttles)
    {
        var snapshot = Desktop with
        {
            Sensors = [.. Desktop.Sensors.Select(sensor => sensor.Id switch
            {
                "/amdcpu/0/clock/1" => sensor with { Value = clock },
                "/amdcpu/0/temperature/2" => sensor with { Value = temperature },
                _ => sensor,
            })],
        };
        string[] ids = [Metrics.CpuTemperature.Id, Metrics.CpuClock.Id, Metrics.GpuTemperature.Id, Metrics.CpuLoad.Id];
        ItemConfig[] bySensor =
        [
            new() { Id = "auto", Auto = Metrics.CpuClock.Id, Sensor = "/amdcpu/0/clock/1" },
            new() { Id = "clock", Sensor = "/amdcpu/0/clock/1" },
            new() { Id = "board", Sensor = "/lpc/it8689e/0/temperature/2" },
            new() { Id = "load", Sensor = "/amdcpu/0/load/2" },
            new() { Id = "gone", Sensor = "/amdcpu/9/clock/1" },
        ];
        var config = new ThermiConfig
        {
            Overlays = [new OverlayConfig { Items = [.. ids.Select(id => new ItemConfig { Id = id, Metric = id }), .. bySensor] }],
        };
        var known = baseClock == "unknown" ? (double?)null : double.Parse(baseClock, CultureInfo.InvariantCulture);
        var input = new TrayInput(snapshot, ServiceState.Running, English, Texts.English, LightTaskbar: false, "") { BaseClock = known };

        var scene = ThermiMark.Overlay(config, 0, input, windowOpenedOnce: true);
        var tooltip = ValueTooltips.Text(config, 0, input, scene.Tooltips[1]);

        Assert.Equal([throttles, throttles, false, false, throttles, throttles, false, false, false], scene.Items.Select(item => item.Throttled));
        Assert.Equal(
            throttles,
            tooltip.Contains("3.1 GHz", StringComparison.Ordinal)
                && tooltip.Contains("4.3 GHz", StringComparison.Ordinal)
                && tooltip.Contains(string.Create(English, $"{temperature:0} °C"), StringComparison.Ordinal));
    }

    // The pointer's distance from a 200 × 40 overlay, right of it and below it, at a scale; the switch, the window, a drag and
    // click-through; the opacity it shows at.
    [Theory]
    [InlineData(0, 0, 1.0, "fade", 0.25)]
    [InlineData(-50, -10, 1.0, "fade", 0.25)]
    [InlineData(48, 0, 1.0, "fade", 0.625)]
    [InlineData(96, 0, 1.0, "fade", 1.0)]
    [InlineData(96, 0, 1.5, "fade", 0.75)]
    [InlineData(30, 40, 1.0, "fade", 0.640625)]
    [InlineData(0, 0, 1.0, "", 1.0)]
    [InlineData(0, 0, 1.0, "fade window", 1.0)]
    [InlineData(0, 0, 1.0, "fade drag", 1.0)]
    [InlineData(0, 0, 1.0, "fade click-through", 1.0)]
    public void TheOverlayFadesAsThePointerComesNear(int right, int below, double scale, string state, double opacity)
    {
        var words = state.Split(' ');
        var overlay = new OverlayConfig { Fade = words.Contains("fade"), ClickThrough = words.Contains("click-through") };
        var at = new PixelRect(100, 100, 300, 140);
        var cursor = new Position(right <= 0 ? 200 + right : 299 + right, below <= 0 ? 120 + below : 139 + below);

        Assert.Equal(opacity, PointerFade.Opacity(overlay, at, cursor, scale, words.Contains("window"), words.Contains("drag")), 6);
    }
}
