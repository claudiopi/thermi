using System.ComponentModel;
using System.Diagnostics;

namespace Thermi.Commands;

/// <summary><c>thermi elevated &lt;verb&gt;</c>: one fix that needs administrator rights, behind one UAC prompt (docs/adr/0010-driver-and-service-from-the-window.md).</summary>
internal sealed record ElevatedRequest(string Verb) : IThermiRequest;

/// <summary>What the elevated mode reaches, which tests replace: the sensor service, and the driver's install.</summary>
internal sealed record ElevatedParts(IServiceManager Services, Func<IServiceManager, DriverParts> Driver)
{
    public static ElevatedParts Windows { get; } = new(new WindowsServices(StartSensors.Service), DriverParts.Windows);
}

/// <summary>
/// The elevated mode: each verb it knows is one fix, and its exit code the result. It takes its verb and nothing else, so
/// whoever starts it cannot steer it, and any other verb runs nothing.
/// </summary>
internal static class Elevated
{
    public const string Command = "elevated";

    /// <summary>The exit code of a verb the mode does not know, as of arguments that make no sense.</summary>
    public const int Unknown = 2;

    public static readonly IReadOnlyList<string> Verbs = [StartSensors.Verb, InstallDriver.Verb];

    private const int Cancelled = 1223;

    public static Task<int> RunAsync(string verb, ElevatedParts parts, TimeProvider time) => verb switch
    {
        StartSensors.Verb => StartSensors.RunAsync(parts.Services, time),
        InstallDriver.Verb => InstallDriver.RunAsync(parts.Driver(parts.Services), time),
        _ => Task.FromResult(Unknown),
    };

    /// <summary>
    /// Runs the mode's <paramref name="verb"/> from <paramref name="exePath"/> behind one UAC prompt and waits for its exit
    /// code; null when the prompt was refused, which starts nothing. Starting it blocks until the prompt closes, so the
    /// caller runs it off its UI thread.
    /// </summary>
    public static async Task<int?> AskAsync(string exePath, string verb)
    {
        if (!Verbs.Contains(verb, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(verb), verb, "The elevated mode does not know this verb.");
        }

        var start = new ProcessStartInfo(exePath) { UseShellExecute = true, Verb = "runas", ArgumentList = { Command, verb } };
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
