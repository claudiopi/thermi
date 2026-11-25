using Microsoft.Extensions.Logging;
using Thermi.Core.Updates;

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The sensor pipe was lost after {Seconds} s without readings: {Reason}")]
    public static partial void SensorPipeLost(this ILogger logger, double seconds, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No readings came for {Seconds} s, then they came again")]
    public static partial void ReadingsPaused(this ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Find it ended: {Outcome}, {Seconds} s after the last reading, the service {State}")]
    public static partial void FanFinderEnded(this ILogger logger, Core.Sensors.FanFinderOutcome outcome, double seconds, Core.Tray.ServiceState state);

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

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The sensors did not start: exit code {Code}, 1 when the service did not run in time or Windows refused, "
            + "3 when it is not installed, which reinstalling thermi fixes")]
    public static partial void StartSensorsEnded(this ILogger logger, int code);

    [LoggerMessage(Level = LogLevel.Error, Message = "thermi elevated {Verb} could not be started")]
    public static partial void ElevatedFailed(this ILogger logger, string verb, Exception error);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "thermi elevated {Verb} ended with exit code {Code}: 1 when the sensor service did not run in time, "
            + "3 when it is not installed, which reinstalling thermi fixes, 4 when the driver's installer could not be downloaded, "
            + "5 when it was not the pinned file, 6 when it failed or did not end in time, 7 when its folder was not safe, "
            + "8 when it installed and Windows must restart for it to load")]
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

    [LoggerMessage(Level = LogLevel.Information, Message = "As administrator, {Verb} ended with exit code {Code}")]
    public static partial void ElevatedModeEnded(this ILogger logger, string verb, int code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The driver's download was answered with HTTP {Status}")]
    public static partial void DriverDownloadRefused(this ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The driver's download failed: {Reason}")]
    public static partial void DriverDownloadFailed(this ILogger logger, string reason, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The driver's download ran past the pinned {Bytes} bytes")]
    public static partial void DriverDownloadTooLong(this ILogger logger, long bytes);

    [LoggerMessage(Level = LogLevel.Debug, Message = "paint")]
    public static partial void Painted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "open settings")]
    public static partial void OpeningSettings(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "input {Input}")]
    public static partial void Input(this ILogger logger, string input);

    [LoggerMessage(Level = LogLevel.Debug, Message = "first frame {Milliseconds} ms")]
    public static partial void FirstFrame(this ILogger logger, long milliseconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "thermi {From} hands over to {To}, which the service installed")]
    public static partial void HandingOver(this ILogger logger, ReleaseVersion from, ReleaseVersion to);

    [LoggerMessage(Level = LogLevel.Information, Message = "thermi {To} came up, so this overlay exits")]
    public static partial void HandedOver(this ILogger logger, ReleaseVersion to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "thermi {To} could not take over, so this overlay stays and leaves it alone: {Why}")]
    public static partial void HandoverFailed(this ILogger logger, ReleaseVersion to, string why);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The service could not be sent code {Code}: {Why}")]
    public static partial void ServiceNotTold(this ILogger logger, int code, string why);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The user's sensor host {Path} did not start: {Why}")]
    public static partial void UserHostNotStarted(this ILogger logger, string path, string why);

    [LoggerMessage(Level = LogLevel.Information, Message = "thermi is installed for everyone now, so this per-user overlay hands over to {Launcher}")]
    public static partial void MovingToMachine(this ILogger logger, string launcher);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removing this user's per-user thermi, {Product}, now that thermi is installed for everyone")]
    public static partial void RemovingPerUser(this ILogger logger, string product);

    [LoggerMessage(Level = LogLevel.Warning, Message = "thermi could not move to the install for everyone: {Why}")]
    public static partial void MoveFailed(this ILogger logger, string why);

    [LoggerMessage(Level = LogLevel.Error, Message = "thermi was started elevated, so it exits: its overlay and settings window run only unelevated")]
    public static partial void RefusedElevated(this ILogger logger);
}
