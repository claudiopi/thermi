namespace Thermi.Commands;

/// <summary>
/// The CPU load of the window's fan finder and of <c>thermi cpu-load</c>, which records the fans' answer for tests: a
/// thread per logical core, at below-normal priority so the PC stays responsive, each spinning until the token is
/// cancelled. It runs in the process that asked, so closing it ends the load. Tests give the finder a load of their own
/// and never run this one.
/// </summary>
internal static class CpuLoad
{
    public static async Task<int> RunAsync(int seconds, TimeProvider time)
    {
        using var stop = new CancellationTokenSource();
        Run(stop.Token);
        await Task.Delay(TimeSpan.FromSeconds(seconds), time);
        await stop.CancelAsync();
        return 0;
    }

    public static void Run(CancellationToken stop)
    {
        for (var core = 0; core < Environment.ProcessorCount; core++)
        {
            new Thread(() => Spin(stop)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "thermi fan finder load" }.Start();
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
