using Microsoft.Extensions.Logging;
using Thermi.Linux.Desktop;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Tray;

/// <summary>
/// What a notice's button asks is done from a signal of the bus, which GLib calls from native code: a choice that fails
/// there is logged, and the process goes on to hear the next click, never ended by it.
/// </summary>
public sealed class NoticeClickTests
{
    /// <summary>The tests' own program, started in a session with it, tells a notice and fails its first click (<see cref="ClickTwice"/>).</summary>
    public const string Clicks = "--notice-clicks";

    private const string Button = "try";

    [Fact(Timeout = Bound.Test)]
    public async Task AChoiceThatFailsIsLoggedAndTheNextClickIsHeard()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.X11, [new TestMonitor(1280, 720)], cancel);
        var watch = await NoticeWatch.StartAsync(desktop, cancel);
        var told = watch.NextAsync(Button, cancel);
        var program = await desktop.Session.StartAsync(Built.Program("Thermi.Linux.Tests"), [Clicks], cancel: cancel);
        var notice = await told;
        var logged = program.Out.FirstAsync(line => line.StartsWith("logged", StringComparison.Ordinal), cancel);

        await watch.ClickAsync(notice, Button, cancel);
        await logged;
        await watch.ClickAsync(notice, Button, cancel);
        await program.WaitForExitAsync(cancel);

        Assert.Equal(0, program.ExitCode);
    }

    /// <summary>
    /// The tests' own program as <see cref="AChoiceThatFailsIsLoggedAndTheNextClickIsHeard"/> starts it: thermi's notices on a
    /// GLib loop of their own, whose first click throws and whose second ends the loop; each line logged is written out.
    /// </summary>
    public static int ClickTwice()
    {
        Gio.Module.Initialize();
        using var bus = Gio.Functions.BusGetSync(Gio.BusType.Session, null);
        var loop = GLib.MainLoop.New(null, false);
        using var notices = new DesktopNotices(bus, new Written());
        var clicks = 0;
        notices.Invoked = (_, _) =>
        {
            if (++clicks == 1)
            {
                throw new InvalidOperationException("The choice failed.");
            }

            loop.Quit();
        };
        _ = notices.Say("thermi", "A notice to click", (Button, "Try"));
        loop.Run();
        return 0;
    }

    private sealed class Written : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Console.WriteLine($"logged {formatter(state, exception)}");
    }
}
