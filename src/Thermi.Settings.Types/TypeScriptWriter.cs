using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thermi.Settings.Types;

/// <summary>
/// The page's side of the bridge, as TypeScript, from the C# interface and every type it reaches, shaped as the bridge's
/// JSON carries them: camel-case fields, choices as their words, lists as arrays. A field is optional where C# may leave
/// it out: a null it does not write, a default it leaves out, a constructor parameter with a default, or an object the
/// settings' reader completes.
/// </summary>
internal sealed class TypeScriptWriter
{
    public const string Header = "// Generated from Thermi.Core by src/Thermi.Settings.Types, which the .NET build runs: never edit it by hand.\n\n";

    // Where the page's name is clearer than the C# one, which says where it lives.
    private static readonly Dictionary<string, string> Renamed = new(StringComparer.Ordinal)
    {
        ["IWindowBridge"] = "Bridge",
        ["ThermiConfig"] = "Settings",
        ["RawSensor"] = "Sensor",
        ["ServiceState"] = "Service",
        ["ConfigProblemKind"] = "Problem",
        ["PageEdit"] = "Edit",
        ["ChartLine"] = "Line",
        ["MonitorView"] = "Monitor",
    };

    // Fields the file may leave out that the bridge always fills: an overlay without a name is named from where it sits.
    private static readonly HashSet<string> Filled = new(StringComparer.Ordinal) { "OverlayConfig.Name" };

    private static readonly Dictionary<Type, string> Primitives = new()
    {
        [typeof(string)] = "string",
        [typeof(bool)] = "boolean",
        [typeof(int)] = "number",
        [typeof(long)] = "number",
        [typeof(float)] = "number",
        [typeof(double)] = "number",
        [typeof(JsonElement)] = "Value",
    };

    private readonly NullabilityInfoContext _nullability = new();
    private readonly SortedDictionary<string, string> _declarations = new(StringComparer.Ordinal);
    private readonly Queue<Type> _pending = new();

    public string Bridge(Type bridge)
    {
        var members = new List<string>();
        foreach (var method in bridge.GetMethods().Where(method => !method.IsSpecialName).OrderBy(method => method.MetadataToken))
        {
            var parameters = method.GetParameters().Select(parameter => $"{parameter.Name}: {Of(parameter.ParameterType, _nullability.Create(parameter))}");
            members.Add($"  {Camel(method.Name)}({string.Join(", ", parameters)}): {Of(method.ReturnType, _nullability.Create(method.ReturnParameter))};");
        }

        foreach (var handler in bridge.GetEvents().OrderBy(handler => handler.MetadataToken))
        {
            var type = handler.EventHandlerType!;
            var listener = type.IsGenericType ? $"(value: {Of(type.GetGenericArguments()[0], null)}) => void" : "() => void";
            members.Add($"  {Camel(handler.Name)}(listener: {listener}): void;");
        }

        while (_pending.TryDequeue(out var type))
        {
            _declarations[NameOf(type)] = Declaration(type);
        }

        return new StringBuilder(Header)
            .Append("export type Value = string | number | boolean | null | Value[] | { [key: string]: Value };\n\n")
            .AppendJoin("\n", _declarations.Values)
            .Append("\nexport interface ").Append(NameOf(bridge)).Append(" {\n").AppendJoin("\n", members).Append("\n}\n")
            .Append("\ndeclare global {\n  interface Window {\n    thermi?: ").Append(NameOf(bridge)).Append(";\n  }\n}\n")
            .ToString();
    }

    private static string Camel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static string NameOf(Type type)
    {
        var name = type.Name;
        return Renamed.TryGetValue(name, out var renamed) ? renamed
            : name.EndsWith("EventArgs", StringComparison.Ordinal) ? name[..^"EventArgs".Length]
            : name.EndsWith("Config", StringComparison.Ordinal) ? name[..^"Config".Length]
            : name;
    }

    private static string? Enumerated(Type type) =>
        type == typeof(string) ? null
        : type.IsArray ? "array"
        : type.IsGenericType && type.GetGenericTypeDefinition().Name.StartsWith("IReadOnlyDictionary", StringComparison.Ordinal) ? "record"
        : typeof(IEnumerable).IsAssignableFrom(type) ? "array"
        : null;

    // A type the reader gives its default object when the file leaves it out: an object, not a text, a list or a choice.
    private static bool Completed(Type type) => type.IsClass && type != typeof(string) && Enumerated(type) is null;

    // The type as the page sees it; null nullability is a type C# never leaves null.
    private string Of(Type type, NullabilityInfo? info)
    {
        var plain = Plain(Nullable.GetUnderlyingType(type) ?? type, info);
        return info?.ReadState == NullabilityState.Nullable ? $"{plain} | null" : plain;
    }

    private string Plain(Type type, NullabilityInfo? info)
    {
        var arguments = info?.GenericTypeArguments ?? [];
        NullabilityInfo? Argument(int at)
        {
            return at < arguments.Length ? arguments[at] : null;
        }

        if (Primitives.TryGetValue(type, out var primitive))
        {
            return primitive;
        }

        if (type == typeof(Task))
        {
            return "Promise<void>";
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return $"Promise<{Of(type.GetGenericArguments()[0], Argument(0))}>";
        }

        return Enumerated(type) switch
        {
            "record" => $"Record<string, {Of(type.GetGenericArguments()[1], Argument(1))}>",
            "array" => $"{Element(type.IsArray ? Of(type.GetElementType()!, info?.ElementType) : Of(type.GetGenericArguments()[0], Argument(0)))}[]",
            _ => Named(type),
        };
    }

    private static string Element(string type) => type.Contains(' ', StringComparison.Ordinal) ? $"({type})" : type;

    private string Named(Type type)
    {
        var name = NameOf(type);
        if (!_declarations.ContainsKey(name) && !_pending.Contains(type))
        {
            _declarations[name] = "";
            _pending.Enqueue(type);
        }

        return name;
    }

    private string Declaration(Type type)
    {
        if (type.IsEnum)
        {
            return $"export type {NameOf(type)} = {string.Join(" | ", type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(Word))};\n";
        }

        var derived = type.GetCustomAttributes<JsonDerivedTypeAttribute>().ToList();
        if (type.GetCustomAttribute<JsonPolymorphicAttribute>() is { } polymorphic)
        {
            var kinds = derived.Select(each => $"  | {{ {string.Join("; ", Fields(each.DerivedType).Prepend($"{polymorphic.TypeDiscriminatorPropertyName}: '{each.TypeDiscriminator}'"))} }}");
            return $"export type {NameOf(type)} =\n{string.Join("\n", kinds)};\n";
        }

        return $"export interface {NameOf(type)} {{\n{string.Concat(Fields(type).Select(field => $"  {field};\n"))}}}\n";
    }

    private static string Word(FieldInfo choice) =>
        $"'{choice.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? JsonNamingPolicy.KebabCaseLower.ConvertName(choice.Name)}'";

    private IEnumerable<string> Fields(Type type)
    {
        var parameters = type.GetConstructors().MaxBy(constructor => constructor.GetParameters().Length)?.GetParameters()
            .ToDictionary(parameter => parameter.Name!, StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0))
        {
            var ignore = property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition;
            if (ignore == JsonIgnoreCondition.Always)
            {
                continue;
            }

            var info = _nullability.Create(property);
            var nullable = info.ReadState == NullabilityState.Nullable;
            var optional = ignore != JsonIgnoreCondition.Never && !Filled.Contains($"{type.Name}.{property.Name}") && (ignore == JsonIgnoreCondition.WhenWritingDefault || nullable
                || (parameters.TryGetValue(property.Name, out var parameter) ? parameter.HasDefaultValue : Completed(property.PropertyType)));
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? Camel(property.Name);
            var shown = Plain(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType, info);
            yield return optional ? $"{name}?: {shown}" : nullable && ignore == JsonIgnoreCondition.Never ? $"{name}: {shown} | null" : $"{name}: {shown}";
        }
    }
}
