using System.Diagnostics;
using System.Globalization;

namespace Thermi.App.Finder;

/// <summary>
/// The CPU load of the window's fan finder and of <c>thermi cpu-load</c>, which records the fans' answer for tests: a
/// thread per logical core, each spinning until the token is cancelled, in a process of idle priority, so the sensor
/// service, the overlay, the window and whatever the person is doing, all above it, keep running under it, while the
/// cores still stay busy enough for the fans to answer. The window runs it
/// as a <c>thermi cpu-load</c> of its own, never in its own process: spinning threads there would share its runtime's
/// pauses and stall the readings the finder waits on. Tests give the finder a load of their own and never run this one.
/// </summary>
internal static class CpuLoad
{
    public static async Task<int> RunAsync(int seconds, TimeProvider time)
    {
        using (var self = Process.GetCurrentProcess())
        {
            self.PriorityClass = ProcessPriorityClass.Idle;
        }

        using var stop = new CancellationTokenSource();
        Run(stop.Token);
        await Task.Delay(TimeSpan.FromSeconds(seconds), time);
        await stop.CancelAsync();
        return 0;
    }

    /// <summary><c>thermi cpu-load</c>'s arguments, for <paramref name="seconds"/> at most.</summary>
    public static IEnumerable<string> Arguments(int seconds) => ["cpu-load", seconds.ToString(CultureInfo.InvariantCulture)];

    /// <summary>
    /// The window's load: <paramref name="program"/> run with <paramref name="arguments"/>, a <c>thermi cpu-load</c> of a few
    /// seconds at most (<see cref="Arguments"/>), so a window that dies leaves no load behind for long, ended at once when
    /// <paramref name="stop"/> is cancelled.
    /// </summary>
    public static void Start(string program, IEnumerable<string> arguments, CancellationToken stop)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var process = Process.Start(start);
        if (process is null)
        {
            return;
        }

        stop.Register(() =>
        {
            using (process)
            {
                End(process);
            }
        });
    }

    private static void Run(CancellationToken stop)
    {
        for (var core = 0; core < Environment.ProcessorCount; core++)
        {
            new Thread(() => Spin(stop)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "thermi fan finder load" }.Start();
        }
    }

    private static void End(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // It ended on its own.
        }
    }

    // Arithmetic on a register keeps a core busy without touching memory; the token is checked on every turn, so a
    // cancel stops every core at once.
    private static void Spin(CancellationToken stop)
    {
        var state = 1UL;
        while (!stop.IsCancellationRequested)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
        }

        GC.KeepAlive(state);
    }
}
