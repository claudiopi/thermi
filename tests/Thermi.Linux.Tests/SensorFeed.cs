namespace Thermi.Linux.Tests;

/// <summary>
/// The real <c>thermi-sensors</c> in a test's session, reading a recorded tree in place of <c>/sys</c> and <c>/proc</c> and
/// serving it as the person's own service does, on the socket in the session's runtime folder, where thermi and
/// <c>thermi-cli</c> look first. A tree holds a folder for each second it recorded, from <c>0</c>, which the service reads
/// in turn, each second.
/// </summary>
internal static class SensorFeed
{
    /// <summary>The recorded trees of the PCs the tests replay, from <c>tests/Thermi.Tests/Recordings/Trees</c>.</summary>
    public const string Desktop = "ryzen-5900x-rtx-3060-service";

    /// <summary>The same desktop as a plain user, who reads no energy counters.</summary>
    public const string PlainUser = "ryzen-5900x-rtx-3060-user";

    /// <summary>A virtual machine with no <c>hwmon</c> at all.</summary>
    public const string VirtualMachine = "vm-no-hwmon";

    public static Task<SessionProcess> ServeAsync(OwnSession session, string tree, CancellationToken cancel) =>
        ServeRootAsync(session, Built.Tree(tree), cancel);

    /// <summary>
    /// Serves the first second of a recorded tree, every second the same: what thermi shows then never changes, so a test
    /// compares a whole screen, and a picture <c>thermi preview</c> paints of <see cref="Steady.Snapshot"/>.
    /// </summary>
    public static async Task<Steady> ServeSteadyAsync(OwnSession session, string tree, CancellationToken cancel)
    {
        var root = Path.Combine(session.Home, $"steady-{tree}");
        Built.Copy(Path.Combine(Built.Tree(tree), "0"), Path.Combine(root, "0"));
        var snapshot = Path.Combine(session.Home, $"steady-{tree}.json");
        var dumped = await session.RunAsync(Built.Program("thermi-sensors"), ["--dump", snapshot, "--root", root], cancel: cancel).ConfigureAwait(false);
        return dumped.Exit == 0
            ? new Steady(await ServeRootAsync(session, root, cancel).ConfigureAwait(false), snapshot)
            : throw new InvalidOperationException($"thermi-sensors saved no snapshot: {dumped.Error}");
    }

    // The service says it listens on its first line, so a client that connects next is served.
    private static async Task<SessionProcess> ServeRootAsync(OwnSession session, string root, CancellationToken cancel)
    {
        var service = await session.StartAsync(Built.Program("thermi-sensors"), ["--console", "--root", root], cancel: cancel).ConfigureAwait(false);
        await service.ReadyAsync(cancel).ConfigureAwait(false);
        return service;
    }

    /// <summary>The service serving one second over and over, and that second's snapshot, saved as <c>--dump</c> saves it.</summary>
    internal sealed record Steady(SessionProcess Service, string Snapshot);
}
