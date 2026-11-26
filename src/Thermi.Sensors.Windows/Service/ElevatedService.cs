using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace Thermi.Sensors.Windows.Service;

/// <summary>
/// The <c>thermi-elevated</c> service: one of the window's two fixes, run as SYSTEM
/// (docs/adr/0010-driver-and-service-from-the-window.md). Only an administrator starts it, through Windows' own sc.exe
/// behind one UAC prompt, with the fix's verb as its one start argument. It runs the elevated mode of the thermi.exe
/// beside it and stops with its exit code, which the window reads from the service manager. A process UAC starts gets
/// the environment of the user's registry, which any of their programs can set to load code into .NET before Main; one
/// the service manager starts gets the machine's.
/// </summary>
internal sealed class ElevatedService : ServiceBase
{
    public const string Name = "thermi-elevated";

    /// <summary>Past the mode's own waits, a download and an install of 2 minutes each and the sensors' restart.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(5);

    private const int Failed = 1;

    private readonly Func<IReadOnlyList<string>, TimeProvider, Task<int>> _run;
    private readonly TimeProvider _time;
    private readonly Action _stop;
    private readonly Action<Exception> _report;
    private Task _running = Task.CompletedTask;

    /// <param name="run">The elevated thermi, with its arguments, until it exits.</param>
    /// <param name="time">The clock its wait runs on.</param>
    /// <param name="report">Where a failure goes: the Application log, through the sink Main hands down.</param>
    /// <param name="stop">How to stop once it ended; the service's own <see cref="ServiceBase.Stop"/> unless a test says otherwise.</param>
    public ElevatedService(Func<IReadOnlyList<string>, TimeProvider, Task<int>> run, TimeProvider time, Action<Exception> report, Action? stop = null)
    {
        _run = run;
        _time = time;
        _report = report;
        _stop = stop ?? Stop;
        ServiceName = Name;
        // Stopped halfway, a fix would leave the driver's install or the sensors' restart undone.
        CanStop = false;
        // Windows would log its starts under its own name, a source nobody registers; its report goes under the sensor service's.
        AutoLog = false;
    }

    /// <summary>
    /// Runs the fix the start arguments name, as the elevated mode's own arguments, which refuses anything but one verb it
    /// knows, and then stops with its exit code. It never throws: whatever happens, the service must stop.
    /// </summary>
    public async Task RunAsync(IReadOnlyList<string> args)
    {
        try
        {
            ExitCode = await _run(["elevated", .. args], _time);
        }
#pragma warning disable CA1031 // Whatever fails, as a kill past the wait that Windows refuses, the service must stop: with CanStop false, nothing else does.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ExitCode = Failed;
            _report(ex);
        }

        _ = Task.Run(_stop, CancellationToken.None);
    }

    /// <summary>
    /// thermi.exe beside this executable with <paramref name="arguments"/>, with only the variables
    /// <see cref="Clean"/> names; ended with its children past <see cref="Wait"/>, which fails.
    /// </summary>
    public static async Task<int> RunThermiAsync(IReadOnlyList<string> arguments, TimeProvider time)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "thermi.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Clean(start.Environment);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("thermi.exe did not start.");
        using var cancel = new CancellationTokenSource(Wait, time);
        try
        {
            await process.WaitForExitAsync(cancel.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            // Left running, it would go on as SYSTEM while the window says it failed.
            process.Kill(entireProcessTree: true);
            return Failed;
        }
    }

    /// <summary>
    /// Leaves in <paramref name="environment"/> only Windows' own folders, read from Windows rather than from it, and a
    /// PATH of System32: the machine's environment is an administrator's to set, and their programs set some too, as a
    /// monitoring agent's profiler does, which would load into the elevated thermi before its Main.
    /// </summary>
    internal static void Clean(IDictionary<string, string?> environment)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        environment.Clear();
        environment["SystemRoot"] = windows;
        environment["windir"] = windows;
        environment["SystemDrive"] = Path.GetPathRoot(windows)!.TrimEnd(Path.DirectorySeparatorChar);
        environment["ComSpec"] = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        environment["PATH"] = Environment.SystemDirectory;
        environment["PATHEXT"] = ".COM;.EXE;.BAT;.CMD";
        environment["ProgramData"] = programData;
        environment["ALLUSERSPROFILE"] = programData;
        environment["ProgramFiles"] = programFiles;
        environment["ProgramW6432"] = programFiles;
        environment["ProgramFiles(x86)"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        environment["CommonProgramFiles"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
        environment["OS"] = "Windows_NT";
        environment["NUMBER_OF_PROCESSORS"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture);
        environment["PROCESSOR_ARCHITECTURE"] = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "ARM64" : "AMD64";
    }

    protected override void OnStart(string[] args) => _running = RunAsync(args);

    protected override void OnStop()
    {
#pragma warning disable VSTHRD002 // OnStop is synchronous: Windows expects the service to have stopped when it returns.
        _running.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
#pragma warning restore VSTHRD002
    }
}
