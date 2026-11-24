using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.Logging;
using Thermi.Windows.Tests.Processes;

namespace Thermi.Windows.Tests;

/// <summary>The log of the overlay or the window, written to a folder of the test's own.</summary>
public sealed class ThermiLogTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "logs", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 22, 15, 30, 45, TimeSpan.Zero));

    private string File => Path.Combine(_folder, "thermi.log");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // A line's time and counter come from the injected clock, so one trace lines up the window's input with the
    // overlay's paint; the debug events are there only with --trace.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EachLineIsStampedFromTheInjectedClockAndDebugNeedsTheTrace(bool trace)
    {
        var counters = new List<string>();
        using (var logs = ThermiLog.Create(File, _clock, trace))
        {
            var log = logs.CreateLogger("Thermi.Tests");
            counters.Add(ThermiLog.Counter(_clock));
            log.Painted();
            _clock.Advance(TimeSpan.FromMilliseconds(12.5));
            counters.Add(ThermiLog.Counter(_clock));
            log.WatchFailed(new IOException("no folder"));
        }

        var lines = System.IO.File.ReadAllLines(File).Where(line => line.StartsWith("2026", StringComparison.Ordinal)).ToList();
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        string[] expected = trace
            ? [
                $"2026-11-22 15:30:45.000 +00:00 {counters[0]} {pid} DBG Thermi.Tests: paint",
                $"2026-11-22 15:30:45.012 +00:00 {counters[1]} {pid} ERR Thermi.Tests: ",
            ]
            : [$"2026-11-22 15:30:45.012 +00:00 {counters[1]} {pid} ERR Thermi.Tests: "];
        Assert.Equal(expected.Length, lines.Count);
        Assert.All(expected.Zip(lines), pair => Assert.StartsWith(pair.First, pair.Second, StringComparison.Ordinal));
        Assert.Contains("System.IO.IOException: no folder", System.IO.File.ReadAllText(File), StringComparison.Ordinal);
    }

    // A counter that has run for long keeps its microseconds, which a double would round away.
    [Fact]
    public void TheCounterKeepsItsMicroseconds()
    {
        _clock.Advance(TimeSpan.FromDays(40) + TimeSpan.FromTicks(1234_567));

        var counter = decimal.Parse(ThermiLog.Counter(_clock), CultureInfo.InvariantCulture);

        Assert.Equal(decimal.Truncate((decimal)_clock.GetTimestamp() / _clock.TimestampFrequency * 1_000_000) / 1_000_000, counter);
        Assert.EndsWith(".123456", ThermiLog.Counter(_clock), StringComparison.Ordinal);
    }

    // A burst of errors rolls the file once it reaches 1 MB, within a line of it, and keeps the three newest, so the log
    // never grows much past 3 MB.
    [Fact]
    public void TheLogRollsAtOneMegabyteAndKeepsThreeFiles()
    {
        var error = new IOException(new string('x', 1000));
        using (var logs = ThermiLog.Create(File, _clock, trace: false))
        {
            var log = logs.CreateLogger("Thermi.Tests");
            for (var line = 0; line < 4500; line++)
            {
                log.SettingsFailed(error);
            }
        }

        var files = Directory.GetFiles(_folder).Select(file => new FileInfo(file)).ToList();
        Assert.Equal(ThermiLog.Files, files.Count);
        Assert.All(files, file => Assert.InRange(file.Length, 0, ThermiLog.FileBytes + 4096));
    }

    // What escapes a process's entry is logged before it ends the process, then goes on as it would have.
    [Fact]
    public async Task AFailureThatEndsAProcessIsLoggedFirst()
    {
        var log = new ListLogger();
        var failure = new InvalidOperationException("no overlay");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => ThermiLog.RunLoggedAsync(log, () => Task.FromException<int>(failure)));

        Assert.Same(failure, thrown);
        Assert.Equal([(Microsoft.Extensions.Logging.LogLevel.Critical, failure)], log.Entries.Select(entry => (entry.Level, entry.Error)));
        Assert.Equal(3, await ThermiLog.RunLoggedAsync(log, () => Task.FromResult(3)));
        Assert.Single(log.Entries);
    }

    // A failure on a thread of its own, as a timer's callback, never reaches the entry: Windows hands it over as it ends
    // the process, and it is logged as the entry would have logged it.
    [Fact]
    public void AFailureOnAnotherThreadIsLoggedBeforeTheProcessEnds()
    {
        var log = new ListLogger();
        var failure = new InvalidOperationException("a timer's callback");

        ThermiLog.Unhandled(log, new UnhandledExceptionEventArgs(failure, isTerminating: true));
        ThermiLog.Unhandled(log, new UnhandledExceptionEventArgs("not an exception", isTerminating: true));

        Assert.Equal(
            [(Microsoft.Extensions.Logging.LogLevel.Critical, "The process ended on an error", failure)],
            log.Entries.Select(entry => (entry.Level, entry.Message, entry.Error)));
    }

    // In a process of its own, since either failure ends it: one on a timer's thread while the work runs never passes
    // through the entry and is logged as Windows hands it over; the work's own, logged by the entry, then leaves it as it
    // would leave Main, unhandled, after the hook is gone. Each is logged once.
    [Theory]
    [InlineData("timer")]
    [InlineData("entry")]
    public async Task AFailureThatEndsTheProcessIsLoggedOnce(string failure)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EndingChild.LogVariable] = File,
            [EndingChild.FailureVariable] = failure,
        };

        // On a desktop of its own, where the dialog Windows may show for a process that failed never reaches the user.
        var (code, output) = await DesktopProcess.RunAsync(Path.Combine(AppContext.BaseDirectory, "Thermi.Windows.Tests.exe"), [], Patience, variables);

        var log = System.IO.File.Exists(File) ? await System.IO.File.ReadAllTextAsync(File, TestContext.Current.CancellationToken) : "";
        Assert.NotEqual(0, code);
        Assert.True(log.Split("The process ended on an error").Length == 2, $"{log}{Environment.NewLine}{output}");
        Assert.Contains($"System.InvalidOperationException: the {failure}'s failure", log, StringComparison.Ordinal);
    }

    // A log that cannot be written, under a file where no folder can be made, never takes the overlay down.
    [Fact]
    public void ALogThatCannotBeWrittenNeverThrows()
    {
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File, "");

        var written = Record.Exception(() =>
        {
            using var logs = ThermiLog.Create(Path.Combine(File, "thermi.log"), _clock, trace: true);
            logs.CreateLogger("Thermi.Tests").Painted();
        });

        Assert.Null(written);
    }
}
