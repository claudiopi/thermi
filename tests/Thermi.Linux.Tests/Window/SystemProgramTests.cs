using Thermi.Linux.Agents;

namespace Thermi.Linux.Tests.Window;

/// <summary>The system's programs the window and thermi-cli run, as systemctl and pkexec, and wait for.</summary>
public sealed class SystemProgramTests
{
    // A program that writes more than a pipe holds, as a chatty systemctl or pkexec may, still ends, and says how.
    [Fact(Timeout = Bound.Test)]
    public async Task AProgramThatWritesMuchEnds()
    {
        var exit = await SystemProgram.RunAsync("/usr/bin/seq", ["1", "300000"], TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
    }
}
