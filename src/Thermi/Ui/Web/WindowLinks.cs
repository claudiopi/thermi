using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Thermi.Core.Bridge;
using Thermi.Logging;

namespace Thermi.Ui.Web;

/// <summary>What the window opens outside itself: the settings file in its editor, and About's pages in the browser.</summary>
internal static class WindowLinks
{
    private const string Repository = "https://github.com/claudiopi/thermi";

    /// <summary>Where thermi is downloaded, for a fix this install cannot run itself.</summary>
    public const string Downloads = Repository + "/releases/latest";

    /// <summary>Opens <paramref name="what"/> as Windows opens a file or a link; one it cannot open is logged.</summary>
    public static void Open(Opened what, string settingsPath, ILogger log)
    {
        var target = what switch
        {
            Opened.SettingsFile => settingsPath,
            Opened.Source => Repository,
            Opened.Report => $"{Repository}/issues/new/choose",
            Opened.Notices or _ => $"{Repository}/blob/main/THIRD-PARTY-NOTICES.md",
        };
        OpenUrl(target, log);
    }

    /// <summary>Opens <paramref name="target"/> as Windows opens a file or a link; one it cannot open is logged.</summary>
    public static void OpenUrl(string target, ILogger log)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            log.OpenFailed(target, ex);
        }
    }
}
