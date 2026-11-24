using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Thermi.Logging;

namespace Thermi.Commands;

/// <summary>
/// <c>thermi elevated &lt;verb&gt;</c>: one fix that needs administrator rights, behind one UAC prompt
/// (docs/adr/0010-driver-and-service-from-the-window.md).
/// </summary>
internal sealed record ElevatedRequest(string Verb) : IThermiRequest;

/// <summary>What the elevated mode reaches, which tests replace: the sensor service, the driver's install, and its log.</summary>
internal sealed record ElevatedParts(IServiceManager Services, Func<IServiceManager, DriverParts> Driver)
{
    public ILogger Log { get; init; } = NullLogger.Instance;

    public static ElevatedParts Windows(ILogger log) =>
        new(new WindowsServices(StartSensors.Service), services => DriverParts.Windows(services, log)) { Log = log };
}

/// <summary>What asking for a fix reaches, which tests replace: the UAC prompt for sc.exe, and the elevated service's state.</summary>
internal sealed record ElevatedAsk(Func<string, Task<int?>> Prompt, IServiceManager Service)
{
    public static ElevatedAsk Windows { get; } = new(Elevated.PromptAsync, new WindowsServices(Elevated.Service));
}

/// <summary>
/// The elevated mode: each verb it knows is one fix, and its exit code the result. It takes its verb and nothing else, so
/// whoever starts it cannot steer it, and any other verb runs nothing. The thermi-elevated service runs it as SYSTEM.
/// </summary>
internal static class Elevated
{
    public const string Command = "elevated";

    /// <summary>The exit code of a verb the mode does not know, as of arguments that make no sense.</summary>
    public const int Unknown = 2;

    /// <summary>The service that runs the mode as SYSTEM, which only administrators may start.</summary>
    public const string Service = "thermi-elevated";

    public static readonly IReadOnlyList<string> Verbs = [StartSensors.Verb, InstallDriver.Verb];

    /// <summary>How long a fix may take, past the service's own wait for the mode.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(6);

    private const int Cancelled = 1223;

    // sc.exe exits with the error of the start it asked for.
    private const int ServiceNotInstalled = 1060;
    private const int AlreadyRunning = 1056;

    public static async Task<int> RunAsync(string verb, ElevatedParts parts, TimeProvider time)
    {
        var code = await (verb switch
        {
            StartSensors.Verb => StartSensors.RunAsync(parts.Services, time),
            InstallDriver.Verb => InstallDriver.RunAsync(parts.Driver(parts.Services), time),
            _ => Task.FromResult(Unknown),
        }).ConfigureAwait(false);
        parts.Log.ElevatedModeEnded(verb, code);
        return code;
    }

    /// <summary>
    /// Runs the mode's <paramref name="verb"/> behind one UAC prompt and waits for its exit code; null when the prompt was
    /// refused, which starts nothing. Starting it blocks until the prompt closes, so the caller runs it off its UI thread.
    /// </summary>
    public static Task<int?> AskAsync(string verb) => AskAsync(verb, ElevatedAsk.Windows, TimeProvider.System);

    /// <summary>
    /// No code of thermi's is elevated from the user's environment, which any of their programs can set to load code
    /// into .NET before Main: the prompt is for Windows' sc.exe, which starts the <see cref="Service"/> with the verb, and
    /// the answer is the exit code the service stops with. A service not installed answers
    /// <see cref="StartSensors.Missing"/>, as an install is what it lacks.
    /// </summary>
    internal static async Task<int?> AskAsync(string verb, ElevatedAsk ask, TimeProvider time)
    {
        if (!Verbs.Contains(verb, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(verb), verb, "The elevated mode does not know this verb.");
        }

        switch (await ask.Prompt(verb).ConfigureAwait(false))
        {
            case null:
                return null;
            case ServiceNotInstalled:
                return StartSensors.Missing;
            case 0 or AlreadyRunning:
                break;
            default:
                return StartSensors.Failed;
        }

        for (var start = time.GetTimestamp(); time.GetElapsedTime(start) < Wait; await Task.Delay(StartSensors.Poll, time).ConfigureAwait(false))
        {
            if (ask.Service.State() is ServiceRun.Stopped)
            {
                return ask.Service.ExitCode();
            }
        }

        return StartSensors.Failed;
    }

    /// <summary>
    /// Asks Windows, behind one UAC prompt, to start the <see cref="Service"/> with <paramref name="verb"/>: sc.exe's exit
    /// code, or null when the prompt was refused.
    /// </summary>
    internal static async Task<int?> PromptAsync(string verb)
    {
        // From Windows' own folder, as Windows reports it, never from a PATH.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            ArgumentList = { "start", Service, verb },
        };
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Cancelled)
        {
            return null;
        }
    }
}
