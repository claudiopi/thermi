using System.Diagnostics;
using System.ServiceProcess;
using Thermi.Core;

namespace Thermi.SensorHost;

/// <summary>
/// The <c>thermi-sensors</c> Windows service. <c>--console</c> serves readings in a terminal, and
/// <c>--dump &lt;file&gt;</c> saves one snapshot, for test recordings.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var source = new LhmSensorSource();
        if (args is ["--dump", var path])
        {
            await File.WriteAllTextAsync(path, SnapshotJson.Write(source.Read()));
            return 0;
        }

        using var server = new SensorPipeServer(source.Read, TimeProvider.System);
        if (!args.Contains("--console", StringComparer.Ordinal))
        {
            using var service = new SensorService(server.RunAsync);
            ServiceBase.Run(service);
            return service.ExitCode;
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        Console.WriteLine("thermi-sensors is serving readings. Ctrl+C stops it.");
        await server.RunAsync(stop.Token);
        return 0;
    }
}

internal sealed class SensorService : ServiceBase
{
    public const string Name = "thermi-sensors";

    private readonly Func<CancellationToken, Task> _run;
    private readonly Action _stopOnFailure;
    private readonly CancellationTokenSource _stop = new();
    private Task _running = Task.CompletedTask;

    /// <param name="run">The server, until the token stops it.</param>
    /// <param name="stopOnFailure">How to stop after a failure; the service's own <see cref="ServiceBase.Stop"/> unless a test says otherwise.</param>
    public SensorService(Func<CancellationToken, Task> run, Action? stopOnFailure = null)
    {
        _run = run;
        _stopOnFailure = stopOnFailure ?? Stop;
        ServiceName = Name;
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
            Report(ex);
        }
    }

    protected override void OnStart(string[] args) => _running = RunAsync();

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

    // The event log may refuse (no rights to create the source, a full log): the restart matters more.
    private void Report(Exception error)
    {
        try
        {
            EventLog.WriteEntry(error.ToString(), EventLogEntryType.Error);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            // Nowhere else to report it.
        }
    }
}
