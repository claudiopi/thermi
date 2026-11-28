using Thermi.Core.Updates;

namespace Thermi.Tests.Acceptance;

/// <summary>On Linux, a process hands over to the files a package changed once they settled, at the session's quiet moment.</summary>
public sealed class HandOverTests
{
    // The seconds since the program's files last changed, none for no change, whether a drag is under way, and the verdict.
    [Theory] // Rows: 4 and 5 seconds sit either side of the settle time, and a drag stands for Quiet's own rows
    [InlineData(null, false, false)]
    [InlineData(4, false, false)]
    [InlineData(5, false, true)]
    [InlineData(5, true, false)]
    public void AProcessHandsOverOnceThePackageSettledAtAQuietMoment(int? seconds, bool dragging, bool handsOver)
    {
        var hidden = new SessionFacts(WindowOpen: false, dragging, TimeSpan.Zero, Locked: false, AllHidden: true);

        Assert.Equal(handsOver, UpdateSchedule.HandsOver(seconds is { } after ? TimeSpan.FromSeconds(after) : null, hidden));
    }
}
