using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Thermi.Core.Editing;
using Thermi.Core.Settings;

namespace Thermi.Core.Look;

/// <summary>What reading a style file gave: the style, or why it was refused, with the reason naming the field.</summary>
public sealed record StyleRead(StyleConfig? Style, Refusal? Refused = null, string? Problem = null);

/// <summary>
/// A <c>.thermi-style</c> file, <c>{ "thermiStyle": 1, "style": { ... } }</c>: the look alone and its name, with no sensor,
/// item, placement, language or path, so it works on any PC. A saved style and an export are the same file. A broken file,
/// one from a newer thermi or one with a value out of its range is refused whole; unknown fields are ignored, and missing
/// ones take their defaults.
/// </summary>
public static class StyleFile
{
    public const int CurrentVersion = 1;

    public const string Extension = ".thermi-style";

    /// <summary>
    /// The most a style is read from, in bytes for a file and characters for text: a style is a few hundred bytes, and a
    /// megabyte leaves room for any written by hand, while a large file dropped by mistake is never read whole.
    /// </summary>
    public const int MaxLength = 1024 * 1024;

    private const string VersionField = "thermiStyle";

    // Plain JSON people paste in a chat: names in any language stay readable, and it is never put in a web page.
    private static readonly StyleJson Context =
        new(new JsonSerializerOptions(StyleJson.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>The look alone: the layout and the direction are the overlay's, so a look written with them leaves them out.</summary>
    public static string Write(StyleConfig style) => JsonSerializer.Serialize(new StyleFileShape(CurrentVersion, Bare(style)), Context.StyleFileShape);

    /// <summary>A style file's text, as read from a file or pasted, refused unparsed past <see cref="MaxLength"/>. Never throws.</summary>
    public static StyleRead Read(string text)
    {
        if (text.Length > MaxLength)
        {
            return new StyleRead(
                null,
                Refusal.StyleTooLarge,
                string.Create(CultureInfo.InvariantCulture, $"it is {text.Length} characters, more than {MaxLength}"));
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return Read(document.RootElement);
        }
        catch (JsonException ex)
        {
            return Broken(ex.Message);
        }
    }

    /// <summary>A style as read, its opacity given its default when the file leaves it out or writes null.</summary>
    internal static StyleConfig Complete(StyleConfig? read) =>
        read is null ? new StyleConfig() : read with { Opacity = read.Opacity ?? new OpacityConfig() };

    private static StyleRead Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Broken("it is not a JSON object");
        }

        if (!root.TryGetProperty(VersionField, out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var number)
            || number < 1)
        {
            return Broken($"{VersionField} is not a version thermi knows");
        }

        if (number > CurrentVersion)
        {
            return new StyleRead(
                null,
                Refusal.NewerStyle,
                string.Create(CultureInfo.InvariantCulture, $"{VersionField} is {number}, newer than {CurrentVersion}"));
        }

        var style = UnknownFields.Kept(Bare(Complete(root.Deserialize(Context.StyleFileShape)?.Style)), had: null);
        return ConfigRules.Problem(style, "style") is { } problem ? Broken(problem) : new StyleRead(style);
    }

    // The defaults are left out when written, so a file holds no layout or direction, and one written before still reads without them.
    private static StyleConfig Bare(StyleConfig style) => style.WornOn(new StyleConfig());

    private static StyleRead Broken(string reason) => new(null, Refusal.BrokenStyle, reason);
}

/// <summary>A style file as written: its version first, so a person sees at once which thermi it is for.</summary>
internal sealed record StyleFileShape(int ThermiStyle = StyleFile.CurrentVersion, StyleConfig? Style = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(StyleFileShape))]
internal sealed partial class StyleJson : JsonSerializerContext;
