using System.Globalization;
using Thermi.Core.Translations;
using Thermi.Core.Updates;

namespace Thermi.Tests.Acceptance;

/// <summary>Spec 004, R25: About asks for a check as it opens, as Chrome's About page does.</summary>
public sealed class UpdateAskTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    // It says it checks while the service has not checked since, for half a minute at most and never before the ask; a check a minute before the ask
    // answers it at once, and one that just worked says so; a version ready with no switch held offers a restart.
    [Theory]
    [InlineData(5, 3600, null, false, UpdateLineKind.Checking, false)]
    [InlineData(31, 3600, null, false, UpdateLineKind.UpToDate, false)]
    [InlineData(5, 50, null, false, UpdateLineKind.CheckedNow, false)]
    [InlineData(5, 70, null, false, UpdateLineKind.Checking, false)]
    [InlineData(-10, 3600, null, false, UpdateLineKind.UpToDate, false)]
    [InlineData(5, 2, "1.3.0", false, UpdateLineKind.Ready, true)]
    [InlineData(null, 3600, "1.3.0", true, UpdateLineKind.Ready, false)]
    public void AboutAsksForACheck(int? asked, int checkedAgo, string? ready, bool held, UpdateLineKind kind, bool restart)
    {
        var policy = new UpdatePolicy(Built: true, TurnedOff: false, Manifest.Stable);
        var state = new UpdateState
        {
            Ready = ready is null ? null : ReleaseVersion.Parse(ready),
            LastCheck = Now.AddSeconds(-checkedAgo),
            LastSuccess = Now.AddSeconds(-checkedAgo),
            SwitchAfter = held ? Now.AddHours(1) : null,
        };

        var line = UpdateStatus.Of(policy, state, Now, asked is { } ago ? Now.AddSeconds(-ago) : null);

        Assert.Equal((kind, restart), (line.Kind, line.Restart));
        Assert.All(Texts.Languages, language => Assert.NotEmpty(UpdateStatus.Say(line, Texts.Of(language), CultureInfo.InvariantCulture, TimeZoneInfo.Utc)));
    }

    // Once a minute, and at once with the clock set back.
    [Theory]
    [InlineData(null, true)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(-10, true)]
    public void AboutAsksOnceAMinute(int? last, bool asks)
    {
        Assert.Equal(asks, UpdateSchedule.MayAsk(last is { } ago ? Now.AddSeconds(-ago) : null, Now));
    }
}
