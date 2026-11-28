using Microsoft.Extensions.Logging;
using Thermi.App.Logging;
using Thermi.Core.Updates;

namespace Thermi.App.Overlay;

/// <summary>
/// When an overlay whose program's files the system's updater changed hands over to them (docs/adr/0019-linux.md): once a
/// second, once they settled, at the session's quiet moment as Core's rule has it for a version ready on Windows, it ends
/// through <paramref name="quit"/>, once. A desktop supplies the inputs: how long ago a file of the program changed, none
/// for no change; and, asked only where the overlays' own state leaves the moment open, the time since the person's last
/// input and whether the session is locked. The settings window holds no overlay back here, as it does on Windows: it
/// hands over too, as it sees the overlay come back.
/// </summary>
public sealed class OverlayHandOver(Func<TimeSpan?> sinceChange, Func<(TimeSpan SinceInput, bool Locked)> session, Action quit, ILogger log)
{
    /// <summary>Whether the overlay ended to hand over.</summary>
    public bool HandedOver { get; private set; }

    /// <summary>The overlay's second, with whether a drag is under way and whether every overlay is hidden.</summary>
    public void Tick(bool dragging, bool allHidden)
    {
        if (HandedOver || sinceChange() is not { } since)
        {
            return;
        }

        var (sinceInput, locked) = dragging || allHidden ? (TimeSpan.Zero, false) : session();
        if (UpdateSchedule.HandsOver(since, new SessionFacts(WindowOpen: false, dragging, sinceInput, locked, allHidden)))
        {
            log.HandingOver();
            HandedOver = true;
            quit();
        }
    }
}
