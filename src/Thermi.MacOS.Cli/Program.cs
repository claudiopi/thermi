using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("macos")]

namespace Thermi.MacOS.Cli;

/// <summary><c>thermi-cli</c> on macOS: the agents' commands and the MCP server, over the service's socket.</summary>
internal static class Program
{
    private static int Main()
    {
        Console.Error.WriteLine("thermi-cli: this build answers no command.");
        return 1;
    }
}
