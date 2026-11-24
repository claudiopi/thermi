using Microsoft.Extensions.Logging;

namespace Thermi.Logging;

/// <summary>
/// Every event the overlay and the window log, declared once with its level and text, so the log says only what is
/// here. Errors come with what failed; the debug events are what <c>--trace</c> follows from one process to the other.
/// </summary>
internal static partial class LogEvents
{
    [LoggerMessage(Level = LogLevel.Error, Message = "A window message failed")]
    public static partial void MessageFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings file cannot be watched")]
    public static partial void WatchFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "WebView2's runtime is missing: the settings window cannot open")]
    public static partial void WebViewMissing(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings could not be read or written")]
    public static partial void SettingsFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reading the sensors failed")]
    public static partial void SensorsFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows did not take the tray icon of {Item}; it is tried again every {Seconds} s")]
    public static partial void TrayIconRefused(this ILogger logger, string item, int seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Start with Windows could not be read or changed")]
    public static partial void StartupFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows would not hand over what was pasted or dropped")]
    public static partial void PasteFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The overlay could not be started")]
    public static partial void OverlayStartFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The taskbar settings could not be opened")]
    public static partial void TaskbarSettingsFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Target} could not be opened")]
    public static partial void OpenFailed(this ILogger logger, string target, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A message {Message} that could not be read, or from another window, was dropped")]
    public static partial void MessageDropped(this ILogger logger, uint message);

    [LoggerMessage(Level = LogLevel.Error, Message = "The sensors could not be started")]
    public static partial void StartSensorsFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The sensors did not start: exit code {Code}, 1 when the service did not run in time or Windows refused, 3 when it is not installed, which reinstalling thermi fixes")]
    public static partial void StartSensorsEnded(this ILogger logger, int code);

    [LoggerMessage(Level = LogLevel.Error, Message = "thermi elevated {Verb} could not be started")]
    public static partial void ElevatedFailed(this ILogger logger, string verb, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "thermi elevated {Verb} ended with exit code {Code}: 1 when the sensor service did not run in time, 3 when it is not installed, which reinstalling thermi fixes, 4 when the driver's installer could not be downloaded, 5 when it was not the pinned file, 6 when it failed or did not end in time, 7 when its folder was not safe")]
    public static partial void ElevatedEnded(this ILogger logger, string verb, int code);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The process ended on an error")]
    public static partial void Ended(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The window's page asked for {Method}, which failed")]
    public static partial void PageCallFailed(this ILogger logger, string method, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The window's page failed: {Kind}")]
    public static partial void PageFailed(this ILogger logger, string kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "The window's page threw: {Error}")]
    public static partial void PageThrew(this ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The window's size and place could not be read or kept")]
    public static partial void WindowPlaceFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "paint")]
    public static partial void Painted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "open settings")]
    public static partial void OpeningSettings(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "input {Input}")]
    public static partial void Input(this ILogger logger, string input);

    [LoggerMessage(Level = LogLevel.Debug, Message = "first frame {Milliseconds} ms")]
    public static partial void FirstFrame(this ILogger logger, long milliseconds);
}
