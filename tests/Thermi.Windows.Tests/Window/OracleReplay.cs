using System.Text.Json;
using System.Text.Json.Nodes;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Overlay;
using Thermi.Ui;
using Thermi.Ui.Editor;
using Thermi.Ui.Web;

namespace Thermi.Windows.Tests.Window;

/// <summary>
/// One case of the fake bridge's oracle run through the real bridge: from its start (the settings file, the saved looks,
/// the readings and the window's language) every call goes over the bridge's wire as the page sends it, and each answer
/// holds what the bridge answered, the settings it then shows and its saved looks where they changed, and its undo
/// steps, as the page reads them next. The calls come at a person's pace, by the test's clock: a call that changes again
/// what the last one changed comes within the second that makes them one step, any other two seconds after.
/// </summary>
internal sealed class OracleReplay : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Sensors = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // A person's pace: two calls of different controls are further apart than the second in which repeats are one step.
    private static readonly TimeSpan Apart = TimeSpan.FromSeconds(2);
    private static readonly string[] Values = ["value", "typed", "dx", "dy"];

    // The US layout, where AltGr types nothing, so the answers are the same on any PC.
    private static readonly HashSet<int> UsKeyboard = [];

    private readonly SettingsFolder _folder = new();
    private readonly Settings _settings;
    private readonly WindowBridge _bridge;

    private OracleReplay(JsonObject start)
    {
        var config = start["settings"].Deserialize<ThermiConfig>(BridgeCalls.Options) ?? new ThermiConfig();
        _folder.Write(start["problem"] is null ? ThermiConfigJson.Write(config) : "{ \"version\": 2, \"overlays\": [");
        var saved = new SavedStyles(ConfigStore.StylesFolder(_folder.File));
        foreach (var (name, look) in start["looks"]?.AsObject() ?? [])
        {
            saved.Save(look.Deserialize<StyleConfig>(BridgeCalls.Options) ?? new StyleConfig(), name);
        }

        _settings = _folder.Settings();
        Assert.True(_folder.Settled(_settings.SyncAsync).IsCompletedSuccessfully);
        var language = start["texts"]?.GetValue<string>() ?? "en";
        var service = start["service"]?.GetValue<string>() switch
        {
            "stopped" => ServiceState.Stopped,
            "stale" => ServiceState.Stale,
            _ => ServiceState.Running,
        };
        var feed = new TestFeed { Latest = service == ServiceState.Stopped ? null : SnapshotOf(start["sensors"]), State = service };
        var turn = new Queue<Action>();
        var editor = new WindowEditor(_settings, turn.Enqueue, _folder.Log, _folder.Clock);
        var startup = new StartWithWindows(@"Software\thermi-tests\Run", @"Software\thermi-tests\Approved", "thermi.exe");
        var environment = new WindowEnvironment(
            "thermi-tests",
            _folder.File,
            "thermi.exe",
            _folder.Log,
            _folder.Clock.GetUtcNow(),
            feed,
            startup)
        { Screens = () => [], HotkeyStatus = () => null, AltGrKeys = () => UsKeyboard };
        var texts = language == "pseudo" ? Texts.Pseudo() : Texts.Of(language);
        _bridge = new WindowBridge(
            new WindowStart(0, _settings, texts, environment, _folder.Clock) { Pseudo = language == "pseudo" },
            editor,
            new ChartHistory(_folder.Clock),
            turn.Enqueue);
        _bridge.Arrived();
    }

    /// <summary>The settings the window shows as it opens on <paramref name="start"/>, and each call's answer after it.</summary>
    public static async Task<(JsonNode? Shown, List<JsonObject> Answers)> RunAsync(JsonObject start, JsonArray calls)
    {
        await using var replay = new OracleReplay(start);
        var (shown, looks) = (await replay.ResultOf("settings"), await replay.ResultOf("looks"));
        var opened = shown;
        var answers = new List<JsonObject>();
        JsonObject? last = null;
        foreach (var call in calls.OfType<JsonObject>())
        {
            if (!Repeats(last, call))
            {
                replay._folder.Clock.Advance(Apart);
            }

            last = call;
            var reply = await replay.AnswerOf(call["method"]?.GetValue<string>() ?? "", call["args"]?.AsArray() ?? []);
            var answer = reply["error"] is { } error
                ? new JsonObject { ["error"] = error.DeepClone() }
                : new JsonObject { ["result"] = reply["result"]?.DeepClone() };
            var (now, nowLooks) = (await replay.ResultOf("settings"), await replay.ResultOf("looks"));
            if (!JsonNode.DeepEquals(now, shown))
            {
                answer["settings"] = now?.DeepClone();
            }

            if (!JsonNode.DeepEquals(nowLooks, looks))
            {
                answer["looks"] = nowLooks?.DeepClone();
            }

            answer["steps"] = await replay.ResultOf("steps");
            (shown, looks) = (now, nowLooks);
            answers.Add(answer);
        }

        return (opened, answers);
    }

    public async ValueTask DisposeAsync()
    {
        _bridge.Dispose();
        await _settings.DisposeAsync();
        _folder.Dispose();
    }

    // A call that changes again what the last one changed, as a held key or a nudge does, comes within its second.
    private static bool Repeats(JsonObject? last, JsonObject call) =>
        last is not null && JsonNode.DeepEquals(last["method"], call["method"]) && JsonNode.DeepEquals(Control(last), Control(call));

    // What a call changes, without the value it sets there.
    private static JsonNode? Control(JsonObject call)
    {
        if (call["args"]?.AsArray().FirstOrDefault()?.DeepClone() is not JsonObject edit)
        {
            return call["args"]?.DeepClone();
        }

        foreach (var value in Values)
        {
            edit.Remove(value);
        }

        return edit;
    }

    /// <summary>The fake catalog's own call for what its search finds, which <see cref="CatalogSearch"/> answers.</summary>
    public const string CatalogSearchMethod = "catalogSearch";

    /// <summary>
    /// What the catalog's search finds for a search, the types chosen, the sensors (a recording's name or the sensors
    /// themselves), the window's language and the user's names, as Core decides it for the window: each device found with
    /// what matched of its name, and its sensors in order, each with what matched of its name, its second line and the
    /// metric it feeds. The fake catalog shows its own rows in this order, with these marks.
    /// </summary>
    public static JsonNode? CatalogSearch(JsonArray args)
    {
        var texts = args[3]?.GetValue<string>() switch
        {
            "pseudo" => Texts.Pseudo(),
            { } language => Texts.Of(language),
            null => Texts.English,
        };
        var devices = SensorCatalog.Build(SnapshotOf(args[2]), args[4].Deserialize<IReadOnlyDictionary<string, string>>(BridgeCalls.Options), texts);
        var kinds = args[1].Deserialize<IReadOnlyList<SensorKind>>(BridgeCalls.Options) ?? [];
        var found = SensorCatalog.Filter(devices, args[0]?.GetValue<string>() ?? "", kinds, texts);
        return JsonSerializer.SerializeToNode(
            found.Select(device => new
            {
                device.Id,
                device.Marks,
                Sensors = device.Sensors.Select(row => new
                {
                    row.Id,
                    Marks = row.Found?.Title,
                    LineMarks = row.Found?.Detail,
                    row.Found?.Alias,
                    row.Found?.AliasMarks,
                }),
            }),
            Sensors);
    }

    private static Snapshot SnapshotOf(JsonNode? sensors) => sensors is JsonValue name
        ? TestRecordings.Read($"ryzen-9950x-rtx-5060-ti-{name.GetValue<string>()}.json")
        : new Snapshot(sensors.Deserialize<List<RawSensor>>(Sensors) ?? []);

    private async Task<JsonNode?> ResultOf(string method) => (await AnswerOf(method, []))["result"]?.DeepClone();

    private async Task<JsonObject> AnswerOf(string method, JsonArray args)
    {
        var message = new JsonObject { ["id"] = 0, ["method"] = method, ["args"] = args.DeepClone() };
        // The real clock only bounds a call that never answers, which fails the replay with its name instead of hanging.
        var answer = await BridgeCalls.AnswerAsync(_bridge, message.ToJsonString())
            .WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);
        return JsonNode.Parse(answer)?.AsObject() ?? [];
    }
}
