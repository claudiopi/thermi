namespace Thermi.MacOS.Tests;

/// <summary>
/// The real <c>thermi-sensors</c> in a test's session, replaying what a Mac's IOKit answered in place of the Mac's own,
/// and serving it as <c>thermi-sensors --console</c> does, on the session's socket, the one the session names for thermi
/// and <c>thermi-cli</c> to read alone (<see cref="OwnSession.Variables"/>).
/// </summary>
internal static class SensorFeed
{
    /// <summary>A Mac mini M4 on macOS 26 at rest, read as a plain user over SSH.</summary>
    public const string MacMini = "mac-mini-m4";

    /// <summary>The same Mac with every core loaded.</summary>
    public const string MacMiniLoaded = "mac-mini-m4-load";

    /// <summary>A MacBook Air M1, another chip's family with other SMC keys, and a battery.</summary>
    public const string MadeM1 = "macbook-air-m1";

    /// <summary>A virtual machine of the CI's, which has no SMC, no HID sensors and no energy counters.</summary>
    public const string VirtualMachine = "macos-vm";

    /// <summary>The service replaying <paramref name="recording"/>, once it says it serves.</summary>
    public static async Task<SessionProcess> ServeAsync(OwnSession session, string recording, CancellationToken cancel)
    {
        var service = session.Start(Built.Program("thermi-sensors"), ["--console", "--replay", Built.Recording(recording)]);
        await service.ReadyAsync(cancel).ConfigureAwait(false);
        return service;
    }
}
