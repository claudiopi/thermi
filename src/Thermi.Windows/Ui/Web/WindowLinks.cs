using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Thermi.App.Window;
using Thermi.Windows.Logging;

namespace Thermi.Windows.Ui.Web;

/// <summary>How the window opens a file or a link outside itself, as Windows' shell does.</summary>
internal static class WindowLinks
{
    /// <summary>Where thermi is downloaded, for a fix this install cannot run itself.</summary>
    public const string Downloads = PageBridge.Repository + "/releases/latest";

    /// <summary>Opens <paramref name="target"/> as Windows opens a file or a link; one it cannot open is logged.</summary>
    public static void OpenUrl(string target, ILogger log)
    {
        try
        {
            Shell(target);
        }
        catch (Win32Exception ex)
        {
            log.OpenFailed(target, ex);
        }
    }

    /// <summary>
    /// Opens a file or a link as Windows' shell does, through Explorer by its full path, never a program found on the PATH:
    /// a link in the default browser, a file in the program it opens with.
    /// </summary>
    public static void Shell(string target) =>
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), [target])
        {
            UseShellExecute = false,
        })?.Dispose();
}
