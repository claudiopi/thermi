using System.Diagnostics;

namespace Thermi.Linux.Tests;

/// <summary>
/// The real <c>thermi-sensors</c> in a test's session, reading a recorded tree in place of <c>/sys</c> and <c>/proc</c> and
/// serving it as the person's own service does, on the socket in the session's runtime folder, where thermi and
/// <c>thermi-cli</c> look first.
/// </summary>
internal static class SensorFeed
{
    /// <summary>The recorded trees of the PCs the tests replay, from <c>tests/Thermi.Tests/Recordings/Trees</c>.</summary>
    public const string Desktop = "ryzen-5900x-rtx-3060-service";

    /// <summary>The same desktop as a plain user, who reads no energy counters.</summary>
    public const string PlainUser = "ryzen-5900x-rtx-3060-user";

    /// <summary>A virtual machine with no <c>hwmon</c> at all.</summary>
    public const string VirtualMachine = "vm-no-hwmon";

    /// <summary>
    /// Serves <paramref name="tree"/> and returns once the service listens, which it says on its first line, so a client
    /// that connects next is served.
    /// </summary>
    public static async Task<Process> ServeAsync(OwnSession session, string tree, CancellationToken cancel)
    {
        var service = await session.StartAsync(Built.Program("thermi-sensors"), ["--console", "--root", Built.Tree(tree)], cancel: cancel)
            .ConfigureAwait(false);
        return await service.StandardOutput.ReadLineAsync(cancel).ConfigureAwait(false) is not null
            ? service
            : throw new InvalidOperationException($"thermi-sensors ended: {await service.StandardError.ReadToEndAsync(cancel).ConfigureAwait(false)}");
    }
}
