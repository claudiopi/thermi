using System.Globalization;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Thermi.App.Logging;

namespace Thermi.Windows.Logging;

/// <summary>
/// The logs of the overlay, the window and the agents' commands in <c>%LOCALAPPDATA%\thermi</c>: <c>thermi.log</c>,
/// <c>thermi-window.log</c> and <c>thermi-agents.log</c>, which the agents' many short processes share. The elevated
/// mode writes none there (<see cref="EventLogger"/>). Each rolls at 1 MB into numbered files, the three newest kept,
/// so a burst of errors never wipes what came before. Information and up always; with <c>--trace</c>, the debug events
/// too. Writing never throws: Serilog keeps a sink's failure to itself, and the overlay matters more than its log.
/// </summary>
internal static class ThermiLog
{
    public const long FileBytes = 1024 * 1024;

    public const int Files = 3;

    // Time from the injected clock, then its counter in seconds to the microsecond: Windows' performance counter, the
    // same in every process, so the time from an input in the window to the overlay's paint reads from their lines.
    private const string Line =
        "{Time:yyyy-MM-dd HH:mm:ss.fff zzz} {Counter:l} {ProcessId} {Level:u3} {SourceContext:l}: {Message:lj}{NewLine}{Exception}";

    public static string Folder { get; } = Path.Combine(UserFolders.Local, "thermi");

    // A file per process: two processes rolling one shared file by its size race to rename it, and lose lines or files.
    public static string OverlayPath { get; } = Path.Combine(Folder, "thermi.log");

    public static string WindowPath { get; } = Path.Combine(Folder, "thermi-window.log");

    // The agents' commands are many short processes at once: they share one file, of what went wrong and what they changed.
    public static string AgentsPath { get; } = Path.Combine(Folder, "thermi-agents.log");

    public static ILoggerFactory Create(string path, TimeProvider time, bool trace)
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(trace ? LogEventLevel.Debug : LogEventLevel.Information)
            .Enrich.With(new Stamp(time))
            .WriteTo.File(
                path,
                outputTemplate: Line,
                formatProvider: CultureInfo.InvariantCulture,
                fileSizeLimitBytes: FileBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: Files,
                // Still shared: a second thermi that only brings the open window to the front writes a line or two.
                shared: true)
            .CreateLogger();
        return new SerilogLoggerFactory(logger, dispose: true);
    }

    /// <summary>
    /// Runs a process's work and logs what escapes it, which then ends the process as it would have: otherwise a failure
    /// of the overlay or the window outside its message loop would leave nothing in the log. A failure on a thread of its
    /// own, as a timer's callback, ends the process without passing through here, so it is logged as Windows hands it
    /// over; that hook is gone before the work's own failure leaves, which is logged here, so nothing is logged twice.
    /// </summary>
    public static async Task<int> RunLoggedAsync(Microsoft.Extensions.Logging.ILogger log, Func<Task<int>> work)
    {
        void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            Unhandled(log, e);
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        try
        {
            return await work();
        }
        catch (Exception ex)
        {
            log.Ended(ex);
            throw;
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        }
    }

    /// <summary>What ends the process from another thread, logged before it does.</summary>
    internal static void Unhandled(Microsoft.Extensions.Logging.ILogger log, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            log.Ended(ex);
        }
    }

    /// <summary>
    /// The counter's seconds to the microsecond, in whole numbers: a double loses the microseconds of a counter that has
    /// run for long.
    /// </summary>
    internal static string Counter(TimeProvider time)
    {
        var (ticks, frequency) = (time.GetTimestamp(), time.TimestampFrequency);
        return string.Create(CultureInfo.InvariantCulture, $"{ticks / frequency}.{ticks % frequency * 1_000_000 / frequency:D6}");
    }

    // Serilog stamps each event from the system clock; the line takes its time from the injected one instead.
    private sealed class Stamp(TimeProvider time) : ILogEventEnricher
    {
        private readonly LogEventProperty _process = new("ProcessId", new ScalarValue(Environment.ProcessId));

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Time", time.GetLocalNow()));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Counter", Counter(time)));
            logEvent.AddPropertyIfAbsent(_process);
        }
    }
}
