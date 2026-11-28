namespace Thermi.MacOS.Tests;

/// <summary>
/// What a session that is not made for tests may start: thermi's programs of plain .NET beside the tests, the tests' own
/// program, the Mac's tools that only read, sign or copy files, and pwsh on a script of the repo's. None of them starts
/// another program it is told to. Anything else, a shell, <c>env</c>, ssh, sudo, osascript, launchctl or thermi's app,
/// could show on the person's screen or load an agent into their launchd, whatever the session's home, so it starts only
/// in a session made for tests. The repo's rules hold this check, which <c>OwnSession</c> makes of every program it starts.
/// </summary>
internal static class PlainSession
{
    // By name, as the session's PATH finds them, the build's programs first.
    private static readonly string[] ByName = ["thermi-cli", "id", "printenv", "plutil", "codesign", "ditto", "lipo"];

    // By their path, in the folder the build puts them in.
    private static readonly string[] Beside = ["thermi-cli", "thermi-sensors", "Thermi.MacOS.Tests"];

    /// <summary>
    /// Whether a plain session may start <paramref name="program"/> with <paramref name="args"/>, where the build puts
    /// thermi's programs in <paramref name="programs"/> and the repo its scripts in <paramref name="scripts"/>.
    /// </summary>
    public static bool MayStart(string program, IReadOnlyList<string> args, string programs, string scripts) =>
        Path.GetFileName(program) != program
            ? Path.GetDirectoryName(program) == programs && Beside.Contains(Path.GetFileName(program), StringComparer.Ordinal)
            : ByName.Contains(program, StringComparer.Ordinal)
                || (program == "pwsh" && args is ["-NoProfile", "-File", var script, ..] && Path.GetDirectoryName(script) == scripts);
}
