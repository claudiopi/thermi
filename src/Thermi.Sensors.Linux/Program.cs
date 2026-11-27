using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("linux")]

namespace Thermi.Sensors.Linux;

/// <summary>The <c>thermi-sensors</c> service on Linux, which reads <c>hwmon</c>, NVML, <c>/proc</c> and <c>/sys</c>.</summary>
internal static class Program
{
    private static int Main()
    {
        Console.Error.WriteLine("thermi-sensors: this build reads no sensors.");
        return 1;
    }
}
