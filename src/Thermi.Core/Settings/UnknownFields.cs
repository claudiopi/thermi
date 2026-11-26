using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Thermi.Core.Settings;

/// <summary>
/// The members of an object of the settings that this version does not know, as a later version wrote them, kept as they
/// came and written back with the object, so a version rolled back to takes nothing away when it writes. Only the reader
/// sets it, and <see cref="Kept(StyleConfig, StyleConfig?)"/> on a copy: an init-only member would be one of the reader's
/// constructor parameters, which extension data cannot be.
/// </summary>
public sealed class UnknownFields : Dictionary<string, JsonElement>, IEquatable<UnknownFields>
{
    public UnknownFields()
        : base(StringComparer.Ordinal)
    {
    }

    // By value, so settings read twice from one file are the same settings.
    public bool Equals(UnknownFields? other) =>
        other is not null && Count == other.Count && this.All(pair => other.TryGetValue(pair.Key, out var value) && JsonElement.DeepEquals(pair.Value, value));

    public override bool Equals(object? obj) => Equals(obj as UnknownFields);

    public override int GetHashCode() => Count;

    /// <summary>
    /// A style the page, an agent or a look file sent, holding at each level only what <paramref name="had"/>, the settings
    /// file's, holds there of a later version: only the file carries it, so a field no thermi wrote never reaches the file.
    /// </summary>
    internal static StyleConfig Kept(StyleConfig sent, StyleConfig? had)
    {
        var kept = sent with
        {
            Label = Kept(sent.Label, had?.Label),
            Value = Kept(sent.Value, had?.Value),
            Opacity = Kept(sent.Opacity, had?.Opacity),
            Colors = Kept(sent.Colors, had?.Colors),
        };
        kept.Unknown = had?.Unknown;
        return kept;
    }

    /// <summary>A snap sent, as <see cref="Kept(StyleConfig, StyleConfig?)"/> keeps a style.</summary>
    internal static SnapConfig Kept(SnapConfig sent, SnapConfig? had)
    {
        var kept = sent with { };
        kept.Unknown = had?.Unknown;
        return kept;
    }

    /// <summary>A reading to warn by sent, as <see cref="Kept(StyleConfig, StyleConfig?)"/> keeps a style.</summary>
    [return: NotNullIfNotNull(nameof(sent))]
    internal static WarnByConfig? Kept(WarnByConfig? sent, WarnByConfig? had)
    {
        if (sent is null)
        {
            return null;
        }

        var kept = sent with { };
        kept.Unknown = had?.Unknown;
        return kept;
    }

    [return: NotNullIfNotNull(nameof(sent))]
    private static TextStyle? Kept(TextStyle? sent, TextStyle? had)
    {
        if (sent is null)
        {
            return null;
        }

        var kept = sent with { };
        kept.Unknown = had?.Unknown;
        return kept;
    }

    [return: NotNullIfNotNull(nameof(sent))]
    private static OpacityConfig? Kept(OpacityConfig? sent, OpacityConfig? had)
    {
        if (sent is null)
        {
            return null;
        }

        var kept = sent with { };
        kept.Unknown = had?.Unknown;
        return kept;
    }

    [return: NotNullIfNotNull(nameof(sent))]
    private static StyleColors? Kept(StyleColors? sent, StyleColors? had)
    {
        if (sent is null)
        {
            return null;
        }

        var kept = sent with { };
        kept.Unknown = had?.Unknown;
        return kept;
    }
}
