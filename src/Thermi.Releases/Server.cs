using System.Net;

namespace Thermi.Releases;

/// <summary>
/// Answers a channels folder over HTTP on this PC only, for a build installed with <c>scripts/install.ps1 -UpdatesFrom</c>:
/// the envelopes as the server does, with a 60-second cache, and the zips as immutable. Read only, and only what is
/// inside the folder.
/// </summary>
internal static class Server
{
    public static async Task<int> RunAsync(string root, int port, TextWriter output, CancellationToken stop)
    {
        using var listener = new HttpListener();
        var prefix = $"http://127.0.0.1:{port}/v1/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        await output.WriteLineAsync($"Serving {root} at {prefix}. Ctrl+C stops it.");
        using var registration = stop.Register(listener.Stop);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (HttpListenerException) when (stop.IsCancellationRequested)
            {
                break;
            }

            await AnswerAsync(context, full, output);
        }

        return 0;
    }

    private static async Task AnswerAsync(HttpListenerContext context, string root, TextWriter output)
    {
        using var response = context.Response;
        var path = Uri.UnescapeDataString(context.Request.Url!.AbsolutePath["/v1/".Length..]);
        var file = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        await output.WriteLineAsync($"{context.Request.HttpMethod} /v1/{path}");
        if (context.Request.HttpMethod != "GET" || !file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)
            || path.StartsWith("history/", StringComparison.Ordinal))
        {
            response.StatusCode = 404;
            return;
        }

        response.Headers["Cache-Control"] = path.StartsWith("zips/", StringComparison.Ordinal) ? "public, max-age=31536000, immutable" : "public, max-age=60";
        var bytes = await File.ReadAllBytesAsync(file);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }
}
