using System.Text.Json;

namespace Thermi.Core.Settings;

/// <summary>
/// The members of an object of the settings that this version does not know, as a later version wrote them, kept as they
/// came and written back with the object, so a version rolled back to takes nothing away when it writes. Only the reader
/// sets it: an init-only member would be one of the reader's constructor parameters, which extension data cannot be.
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
}
