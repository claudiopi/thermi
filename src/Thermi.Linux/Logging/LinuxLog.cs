using Microsoft.Extensions.Logging;
using Thermi.Core;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Logging;

/// <summary>The events of thermi's overlay on Linux, declared once with their level and text.</summary>
internal static partial class LinuxLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "The extension's arrival could not be taken")]
    public static partial void ArrivalFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The monitors could not be read from the GNOME Shell extension")]
    public static partial void MonitorsFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "GTK's monitors were read on thread {Thread}, off its main loop: thermi may crash")]
    public static partial void MonitorsReadOffLoop(this ILogger logger, int thread);

    [LoggerMessage(Level = LogLevel.Error, Message = "The pointer or a key on an overlay could not be handled")]
    public static partial void PointerFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The menu's choice could not be done")]
    public static partial void ChoiceFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Another overlay runs in this session: this one ends")]
    public static partial void AlreadyRunning(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "This Wayland desktop has nowhere to show the overlays ({Kind}): no overlay shows, and thermi is not started again")]
    public static partial void NoSurfaces(this ILogger logger, SurfaceKind kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "GTK could not start, with no display or a broken one: no overlay shows")]
    public static partial void NoDisplay(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The desktop's notices did not take the one that says so within {Bound}: it is told at the next start")]
    public static partial void NoticeUnanswered(this ILogger logger, TimeSpan bound);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The GNOME Shell extension is {State}: the overlays show once it runs thermi's protocol {Protocol}")]
    public static partial void ExtensionNotReady(this ILogger logger, ShellExtensionState state, uint protocol);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The GNOME Shell extension is of a protocol before {Protocol}: the overlays show with what it has")]
    public static partial void ExtensionEarlier(this ILogger logger, uint protocol);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The GNOME Shell extension that runs has no {Call}: thermi does without it")]
    public static partial void ShellCallMissing(this ILogger logger, string call);

    [LoggerMessage(Level = LogLevel.Error, Message = "GNOME Shell did not take a call of the extension's")]
    public static partial void ShellCallFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "thermi's first start could not turn start at sign-in on: the next start tries again")]
    public static partial void SignInNotEnabled(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Program} could not be started")]
    public static partial void StartFailed(this ILogger logger, string program, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings window ended with code {Code}: its own log says why")]
    public static partial void WindowEnded(this ILogger logger, int code);

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings window ended with code {Code}; it last wrote on its standard error:\n{Said}")]
    public static partial void WindowDied(this ILogger logger, int code, string said);

    [LoggerMessage(Level = LogLevel.Error, Message = "The overlay could not be painted")]
    public static partial void PaintFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The GNOME Shell extension sent {Signal} as {Shape}, outside thermi's protocol: it is left unread")]
    public static partial void ShellSignalRefused(this ILogger logger, string signal, string shape);

    [LoggerMessage(Level = LogLevel.Error, Message = "The GNOME Shell extension's {Signal} could not be read")]
    public static partial void ShellSignalFailed(this ILogger logger, string signal, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The compositor tells no edge its panels keep room along: the snaps take it to be the left and the top")]
    public static partial void PanelEdgesUntold(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "plasmashell told nothing of its panels' tray and launcher: an overlay beside them keeps room")]
    public static partial void PlasmaPanelsUnread(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Role} hotkey, {Combination}, is held by another program or the desktop: thermi could not take it")]
    public static partial void HotkeyRefused(this ILogger logger, HotkeyRole role, string combination);

    [LoggerMessage(Level = LogLevel.Error, Message = "The desktop took none of the hotkeys")]
    public static partial void HotkeysNotTaken(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "A call to thermi's tray icon {Path} could not be answered")]
    public static partial void TrayCallFailed(this ILogger logger, string path, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No tray takes StatusNotifierItems in this session: thermi's icons show once one does")]
    public static partial void NoTray(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "thermi could not do what {What} asked")]
    public static partial void SignalFailed(this ILogger logger, string what, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The notice could not be shown or done")]
    public static partial void NoticeFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings portal's {Signal} could not be read")]
    public static partial void PortalSignalFailed(this ILogger logger, string signal, Exception error);
}
