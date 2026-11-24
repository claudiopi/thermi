using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using Thermi.Commands;
using Thermi.Core.Updates;
using Thermi.Logging;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Thermi.Updates;

/// <summary>
/// The overlay's side of updates (docs/adr/0008-updates.md), once a second on a timer of its own. While the service has a
/// version ready, a quiet session tells it so with code 128. Once the service runs another version, the overlay switches
/// itself at its own quiet moment: it closes as its tray's "Exit thermi" does, lets go of its single-instance mutex, starts
/// the fixed launcher, and is done once the new overlay's host window shows; past 60 of its ticks it ends that process,
/// runs its overlay again and leaves that version alone.
/// </summary>
internal sealed class OverlayUpdates(Func<SessionFacts> facts, Action close, TimeProvider time, ILogger log)
{
    private readonly string? _state = ThermiPaths.State;
    private readonly ReleaseVersion? _own = ReleaseVersion.Parse(
        typeof(OverlayUpdates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    private readonly HashSet<ReleaseVersion> _left = [];
    private readonly Lock _lock = new();
    private DateTime _read;
    private UpdateState _known = new();
    private DateTimeOffset _told = DateTimeOffset.MinValue;

    public ReleaseVersion? Leaving { get; private set; }

    public void Tick()
    {
        if (_state is null || _own is null || Leaving is not null || !_lock.TryEnter())
        {
            return;
        }

        try
        {
            var state = Read();
            if (state.Installed is { } installed && installed != _own && !_left.Contains(installed))
            {
                if (UpdateSchedule.Quiet(facts(), state.InstalledUrgent))
                {
                    log.HandingOver(_own, installed);
                    Leaving = installed;
                    close();
                }
            }
            else if (state.Ready is not null && state.Installed == _own && time.GetUtcNow() - _told >= UpdateSchedule.QuietEvery
                && UpdateSchedule.Quiet(facts(), state.ReadyUrgent))
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
            if (successor is { HasExited: false })
            {
                // Gone before this overlay runs again, so the two never show at once.
                successor.Kill();
                successor.WaitForExit(TimeSpan.FromSeconds(5));
            }

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
            var written = File.GetLastWriteTimeUtc(_state!);
            if (written != _read)
            {
                _read = written;
                _known = UpdateState.Read(File.Exists(_state) ? File.ReadAllText(_state) : null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The service writes it whole by a move; a read that meets it mid-move tries again at the next tick.
        }

        return _known;
    }

    private void TellService()
    {
        try
        {
            using var service = new ServiceController(StartSensors.Service);
            service.ExecuteCommand(UpdateSchedule.QuietCode);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            log.QuietNotTold(ex.Message);
        }
    }
}
