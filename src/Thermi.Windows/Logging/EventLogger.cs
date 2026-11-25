using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Microsoft.Extensions.Logging;

namespace Thermi.Windows.Logging;

/// <summary>
/// The elevated mode's log: the Application event log, under the sensor service's source, which thermi's install
/// registers and its uninstall removes. A file in the user's folder would be written as administrator where the user can
/// put a link, which sends the writes, and a roll's deletes, anywhere.
/// </summary>
internal sealed class EventLogger(string source) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var type = logLevel == LogLevel.Information ? EventLogEntryType.Information
            : logLevel == LogLevel.Warning ? EventLogEntryType.Warning
            : EventLogEntryType.Error;
        var message = exception is null ? formatter(state, exception) : $"{formatter(state, exception)}{Environment.NewLine}{exception}";
        try
        {
            EventLog.WriteEntry(source, message, type);
        }
        // A source not registered, as in a thermi never installed, or a full log: the fix matters more than its log.
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or SecurityException)
        {
            return;
        }
    }
}
