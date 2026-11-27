using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("linux")]

namespace Thermi.Linux;

/// <summary><c>thermi</c> on Linux: the overlay, <c>thermi settings</c> and <c>thermi preview</c>.</summary>
internal static class Program
{
    private static int Main()
    {
        Console.Error.WriteLine("thermi: this build shows no overlay.");
        return 1;
    }
}
