using Thermi.Core.Bridge;

namespace Thermi.Linux.Window;

/// <summary>
/// The settings window's page, served from the install's folder on a scheme of thermi's own that WebKit counts as secure,
/// under the page's policy; WebKit asks for each file from its native code.
/// </summary>
internal static class PageServer
{
    public const string Scheme = "thermi";

    // Beside thermi in the install's folder, as the build copies it.
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "web");

    /// <summary>Serves the page's files on thermi's scheme before any page loads.</summary>
    public static void Serve(WebKit.WebContext context)
    {
        context.GetSecurityManager().RegisterUriSchemeAsSecure(Scheme);
        context.RegisterUriScheme(Scheme, Served);
    }

    /// <summary>
    /// What the file of <paramref name="files"/> a request's path asks for holds, the page itself for none, with its type:
    /// null for one outside it, as a path of <c>..</c> asks, one it does not hold, or one it cannot read now, as while the
    /// package manager replaces it, which the page gets as a file it lacks: an exception would end WebKit's native code.
    /// </summary>
    internal static (byte[] Content, string Type)? Read(string files, string path)
    {
        if (FileOf(files, path) is not { } file || PageFiles.TypeOf(file) is not { } type)
        {
            return null;
        }

        try
        {
            return (File.ReadAllBytes(file), type);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FileOf(string files, string path)
    {
        var asked = path.TrimStart('/');
        var file = Path.GetFullPath(Path.Combine(files, asked.Length == 0 ? PageFiles.Index : asked));
        return file.StartsWith(files + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(file) ? file : null;
    }

    private static void Served(WebKit.URISchemeRequest request)
    {
        var read = Read(Files, request.GetPath());
        var type = read?.Type;
        var content = read?.Content ?? [];
        using var bytes = GLib.Bytes.New(content);
        using var stream = Gio.MemoryInputStream.NewFromBytes(bytes);
        using var response = WebKit.URISchemeResponse.New(stream, content.Length);
        using var headers = Soup.MessageHeaders.New(Soup.MessageHeadersType.Response);
        headers.Append("Content-Security-Policy", PageFiles.Policy);
        headers.Append("X-Content-Type-Options", "nosniff");
        headers.Append("Cache-Control", "no-store");

        // nosniff reads the type from the headers, not from the response's own, so a style sheet without it never applies.
        if (type is not null)
        {
            headers.Append("Content-Type", type);
        }

        response.SetHttpHeaders(headers);
        if (type is not null)
        {
            response.SetContentType(type);
        }
        else
        {
            response.SetStatus(404, "Not Found");
        }

        request.FinishWithResponse(response);
    }
}
