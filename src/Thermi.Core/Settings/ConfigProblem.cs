using Thermi.Core.Translations;

namespace Thermi.Core.Settings;

/// <summary>What keeps the settings file from being used, each with a title of its own in the translations.</summary>
[Translated]
public enum ConfigProblemKind
{
    /// <summary>A mistake in the file: JSON that does not parse, a value it does not take, or a version thermi never had.</summary>
    Broken,

    /// <summary>A file a newer thermi wrote, never read in part.</summary>
    Newer,

    /// <summary>A file Windows would not open, or a folder it would not list, even after the read's retry.</summary>
    Locked,

    /// <summary>A file that is missing and could not be created.</summary>
    Missing,
}

/// <summary>How a problem reads after its title.</summary>
[Translated]
public enum ConfigProblemText
{
    /// <summary>The line counted from 1, then the reason.</summary>
    AtLine,

    /// <summary>The file's version, then the newest this thermi reads.</summary>
    Newer,

    /// <summary>The title, then the detail, as one line of a menu.</summary>
    OneLine,
}

/// <summary>
/// Why the settings file cannot be used. <see cref="Reason"/> is as .NET, the file's rules or Windows give it, in English
/// like any message from Windows, naming the field when a rule is broken; <see cref="Line"/>, counted from 1, is where the
/// JSON stops making sense, when it is that; <see cref="Version"/> is a newer file's.
/// </summary>
public sealed record ConfigProblem(ConfigProblemKind Kind, string Reason = "", long? Line = null, int? Version = null)
{
    // .NET ends a JSON error with where it happened, its line counted from 0; the line is given apart, counted from 1.
    private const string Where = " LineNumber:";

    public static ConfigProblem Broken(string reason) => new(ConfigProblemKind.Broken, reason);

    /// <summary>A JSON error, its reason without .NET's own line and place, and its line counted from 1.</summary>
    public static ConfigProblem Broken(System.Text.Json.JsonException error)
    {
        var message = error.Message;
        var cut = message.IndexOf(Where, StringComparison.Ordinal);
        return new(ConfigProblemKind.Broken, (cut < 0 ? message : message[..cut]).TrimEnd(' ', '|'), error.LineNumber + 1);
    }

    public static ConfigProblem Newer(int version) => new(ConfigProblemKind.Newer, Version: version);

    public static ConfigProblem Locked(string reason) => new(ConfigProblemKind.Locked, reason);

    public static ConfigProblem Missing { get; } = new(ConfigProblemKind.Missing);

    public string Title(Texts texts) => texts.Of(Kind);

    /// <summary>Where and why: the line and the reason, the newer version, or Windows' words; empty when the title says it all.</summary>
    public string Detail(Texts texts, IFormatProvider numbers) => Kind switch
    {
        ConfigProblemKind.Broken when Line is { } line => texts.Format(numbers, ConfigProblemText.AtLine, line, Reason),
        ConfigProblemKind.Newer => texts.Format(numbers, ConfigProblemText.Newer, Version, ThermiConfig.CurrentVersion),
        ConfigProblemKind.Broken or ConfigProblemKind.Locked or ConfigProblemKind.Missing => Reason,
        _ => Reason,
    };

    /// <summary>The title and the detail on one line, as the overlay's menu shows them.</summary>
    public string OneLine(Texts texts, IFormatProvider numbers) =>
        Detail(texts, numbers) is { Length: > 0 } detail ? texts.Format(numbers, ConfigProblemText.OneLine, Title(texts), detail) : Title(texts);
}
