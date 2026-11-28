using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.Linux.Window;

namespace Thermi.Linux.Updates;

/// <summary>
/// The settings window's side of an update on Linux (docs/adr/0019-linux.md): once a package changed its program's files,
/// it hands over as the overlay comes back from its own hand-over, which waited for them to settle and for the session's
/// quiet moment. It closes, and starts its successor from the same program with the same arguments, writing where it
/// wrote; the successor waits for it to end, as only one window registers on the session's bus.
/// </summary>
internal sealed partial class WindowUpdates
{
    // The id of the window the successor waits for, as the one it starts gives it.
    private const string Succeeds = "THERMI_WINDOW_SUCCEEDS";

    // A window ends within a second of closing; past this, the successor runs anyway and, if it still finds the old one
    // registered, hands it its arguments and ends, as a second start does.
    private static readonly TimeSpan WaitAtMost = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(100);

    /// <summary>Whether the window closed to hand over, and starts its successor as it ends.</summary>
    public bool HandsOver { get; private set; }

    /// <summary>
    /// Watches the window's program's files while <paramref name="app"/> runs, which it quits, once, to hand over as the
    /// overlay <paramref name="host"/> links to comes back after a package changed them. Made and let go on GTK's loop.
    /// </summary>
    public IDisposable Follow(Gtk.Application app, LinuxHost host, TimeProvider time, ILogger log)
    {
        var folder = new ProgramFolder(time, log);
        host.OverlayChanged = () =>
        {
            if (!HandsOver && host.OverlayRunning() && folder.SinceChange is not null)
            {
                HandsOver = true;
                app.Quit();
            }
        };
        return folder;
    }

    /// <summary>Starts the window's successor, from <paramref name="program"/>, with <paramref name="args"/>, sharing its output.</summary>
    public static void StartSuccessor(string program, IReadOnlyList<string> args, ILogger log)
    {
        var start = new ProcessStartInfo(program, args) { UseShellExecute = false };
        start.Environment[Succeeds] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        try
        {
            using var successor = Process.Start(start);
            HandedOver(log, program);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            NotHandedOver(log, program, ex);
        }
    }

    /// <summary>A successor waits here for the window it follows to end, if it is one.</summary>
    public static async Task WaitForPredecessorAsync(TimeProvider time)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable(Succeeds), CultureInfo.InvariantCulture, out var id))
        {
            return;
        }

        Environment.SetEnvironmentVariable(Succeeds, null);
        var since = time.GetTimestamp();
        while (Directory.Exists($"/proc/{id}") && time.GetElapsedTime(since) < WaitAtMost)
        {
            await Task.Delay(Every, time).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A package changed thermi's files: the settings window handed over to {Program}")]
    private static partial void HandedOver(ILogger logger, string program);

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings window could not start {Program} to hand over to it")]
    private static partial void NotHandedOver(ILogger logger, string program, Exception error);
}
