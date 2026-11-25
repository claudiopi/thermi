using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Thermi.Core.Updates;
using Thermi.Logging;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Thermi.Updates;

/// <summary>
/// The overlay's side of updates (docs/adr/0008-updates.md), once a second on a timer of its own. While the service has a
/// version ready, a quiet session tells it so with code 128. Once the service runs another version, the overlay switches
/// itself at its own quiet moment once the fixed launcher starts that version: it closes as its tray's "Exit thermi" does,
/// lets go of its single-instance mutex, starts the launcher, and is done once the new overlay's host window shows;
/// past 60 of its ticks it ends that process, runs its overlay again and leaves that version alone.
/// </summary>
internal sealed class OverlayUpdates(Func<SessionFacts> facts, Action close, TimeProvider time, ILogger log)
{
    private readonly string? _updates = ThermiPaths.Updates;
    private readonly ReleaseVersion? _own = ReleaseVersion.Parse(
        typeof(OverlayUpdates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    private readonly HashSet<ReleaseVersion> _left = [];
    private readonly Lock _lock = new();
    private (string, DateTime)? _read;
    private UpdateState _known = new();
    private DateTimeOffset _told = DateTimeOffset.MinValue;

    public ReleaseVersion? Leaving { get; private set; }

    public void Tick()
    {
        if (_updates is null || _own is null || Leaving is not null || !_lock.TryEnter())
        {
            return;
        }

        try
        {
            var state = Read();
            if (state.Installed is { } installed && installed != _own && !_left.Contains(installed))
            {
                // A launcher a program held stays the old one's until the service puts it back; the version's own program would lose the tray icons.
                if (UpdateSchedule.Quiet(facts(), state.InstalledUrgent) && ThermiPaths.LauncherStarts(installed))
                {
                    log.HandingOver(_own, installed);
                    Leaving = installed;
                    close();
                }
            }
            else if (state.Installed == _own && time.GetUtcNow() - _told >= UpdateSchedule.QuietEvery
                && UpdateSchedule.SaysQuiet(state, facts(), time.GetUtcNow()))
            {
                _told = time.GetUtcNow();
                TellService();
            }
        }
        finally
        {
            _lock.Exit();
        }
    }

    /// <summary>
    /// Starts the launcher once the overlay closed, and waits for the new overlay by counting its own ticks: true when it
    /// came up, false when this process should run its overlay again.
    /// </summary>
    public async Task<bool> HandOverAsync()
    {
        var to = Leaving!;
        Leaving = null;
        lock (_lock)
        {
            _left.Add(to);
        }
        Process? successor;
        try
        {
            successor = Process.Start(new ProcessStartInfo(ThermiPaths.Launcher) { UseShellExecute = false });
        }
        catch (Win32Exception ex)
        {
            log.HandoverFailed(to, ex.Message);
            return false;
        }

        using (successor)
        {
            var verdict = Verdict.Waiting;
            var ticks = 0;
            while (verdict == Verdict.Waiting)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), time);
                verdict = SwitchHealth.Overlay(++ticks, Shows(successor), successor?.HasExited ?? true);
            }

            if (verdict == Verdict.Passed)
            {
                log.HandedOver(to);
                return true;
            }

            log.HandoverFailed(to, "no overlay of it came up in time");
            End(successor);

            return false;
        }
    }

    // The new overlay's host window, of the process just started.
    private static bool Shows(Process? successor)
    {
        if (successor is null)
        {
            return false;
        }

        for (var window = PInvoke.FindWindowEx(HWND.Null, HWND.Null, HiddenWindow.HostClass, null);
            !window.IsNull;
            window = PInvoke.FindWindowEx(HWND.Null, window, HiddenWindow.HostClass, null))
        {
            if (PInvoke.GetWindowThreadProcessId(window, out var id) != 0 && id == (uint)successor.Id)
            {
                return true;
            }
        }

        return false;
    }

    private UpdateState Read()
    {
        try
        {
            if (ThermiPaths.State() is { } newest && (newest, File.GetLastWriteTimeUtc(newest)) is var seen && seen != _read)
            {
                _known = UpdateState.Read(File.ReadAllText(newest));
                _read = seen;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A copy deleted mid-read is retried at the next tick.
        }

        return _known;
    }

    // Gone before this overlay runs again, so the two never show at once; one that ended on its own already is.
    private static void End(Process? successor)
    {
        try
        {
            if (successor is { HasExited: false })
            {
                successor.Kill();
                successor.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _ = ex;
        }
    }

    private void TellService()
    {
        try
        {
            UpdatesHere.Tell(UpdateSchedule.QuietCode);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            log.ServiceNotTold(UpdateSchedule.QuietCode, ex.Message);
        }
    }
}
