namespace Thermi.Core.Bridge;

/// <summary>How every host serves the page's files to its web view: each file's type, and the policy sent with each.</summary>
public static class PageFiles
{
    /// <summary>
    /// A second line of defense behind the page's own policy in index.html, which keeps the only style it allows, the
    /// empty element Fluent fills: this one, sent with every file, forbids any other origin even if that one changed.
    /// </summary>
    public const string Policy = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; base-uri 'none'; " +
        "form-action 'none'; frame-ancestors 'none'; object-src 'none'";

    /// <summary>The page's first file, which a request for none gets.</summary>
    public const string Index = "index.html";

    /// <summary>The type a file of the page is served as, or null for one the host does not know, which it does not serve.</summary>
    public static string? TypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript",
        ".css" => "text/css",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".json" => "application/json",
        _ => null,
    };
}
