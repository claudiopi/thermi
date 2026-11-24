using Thermi.Core.Updates;

namespace Thermi.Tests.Acceptance;

/// <summary>A switch that went back without failing waits a check's interval, so a slow start or a held pipe is no loop.</summary>
public sealed class SwitchHoldTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ASwitchThatWentBackWaitsACheck()
    {
        var ready = new UpdateState { Installed = Parse("1.2.0"), Ready = Parse("1.3.0") };
        var switching = SwitchHealth.Begin(ready, Parse("1.3.0"));

        var held = SwitchHealth.HeldBack(switching, cut: false, Now);
        var cut = SwitchHealth.HeldBack(switching, cut: true, Now);

        Assert.Equal((false, true), (SwitchHealth.MaySwitch(held, Now.AddHours(1)), SwitchHealth.MaySwitch(held, Now + UpdateSchedule.Every)));
        Assert.Equal((Parse("1.3.0"), null, 1), (held.Ready, held.Switch, cut.Switch!.Cuts));
        Assert.Equal("1.3.0", Assert.Single(SwitchHealth.HeldBack(SwitchHealth.Begin(cut, Parse("1.3.0")), cut: true, Now).Failed).ToString());
        Assert.True(SwitchHealth.MaySwitch(SwitchHealth.Done(SwitchHealth.Begin(held, Parse("1.3.0")), Now), Now));
        Assert.True(SwitchHealth.MaySwitch(SwitchHealth.InstalledByHand(held, Parse("1.2.1")), Now));
        Assert.True(SwitchHealth.MaySwitch(SwitchHealth.HeldBack(switching, cut: false, Now.AddDays(90)), Now));
    }

    private static ReleaseVersion Parse(string version) => ReleaseVersion.Parse(version)!;
}
