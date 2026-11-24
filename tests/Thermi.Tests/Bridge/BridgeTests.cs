using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Messages;
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
    [InlineData("""{"id":1,"method":"settings","args":[]}""", """{"id":1,"result":{"version":2,"temperature":"f","overlays":[]}}""")]
    [InlineData("""{"id":2,"method":"edit","args":[{"kind":"moveItem","item":"cpu.load","overlay":null,"index":2}]}""", """{"id":2,"result":"MoveItemEdit { Item = cpu.load, Overlay = , Index = 2 }"}""")]
    [InlineData("""{"id":3,"method":"edit","args":[{"kind":"line","item":"cpu.temperature","line":"critical","value":95}]}""", """{"id":3,"result":"LineEdit { Item = cpu.temperature, Line = Critical, Value = 95 }"}""")]
    [InlineData("""{"id":4,"method":"history","args":["cpu.load"]}""", """{"id":4,"result":[1.5,null]}""")]
    [InlineData("""{"id":5,"method":"editing","args":[null,"cpu.load",null]}""", """{"id":5,"result":null}""")]
    [InlineData("""{"id":6,"method":"undo","args":[]}""", """{"id":6,"error":"Boom"}""")]
    [InlineData("""{"id":7,"method":"format","args":[]}""", """{"id":7,"error":"*"}""")]
    [InlineData("""{"id":8,"method":"history","args":[]}""", """{"id":8,"error":"*"}""")]
    [InlineData("""{"id":9,"method":"edit","args":[{"kind":"paint"}]}""", """{"id":9,"error":"*"}""")]
    [InlineData("""{"id":10,"method":"crowded","args":[]}""", """{"id":10,"result":[1]}""")]
    [InlineData("""{"id":11,"method":"report","args":["TypeError: x"]}""", """{"id":11,"result":null}""")]
    [InlineData("""{"id":12,"method":"chart","args":["none"]}""", """{"id":12,"error":"*"}""")]
    [InlineData("""{"id":13,"method":"deleteLook","args":["none"]}""", """{"id":13,"error":"Boom"}""")]
    [InlineData("""{"method":"undo","args":[]}""", """{"id":null,"error":"Boom"}""")]
    [InlineData("""{"id":14,"args":[]}""", """{"id":14,"error":"*"}""")]
    [InlineData("""{"id":15,"method":"crowded"}""", """{"id":15,"result":[1]}""")]
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

    // The overlay's ten minutes, moved on by the seconds since it sent them, under what the window read itself; a gap in
    // either stays a gap.
    [Theory]
    [InlineData(0, 0, 599, 599.0)]
    [InlineData(10, 0, 589, 599.0)]
    [InlineData(10, 0, 599, null)]
    [InlineData(0, 0, 300, null)]
    [InlineData(700, 0, 0, null)]
    [InlineData(0, 3, 599, 1000.0)]
    [InlineData(0, 3, 597, 597.0)]
    public void TheChartJoinsTheOverlaysTenMinutesToTheWindowsOwn(int behind, int own, int at, double? expected)
    {
        var time = new FakeTimeProvider();
        var chart = new ChartHistory(time);
        var sent = Enumerable.Range(0, 600).Select(second => second == 300 ? float.NaN : second).ToArray();
        chart.Take(new History([new Thermi.Core.Messages.ItemHistory("cpu.load", time.GetTimestamp(), sent)]));
        time.Advance(TimeSpan.FromSeconds(behind));
        var read = Enumerable.Range(0, own).Select(second => second == 0 ? (double?)null : 1000).ToList();

        var values = chart.Of("cpu.load", read);

        Assert.Equal(600, values.Count);
        Assert.Equal(expected, values[at]);
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
            nameof(IWindowBridge.Crowded) => Task.FromResult<IReadOnlyList<int>>([1]),
            nameof(IWindowBridge.Undo) => Task.FromException<Undone?>(new InvalidOperationException("Boom")),
            nameof(IWindowBridge.Chart) => throw new ArgumentException("Boom", nameof(args)),
            nameof(IWindowBridge.DeleteLook) => throw new KeyNotFoundException("Boom"),
            _ => Task.CompletedTask,
        };
    }
}
