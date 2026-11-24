using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using Thermi.Core.Updates;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// What this build of the service knows of updates (docs/adr/0008-updates.md): the channels' address and the public keys
/// it trusts, from the build, none in a build that never updates itself; whether the zip must carry a signed catalog; the
/// version it is; and the PC's processor.
/// </summary>
internal sealed record UpdateBuild(Uri? From, byte[]? Release, byte[] Backup, bool Catalog, ReleaseVersion Version, string Processor)
{
    public bool Built => From is not null;

    public string? ReleaseKeyHash => Release is null ? null : Convert.ToHexStringLower(SHA256.HashData(Release));

    public static UpdateBuild Of(Assembly assembly)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Value is { Length: > 0 })
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value!, StringComparer.Ordinal);
        var version = ReleaseVersion.Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
            ?? throw new InvalidOperationException("The service's own version is not a release version.");
        var processor = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var from = metadata.TryGetValue("ThermiUpdatesFrom", out var address) && Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null;
        var release = Key(metadata, "ThermiUpdatesKey");
        var backup = Key(metadata, "ThermiUpdatesBackupKey");
        return from is null || release is null || backup is null
            ? new UpdateBuild(null, null, [], Catalog: false, version, processor)
            : new UpdateBuild(from, release, backup, metadata.TryGetValue("ThermiUpdatesCatalog", out var catalog) && catalog == "true", version, processor);
    }

    /// <summary>The PC's policy as an administrator left it: read at each check, so a change needs no restart.</summary>
    public UpdatePolicy Policy()
    {
        using var key = Registry.LocalMachine.OpenSubKey(UpdatePolicy.Key);
        return UpdatePolicy.Of(Built, key?.GetValue(UpdatePolicy.UpdatesValue), key?.GetValue(UpdatePolicy.ChannelValue));
    }

    public Uri ManifestOf(string channel) => new(From!, $"{channel}.json");

    private static byte[]? Key(Dictionary<string, string> metadata, string name)
    {
        try
        {
            return metadata.TryGetValue(name, out var key) ? Convert.FromBase64String(key) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
