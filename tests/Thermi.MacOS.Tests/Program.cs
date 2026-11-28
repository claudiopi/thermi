using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;

// What these tests start, thermi's programs, launchd's agents and the Mac's own tools, is macOS's alone.
[assembly: SupportedOSPlatform("macos")]

// The Mac's one screen, pointer, keyboard, Dock, preferences and launchd for its user, with the agents' fixed labels, are
// every test's at once, which no session of a test's own holds apart: one test runs at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Thermi.MacOS.Tests;

/// <summary>
/// The tests' program, xunit's own entry point but for one more use: started in a test's session, it is a program of the
/// person's that reads thermi through the SDK (<see cref="OwnSession.SdkStatusAsync"/>).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "The entry point, which xunit would generate")]
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is [TheSdk.Status])
        {
            return await TheSdk.AnswerAsync().ConfigureAwait(false);
        }

        return args.Any(arg => arg is "--server" or "--internal-msbuild-node")
            ? await Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions)
                .ConfigureAwait(false)
            : await Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).ConfigureAwait(false);
    }
}
