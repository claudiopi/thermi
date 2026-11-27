using Microsoft.Extensions.Logging.Abstractions;
using Thermi.Linux.Agents;

namespace Thermi.Linux.Tests.Window;

/// <summary>Start at sign-in, as the window and thermi-cli set it: the person's own <c>systemctl --user</c>.</summary>
public sealed class SignInTests
{
    // A user manager's switch as a file beside it: systemctl --user --quiet <verb> thermi.service.
    private const string Systemctl = """
        #!/bin/sh
        case "$3" in enable) : > "$0.on" ;; disable) rm -f "$0.on" ;; is-enabled) test -e "$0.on" ;; esac
        """;

    // What systemctl answers is the switch, and a switch it refused is an error the window and thermi-cli say, never one
    // that only looks set: a systemctl that agrees to everything, and one that refuses everything.
    [Theory] // Rows: systemctl agreeing, and refusing.
    [InlineData("/usr/bin/true", true)]
    [InlineData("/usr/bin/false", false)]
    public void TheSwitchIsWhatSystemctlAnswers(string systemctl, bool agrees)
    {
        var unit = new UserUnit(systemctl);

        var refused = Record.Exception(() => unit.Set(on: true));

        Assert.Equal((agrees, agrees), (unit.On, refused is null));
    }

    // thermi's first start in a person's session turns start at sign-in on, as Windows' installer does; once, so the
    // person's off stays off at every start after.
    [Fact]
    public void TheFirstStartTurnsItOnOnce()
    {
        var folder = Directory.CreateTempSubdirectory("thermi-sign-in-");
        try
        {
            var systemctl = Path.Combine(folder.FullName, "systemctl");
            // The kernel reads the first line whole, so a checkout's own line ends never reach it.
            File.WriteAllText(systemctl, Systemctl.ReplaceLineEndings("\n") + "\n");
            File.SetUnixFileMode(systemctl, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var unit = new UserUnit(systemctl);
            var marker = Path.Combine(folder.FullName, "thermi", "start-at-sign-in");

            unit.EnableOnce(marker, NullLogger.Instance);
            var first = unit.On;
            unit.Set(on: false);
            unit.EnableOnce(marker, NullLogger.Instance);

            Assert.Equal((true, false), (first, unit.On));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
