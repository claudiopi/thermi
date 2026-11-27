using System.Globalization;

namespace Thermi.Linux.Tests;

/// <summary>
/// What every Linux test stands on: a process it starts sees the session's environment alone, talks to the session's own
/// bus, and ends with the session, which leaves nothing behind.
/// </summary>
public sealed class OwnSessionTests
{
    [Fact(Timeout = Bound.Test)]
    public async Task ASessionHoldsNothingOfThePersons()
    {
        var cancel = TestContext.Current.CancellationToken;
        var session = new OwnSession();
        Ran seen;
        Ran bus;
        try
        {
            seen = await session.RunAsync("env", [], cancel: cancel);
            bus = await session.RunAsync(
                "gdbus",
                ["call", "--session", "--dest", "org.freedesktop.DBus", "--object-path", "/org/freedesktop/DBus",
                    "--method", "org.freedesktop.DBus.GetConnectionUnixProcessID", "org.freedesktop.DBus"],
                cancel: cancel);
        }
        finally
        {
            await session.DisposeAsync();
        }

        var variables = seen.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Select(pair => (pair[0], pair[1]));
        Assert.Equal(session.Variables.Select(pair => (pair.Key, pair.Value)).Order(), variables.Order());
        // The bus that answered is the session's daemon, whose process ended with the session, and its folders went with it.
        var daemon = int.Parse(bus.Out.Trim('(', ')', ',', '\n').Replace("uint32 ", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        Assert.Equal(0, bus.Exit);
        Assert.False(Directory.Exists(Path.Combine("/proc", daemon.ToString(CultureInfo.InvariantCulture))));
        Assert.False(Directory.Exists(session.Home));
        Assert.False(Directory.Exists(session.RuntimeFolder));
    }
}
