using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Thermi.Core.Look;
using Thermi.Core.Search;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Core.Bridge;

/// <summary>A call the page made that C# could not answer, which its promise rejects with.</summary>
public sealed class BridgeCallException(string message) : Exception(message);

/// <summary>
/// The bridge's wire, as JSON messages: the page sends <c>{"id", "method", "args"}</c>, and C# answers
/// <c>{"id", "result"}</c> or <c>{"id", "error"}</c>, and raises <c>{"event", "value"}</c>. A method or event is its C#
/// name in camel case, as the page's generated types name it.
/// </summary>
public static class BridgeCalls
{
    private static readonly Dictionary<string, MethodInfo> ByName = typeof(IWindowBridge).GetMethods()
        .Where(method => !method.IsSpecialName)
        .ToDictionary(method => NameOf(method.Name), StringComparer.Ordinal);

    public static IReadOnlyList<string> Methods { get; } = [.. ByName.Keys.Order(StringComparer.Ordinal)];

    public static IReadOnlyList<string> Events { get; } =
        [.. typeof(IWindowBridge).GetEvents().Select(each => NameOf(each.Name)).Order(StringComparer.Ordinal)];

    /// <summary>
    /// The page's side of the wire, run before the page's own code: <c>window.thermi</c> as the page's generated types
    /// describe it, a promise per call answered by its id, with the result exactly as C# sent it, null included, and
    /// listeners per event, each given back a function that takes it away; and every error the page leaves unhandled,
    /// reported. It speaks whichever web view hosts the page: WebView2's messages on Windows, or WebKit's script message
    /// handler <c>thermi</c>, whose host answers with <see cref="Dispatched"/>.
    /// </summary>
    public static string Client { get; } = $$"""
        (() => {
          const webview = window.chrome?.webview;
          const post = webview === undefined
            ? (message) => window.webkit.messageHandlers.{{WebKitHandler}}.postMessage(message)
            : (message) => webview.postMessage(message);
          const hear = (listener) => webview === undefined
            ? window.addEventListener('{{WebKitHandler}}', listener)
            : webview.addEventListener('message', listener);
          const waiting = new Map();
          const listeners = new Map({{Quoted(Events)}}.map((name) => [name, []]));
          // Each load numbers its calls apart: C# may still owe a page loaded before this one an answer, which answers nothing
          // here. Not a UUID, which only a secure context makes.
          const load = Array.from(crypto.getRandomValues(new Uint32Array(4)), (part) => part.toString(36)).join('-');
          let next = 0;
          hear(({ data }) => {
            if (data.event !== undefined) {
              listeners.get(data.event)?.forEach((listener) => listener(data.value));
              return;
            }
            const call = waiting.get(data.id);
            waiting.delete(data.id);
            if (data.error === undefined) call?.resolve(data.result); else call?.reject(new Error(data.error));
          });
          const bridge = {};
          for (const method of {{Quoted(Methods)}}) {
            bridge[method] = (...args) => new Promise((resolve, reject) => {
              const id = `${load}:${next++}`;
              waiting.set(id, { resolve, reject });
              post({ id, method, args: args.map((arg) => arg ?? null) });
            });
          }
          for (const name of listeners.keys()) {
            bridge[name] = (listener) => {
              listeners.get(name).push(listener);
              return () => listeners.set(name, listeners.get(name).filter((each) => each !== listener));
            };
          }
          window.thermi = Object.freeze(bridge);
          // What the page throws and does not handle goes to thermi's log, which a window that went blank can show.
          const report = (error) => bridge.report(String(error?.stack ?? error)).catch(() => {});
          window.addEventListener('error', (event) => report(event.error ?? event.message));
          window.addEventListener('unhandledrejection', (event) => report(event.reason));
        })();
        """;

    /// <summary>The name of WebKit's script message handler the page posts its calls to, and of the event its answers come as.</summary>
    public const string WebKitHandler = "thermi";

    /// <summary>
    /// The script a WebKit host runs in the page to hand it an answer or an event, as WebView2 posts one: the message, which
    /// is JSON and so a script's value as it is, as the data of a <c>thermi</c> event on the window.
    /// </summary>
    public static string Dispatched(string message) =>
        $"window.dispatchEvent(new MessageEvent('{WebKitHandler}', {{ data: {message} }}));";

    /// <summary>
    /// The options every value crosses the bridge with: camel case, nulls left out of objects, choices as kebab-case words.
    /// </summary>
    public static JsonSerializerOptions Options => BridgeJson.Default.Options;

    public static string NameOf(string member) => JsonNamingPolicy.CamelCase.ConvertName(member);

    /// <summary>
    /// Runs the call a message names on <paramref name="bridge"/> and gives the answer to post back; a call that fails answers
    /// its error.
    /// </summary>
    public static async Task<string> AnswerAsync(IWindowBridge bridge, string message)
    {
        JsonNode? id = null;
        try
        {
            var call = JsonNode.Parse(message) as JsonObject ?? throw new BridgeCallException("A call is a JSON object.");
            id = call["id"]?.DeepClone();
            var method = call["method"]?.GetValue<string>() is { } name && ByName.TryGetValue(name, out var found)
                ? found
                : throw new BridgeCallException("No such method.");
            var result = await InvokeAsync(bridge, method, ArgumentsOf(method, call["args"] as JsonArray ?? []));
            return new JsonObject { ["id"] = id, ["result"] = result }.ToJsonString();
        }
        catch (Exception ex) when (ex is JsonException or BridgeCallException or InvalidOperationException or FormatException
            or ArgumentException or KeyNotFoundException)
        {
            return new JsonObject { ["id"] = id, ["error"] = ex.Message }.ToJsonString();
        }
    }

    public static string Failed(string message, string error)
    {
        JsonNode? id;
        try
        {
            id = (JsonNode.Parse(message) as JsonObject)?["id"]?.DeepClone();
        }
        catch (JsonException)
        {
            id = null;
        }

        return new JsonObject { ["id"] = id, ["error"] = error }.ToJsonString();
    }

    public static string EventOf(string name, object? value) =>
        new JsonObject
        {
            ["event"] = NameOf(name),
            ["value"] = value is null ? null : JsonSerializer.SerializeToNode(value, value.GetType(), BridgeJson.Default),
        }.ToJsonString();

    private static string Quoted(IReadOnlyList<string> names) => $"[{string.Join(", ", names.Select(name => $"'{name}'"))}]";

    private static object?[] ArgumentsOf(MethodInfo method, JsonArray args)
    {
        var parameters = method.GetParameters();
        if (args.Count != parameters.Length)
        {
            throw new BridgeCallException($"{method.Name} takes {parameters.Length} arguments.");
        }

        return [.. parameters.Select((parameter, at) => args[at]?.Deserialize(parameter.ParameterType, BridgeJson.Default))];
    }

    private static async Task<JsonNode?> InvokeAsync(IWindowBridge bridge, MethodInfo method, object?[] arguments)
    {
        Task task;
        try
        {
            task = (Task)method.Invoke(bridge, arguments)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner)
        {
            throw inner is ArgumentException ? new BridgeCallException(inner.Message) : inner;
        }

        await task;
        var type = method.ReturnType;
        return type.IsGenericType
            ? JsonSerializer.SerializeToNode(type.GetProperty("Result")!.GetValue(task), type.GetGenericArguments()[0], BridgeJson.Default)
            : null;
    }
}

/// <summary>Choices cross the bridge as the settings file writes them: kebab-case words, never numbers.</summary>
internal sealed class BridgeEnums : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        ((JsonConverterFactory)Activator.CreateInstance(
            typeof(JsonStringEnumConverter<>).MakeGenericType(typeToConvert),
            JsonNamingPolicy.KebabCaseLower,
            false)!).CreateConverter(typeToConvert, options);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(BridgeEnums)])]
[JsonSerializable(typeof(ThermiConfig))]
[JsonSerializable(typeof(ConfigProblemKind?))]
[JsonSerializable(typeof(PageEdit))]
[JsonSerializable(typeof(SensorView))]
[JsonSerializable(typeof(IReadOnlyList<double?>))]
[JsonSerializable(typeof(IReadOnlyList<int>))]
[JsonSerializable(typeof(IReadOnlyList<SnapConfig>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(IReadOnlyList<MonitorView>))]
[JsonSerializable(typeof(IReadOnlyList<Rect?>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, StyleConfig>))]
[JsonSerializable(typeof(IReadOnlyList<WornLook?>))]
[JsonSerializable(typeof(FinderView))]
[JsonSerializable(typeof(CatalogView))]
[JsonSerializable(typeof(IReadOnlyList<SensorKind>))]
[JsonSerializable(typeof(IReadOnlyList<Findable>))]
[JsonSerializable(typeof(IReadOnlyList<Found>))]
[JsonSerializable(typeof(WindowView))]
[JsonSerializable(typeof(ChartView))]
[JsonSerializable(typeof(OverlayPicture))]
[JsonSerializable(typeof(LookSamples))]
[JsonSerializable(typeof(DesktopPickEventArgs))]
[JsonSerializable(typeof(WindowChangeEventArgs))]
[JsonSerializable(typeof(WindowReadingsEventArgs))]
[JsonSerializable(typeof(DriverInstall))]
[JsonSerializable(typeof(Undone))]
[JsonSerializable(typeof(WindowStatus))]
[JsonSerializable(typeof(UndoSteps))]
[JsonSerializable(typeof(CaptionAction))]
[JsonSerializable(typeof(About))]
[JsonSerializable(typeof(Agents.AgentsView))]
[JsonSerializable(typeof(Opened))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(bool))]
internal sealed partial class BridgeJson : JsonSerializerContext;
