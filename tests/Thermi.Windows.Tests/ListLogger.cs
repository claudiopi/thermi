using Microsoft.Extensions.Logging;

namespace Thermi.Windows.Tests;

/// <summary>
/// A log that keeps what it is told in a list, so no test ever writes the user's <c>thermi.log</c>. It keeps
/// <paramref name="minimum"/> and above: information by default, as an overlay started without <c>--trace</c> does.
/// </summary>
internal sealed class ListLogger(LogLevel minimum = LogLevel.Information) : ILogger
{
    private readonly List<Entry> _entries = [];

    public sealed record Entry(LogLevel Level, string Message, Exception? Error);

    /// <summary>What it was told so far, oldest first.</summary>
    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        lock (_entries)
        {
            _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
        }
    }
}
