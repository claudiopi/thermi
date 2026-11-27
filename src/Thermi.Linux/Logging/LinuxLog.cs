using Microsoft.Extensions.Logging;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Logging;

/// <summary>The events of thermi's overlay on Linux, declared once with their level and text.</summary>
internal static partial class LinuxLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "The extension's arrival could not be taken")]
    public static partial void ArrivalFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The monitors could not be read from the GNOME Shell extension")]
    public static partial void MonitorsFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The pointer or a key on an overlay could not be handled")]
    public static partial void PointerFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The menu's choice could not be done")]
    public static partial void ChoiceFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Another overlay runs in this session: this one ends")]
    public static partial void AlreadyRunning(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "This Wayland desktop offers neither GNOME Shell nor layer surfaces: no overlay shows")]
    public static partial void NoSurfaces(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The desktop's notices did not take the one that says so within {Bound}: it is told at the next start")]
    public static partial void NoticeUnanswered(this ILogger logger, TimeSpan bound);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The GNOME Shell extension is {State}: the overlays show once it runs thermi's protocol {Protocol}")]
    public static partial void ExtensionNotReady(this ILogger logger, ExtensionState state, uint protocol);

    [LoggerMessage(Level = LogLevel.Error, Message = "GNOME Shell did not take a call of the extension's")]
    public static partial void ShellCallFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Program} could not be started")]
    public static partial void StartFailed(this ILogger logger, string program, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The overlay could not be painted")]
    public static partial void PaintFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The GNOME Shell extension sent {Signal} as {Shape}, outside thermi's protocol: it is left unread")]
    public static partial void ShellSignalRefused(this ILogger logger, string signal, string shape);

    [LoggerMessage(Level = LogLevel.Error, Message = "The GNOME Shell extension's {Signal} could not be read")]
    public static partial void ShellSignalFailed(this ILogger logger, string signal, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The compositor tells no edge its panels keep room along: the snaps take it to be the left and the top")]
    public static partial void PanelEdgesUntold(this ILogger logger);
}
