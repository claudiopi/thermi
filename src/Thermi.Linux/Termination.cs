using System.Runtime.InteropServices;

namespace Thermi.Linux;

/// <summary>
/// SIGTERM, which systemd sends at sign-out and shutdown and <c>kill</c> sends by default, ends a process of thermi's as its
/// own quit does: on GLib's main loop, so what it owes is written, and with 0, which systemd and the overlay count as no
/// failure. .NET's own end on it is 143, which both count as one, so the overlay told of a window that closed with an error.
/// </summary>
internal static class Termination
{
    /// <summary>Runs <paramref name="quit"/> on the main loop at SIGTERM, in place of .NET's own end, until disposed.</summary>
    public static PosixSignalRegistration QuitsOn(Action quit) => PosixSignalRegistration.Create(PosixSignal.SIGTERM, signal =>
    {
        signal.Cancel = true;
        _ = GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, () =>
        {
            quit();
            return false;
        });
    });
}
