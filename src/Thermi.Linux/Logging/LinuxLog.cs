using Microsoft.Extensions.Logging;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Logging;

/// <summary>
/// The events of thermi's overlay on Linux, declared once with their level and text. The debug ones are what
/// <c>--trace</c> writes to the standard error too, a line each, which the tests wait on.
/// </summary>
internal static partial class LinuxLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "paint")]
    public static partial void Painted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Another overlay runs in this session: this one ends")]
    public static partial void AlreadyRunning(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The GNOME Shell extension is {State}: the overlays show once it runs thermi's protocol {Protocol}")]
    public static partial void ExtensionNotReady(this ILogger logger, ExtensionState state, uint protocol);

    [LoggerMessage(Level = LogLevel.Error, Message = "GNOME Shell did not take a call of the extension's")]
    public static partial void ShellCallFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Program} could not be started")]
    public static partial void StartFailed(this ILogger logger, string program, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The overlay could not be painted")]
    public static partial void PaintFailed(this ILogger logger, Exception error);
}
