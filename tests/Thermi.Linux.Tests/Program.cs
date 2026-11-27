using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;

// What these tests start, compositors, sockets and the service's files, is Linux's alone.
[assembly: SupportedOSPlatform("linux")]

namespace Thermi.Linux.Tests;

/// <summary>
/// The tests' program, xunit's own entry point but for one more use: started in a test's session, it is a program of the
/// person's that reads thermi through the SDK (<see cref="OwnSession.SdkStatusAsync"/>), or the settings window's watch on the
/// desktop's look (<see cref="Window.PortalTests.WatchAsync"/>), or its link to the overlay (<see cref="Window.OverlayLinkTests.AnswerAsTheWindowAsync"/>).
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

        return args.Any(arg => arg is "--server" or "--internal-msbuild-node")
            ? await Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions)
                .ConfigureAwait(false)
            : await Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).ConfigureAwait(false);
    }
}
