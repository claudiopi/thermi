using System.Reflection;

namespace Thermi.MacOS.Tests;

/// <summary>What the build and the repo put where the tests read it: thermi's programs and app, and the recorded Macs.</summary>
internal static class Built
{
    /// <summary>The repo's root, the folder above the test output that holds <c>thermi.slnx</c>.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>
    /// <c>thermi.app</c> as the build of <c>src/Thermi.MacOS</c> makes it for Apple silicon, in the tests' own configuration:
    /// a test project of plain .NET loads none of its AppKit, so it starts the app.
    /// </summary>
    public static string App { get; } = Path.Combine(
        Root, "src", "Thermi.MacOS", "bin", typeof(Built).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration,
        "net10.0-macos", "osx-arm64", "thermi.app");

    /// <summary><c>thermi</c> itself, in the app.</summary>
    public static string Thermi { get; } = Path.Combine(App, "Contents", "MacOS", "thermi");

    /// <summary>One of thermi's programs of plain .NET, <c>thermi-cli</c> or <c>thermi-sensors</c>, as the build puts it beside the tests.</summary>
    public static string Program(string name) => Path.Combine(AppContext.BaseDirectory, name);

    /// <summary>
    /// What <c>thermi-sensors --raw</c> recorded on a Mac: what the SMC, the HID sensors, IOReport and IOKit's registry
    /// answered each second. Its snapshots, as <c>--dump</c> saved them, are beside it, in its name's <c>.json</c>.
    /// </summary>
    public static string Recording(string name) => Path.Combine(Root, "tests", "Thermi.Tests", "Recordings", "Mac", name);

    /// <summary>A settings file the tests of the settings' versions hold, as another OS wrote it.</summary>
    public static string Config(string name) => Path.Combine(Root, "tests", "Thermi.Tests", "Configs", name);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "thermi.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("thermi.slnx not found above the test output folder.");
    }
}
