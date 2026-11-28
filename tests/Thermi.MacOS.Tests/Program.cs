using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;

// What these tests start, thermi's programs, launchd's agents and the Mac's own tools, is macOS's alone.
[assembly: SupportedOSPlatform("macos")]

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
        if (args is [OwnSession.SdkStatus])
        {
            return await OwnSession.AnswerAsTheSdkAsync().ConfigureAwait(false);
        }

        return args.Any(arg => arg is "--server" or "--internal-msbuild-node")
            ? await Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions)
                .ConfigureAwait(false)
            : await Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).ConfigureAwait(false);
    }
}
