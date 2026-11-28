namespace Thermi.MacOS.Tests;

/// <summary>
/// What every macOS test stands on: a process it starts sees the session's environment alone, what would show on the
/// screen or load an agent never starts in a session that may not, and the session leaves nothing behind.
/// </summary>
public sealed class OwnSessionTests
{
    [Fact(Timeout = Bound.Test)]
    public async Task ASessionHoldsNothingOfThePersons()
    {
        var cancel = TestContext.Current.CancellationToken;
        var session = new OwnSession();
        Ran seen;
        try
        {
            seen = await session.RunAsync("printenv", [], cancel: cancel);
            Assert.Throws<InvalidOperationException>(() => session.Start("launchctl", ["list"]));
            Assert.Throws<InvalidOperationException>(() => session.Start("/bin/sh", ["-c", "launchctl list"]));
        }
        finally
        {
            await session.DisposeAsync();
        }

        var variables = seen.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Select(pair => (pair[0], pair[1]));
        Assert.Equal(0, seen.Exit);
        Assert.Equal(session.Variables.Select(pair => (pair.Key, pair.Value)).Order(), variables.Order());
        Assert.False(Directory.Exists(session.Home));
        Assert.False(Directory.Exists(session.Temporary));
    }
}
