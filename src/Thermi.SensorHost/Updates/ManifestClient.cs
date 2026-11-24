using System.Net.Http.Headers;
using System.Security.Cryptography;
using Thermi.Core.Updates;

namespace Thermi.SensorHost.Updates;

/// <summary>A zip downloaded and checked, held open so nothing changes it before it is unpacked; or why it was refused.</summary>
internal sealed record Download(FileStream? Zip, string? Refused);

/// <summary>
/// The service's only traffic for updates (docs/adr/0008-updates.md): one anonymous GET of the channel's manifest, with
/// no query, no cookie and <c>thermi</c> as the whole User-Agent, and the zip it names. Sizes are capped before reading,
/// and nothing read is trusted until it passed its checks.
/// </summary>
internal sealed class ManifestClient : IDisposable
{
    /// <summary>How much free space a download needs, in zips: the zip, its unpacked version, and room to spare.</summary>
    public const int FreeSpace = 3;

    // A connection that moves nothing this long is given up, as a dropped network or a server that stalls it would hold
    // the updater for good; one that moves, however slowly, is never cut.
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(1);

    private readonly HttpClient _http;
    private readonly TimeProvider _time;

    public ManifestClient(TimeProvider time)
    {
        _time = time;
#pragma warning disable CA2000 // The client owns the handler and disposes it with itself.
        var handler = new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false };
#pragma warning restore CA2000
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("thermi", null));
    }

    /// <summary>The envelope's bytes, cut one past its cap so a longer one is refused as too long; null when it could not be read.</summary>
    public async Task<byte[]?> EnvelopeAsync(Uri address, CancellationToken stop)
    {
        using var idle = new CancellationTokenSource(Idle, _time);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(idle.Token, stop);
        using var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, either.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var body = await response.Content.ReadAsStreamAsync(either.Token);
        var buffer = new byte[ManifestEnvelope.MaxBytes + 1];
        var read = 0;
        int got;
        while (read < buffer.Length && (got = await body.ReadAsync(buffer.AsMemory(read), either.Token)) > 0)
        {
            read += got;
            idle.CancelAfter(Idle);
        }

        return buffer[..read];
    }

    /// <summary>
    /// Downloads <paramref name="package"/> into a new file in <paramref name="folder"/>, reading no more than its signed
    /// size and hashing as it writes, the file opened for this process alone and deleted when it is closed.
    /// </summary>
    public async Task<Download> DownloadAsync(Package package, InstallFolder folder, CancellationToken stop)
    {
        if (new DriveInfo(folder.Top).AvailableFreeSpace < package.Size * FreeSpace)
        {
            return new Download(null, $"the disk has less than {FreeSpace} times the zip's {package.Size} bytes free");
        }

        if (!folder.AdminsOnly(folder.Update))
        {
            return new Download(null, $"{folder.Update} is not a folder only administrators change");
        }

        var path = Path.Combine(folder.Update, "download-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + ".zip");
#pragma warning disable CA2000 // Handed to the caller once checked, and disposed in the finally below otherwise.
        FileStream? zip = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
#pragma warning restore CA2000
        try
        {
            var refused = await FetchAsync(package, zip, stop);
            if (refused is not null)
            {
                return new Download(null, refused);
            }

            zip.Position = 0;
            var held = zip;
            zip = null;
            return new Download(held, null);
        }
        finally
        {
            if (zip is not null)
            {
                await zip.DisposeAsync();
            }
        }
    }

    public void Dispose() => _http.Dispose();

    private async Task<string?> FetchAsync(Package package, FileStream zip, CancellationToken stop)
    {
        using var idle = new CancellationTokenSource(Idle, _time);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(idle.Token, stop);
        using var response = await _http.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, either.Token);
        if (!response.IsSuccessStatusCode)
        {
            return $"{package.Url} answered {(int)response.StatusCode}";
        }

        await using var body = await response.Content.ReadAsStreamAsync(either.Token);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int got;
        while ((got = await body.ReadAsync(buffer, either.Token)) > 0)
        {
            idle.CancelAfter(Idle);
            total += got;
            if (total > package.Size)
            {
                return $"the zip is longer than its {package.Size} bytes";
            }

            hash.AppendData(buffer, 0, got);
            await zip.WriteAsync(buffer.AsMemory(0, got), stop);
        }

        await zip.FlushAsync(stop);
        return total != package.Size ? $"the zip has {total} bytes, not {package.Size}"
            : !string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), package.Sha256, StringComparison.Ordinal)
                ? "the zip's SHA-256 is not the manifest's"
            : null;
    }
}
