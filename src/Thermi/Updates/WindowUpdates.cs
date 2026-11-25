using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Thermi.Core.Updates;
using Thermi.Logging;

namespace Thermi.Updates;

/// <summary>About's side of updates, as Chrome's About page (docs/adr/0008-updates.md): a check as it opens, a restart at the person's word.</summary>
internal sealed class WindowUpdates(TimeProvider time, ILogger log)
{
    private DateTimeOffset? _asked;

    public UpdateLine Line() => UpdatesHere.Line(time.GetUtcNow(), _asked);

    public void Ask()
    {
        var now = time.GetUtcNow();
        if (UpdatesHere.Line(now).Kind is not (UpdateLineKind.NotUpdating or UpdateLineKind.TurnedOff)
            && (_asked is not { } asked || now - asked >= UpdateSchedule.AskEvery)
            && Tell(UpdateSchedule.CheckCode))
        {
            _asked = now;
        }
    }

    /// <summary>Whether the service was told to switch to the version ready now.</summary>
    public bool Restart() => Line().Restart && Tell(UpdateSchedule.RestartCode);

    private bool Tell(int code)
    {
        try
        {
            UpdatesHere.Tell(code);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            log.ServiceNotTold(code, ex.Message);
            return false;
        }
    }
}
