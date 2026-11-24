using System.CommandLine;
using System.Diagnostics;
using System.ServiceProcess;
using Thermi.Core.Sensors;

namespace Thermi.SensorHost;

/// <summary>
/// The <c>thermi-sensors</c> Windows service. <c>--console</c> serves readings in a terminal, and
/// <c>--dump &lt;file&gt;</c> saves one snapshot, and <c>--dump &lt;file&gt; --seconds &lt;n&gt;</c> a series, for test recordings.
/// Arguments that make no sense say why and exit with 2, before the sensors are opened.
/// </summary>
internal static class Program
{
    private static Task<int> Main(string[] args) => RunAsync(args, SensorHostEnvironment.Default, CancellationToken.None);

    /// <summary>What <c>thermi-sensors</c> does with <paramref name="args"/>; <paramref name="stop"/> ends serving in a terminal, as Ctrl+C does.</summary>
    internal static async Task<int> RunAsync(IReadOnlyList<string> args, SensorHostEnvironment environment, CancellationToken stop)
    {
        var request = SensorCommands.Parse(args, out var errors);
        if (request is null)
        {
            foreach (var error in errors)
            {
                await environment.Error.WriteLineAsync(error);
            }

            return 2;
        }

        if (request is SensorBuiltIn builtIn)
        {
            return await builtIn.Parsed.InvokeAsync(new InvocationConfiguration { Output = environment.Out, Error = environment.Error }, stop);
        }

        if (request is SensorElevated)
        {
            return RunElevated(environment.Time);
        }

        using var source = environment.Open();
        if (request is SensorDump dump)
        {
            await DumpAsync(source, dump, environment.Time);
            return 0;
        }

        using var events = new EventLog("Application") { Source = SensorService.Name };
        using var server = new SensorPipeServer(source.Read, environment.Time, environment.PipeName)
        {
            Squatted = () => SensorService.Say(
                events,
                $"Another program holds the {environment.PipeName} pipe, so thermi's overlay and window get no readings. "
                    + "The service tries again every 5 s until that program closes it.",
                EventLogEntryType.Warning),
        };
        if (request is not SensorServe { Console: true })
        {
            using var service = new SensorService(server.RunAsync, resumed: () => _ = source.Read());
            ServiceBase.Run(service);
            return service.ExitCode;
        }

        await ServeAsync(server, environment.Out, stop);
        return 0;
    }

    // Before the sensors are opened: the fixes read none.
    private static int RunElevated(TimeProvider time)
    {
        using var elevated = new ElevatedService(ElevatedService.RunThermiAsync, time);
        ServiceBase.Run(elevated);
        return elevated.ExitCode;
    }

    private static async Task DumpAsync(ISensorSource source, SensorDump dump, TimeProvider time)
    {
        if (dump.Seconds is not { } seconds)
        {
            await File.WriteAllTextAsync(dump.File, SnapshotJson.Write(source.Read()));
            return;
        }

        await using var writer = new StreamWriter(dump.File);
        await SeriesRecorder.RecordAsync(source.Read, writer, seconds, time, CancellationToken.None);
    }

    // Ctrl+C stops serving, and only serving: a dump it ends as it ends any program.
    private static async Task ServeAsync(SensorPipeServer server, TextWriter output, CancellationToken stop)
    {
        using var serving = CancellationTokenSource.CreateLinkedTokenSource(stop);
        void Stop(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            serving.Cancel();
        }

        Console.CancelKeyPress += Stop;
        try
        {
            await output.WriteLineAsync("thermi-sensors is serving readings. Ctrl+C stops it.");
            await server.RunAsync(serving.Token);
        }
        finally
        {
            Console.CancelKeyPress -= Stop;
        }
    }
}

internal sealed class SensorService : ServiceBase
{
    public const string Name = "thermi-sensors";

    private readonly Func<CancellationToken, Task> _run;
    private readonly Action _stopOnFailure;
    private readonly Action? _resumed;
    private readonly CancellationTokenSource _stop = new();
    private Task _running = Task.CompletedTask;

    /// <param name="run">The server, until the token stops it.</param>
    /// <param name="stopOnFailure">How to stop after a failure; the service's own <see cref="ServiceBase.Stop"/> unless a test says otherwise.</param>
    /// <param name="resumed">What runs on waking from sleep; without it, the service asks Windows for no power events.</param>
    public SensorService(Func<CancellationToken, Task> run, Action? stopOnFailure = null, Action? resumed = null)
    {
        _run = run;
        _stopOnFailure = stopOnFailure ?? Stop;
        _resumed = resumed;
        ServiceName = Name;
        CanHandlePowerEvent = resumed is not null;
    }

    /// <summary>
    /// Runs the server. A failure stops the service with an error code, so the recovery actions restart it.
    /// It never throws: whatever happens, OnStop must be able to wait for it.
    /// </summary>
    public async Task RunAsync()
    {
        try
        {
            await _run(_stop.Token);
        }
#pragma warning disable CA1031 // Whatever failed, the answer is the same: stop with an error and let Windows restart the service.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ExitCode = 1;
            _ = Task.Run(_stopOnFailure, CancellationToken.None);
            Report(EventLog, ex);
        }
    }

    /// <summary>Writes <paramref name="error"/> to <paramref name="log"/>, which may refuse: what the service does next matters more.</summary>
    internal static void Report(EventLog log, Exception error) => Say(log, error.ToString(), EventLogEntryType.Error);

    /// <summary>Writes <paramref name="message"/> to <paramref name="log"/>, which may refuse: what the service does next matters more.</summary>
    internal static void Say(EventLog log, string message, EventLogEntryType type)
    {
        try
        {
            log.WriteEntry(message, type);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            // Nowhere else to report it.
        }
    }

    protected override void OnStart(string[] args) => _running = RunAsync();

    // Windows waits on this answer, and the library may take a while to read, so the read on waking runs apart.
    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        if (powerStatus == PowerBroadcastStatus.ResumeAutomatic && _resumed is { } resumed)
        {
            _ = Task.Run(resumed, CancellationToken.None);
        }

        return true;
    }

    protected override void OnStop()
    {
        _stop.Cancel();
#pragma warning disable VSTHRD002 // OnStop is synchronous: Windows expects the service to have stopped when it returns.
        _running.Wait(5000, CancellationToken.None);
#pragma warning restore VSTHRD002
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stop.Dispose();
        }

        base.Dispose(disposing);
    }
}
