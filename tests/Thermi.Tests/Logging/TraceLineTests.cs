using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;
using Thermi.App.Logging;

namespace Thermi.Tests.Logging;

/// <summary>What <c>--trace</c> writes on the standard error, where the log echoes its warnings too.</summary>
public sealed partial class TraceLineTests : IDisposable
{
    private const int Logged = 2000;

    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "logs", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // A test waits for a trace line read whole: one that lands inside a log line written from another thread is missed,
    // and the test runs out of time. The standard error is synchronized, as Console's is. The trace writes from before the
    // log's first line to after its last, so the two meet however fast each is.
    [Fact]
    public async Task ATraceLineStaysWholeAmongTheLogsLinesFromAnotherThread()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero));
        using var written = new StringWriter();
        using var errors = TextWriter.Synchronized(written);
        var trace = new TraceEvents(clock, errors);
        int traced;
        using (var logs = ThermiLog.Create(Path.Combine(_folder, "thermi.log"), clock, trace: true, errors))
        {
            var log = logs.CreateLogger("Thermi.Tests");
            using var tracing = new ManualResetEventSlim();
            var logging = Task.Run(
                () =>
                {
                    tracing.Wait(TestContext.Current.CancellationToken);
                    for (var line = 0; line < Logged; line++)
                    {
                        log.ReadingsPaused(5);
                    }
                },
                TestContext.Current.CancellationToken);
            var tracer = Task.Run(() => TraceWhile(trace, tracing, logging), TestContext.Current.CancellationToken);
            await logging;
            traced = await tracer;
        }

        var said = written.ToString().Split(Environment.NewLine);
        Assert.Equal(traced, said.Count(line => Paint().IsMatch(line)));
        Assert.Equal(Logged, said.Count(line => line.EndsWith("No readings came for 5 s, then they came again", StringComparison.Ordinal)));
    }

    private static int TraceWhile(TraceEvents trace, ManualResetEventSlim tracing, Task logging)
    {
        var lines = 0;
        do
        {
            trace.Write(TraceEvents.Paint);
            lines++;
            tracing.Set();
        }
        while (!logging.IsCompleted);
        return lines;
    }

    [GeneratedRegex(@"^\d+\.\d{3} paint$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Paint();
}
