using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thermi.Core.Updates;

/// <summary>One processor's zip or MSI of a version: where it is, and the size and SHA-256 it must have.</summary>
public sealed record Package(Uri Url, long Size, string Sha256)
{
    /// <summary>
    /// Why a file of <paramref name="bytes"/> bytes, hashed as <paramref name="sha256"/> in lowercase hex, is not this
    /// package, or null when it is. A file longer than its size is refused first, as a download stops reading there.
    /// </summary>
    public string? Refusal(long bytes, string sha256) =>
        bytes > Size ? string.Create(CultureInfo.InvariantCulture, $"the file is longer than its {Size} bytes")
            : bytes != Size ? string.Create(CultureInfo.InvariantCulture, $"the file has {bytes} bytes, not {Size}")
            : !string.Equals(sha256, Sha256, StringComparison.Ordinal) ? "the file's SHA-256 is not the manifest's"
            : null;
}

/// <summary>A version's files by processor: the zips an update unpacks, and the machine MSIs a per-user install runs.</summary>
public sealed record VersionFiles(IReadOnlyDictionary<string, Package> Zips, IReadOnlyDictionary<string, Package> Msis)
{
    public static VersionFiles Of(Manifest manifest) => new(manifest.Packages, manifest.Msis);
}

/// <summary>
/// What a channel offers now, signed by thermi's release key: its version and each processor's zip, the rollout's
/// percentage, whether it is urgent or the channel is turned off, and the versions withdrawn. Each new manifest of a
/// channel takes the next sequence, so an old one replayed is refused, and expires, so one replayed unchanged stops
/// counting as a check. One signed by the backup key may revoke the release key, and then its sequence starts over.
/// </summary>
public sealed record Manifest
{
    public const int CurrentSchema = 1;

    public const string Stable = "stable";

    public const string Beta = "beta";

    public static readonly IReadOnlyList<string> Channels = [Stable, Beta];

    public static readonly IReadOnlyList<string> Processors = ["x64", "arm64"];

    /// <summary>How long a manifest counts after it is signed: the levers sign each channel again within it.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(45);

    public int Schema { get; init; } = CurrentSchema;

    public required string Channel { get; init; }

    public long Sequence { get; init; }

    public DateTimeOffset Expires { get; init; }

    /// <summary>The SHA-256 of the release key this revokes, honored only from the backup key.</summary>
    public string? RevokedKey { get; init; }

    public ReleaseVersion? Version { get; init; }

    public IReadOnlyDictionary<string, Package> Packages { get; init; } = new Dictionary<string, Package>(StringComparer.Ordinal);

    /// <summary>Each processor's machine MSI of the version, which a per-user install runs to install for everyone.</summary>
    public IReadOnlyDictionary<string, Package> Msis { get; init; } = new Dictionary<string, Package>(StringComparer.Ordinal);

    public int Rollout { get; init; } = 100;

    public bool Urgent { get; init; }

    public bool Disabled { get; init; }

    public IReadOnlyList<ReleaseVersion> Withdrawn { get; init; } = [];
}

public enum ManifestProblem
{
    TooLong,
    Malformed,
    NotSigned,
    Invalid,
}

public sealed record ManifestRead(Manifest? Manifest, ManifestProblem? Problem, bool Backup = false);

/// <summary>
/// The file a channel serves, <c>{ "manifest": base64, "signature": base64 }</c>: the manifest's exact bytes and an ECDSA
/// P-256 signature of them over SHA-256, so what is checked is what is read, whatever a cache did to the JSON around it.
/// </summary>
public static class ManifestEnvelope
{
    /// <summary>The most an envelope is read from: a manifest is a few hundred bytes.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>The largest zip a manifest may list: a version is about 35 MB.</summary>
    public const long MaxPackage = 512L * 1024 * 1024;

    /// <summary>
    /// The manifest <paramref name="envelope"/> holds, when <paramref name="release"/> or <paramref name="backup"/> (each a
    /// public key's SubjectPublicKeyInfo, the release key null once revoked) signed it and it is well formed: only the
    /// backup key may revoke. Never throws.
    /// </summary>
    public static ManifestRead Open(ReadOnlySpan<byte> envelope, byte[]? release, byte[] backup)
    {
        if (envelope.Length > MaxBytes)
        {
            return new ManifestRead(null, ManifestProblem.TooLong);
        }

        Envelope? sealedOne;
        byte[] body;
        byte[] signature;
        try
        {
            sealedOne = JsonSerializer.Deserialize(envelope, UpdateJson.Default.Envelope);
            body = Convert.FromBase64String(sealedOne?.Manifest ?? "");
            signature = Convert.FromBase64String(sealedOne?.Signature ?? "");
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return new ManifestRead(null, ManifestProblem.Malformed);
        }

        var byBackup = !(release is not null && Verifies(release, body, signature));
        if (byBackup && !Verifies(backup, body, signature))
        {
            return new ManifestRead(null, ManifestProblem.NotSigned);
        }

        Manifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(body, UpdateJson.Default.Manifest);
        }
        catch (JsonException)
        {
            return new ManifestRead(null, ManifestProblem.Invalid);
        }

        return manifest is not null && Valid(manifest) && (byBackup || manifest.RevokedKey is null)
            ? new ManifestRead(manifest, null, byBackup)
            : new ManifestRead(null, ManifestProblem.Invalid);
    }

    /// <summary>The envelope of <paramref name="manifest"/>, signed with <paramref name="key"/>: what the levers publish.</summary>
    public static byte[] Seal(Manifest manifest, ECDsa key)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(manifest, UpdateJson.Default.Manifest);
        var signature = key.SignData(body, HashAlgorithmName.SHA256);
        return JsonSerializer.SerializeToUtf8Bytes(new Envelope(Convert.ToBase64String(body), Convert.ToBase64String(signature)), UpdateJson.Default.Envelope);
    }

    internal static bool Verifies(byte[] key, byte[] body, byte[] signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(key, out _);
            return ecdsa.KeySize == 256 && ecdsa.VerifyData(body, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool Valid(Manifest manifest) =>
        manifest.Schema == Manifest.CurrentSchema
        && Manifest.Channels.Contains(manifest.Channel, StringComparer.Ordinal)
        && manifest.Sequence > 0
        && manifest.Rollout is >= 0 and <= 100
        && Valid(manifest.Packages)
        && Valid(manifest.Msis);

    private static bool Valid(IReadOnlyDictionary<string, Package> packages) =>
        packages.All(package => Manifest.Processors.Contains(package.Key, StringComparer.Ordinal) && Valid(package.Value));

    // Over HTTPS, or HTTP to this PC alone, for a test channel: trust rests on the signature and the hash, never the transport.
    private static bool Valid(Package package) =>
        package.Url.IsAbsoluteUri
        && (package.Url.Scheme == Uri.UriSchemeHttps || (package.Url.Scheme == Uri.UriSchemeHttp && package.Url.IsLoopback))
        && package.Size is > 0 and <= MaxPackage
        && package.Sha256.Length == 64
        && package.Sha256.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
}

public sealed record Envelope(string Manifest, string Signature);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    WriteIndented = false)]
[JsonSerializable(typeof(Envelope))]
[JsonSerializable(typeof(Manifest))]
[JsonSerializable(typeof(UpdateState))]
[JsonSerializable(typeof(ChannelHistory))]
internal sealed partial class UpdateJson : JsonSerializerContext;
