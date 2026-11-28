using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;

// What these tests start, compositors, sockets and the service's files, is Linux's alone.
[assembly: SupportedOSPlatform("linux")]

namespace Thermi.Linux.Tests;

/// <summary>
/// The tests' program, xunit's own entry point but for a few more uses: started in a test's session, it is a program of the
/// person's that reads thermi through the SDK (<see cref="OwnSession.SdkStatusAsync"/>), the settings window's watch on the
/// desktop's look (<see cref="Window.PortalTests.WatchAsync"/>), its link to the overlay
/// (<see cref="Window.OverlayLinkTests.AnswerAsTheWindowAsync"/>, <see cref="Window.OverlayLinkTests.SendAsTheWindow"/>), its
/// map's monitors (<see cref="Acceptance.GestureTests.SayWindowMap"/>), thermi's notices with a choice that fails
/// (<see cref="Tray.NoticeClickTests.ClickTwice"/>), or the window asking the overlay for its hotkeys
/// (<see cref="Acceptance.DesktopTests.AskHotkeyStatus"/>).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "The entry point, which xunit would generate")]
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is [OwnSession.SdkStatus])
        {
            return await OwnSession.AnswerAsTheSdkAsync().ConfigureAwait(false);
        }

        if (args is [Window.PortalTests.Watch])
        {
            return Window.PortalTests.WatchAsync();
        }

        if (args is [Window.OverlayLinkTests.Linked, var what, .. var thermi] && thermi.Length < 2)
        {
            return await Window.OverlayLinkTests.AnswerAsTheWindowAsync(what, thermi.FirstOrDefault()).ConfigureAwait(false);
        }

        if (args is [Window.OverlayLinkTests.Sends, var count])
        {
            return Window.OverlayLinkTests.SendAsTheWindow(int.Parse(count, System.Globalization.CultureInfo.InvariantCulture));
        }

        if (args is [Tray.NoticeClickTests.Clicks])
        {
            return Tray.NoticeClickTests.ClickTwice();
        }

        if (args is [Acceptance.GestureTests.WindowMap])
        {
            return Acceptance.GestureTests.SayWindowMap();
        }

        if (args is [Acceptance.DesktopTests.HotkeyStatus])
        {
            return Acceptance.DesktopTests.AskHotkeyStatus();
        }

        return args.Any(arg => arg is "--server" or "--internal-msbuild-node")
            ? await Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions)
                .ConfigureAwait(false)
            : await Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).ConfigureAwait(false);
    }
}
