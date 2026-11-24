using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thermi.SensorHost.Install;

/// <summary>
/// What an install found before it changed anything, kept in <c>update\</c> so the MSI's rollback puts it back: each
/// service's image path, whether the version's folder was there, the update state's text and Installed apps' entry.
/// </summary>
internal sealed record AdoptUndo(string? Sensors, string? Elevated, bool VersionThere, string? State, InstalledApp? App)
{
    public string ToJson() => JsonSerializer.Serialize(this, AdoptUndoJson.Default.AdoptUndo);

    /// <summary>The record, or null for none or one that cannot be read, which leaves everything as it is.</summary>
    public static AdoptUndo? Read(string? json)
    {
        try
        {
            return json is null ? null : JsonSerializer.Deserialize(json, AdoptUndoJson.Default.AdoptUndo);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSerializable(typeof(AdoptUndo))]
internal sealed partial class AdoptUndoJson : JsonSerializerContext;
