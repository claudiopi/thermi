using Microsoft.Extensions.Logging;

namespace Thermi.App.Logging;

/// <summary>The events of what any OS's host shares, declared once with their level and text, as the hosts' own are.</summary>
internal static partial class AppLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "The settings file cannot be watched")]
    public static partial void WatchFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings could not be read or written")]
    public static partial void SettingsFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Find it ended: {Outcome}, {Seconds} s after the last reading, the service {State}")]
    public static partial void FanFinderEnded(this ILogger logger, Core.Sensors.FanFinderOutcome outcome, double seconds, Core.Tray.ServiceState state);

    [LoggerMessage(Level = LogLevel.Error, Message = "The window could not read the sensors' snapshot; it shows the last it read until one reads")]
    public static partial void SnapshotUnread(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ping {Host}: {Outcome}")]
    public static partial void PingChanged(this ILogger logger, string host, string outcome);

    [LoggerMessage(Level = LogLevel.Debug, Message = "input {Input}")]
    public static partial void Input(this ILogger logger, string input);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The process ended on an error")]
    public static partial void Ended(this ILogger logger, Exception error);
}
