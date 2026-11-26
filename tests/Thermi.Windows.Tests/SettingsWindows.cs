using System.Collections.Concurrent;
using Thermi.App.SettingsFile;
using Thermi.Core.Look;
using Thermi.Core.Pipe;
using Thermi.Core.Translations;
using Thermi.Windows.Ui;
using Thermi.Windows.Ui.Editor;
using Thermi.Windows.Ui.Web;

namespace Thermi.Windows.Tests;

/// <summary>The settings window on a test's <see cref="SettingsFolder"/>, which only the Windows tests can open.</summary>
internal static class SettingsWindows
{
    /// <summary>
    /// What a window on this file reaches, with <paramref name="sensors"/> for the pipe, a start-up entry of the tests' own,
    /// no monitor and no overlay to answer for its hotkeys.
    /// </summary>
    public static WindowEnvironment Window(this SettingsFolder folder, ISensorFeed sensors)
    {
        var startup = new StartWithWindows(@"Software\thermi-tests\Run", @"Software\thermi-tests\Approved", "thermi.exe");
        return new("thermi-tests", folder.File, "thermi.exe", folder.Log, folder.Clock.GetUtcNow(), sensors, startup)
        {
            Screens = () => [],
            HotkeyStatus = () => null,
        };
    }

    /// <summary>
    /// A window's bridge on <paramref name="settings"/>, opened on <paramref name="overlay"/>, its editor and its turns on
    /// <paramref name="turn"/>, on the folder's clock.
    /// </summary>
    public static WindowBridge Bridge(
        this SettingsFolder folder, Settings settings, WindowEnvironment window, ConcurrentQueue<Action> turn, int overlay = 0, ChartHistory? chart = null)
    {
        var start = new WindowStart(overlay, settings, Texts.English, window, folder.Clock);
        return new(start, new WindowEditor(settings, turn.Enqueue, folder.Log, folder.Clock), chart ?? new(folder.Clock), turn.Enqueue);
    }
}
