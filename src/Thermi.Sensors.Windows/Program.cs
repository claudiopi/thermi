using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using Thermi.Core.Pipe;
using Thermi.Core.Sensors;
using Thermi.Core.Updates;
using Thermi.Sensors.Windows.Frames;
using Thermi.Sensors.Windows.Install;
using Thermi.Sensors.Windows.Pipe;
using Thermi.Sensors.Windows.Reading;
using Thermi.Sensors.Windows.Service;
using Thermi.Sensors.Windows.Updates;
using Thermi.Sensors.Windows.User;

// Windows 10 1607 and later, as for thermi.exe (Thermi.Sensors.Windows.csproj says why it is set here).
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows10.0.14393")]

namespace Thermi.Sensors.Windows;

/// <summary>
/// The <c>thermi-sensors</c> Windows service. <c>--console</c> serves readings in a terminal, and
/// <c>--dump &lt;file&gt;</c> saves one snapshot, and <c>--dump &lt;file&gt; --seconds &lt;n&gt;</c> a series, for test recordings;
/// <c>--frames &lt;file&gt; --seconds &lt;n&gt;</c> records presents.
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

        if (request is SensorElevated or SensorSwitch or SensorInstall)
        {
            return await RunModeAsync(request, environment, stop);
        }

        if (request is SensorServe { User: true })
        {
            return await UserMode.RunAsync(environment, stop);
        }

        if (request is SensorFrames frames)
        {
            return await RecordFramesAsync(environment, frames);
        }

        var service = request is not (SensorServe { Console: true } or SensorDump);
        // A single dump is all it reads, so it waits for every device, however slow: one it did not wait for would be missing.
        using var source = Open(
            environment,
            service ? said => environment.Log(said, EventLogEntryType.Warning) : environment.Error.WriteLine,
            request is SensorDump { Seconds: null } ? Timeout.InfiniteTimeSpan : DeviceUpdates.Patience);
        if (request is SensorDump dump)
        {
            await DumpAsync(source, dump, environment.Time);
            return 0;
        }

        return await RunOrServeAsync(source, environment, service, stop);
    }

    private static async Task<int> RunOrServeAsync(
        ISensorSource source, SensorHostEnvironment environment, bool service, CancellationToken stop)
    {
        Action<string> say = service ? said => environment.Log(said, EventLogEntryType.Warning) : environment.Error.WriteLine;
        using var framed = Framed(source, environment, say);
        var read = (ISensorSource?)framed ?? source;
        using var server = new SensorPipeServer(read.ReadAsync, environment.Time, environment.PipeName)
        {
            Squatted = () => Squatted(environment),
        };
        if (service)
        {
            return RunService(server, source, environment);
        }

        await ServeAsync(server, environment.Out, stop);
        return 0;
    }

    // The updater runs beside the server and only in an installed layout; it never stops the service, whatever it meets.
    private static int RunService(SensorPipeServer server, ISensorSource source, SensorHostEnvironment environment)
    {
        var build = UpdateBuild.Of(typeof(Program).Assembly);
        var folder = InstallFolder.Of(AppContext.BaseDirectory, build.Version);
        if (build.Built && folder is null)
        {
            var where = AppContext.BaseDirectory;
            environment.Log($"thermi runs from {where}, which is no version's folder, so it does not update itself.", EventLogEntryType.Warning);
        }

        using var client = new ManifestClient(environment.Time);
        using var updater = folder is null ? null : new Updater(build, folder, client, environment.Time, environment.Log);
        using var service = new SensorService(
            stop =>
            {
                _ = updater?.RunAsync(stop);
                return server.RunAsync(stop);
            },
            error => environment.Log(error.ToString(), EventLogEntryType.Error),
            resumed: () =>
            {
                _ = source.ReadAsync(CancellationToken.None);
                updater?.Resumed();
            })
        {
            Updates = updater,
        };
        ServiceBase.Run(service);
        return service.ExitCode;
    }

    // A switch's watchdog reads the note, so a version is never failed for a pipe another program held.
    private static void Squatted(SensorHostEnvironment environment)
    {
        environment.Log(
            $"Another program holds the {environment.PipeName} pipe, so thermi's overlay and window get no readings. "
                + "The service tries again every 5 s until that program closes it.",
            EventLogEntryType.Warning);
        InstallFolder.Of(AppContext.BaseDirectory, UpdateBuild.Of(typeof(Program).Assembly).Version)?.MarkPipeHeld();
    }

    private static async Task<int> WatchAsync(ReleaseVersion to, SensorHostEnvironment environment, CancellationToken stop)
    {
        var build = UpdateBuild.Of(typeof(Program).Assembly);
        var folder = InstallFolder.Of(AppContext.BaseDirectory, build.Version);
        return folder is null
            ? 2
            : await new Switcher(folder, environment.Time, environment.Log, shown: WindowsSetup.Show)
                .WatchAsync(to, environment.PipeName, stop);
    }

    // The modes that read no sensors.
    private static async Task<int> RunModeAsync(ISensorRequest request, SensorHostEnvironment environment, CancellationToken stop) => request switch
    {
        SensorSwitch { User: true } watch => await UserMode.WatchAsync(watch.To, environment, stop),
        SensorSwitch watch => await WatchAsync(watch.To, environment, stop),
        SensorInstall install => await InstallAsync(install, environment, stop),
        _ => RunElevated(environment),
    };

    // Run from the folder the MSI or scripts/install.ps1 put this version in, beside the versions an update keeps.
    private static Task<int> InstallAsync(SensorInstall install, SensorHostEnvironment environment, CancellationToken stop)
    {
        var version = UpdateBuild.Of(typeof(Program).Assembly).Version;
        var own = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var installation = install.User
            ? UserMode.Installation(Path.GetDirectoryName(own)!, environment)
            : new Installation(new InstallFolder(Path.GetDirectoryName(own)!), new WindowsSetup(), environment.Time, environment.Error.WriteLine);
        return install.Mode switch
        {
            InstallMode.Adopt => installation.AdoptAsync(version, own, install.Product, stop),
            InstallMode.Undo => installation.UndoAsync(version, own, stop),
            InstallMode.Forget => Task.FromResult(installation.Forget(own)),
            InstallMode.Remove => installation.RemoveAsync(own, stop),
            _ => throw new ArgumentOutOfRangeException(nameof(install), install.Mode, null),
        };
    }

    private static int RunElevated(SensorHostEnvironment environment)
    {
        using var elevated = new ElevatedService(
            ElevatedService.RunThermiAsync, environment.Time, error => environment.Log(error.ToString(), EventLogEntryType.Error));
        ServiceBase.Run(elevated);
        return elevated.ExitCode;
    }

    private static FramedSource? Framed(ISensorSource sensors, SensorHostEnvironment environment, Action<string> say) =>
        environment.Trace is { } trace ? new FramedSource(sensors, trace, new ProcessTable(environment.Time), environment.Time, say) : null;

    // Before the sensors are opened: presents need none. A trace that cannot start says so and exits with 1.
    private static async Task<int> RecordFramesAsync(SensorHostEnvironment environment, SensorFrames frames)
    {
        using var trace = (environment.Recording ?? environment.Trace)?.Invoke();
        await using var writer = new StreamWriter(frames.File);
        using var processes = new ProcessTable(environment.Time);
        if (trace is not null && await FrameRecorder.RecordAsync(trace, processes, writer, frames.Seconds, environment.Time))
        {
            return 0;
        }

        await environment.Error.WriteLineAsync("Presents cannot be traced: run it as administrator, with no other thermi-sensors recording.");
        return 1;
    }

    private static async Task DumpAsync(ISensorSource source, SensorDump dump, TimeProvider time)
    {
        if (dump.Seconds is not { } seconds)
        {
            await File.WriteAllTextAsync(dump.File, SnapshotJson.Write(await source.ReadAsync(CancellationToken.None)));
            return;
        }

        await using var writer = new StreamWriter(dump.File);
        await SeriesRecorder.RecordAsync(source.ReadAsync, writer, seconds, time, CancellationToken.None);
    }

    private static ISensorSource Open(SensorHostEnvironment environment, Action<string> say, TimeSpan patience) =>
        environment.Open((device, took, failed) => say(Slow(device, took, failed)), patience);

    // Another program holding the board's bus, as a fan controller, is the usual cause; the log tells it from a driver that hangs.
    private static string Slow(string device, TimeSpan took, Exception? failed) =>
        string.Create(CultureInfo.InvariantCulture, $"Reading {device} ")
            + (failed is null
                ? string.Create(CultureInfo.InvariantCulture, $"took {took.TotalSeconds:0.0} s; the other devices were read without waiting for it. ")
                : string.Create(CultureInfo.InvariantCulture, $"failed after {took.TotalSeconds:0.0} s ({failed.GetType().Name}: {failed.Message}); ")
                    + "its last values were kept. ")
            + "Another program that reads or controls the same hardware, as a fan controller, may be holding it.";

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
    private readonly Action<Exception> _report;
    private readonly CancellationTokenSource _stop = new();
    private Task _running = Task.CompletedTask;

    /// <param name="run">The server, until the token stops it.</param>
    /// <param name="report">Where a failure goes: the Application log, through the sink Main hands down.</param>
    /// <param name="stopOnFailure">How to stop after a failure; the service's own <see cref="ServiceBase.Stop"/> unless a test says otherwise.</param>
    /// <param name="resumed">What runs on waking from sleep; without it, the service asks Windows for no power events.</param>
    public SensorService(Func<CancellationToken, Task> run, Action<Exception> report, Action? stopOnFailure = null, Action? resumed = null)
    {
        _run = run;
        _report = report;
        _stopOnFailure = stopOnFailure ?? Stop;
        _resumed = resumed;
        ServiceName = Name;
        CanHandlePowerEvent = resumed is not null;
        // Each quiet code would log a line a minute; the service says what it does itself, and Windows logs its starts and stops.
        AutoLog = false;
        // A shutdown is a stop asked for: without it, a restart soon after an update would count as a crash of the new version.
        CanShutdown = true;
    }

    public Updater? Updates { get; init; }

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
            _report(ex);
        }
    }

    // Above normal, so a PC busy on every core, which is when its readings matter most, still gets them each second.
    protected override void OnStart(string[] args)
    {
        using (var self = Process.GetCurrentProcess())
        {
            self.PriorityClass = ProcessPriorityClass.AboveNormal;
        }

        try
        {
            Update(updates => updates.Started());
        }
        catch (Exception ex)
        {
            // With AutoLog off, Windows logs no reason of its own for a start that failed.
            _report(ex);
            throw;
        }

        _running = RunAsync();
    }

    // Windows waits on this answer, so the switch or the check runs apart.
    protected override void OnCustomCommand(int command)
    {
        if (command == UpdateSchedule.QuietCode && Updates is { } updates)
        {
            _ = Task.Run(() => Update(_ => updates.Quiet()), CancellationToken.None);
        }
        else if (command == UpdateSchedule.RestartCode && Updates is { } restart)
        {
            _ = Task.Run(() => restart.RestartAsync(_stop.Token), CancellationToken.None);
        }
        else if (command == UpdateSchedule.CheckCode && Updates is { } asked)
        {
            _ = Task.Run(() => asked.AskedAsync(_stop.Token), CancellationToken.None);
        }
    }

    protected override void OnShutdown() => OnStop();

    // What the updater meets is written to the log and never stops the service.
    private void Update(Action<Updater> step)
    {
        try
        {
            if (Updates is { } updates)
            {
                step(updates);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _report(ex);
        }
    }

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
        // First: Windows gives a service only seconds at shutdown. A server failure exits with an error code, a crash on probation.
        if (ExitCode == 0)
        {
            Update(updates => updates.Stopped());
        }

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
