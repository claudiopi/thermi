using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Thermi.Core.Updates;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// What this build of the service knows of updates (docs/adr/0008-updates.md): the channels' address and the public keys
/// it trusts, from the build, none in a build that never updates itself; in a release build, the publisher whose signed
/// catalog every version must carry; the version it is; and the PC's processor.
/// </summary>
internal sealed record UpdateBuild(Uri? From, byte[]? Release, byte[] Backup, string? Publisher, ReleaseVersion Version, string Processor)
{
    public bool Built => From is not null;

    public string? ReleaseKeyHash => Release is null ? null : Convert.ToHexStringLower(SHA256.HashData(Release));

    public static UpdateBuild Of(Assembly assembly)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Value is { Length: > 0 })
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value!, StringComparer.Ordinal);
        // A build of no release version, as 1.0.0-dev, names no folder an update could keep, so it never updates itself.
        var released = ReleaseVersion.Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        var version = released ?? new ReleaseVersion(0, 0, 0);
        var processor = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var from = metadata.TryGetValue("ThermiUpdatesFrom", out var address) && Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null;
        var release = Key(metadata, "ThermiUpdatesKey");
        var backup = Key(metadata, "ThermiUpdatesBackupKey");
        return released is null || from is null || release is null || backup is null
            ? new UpdateBuild(null, null, [], Publisher: null, version, processor)
            : new UpdateBuild(from, release, backup, metadata.GetValueOrDefault("ThermiUpdatesPublisher"), version, processor);
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
