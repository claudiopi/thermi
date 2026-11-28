using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Thermi.Linux.Agents;

namespace Thermi.Linux.Tests.Window;

/// <summary>Start at sign-in, as the window and thermi-cli set it: the person's own <c>systemctl --user</c>.</summary>
public sealed class SignInTests
{
    // A user manager's switch as a file beside it: systemctl --user --quiet <verb> thermi.service.
    private const string Switch = """
        case "$3" in enable) : > "$0.on" ;; disable) rm -f "$0.on" ;; is-enabled) test -e "$0.on" ;; esac
        """;

    // What systemctl answers is the switch, and a switch it refused is an error the window and thermi-cli say, never one
    // that only looks set; one whose user manager never answers reads off and refuses once its time is up, and leaves
    // neither waiting.
    [Theory(Timeout = Bound.Test)] // Rows: systemctl agreeing, refusing, and never answering.
    [InlineData("exit 0", true, false)]
    [InlineData("exit 1", false, false)]
    [InlineData("exec sleep 600", false, true)]
    public async Task TheSwitchIsWhatSystemctlAnswers(string systemctl, bool agrees, bool hangs)
    {
        var cancel = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("thermi-sign-in-");
        try
        {
            var time = new FakeTimeProvider();
            var unit = new UserUnit(time, Stub(folder, systemctl));

            var set = Record.ExceptionAsync(() => unit.SetAsync(on: true, cancel));
            time.Advance(hangs ? UserUnit.Answers : TimeSpan.Zero);
            var refused = await set;
            var on = unit.OnAsync(cancel);
            time.Advance(hangs ? UserUnit.Answers : TimeSpan.Zero);

            Assert.Equal((agrees, agrees), (await on, refused is null));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // thermi's first start in a person's session turns start at sign-in on, as Windows' installer does; once, so the
    // person's off stays off at every start after.
    [Fact(Timeout = Bound.Test)]
    public async Task TheFirstStartTurnsItOnOnce()
    {
        var cancel = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("thermi-sign-in-");
        try
        {
            var unit = new UserUnit(TimeProvider.System, Stub(folder, Switch));
            var marker = Path.Combine(folder.FullName, "thermi", "start-at-sign-in");

            await unit.EnableOnceAsync(marker, NullLogger.Instance, cancel);
            var first = await unit.OnAsync(cancel);
            await unit.SetAsync(on: false, cancel);
            await unit.EnableOnceAsync(marker, NullLogger.Instance, cancel);

            Assert.Equal((true, false), (first, await unit.OnAsync(cancel)));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // A systemctl of the test's own that runs the shell's lines given.
    private static string Stub(DirectoryInfo folder, string lines)
    {
        var systemctl = Path.Combine(folder.FullName, "systemctl");
        // The kernel reads the first line whole, so a checkout's own line ends never reach it.
        File.WriteAllText(systemctl, "#!/bin/sh\n" + lines.ReplaceLineEndings("\n") + "\n");
        File.SetUnixFileMode(systemctl, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return systemctl;
    }
}
