using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("macos")]

namespace Thermi.Sensors.MacOS;

/// <summary>The <c>thermi-sensors</c> service on macOS, which reads the SMC, the HID sensors and IOReport as the person.</summary>
internal static class Program
{
    private static int Main()
    {
        Console.Error.WriteLine("thermi-sensors: this build reads no sensors.");
        return 1;
    }
}
