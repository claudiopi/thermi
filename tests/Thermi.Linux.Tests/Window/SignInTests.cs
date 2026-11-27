using Thermi.Linux.Agents;

namespace Thermi.Linux.Tests.Window;

/// <summary>Start at sign-in, as the window and thermi-cli set it: the person's own <c>systemctl --user</c>.</summary>
public sealed class SignInTests
{
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
}
