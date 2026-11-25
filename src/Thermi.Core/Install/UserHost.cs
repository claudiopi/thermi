namespace Thermi.Core.Install;

/// <summary>The process behind the user's pipe, as Windows names it: the pipe's owner, its image and its integrity level.</summary>
public sealed record PipeServer(string Owner, string Image, int Integrity);

/// <summary>
/// The sensors as the user, when the service is not installed (docs/adr/0016-admin-on-demand.md): which pipe the overlay
/// reads, which host behind it it trusts, and when it starts the next one.
/// </summary>
public static class UserHost
{
    public const string ServicePipe = "thermi-sensors";

    private const string System = "S-1-5-18";
    private const string Administrators = "S-1-5-32-544";

    private static readonly TimeSpan First = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(1);

    // A host that ends sooner than this after its start failed to start, rather than ran out of clients.
    private static readonly TimeSpan QuickEnd = TimeSpan.FromSeconds(10);

    public static string Pipe(bool serviceInstalled, string user) => serviceInstalled ? ServicePipe : ServicePipe + "-" + user;

    /// <summary>
    /// Whether the overlay of <paramref name="user"/>, at <paramref name="ownIntegrity"/>, reads <paramref name="server"/>:
    /// its owner is the user, SYSTEM or administrators, and it runs from the user's install, no lower than the overlay, so
    /// neither another user nor a sandboxed process of the same user that took the name first is ever read.
    /// </summary>
    public static bool Trusts(PipeServer server, string user, string installFolder, int ownIntegrity) =>
        (server.Owner is System or Administrators || string.Equals(server.Owner, user, StringComparison.OrdinalIgnoreCase))
        && server.Integrity >= ownIntegrity
        && Inside(server.Image, installFolder);

    /// <summary>
    /// How long the overlay waits before it starts a host again, after one that ran for <paramref name="ran"/> and the wait
    /// before it, <paramref name="waited"/>; null while a switch holds its lock, when it starts none.
    /// </summary>
    public static TimeSpan? NextTry(TimeSpan ran, TimeSpan? waited, bool locked) =>
        locked ? null
        : ran < QuickEnd && waited is { } last ? TimeSpan.FromTicks(Math.Min(last.Ticks * 2, Longest.Ticks))
        : First;

    private static bool Inside(string image, string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(image).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
