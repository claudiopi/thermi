using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using ItemHistory = Thermi.Core.Messages.ItemHistory;
using ItemsHistory = Thermi.Core.Look.ItemHistory;

namespace Thermi.Tests;

/// <summary>The payloads the overlay and the window send each other; <c>MessagesWindowsTests</c> sends them between two windows.</summary>
public sealed class MessageTests
{
    // Each kind decodes as it was encoded; a payload cut short, of another version, with bytes left over, a length or an
    // enum that cannot be, broken UTF-8 or past the cap decodes to null.
    [Theory]
    [InlineData("history", true)]
    [InlineData("select", true)]
    [InlineData("edited", true)]
    [InlineData("edited-none", true)]
    [InlineData("gesture", true)]
    [InlineData("raise", true)]
    [InlineData("asked", true)]
    [InlineData("cut", false)]
    [InlineData("version", false)]
    [InlineData("left-over", false)]
    [InlineData("length", false)]
    [InlineData("floats", false)]
    [InlineData("enum", false)]
    [InlineData("utf8", false)]
    [InlineData("kind", false)]
    [InlineData("too-long", false)]
    [InlineData("empty", false)]
    public void APayloadDecodesAsItWasEncodedOrToNull(string row, bool decodes)
    {
        var (sent, payload) = Payload(row);

        var decoded = WindowMessages.Decode(payload);

        Assert.Equal(decodes, decoded is not null);
        if (decoded is not null)
        {
            Assert.Equal(WindowMessages.Encode(sent), WindowMessages.Encode(decoded));
            Assert.Equal(WindowMessages.NameOf(sent), WindowMessages.NameOf(decoded));
        }
    }

    // The window takes the overlay's history through the message as it opens, having read a second already or not, and
    // again after a reset; from then on both read the same values, a missing one among them, and each second the
    // window's item shows the text, the label and the tag the overlay draws, the tag after its value though it is not the
    // first item of its pill. One that warns by a sensor that reads nothing has no ten minutes to send, and after a reset
    // no maximum either: the window starts its maximum again all the same.
    [Theory]
    [InlineData(ItemStat.Average, 0, 0, false, false)]
    [InlineData(ItemStat.Min, 3, 0, true, false)]
    [InlineData(ItemStat.Now, 5, -5, true, false)]
    [InlineData(ItemStat.Max, 0, 0, false, true)]
    [InlineData(ItemStat.Max, 0, 0, false, true, true)]
    public void TheWindowsHistoryShowsWhatTheOverlaysShows(ItemStat stat, int smooth, double offset, bool readFirst, bool reset, bool warnsByNothing = false)
    {
        var time = new FakeTimeProvider();
        var cpu = new ItemConfig
        {
            Id = Metrics.CpuTemperature.Id,
            Metric = Metrics.CpuTemperature.Id,
            Stat = stat,
            Smooth = smooth,
            Offset = offset,
            WarnBy = warnsByNothing ? new WarnByConfig { Metric = Metrics.GpuTemperature.Id } : null,
        };
        var overlay = new OverlayConfig
        {
            Items = [OverlayConfig.DefaultItems[4], OverlayConfig.DefaultItems[2], cpu],
            Style = new StyleConfig { Layout = PillLayout.One },
        };
        var config = new ThermiConfig { Overlays = [overlay] };
        var (onOverlay, inWindow) = (new ItemsHistory(time), new ItemsHistory(time));
        double[] values = [61, 64, 70, double.NaN, 82, 77, 90, 58, 66, 73, 95, 49, 60, 71];
        var drawn = new List<(string Value, string? Label, string? Tag)>();
        var shown = new List<(string Value, string? Label, string? Tag)>();

        for (var second = 0; second < values.Length; second++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            var readings = new Dictionary<string, Reading>(StringComparer.Ordinal)
            {
                [Metrics.CpuTemperature.Id] = new(SensorKind.Temperature, values[second]),
                [Metrics.GpuLoad.Id] = new(SensorKind.Load, 30),
            };
            if (reset && second == 9)
            {
                onOverlay.Reset();
                inWindow.Take(Sent(onOverlay, config));
            }

            onOverlay.Add(overlay, readings);
            if (second == 5)
            {
                inWindow.Take(Sent(onOverlay, config));
            }

            if (second >= 5 || (readFirst && second == 4))
            {
                inWindow.Add(overlay, readings);
            }

            if (second >= 5)
            {
                drawn.Add(Of(OverlayLayout.Build(overlay, readings, CultureInfo.InvariantCulture, history: onOverlay)));
                shown.Add(Of(OverlayLayout.Build(overlay, readings, CultureInfo.InvariantCulture, history: inWindow)));
            }
        }

        Assert.Equal(drawn, shown);
        Assert.All(shown, item => Assert.Equal((null, OverlayLayout.TagOf(cpu, CultureInfo.InvariantCulture)), (item.Label, item.Tag)));
    }

    private static History Sent(ItemsHistory history, ThermiConfig config) =>
        (History)WindowMessages.Decode(WindowMessages.Encode(history.Sent(config)))!;

    private static (string Value, string? Label, string? Tag) Of(IReadOnlyList<OverlayItem> items) =>
        items.Single(item => item.Id == Metrics.CpuTemperature.Id) is var item ? (item.Value, item.Label, item.Tag) : default;

    private static (WindowMessage Sent, byte[] Payload) Payload(string row)
    {
        var select = new Select(2, "cpu.temperature", SelectHow.PointIn);
        var good = WindowMessages.Encode(select);
        return row switch
        {
            "history" => Encoded(new History([
                new ItemHistory("cpu.temperature", 1_000_000, [40, float.NaN, 41.5f]) { Values = [40.25, double.NaN], Stats = new(38, 41.5, 120, 3, 999_000) },
                new ItemHistory("ram.used", 0, [])])),
            "select" => (select, good),
            "edited" => Encoded(new Edited(1, "cpu.temperature", "ram.used")),
            "edited-none" => Encoded(new Edited(null, null, null)),
            "gesture" => Encoded(new Gesture(GestureKind.Merge, "{ \"version\": 2 }", "{ \"version\": 2, \"überall\": 1 }")),
            "raise" => Encoded(new Raise(0, null)),
            "asked" => Encoded(new HistoryAsked()),
            "cut" => (select, good[..^1]),
            "version" => (select, [1, .. good[1..]]),
            "left-over" => (select, [.. good, 0]),
            "length" => (select, [.. good[..6], 0xFF, 0xFF, 0xFF, 0x7F, .. good[10..]]),
            "floats" => (select, [2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F]),
            "enum" => (select, [.. good[..^1], 9]),
            "utf8" => (select, [2, 5, 0, 0, 0, 0, 1, 0, 0, 0, 0xFF]),
            "kind" => (select, [2, 9]),
            "too-long" => (select, [.. good, .. new byte[WindowMessages.MaxLength]]),
            _ => (select, []),
        };
    }

    private static (WindowMessage, byte[]) Encoded(WindowMessage message) => (message, WindowMessages.Encode(message));
}
