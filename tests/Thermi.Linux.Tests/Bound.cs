using System.Globalization;

namespace Thermi.Linux.Tests;

/// <summary>
/// How long a Linux test, and each of its helpers' waits, may take: a line a program never writes, or a program that never
/// ends, would otherwise hang the whole run, and the container a test holds, forever. A helper's wait runs out first and
/// fails saying what it waited for and the program's last lines on stderr; each test's own bound, which a repo rule asks
/// of every async Linux test, ends what no helper waits for.
/// </summary>
internal static class Bound
{
    /// <summary>A test's, in milliseconds, as xunit's <c>Timeout</c> takes it: past every helper's bound below.</summary>
    public const int Test = 20 * 60 * 1000;

    /// <summary>For a line a program writes: a compositor's start, a trace event, a watcher's answer.</summary>
    public static readonly TimeSpan Line = TimeSpan.FromMinutes(2);

    /// <summary><see cref="Line"/> in whole seconds, for a program that waits itself, as <c>gdbus wait --timeout</c> for a bus name.</summary>
    public static string LineSeconds => ((int)Line.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    /// <summary>For a program to end, as a package's install in a container.</summary>
    public static readonly TimeSpan End = TimeSpan.FromMinutes(10);
}
