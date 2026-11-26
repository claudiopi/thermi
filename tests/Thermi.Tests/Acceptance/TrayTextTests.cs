using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Editing;
using Thermi.Core.Frames;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// What each tray icon shows and says, decided in Core.
/// </summary>
public sealed class TrayTextTests
{
    private const string Installed = @"C:\Program Files\thermi\thermi.exe";

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    /// <summary>The default items' values as the spec gives them, every meter normal.</summary>
    internal static Dictionary<string, Reading> Quiet => new(StringComparer.Ordinal)
    {
        [Metrics.CpuFan.Id] = new(SensorKind.Fan, 1087),
        [Metrics.CpuFanDuty.Id] = new(SensorKind.Control, 40),
        [Metrics.CpuTemperature.Id] = new(SensorKind.Temperature, 62),
        [Metrics.CpuLoad.Id] = new(SensorKind.Load, 12.6),
        [Metrics.CpuPower.Id] = new(SensorKind.Power, 88.4),
        [Metrics.GpuLoad.Id] = new(SensorKind.Load, 3),
        [Metrics.RamUsed.Id] = new(SensorKind.Data, 27.3),
        [Metrics.RamLoad.Id] = new(SensorKind.Load, 44),
    };

    internal static TrayInput Input(ServiceState service = ServiceState.Running, bool light = false, Snapshot? snapshot = null) =>
        new(snapshot ?? Desktop, service, English, Texts.English, light, Installed);

    /// <summary>Settings whose items of those ids ask for a tray icon of their own.</summary>
    internal static ThermiConfig InTray(ThermiConfig config, params string[] ids) =>
        ids.Aggregate(config, (current, id) => WithItem(current, id, item => item with { Tray = true }));

    /// <summary>An overlay as it shows, in lines: one per pill of its layout, which in a single bar is one per label.</summary>
    internal static IEnumerable<string> Lines(IReadOnlyList<OverlayItem> shown) =>
        OverlayLayout.Groups(shown)
            .Select(pill => string.Join(' ', pill.Select(item => item.Label is null ? item.Value : string.Concat(item.Label, " ", item.Value))));

    private static string Pills(OverlayConfig overlay) => string.Join('\n', Lines(OverlayLayout.Build(overlay, Quiet, English)));

    private static TrayIcon Icon(IReadOnlyList<TrayIcon> icons, string? id) => icons.Single(icon => icon.ItemId == id);

    [Fact]
    public void EachPillOfTheLayoutHoldsItsItems()
    {
        var split = Done(ItemEdits.SetOwn(new ThermiConfig(), Metrics.CpuPower.Id, true)).Overlays[0];

        Assert.Equal("CPU 62 °C 1,087 RPM 13 % 88 W\nGPU 3 %\nRAM 27.3 GB", Pills(new OverlayConfig()));
        Assert.Equal("CPU 62 °C 1,087 RPM 13 %\nCPU 88 W\nGPU 3 %\nRAM 27.3 GB", Pills(split));
        Assert.Equal(
            "CPU 62 °C\nCPU 1,087 RPM\nCPU 13 %\nCPU 88 W\nGPU 3 %\nRAM 27.3 GB",
            Pills(split with { Style = new StyleConfig { Layout = PillLayout.Each } }));
        Assert.Equal("CPU 62 °C 1,087 RPM 13 % 88 W\nGPU 3 %\nRAM 27.3 GB", Pills(split with { Style = new StyleConfig { Layout = PillLayout.One } }));
    }

    [Fact]
    public void TheTrayTooltipStopsAtWindowsLimit()
    {
        List<string> many = [.. Enumerable.Range(1, 20).Select(number => $"PILL{number} 1,087 RPM")];
        var longLine = new string('L', 150) + " 62 °C";

        var cut = TrayTexts.Fit(many);
        var alone = TrayTexts.Fit([longLine]);
        var after = TrayTexts.Fit([many[0], longLine]);

        Assert.InRange(cut.Length, 100, TrayTexts.MaxTooltip);
        Assert.EndsWith("\n…", cut, StringComparison.Ordinal);
        Assert.All(cut.Split('\n')[..^1], (line, at) => Assert.Equal($"PILL{at + 1} 1,087 RPM", line));
        Assert.Equal(new string('L', TrayTexts.MaxTooltip - 1) + "…", alone);
        Assert.Equal("PILL1 1,087 RPM\n…", after);
    }

    [Fact]
    public void ATrayIconFitsItsValueInFourCharacters()
    {
        Reading[] values =
        [
            new(SensorKind.Temperature, 62), new(SensorKind.Load, 12.6), new(SensorKind.Fan, 1087), new(SensorKind.Fan, 12345),
            new(SensorKind.Data, 27.3), new(SensorKind.Data, 128.4), new(SensorKind.Temperature, null),
        ];

        Assert.Equal(["62", "13", "1087", "12k", "27.3", "128", "–"], values.Select(value => TrayTexts.IconText(value, English)));
        Assert.Equal("27,3", TrayTexts.IconText(values[4], CultureInfo.GetCultureInfo("es-ES")));
        Assert.Equal("144", TrayTexts.IconText(new Reading(SensorKind.Temperature, 62), English, TemperatureUnit.Fahrenheit));
        Assert.Equal("-12k", TrayTexts.IconText(new Reading(SensorKind.Fan, -12345), English));
        Assert.Equal("", TrayTexts.Fit([]));
        Assert.Equal(("75", "CPU 75 °C"), MaximumIcon(60, 75, 70));
    }

    // An item in the tray showing its maximum, its history fed a value a second; its icon shows what the overlay does.
    private static (string? Text, string Tooltip) MaximumIcon(params double[] values)
    {
        var time = new FakeTimeProvider();
        var history = new ItemHistory(time);
        var overlay = new OverlayConfig
        {
            Items = [new ItemConfig { Id = Metrics.CpuTemperature.Id, Metric = Metrics.CpuTemperature.Id, Stat = ItemStat.Max, Tray = true }],
        };
        foreach (var value in values)
        {
            history.Add(overlay, new Dictionary<string, Reading>(StringComparer.Ordinal) { [Metrics.CpuTemperature.Id] = new(SensorKind.Temperature, value) });
            time.Advance(TimeSpan.FromSeconds(1));
        }

        var icon = Icon(TrayScene.Of(new ThermiConfig { Overlays = [overlay] }, Input() with { History = history }), Metrics.CpuTemperature.Id);
        return (icon.Text, icon.Tooltip);
    }

    [Fact]
    public void ATrayIconTakesItsMetersColorAndStaysReadable()
    {
        // The CPU fan meters its own speed with no lines, so it is given some its 1,087 RPM is past.
        var warned = Done(ItemEdits.SetThreshold(new ThermiConfig(), Metrics.CpuFan.Id, 1000, 2000));
        var config = InTray(warned, Metrics.CpuFan.Id, Metrics.CpuPower.Id);

        var dark = TrayScene.Of(config, Input()).Where(icon => icon.ItemId is not null).ToList();
        var light = TrayScene.Of(config, Input(light: true)).Where(icon => icon.ItemId is not null).ToList();

        Assert.Equal(["1087", "71"], dark.Select(icon => icon.Text));
        Assert.Equal([OverlayLook.DefaultWarning, Taskbar.Dark.Text], dark.Select(icon => icon.Color));
        Assert.Equal([Rgb.Parse("#8A5A00"), Taskbar.Light.Text], light.Select(icon => icon.Color));
        Assert.True(dark[0].Color.ContrastWith(Taskbar.Dark.Background) >= 3);
        Assert.True(light[0].Color.ContrastWith(Taskbar.Light.Background) >= 3);

        // A color of the user's that the light taskbar would not let anyone read falls back to the default there.
        var pale = InTray(Done(StyleEdits.SetColor(warned, 0, StyleColor.Warning, "#FFE680")), Metrics.CpuFan.Id);
        Assert.Equal(Rgb.Parse("#8A5A00"), Icon(TrayScene.Of(pale, Input(light: true)), Metrics.CpuFan.Id).Color);
        Assert.Equal(Rgb.Parse("#FFE680"), Icon(TrayScene.Of(pale, Input()), Metrics.CpuFan.Id).Color);
    }

    [Fact]
    public void TrayIconsNeverShowAnOldNumber()
    {
        var config = InTray(new ThermiConfig(), Metrics.CpuFan.Id, Metrics.RamUsed.Id);

        var stopped = TrayScene.Of(config, Input(ServiceState.Stopped));
        var stale = TrayScene.Of(config, Input(ServiceState.Stale));
        var notRunning = Texts.English.Of(ServiceState.Stopped);
        var thermis = "thermi · " + Texts.English.Of(TrayText.SensorsStopped);
        var gone = new ThermiConfig
        {
            Overlays =
            [
                new OverlayConfig
                {
                    Items =
                    [
                        new ItemConfig
                        {
                            Id = "gone",
                            Sensor = "/nowhere/0/fan/9",
                            Tray = true,
                            WarnBy = new WarnByConfig { Sensor = "/nowhere/0/control/9" },
                        },
                    ],
                },
            ],
        };

        Assert.All(stopped.Concat(stale).Where(icon => icon.ItemId is not null), icon => Assert.Equal(("–", Taskbar.Dark.Text), (icon.Text, icon.Color)));
        Assert.Equal([thermis, "CPU\n" + notRunning, "RAM\n" + notRunning], stopped.Select(icon => icon.Tooltip));
        Assert.Equal(notRunning, TrayTexts.ItemTooltip(null, null, ServiceState.Stopped, Texts.English, English, TemperatureUnit.Celsius));
        var goneIcon = Icon(TrayScene.Of(gone, Input()), "gone");
        Assert.Equal(("–", Taskbar.Dark.Text, "–"), (goneIcon.Text, goneIcon.Color, goneIcon.Tooltip));
        Assert.Equal("RAM\n" + Texts.English.Of(ServiceState.Stale), Icon(stale, Metrics.RamUsed.Id).Tooltip);
        Assert.Equal((Taskbar.Dark.Gray, thermis), (Icon(stale, null).Color, Icon(stale, null).Tooltip));
        Assert.Equal(thermis, Icon(TrayScene.Of(config, Input() with { Snapshot = null }), null).Tooltip);
    }

    [Fact]
    public void TrayIconsKeepTheirIdentityPerInstall()
    {
        const string Elsewhere = @"C:\Program Files\thermi-build\thermi.exe";
        var ids = ThermiConfigJson.Read(TestFiles.Config("v6.json")).Config.Overlays.SelectMany(overlay => overlay.Items).Select(item => item.Id).ToList();

        List<Guid> Made(string path)
        {
            return [TrayScene.IdOf(null, path), .. ids.Select(id => TrayScene.IdOf(id, path))];
        }

        Assert.Equal(Made(Installed), Made(Installed));
        Assert.Equal(Made(Installed), Made(Installed.ToLowerInvariant()));
        Assert.Empty(Made(Installed).Intersect(Made(Elsewhere)));
        Assert.Equal(ids.Count + 1, Made(Installed).Distinct().Count());
    }

    [Fact]
    public void TrayIconsAreRedrawnOnlyWhenTheyChange()
    {
        var config = InTray(new ThermiConfig(), Metrics.CpuFan.Id, Metrics.CpuPower.Id);
        var refresh = new TrayRefresh();
        var first = refresh.Next(TrayScene.Of(config, Input()));
        // Faster fans redraw the fan's icon; a warmer CPU changes only the words of thermi's, which names the hottest value.
        var faster = Desktop with
        {
            Sensors = [.. Desktop.Sensors.Select(sensor => sensor switch
            {
                { HardwareType: "SuperIO", Type: "Fan" } => sensor with { Value = sensor.Value + 100 },
                { HardwareType: "Cpu", Type: "Temperature" } => sensor with { Value = sensor.Value + 1 },
                _ => sensor,
            })],
        };

        var still = Enumerable.Range(0, 10).Select(_ => refresh.Next(TrayScene.Of(config, Input()))).ToList();
        var changed = refresh.Next(TrayScene.Of(config, Input(snapshot: faster)));
        var gone = refresh.Next(TrayScene.Of(new ThermiConfig(), Input(snapshot: faster)));

        Assert.Equal(3, first.Redrawn.Count);
        Assert.All(still, update => Assert.Equal(0, update.Redrawn.Count + update.Retipped.Count + update.Removed.Count));
        Assert.Equal([Metrics.CpuFan.Id], changed.Redrawn.Select(icon => icon.ItemId));
        Assert.Equal([null], changed.Retipped.Select(icon => icon.ItemId));
        Assert.Equal(2, gone.Removed.Count);
    }

    // A state's words: the CPU temperature at warning or critical, the recording without the driver, the CPU fan reading
    // nothing, the service stopped or stale, an overlay hidden, click-through, high contrast, an overlay with nothing to
    // show, four overlays all critical, the window never opened yet, and the PC asleep past the stale limit, then woken by
    // a fresh snapshot, and a clock among the items, which Windows reads with no service at all, and while the service runs
    // but has sent nothing yet, which the game's sensors every reader adds do not stand in for. The hint is the first
    // value tooltip's that has one.
    [Theory] // Rows: each a state its steps name: levels, no driver, reading nothing, stopped, stale, asleep and woken, contrast, first run
    [InlineData("", MarkState.Calm, false, ValueHint.None)]
    [InlineData("warning", MarkState.Warm, false, ValueHint.None)]
    [InlineData("critical", MarkState.Hot, false, ValueHint.None)]
    [InlineData("no-driver", MarkState.Calm, false, ValueHint.NeedsDriver)]
    [InlineData("reads-nothing", MarkState.Calm, false, ValueHint.ReadsNothing)]
    [InlineData("critical stopped clock", MarkState.Stopped, true, ValueHint.SensorsStopped)]
    [InlineData("critical awaited clock", MarkState.Stopped, false, ValueHint.SensorsStopped)]
    [InlineData("critical stale", MarkState.Stopped, false, ValueHint.SensorsStopped)]
    [InlineData("critical high-contrast", MarkState.HighContrast, false, ValueHint.None)]
    [InlineData("empty", MarkState.Calm, false, ValueHint.None)]
    [InlineData("four critical hidden click-through", MarkState.Hot, false, ValueHint.None)]
    [InlineData("first-run", MarkState.Calm, false, ValueHint.SetUp)]
    [InlineData("critical asleep", MarkState.Stopped, false, ValueHint.SensorsStopped)]
    [InlineData("critical asleep woken", MarkState.Hot, false, ValueHint.None)]
    public void TheOverlayAndThermisIconSayWhatTheyShow(string state, MarkState mark, bool stoppedPill, ValueHint hint)
    {
        var words = state.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var (config, input) = SceneOf(words);

        var thermi = Icon(TrayScene.Of(config, input), null);
        var scene = ThermiMark.Overlay(config, 0, input, windowOpenedOnce: !words.Contains("first-run"));

        Assert.Equal(mark, ThermiMark.Of(config, input, highContrast: words.Contains("high-contrast")));
        Assert.InRange(thermi.Tooltip.Length, 1, TrayTexts.MaxTooltip);
        Assert.Equal(stoppedPill, scene.SensorsStopped);
        var clock = scene.Items.Where(item => item.Id == Metrics.Clock.Id).ToList();
        Assert.True(!stoppedPill || scene.Items.Count == clock.Count, "While sensors are stopped, the one pill stands in for every sensor's item.");
        Assert.True(
            hint != ValueHint.SensorsStopped || scene.Items.Except(clock).All(item => item.Value == ReadingFormatter.Missing),
            "No value from before the gap shows as live.");
        Assert.True(
            !words.Contains("clock")
                || (clock is [{ Value: not ReadingFormatter.Missing }] && Icon(TrayScene.Of(config, input), Metrics.Clock.Id).Text != ReadingFormatter.Missing),
            "The clock never waits on the sensor service.");
        Assert.Equal(hint, scene.Tooltips.Select(tooltip => tooltip.Hint).FirstOrDefault(each => each != ValueHint.None));
        Assert.All(scene.Tooltips, tooltip => Assert.Equal(TooltipLines(tooltip), ValueTooltips.Text(config, 0, input, tooltip).Split('\n').Length));
    }

    // A value's tooltip says its name and value, the CPU's or GPU's model it is read from, its minimum and maximum, and how
    // to edit it; a missing one only why and what to do. The CPU fan thermi picked by its input's name says it is only
    // likely, until the finder or a hand picks it (cold review 4, G3).
    private static int TooltipLines(ValueTooltip tooltip) =>
        tooltip.Hint is ValueHint.SensorsStopped or ValueHint.NeedsDriver or ValueHint.ReadsNothing ? 1
        : tooltip.ItemId is { } id
            && (id.StartsWith("cpu.", StringComparison.Ordinal) || id.StartsWith("gpu.", StringComparison.Ordinal) || id == Metrics.CpuFan.Id) ? 4
        : 3;

    private static (ThermiConfig Config, TrayInput Input) SceneOf(string[] words)
    {
        var service = words.Contains("stopped") ? ServiceState.Stopped : words.Contains("stale") ? ServiceState.Stale : ServiceState.Running;
        var input = Input(service, snapshot: SnapshotOf(words));
        if (words.Contains("asleep"))
        {
            // The last snapshot came before the PC slept; on waking, the clock has run far past the stale limit.
            var time = new FakeTimeProvider();
            var latest = new LatestSnapshot(time);
            latest.Set(Desktop);
            time.Advance(TimeSpan.FromHours(1));
            if (words.Contains("woken"))
            {
                latest.Set(Desktop);
            }

            input = input with { Service = latest.State, Snapshot = latest.Current };
        }

        return (ConfigOf(words), input);
    }

    private static ThermiConfig ConfigOf(string[] words)
    {
        var hidden = words.Contains("hidden") ? "'hidden':true" : "";
        var through = words.Contains("click-through") ? "'clickThrough':true" : "";
        var overlay = $"{{{string.Join(',', new[] { hidden, through }.Where(part => part.Length > 0))}}}";
        var overlays = string.Join(',', Enumerable.Repeat(overlay, words.Contains("four") ? 4 : 1));
        var (config, problem) = ThermiConfigJson.Read($"{{'version':2,'overlays':[{overlays}]}}".Replace('\'', '"'));
        Assert.Null(problem);

        var threshold = words.Contains("critical") ? new Threshold(60, 75) : words.Contains("warning") ? new Threshold(70, 90) : null;
        var empty = words.Contains("empty");
        ItemConfig[] clock = words.Contains("clock") ? [new ItemConfig { Id = Metrics.Clock.Id, Metric = Metrics.Clock.Id, Tray = true }] : [];
        IReadOnlyList<ItemConfig> Items(OverlayConfig overlay)
        {
            return empty
                ? []
                : [
                    .. overlay.Items.Select(item => item.Id != Metrics.CpuTemperature.Id || threshold is null ? item : item with { Threshold = threshold }),
                    .. clock,
                ];
        }

        return config with { Overlays = [.. config.Overlays.Select(each => each with { Items = Items(each) })] };
    }

    // A stopped service sends no snapshot, so with a clock the feed holds no sensors but what Windows itself says, and the
    // game's, which read nothing without frame slots.
    private static Snapshot SnapshotOf(string[] words) =>
        words.Contains("no-driver") ? TestRecordings.Read(TestRecordings.NoAdmin)
        : words.Contains("reads-nothing")
            ? Desktop with
            {
                Sensors = [.. Desktop.Sensors.Select(sensor => sensor.Id == "/lpc/it8689e/0/fan/0" ? sensor with { Value = null } : sensor)],
            }
        : words.Contains("clock")
            ? GameSensors.With(SystemSensors.With(new Snapshot([]), new WindowsFacts(TimeSpan.FromHours(2), null, DateTimeOffset.UnixEpoch.AddHours(12))), null)
        : Desktop;
}
