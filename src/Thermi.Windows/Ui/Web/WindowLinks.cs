using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Thermi.App.Window;
using Thermi.Windows.Logging;

namespace Thermi.Windows.Ui.Web;

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

    /// <summary>Through Explorer by its full path, never a program found on the PATH.</summary>
    public static void Shell(string target) =>
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), [target])
        {
            UseShellExecute = false,
        })?.Dispose();
}
