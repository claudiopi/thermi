using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Tests.Bridge;

/// <summary>The bridge between the window's page and C#, as its JSON crosses, and the ten minutes its charts start with.</summary>
public sealed class BridgeTests
{
    // A call goes to the C# method its camel-case name names, its arguments read as the page's generated types write
    // them, and its answer, or why it failed, goes back under its id; an event goes out with its value. "event" and
    // "event:none" raise one with a value and without; "failed:" and a call give the answer that rejects that call. An
    // error of "*" is one whose words are the bridge's, which only a log reads: that it fails is the behavior, and an
    // exception's own message crosses as it is.
    [Theory]
    [InlineData("""{"id":1,"method":"settings","args":[]}""", """{"id":1,"result":{"version":6,"temperature":"f","overlays":[]}}""")]
    [InlineData(
        """{"id":2,"method":"edit","args":[{"kind":"moveItem","item":"cpu.load","overlay":null,"index":2}]}""",
        """{"id":2,"result":"MoveItemEdit { Item = cpu.load, Overlay = , Index = 2 }"}""")]
    [InlineData(
        """{"id":3,"method":"edit","args":[{"kind":"line","item":"cpu.temperature","line":"critical","value":95}]}""",
        """{"id":3,"result":"LineEdit { Item = cpu.temperature, Line = Critical, Value = 95 }"}""")]
    [InlineData("""{"id":4,"method":"history","args":["cpu.load"]}""", """{"id":4,"result":[1.5,null]}""")]
    [InlineData("""{"id":5,"method":"editing","args":[null,"cpu.load",null]}""", """{"id":5,"result":null}""")]
    [InlineData("""{"id":6,"method":"undo","args":[]}""", """{"id":6,"error":"Boom"}""")]
    [InlineData("""{"id":7,"method":"format","args":[]}""", """{"id":7,"error":"*"}""")]
    [InlineData("""{"id":8,"method":"history","args":[]}""", """{"id":8,"error":"*"}""")]
    [InlineData("""{"id":9,"method":"edit","args":[{"kind":"paint"}]}""", """{"id":9,"error":"*"}""")]
    [InlineData(
        """{"id":10,"method":"preview","args":[0]}""",
        """{"id":10,"result":{"png":"data:image/png;base64,","width":120,"height":38,"taskbar":null}}""")]
    [InlineData("""{"id":11,"method":"report","args":["TypeError: x"]}""", """{"id":11,"result":null}""")]
    [InlineData("""{"id":12,"method":"chart","args":["none"]}""", """{"id":12,"error":"*"}""")]
    [InlineData("""{"id":13,"method":"deleteLook","args":["none"]}""", """{"id":13,"error":"Boom"}""")]
    [InlineData("""{"method":"undo","args":[]}""", """{"id":null,"error":"Boom"}""")]
    [InlineData("""{"id":14,"args":[]}""", """{"id":14,"error":"*"}""")]
    [InlineData("""{"id":15,"method":"undo"}""", """{"id":15,"error":"Boom"}""")]
    [InlineData("[1]", """{"id":null,"error":"*"}""")]
    [InlineData("event", """{"event":"picked","value":{"overlay":1,"item":null,"how":"point-in"}}""")]
    [InlineData("event:none", """{"event":"picked","value":null}""")]
    [InlineData("""failed:{"id":16,"method":"undo"}""", """{"id":16,"error":"Boom"}""")]
    [InlineData("failed:[1]", """{"id":null,"error":"Boom"}""")]
    [InlineData("failed:{", """{"id":null,"error":"Boom"}""")]
    public async Task TheBridgeAnswersEachCallAsThePageTypesIt(string call, string answer)
    {
        var bridge = DispatchProxy.Create<IWindowBridge, CannedBridge>();

        var answered = call switch
        {
            "event" => BridgeCalls.EventOf(nameof(IWindowBridge.Picked), new DesktopPickEventArgs(1, null, SelectHow.PointIn)),
            "event:none" => BridgeCalls.EventOf(nameof(IWindowBridge.Picked), null),
            _ when call.StartsWith("failed:", StringComparison.Ordinal) => BridgeCalls.Failed(call["failed:".Length..], "Boom"),
            _ => await BridgeCalls.AnswerAsync(bridge, call),
        };

        Assert.Equal(answer, answer.Contains("\"error\":\"*\"", StringComparison.Ordinal) ? AnyError(answered) : answered);
    }

    // The answer with the words of its error, when it has one, as "*".
    private static string AnyError(string answered)
    {
        var shown = JsonNode.Parse(answered)!.AsObject();
        if (shown["error"]?.GetValue<string>() is { Length: > 0 })
        {
            shown["error"] = "*";
        }

        return shown.ToJsonString();
    }

    // The overlay's ten minutes, its newest the reading of a tenth of a second before it sent them, moved on by the seconds
    // since, under what the window read itself a second apart from then on; a gap stays a gap. Drawn at any moment of a
    // second, the overlay's newest second sits right before the window's first.
    [Theory]
    [InlineData(0.1, 0, 599, 599.0)]
    [InlineData(10.1, 0, 589, 599.0)]
    [InlineData(10.1, 0, 599, null)]
    [InlineData(0.1, 0, 300, null)]
    [InlineData(700.1, 0, 0, null)]
    [InlineData(3.2, 3, 599, 50.0)]
    [InlineData(3.2, 3, 596, 599.0)]
    [InlineData(3.7, 3, 596, 599.0)]
    public void TheChartJoinsTheOverlaysTenMinutesToTheWindowsOwn(double drawnAt, int own, int at, double? expected)
    {
        var time = new FakeTimeProvider();
        var now = 0.0;
        void To(double second)
        {
            time.Advance(TimeSpan.FromSeconds(second - now));
            now = second;
        }

        var chart = new ChartHistory(time);
        var overlay = new OverlayConfig { Items = [new ItemConfig { Id = "cpu.load", Metric = Metrics.CpuLoad.Id }] };
        var sent = Enumerable.Range(0, 600).Select(second => second == 300 ? float.NaN : second).ToArray();
        To(0.1);
        chart.Take(new History([new Thermi.Core.Messages.ItemHistory("cpu.load", time.GetTimestamp(), sent)]));
        for (var second = 1; second <= own; second++)
        {
            To(second);
            chart.Items.Add(overlay, new Dictionary<string, Reading>(StringComparer.Ordinal) { [Metrics.CpuLoad.Id] = new(SensorKind.Load, 50) });
        }

        To(drawnAt);
        var values = chart.Of("cpu.load");

        Assert.Equal(600, values.Count);
        Assert.Equal(expected, values[at]);
    }

    // An item's chart, and its minimum, after what it meters or reads changes and back by undo: only seconds of the reading
    // it draws now, what the overlay sent and the window kept of the one before dropped, and what the window read of the new
    // one taken in. RAM used, 30 GB, meters its load, 56 %, then nothing; the CPU temperature, 70 °C, reads a DIMM, 34 °C.
    [Fact]
    public void AChartDrawsOnlyTheReadingItShows()
    {
        var time = new FakeTimeProvider();
        var chart = new ChartHistory(time);
        var ram = new ItemConfig { Id = "ram", Metric = Metrics.RamUsed.Id, WarnBy = new WarnByConfig { Metric = Metrics.RamLoad.Id } };
        var cpu = new ItemConfig { Id = "cpu", Metric = Metrics.CpuTemperature.Id, Stat = ItemStat.Min };
        var before = new OverlayConfig { Items = [ram, cpu] };
        var after = new OverlayConfig { Items = [ram with { WarnBy = null }, cpu with { Metric = null, Sensor = "dimm", Auto = Metrics.CpuTemperature.Id }] };
        var readings = new Dictionary<string, Reading>(StringComparer.Ordinal)
        {
            [Metrics.RamUsed.Id] = new(Metrics.RamUsed.Kind, 30),
            [Metrics.RamLoad.Id] = new(SensorKind.Load, 56),
            [Metrics.CpuTemperature.Id] = new(SensorKind.Temperature, 70),
            ["dimm"] = new(SensorKind.Temperature, 34),
        };
        chart.Take(new History([new Thermi.Core.Messages.ItemHistory("ram", time.GetTimestamp(), [.. Enumerable.Repeat(56f, 600)])]));
        string Drawn(OverlayConfig overlay)
        {
            for (var second = 0; second < 3; second++)
            {
                chart.Follow(new ThermiConfig { Overlays = [overlay] }, item => [readings[item.WarnBy?.Metric ?? item.Sensor ?? item.Metric!].Value]);
                chart.Items.Add(overlay, readings);
                time.Advance(TimeSpan.FromSeconds(1));
            }

            var shown = chart.Items.Shown(overlay.Items[1], new Reading(SensorKind.Temperature, 70))?.Value;
            return string.Join(
                ' ',
                overlay.Items.Select(item => string.Join(',', chart.Of(item.Id).OfType<double>().Distinct())))
                + $" min {shown}";
        }

        Assert.Equal(["56 70 min 70", "30 34 min 34", "56 70 min 70"], new[] { before, after, before }.Select(Drawn));
    }

    /// <summary>A bridge whose every answer is fixed, which echoes the edit it was given.</summary>
#pragma warning disable CA1852 // DispatchProxy derives from it.
    public class CannedBridge : DispatchProxy
#pragma warning restore CA1852
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IWindowBridge.Settings) => Task.FromResult(new ThermiConfig { Temperature = TemperatureUnit.Fahrenheit, Overlays = [] }),
            nameof(IWindowBridge.Edit) => Task.FromResult<string?>(args![0]!.ToString()),
            nameof(IWindowBridge.History) => Task.FromResult<IReadOnlyList<double?>>([1.5, null]),
            nameof(IWindowBridge.Preview) => Task.FromResult<OverlayPicture?>(new OverlayPicture("data:image/png;base64,", 120, 38, null)),
            nameof(IWindowBridge.Undo) => Task.FromException<Undone?>(new InvalidOperationException("Boom")),
            nameof(IWindowBridge.Chart) => throw new ArgumentException("Boom", nameof(args)),
            nameof(IWindowBridge.DeleteLook) => throw new KeyNotFoundException("Boom"),
            _ => Task.CompletedTask,
        };
    }
}
