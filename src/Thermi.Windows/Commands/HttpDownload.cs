using Microsoft.Extensions.Logging;
using Thermi.Windows.Logging;

namespace Thermi.Windows.Commands;

/// <summary>
/// The download over HTTPS, read no further than the pinned size, since a longer file is not the pinned one. Why it
/// failed goes to the elevated mode's log, since its exit code alone cannot tell a proxy from a missing release.
/// </summary>
internal sealed class HttpDownload(ILogger log) : IDriverDownload
{
    public async Task<bool> DownloadAsync(Uri url, Stream into, long size, CancellationToken cancel)
    {
        // The caller's token bounds the download on its clock, so the client's own timeout is off.
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                log.DriverDownloadRefused((int)response.StatusCode);
                return false;
            }

            var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                return await CopyAsync(body, into, size, cancel).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException ex)
        {
            log.DriverDownloadFailed(ex.HttpRequestError.ToString(), ex);
            return false;
        }
        catch (OperationCanceledException ex)
        {
            log.DriverDownloadFailed("it took too long", ex);
            throw;
        }
    }

    private async Task<bool> CopyAsync(Stream body, Stream into, long size, CancellationToken cancel)
    {
        var buffer = new byte[81_920];
        long total = 0;
        for (int read; (read = await body.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0;)
        {
            total += read;
            if (total > size)
            {
                log.DriverDownloadTooLong(size);
                return false;
            }

            await into.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
        }

        return true;
    }
}
