namespace Thermi.Core.Install;

/// <summary>
/// The process behind the user's pipe, as Windows names it: the pipe's owner, its image and its integrity level, and the
/// pipe's own integrity label, medium when it has none.
/// </summary>
public sealed record PipeServer(string Owner, string Image, int Integrity, int Label);

/// <summary>
/// The sensors as the user, when the service is not installed (docs/adr/0016-admin-on-demand.md): which pipe the overlay
/// reads, which host behind it it trusts, and when it starts the next one.
/// </summary>
public static class UserHost
{
    public const string ServicePipe = Sensors.SnapshotJson.PipeName;

    private const string System = "S-1-5-18";
    private const string Administrators = "S-1-5-32-544";

    /// <summary>Windows' medium integrity level, a plain process's, and a pipe's with no label.</summary>
    public const int Medium = 0x2000;

    private static readonly TimeSpan First = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(1);

    // A host that ends sooner than this after its start failed to start, rather than ran out of clients.
    private static readonly TimeSpan QuickEnd = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether the install at <paramref name="top"/> is a per-user one, under the user's <paramref name="localAppData"/>:
    /// only such a host updates itself and offers to install for everyone, and only its window offers that.
    /// </summary>
    public static bool PerUserInstall(string top, string localAppData) =>
        InstallLayout.Inside(Path.Combine(top, "thermi.exe"), localAppData);

    /// <summary>
    /// Whether the window's fix installs thermi for everyone: in a per-user install while it reads <paramref name="pipeRead"/>,
    /// the user's own host's; once the service's is read, the fix is the machine's again.
    /// </summary>
    public static bool InstallsForEveryone(string top, string localAppData, string pipeRead) =>
        PerUserInstall(top, localAppData) && pipeRead != ServicePipe;

    public static string Pipe(bool serviceInstalled, string user) => serviceInstalled ? ServicePipe : PipeOf(ServicePipe, user);

    /// <summary>The user's own host's pipe, named after <paramref name="pipeName"/>, the service's but in tests.</summary>
    public static string PipeOf(string pipeName, string user) => pipeName + "-" + user;

    /// <summary>The name of the host's event <paramref name="what"/>, a code or its stop, in the user's session.</summary>
    public static string EventOf(string pipe, string what) => @"Local\" + pipe + "-" + what;

    /// <summary>
    /// Whether the overlay of <paramref name="user"/>, at <paramref name="ownIntegrity"/>, reads <paramref name="server"/>:
    /// its owner is the user, SYSTEM or administrators, and it runs from the user's install, no lower than the overlay, so
    /// neither another user nor a sandboxed process of the same user that took the name first is ever read. The pipe's own
    /// label is no lower either: a sandboxed process that made it and left cannot pass on a host that later got its id. An
    /// integrity that could not be read trusts nothing.
    /// </summary>
    public static bool Trusts(PipeServer server, string user, string installFolder, int ownIntegrity) =>
        (server.Owner is System or Administrators || string.Equals(server.Owner, user, StringComparison.OrdinalIgnoreCase))
        && ownIntegrity > 0
        && server.Integrity >= ownIntegrity
        && server.Label >= Math.Min(ownIntegrity, Medium)
        && InstallLayout.Inside(server.Image, installFolder);

    /// <summary>
    /// How long the overlay waits before it starts a host again, after one that ran for <paramref name="ran"/> and the wait
    /// before it, <paramref name="waited"/>; null while a switch holds its lock, when it starts none.
    /// </summary>
    public static TimeSpan? NextTry(TimeSpan ran, TimeSpan? waited, bool locked) =>
        locked ? null
        : ran < QuickEnd && waited is { } last ? TimeSpan.FromTicks(Math.Min(last.Ticks * 2, Longest.Ticks))
        : First;
}
