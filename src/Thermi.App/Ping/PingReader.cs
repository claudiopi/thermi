using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Thermi.App.Logging;
using Thermi.Core.Ping;
using Thermi.Core.Settings;

namespace Thermi.App.Ping;

/// <summary>
/// The ping of a process that shows readings: one echo a second, the name's lookup included, waited for a second at most,
/// to the host the settings it follows name while an item of one of their overlays reads the ping, and none otherwise
/// (<c>docs/adr/0020-ping.md</c>). Each such process of the user's sends its own; the sensor service never pings.
/// </summary>
internal sealed class PingReader(IEcho echo, TimeProvider time, ILogger log)
{
    /// <summary>How often an echo goes out, which is also how long one is waited for.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(1);

    /// <summary>How old the last echo's end may be and still read: past it, the ping waits, as no number may look live.</summary>
    public static readonly TimeSpan Stale = 2 * Period;

    private volatile Func<ThermiConfig?> _settings = () => null;
    private volatile Heard? _heard;
    private (string Host, string Outcome)? _said;

    /// <summary>Raised on the reader's thread as each echo ends.</summary>
    public event EventHandler? Answered;

    /// <summary>
    /// The last echo's reply to the host the settings name now, or <see cref="PingFailure.Waiting"/> before one has ended in
    /// <see cref="Stale"/>; null while the settings ask for no ping.
    /// </summary>
    public EchoReply? Latest
    {
        get
        {
            var host = PingSensors.HostOf(_settings());
            return host is null ? null
                : _heard is { } heard && heard.Host == host && time.GetElapsedTime(heard.At) <= Stale ? heard.Reply
                : EchoReply.Failed(PingFailure.Waiting);
        }
    }

    /// <summary>Follows the settings <paramref name="settings"/> gives, read before each echo; with null, none goes out.</summary>
    public void Follow(Func<ThermiConfig?>? settings) => _settings = settings ?? (() => null);

    /// <summary>Sends the echoes until <paramref name="stop"/>; whatever one throws reads as missing, so it never ends otherwise, nor throws.</summary>
    public async Task RunAsync(CancellationToken stop)
    {
        while (true)
        {
            var started = time.GetTimestamp();
            if (PingSensors.HostOf(_settings()) is { } host)
            {
                _heard = new Heard(host, await EchoAsync(host, stop), time.GetTimestamp());
                Answered?.Invoke(this, EventArgs.Empty);
            }

            var left = Period - time.GetElapsedTime(started);
            try
            {
                await Task.Delay(left > TimeSpan.Zero ? left : TimeSpan.Zero, time, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Its process stopped reading, which is how it ends.
                return;
            }
        }
    }

    // A name's lookup may outlast the echo's own timeout by far, with a resolver that answers nothing, so the second bounds both.
    private async Task<EchoReply> EchoAsync(string host, CancellationToken stop)
    {
        if (!echo.NetworkUp())
        {
            return Said(host, EchoReply.Failed(PingFailure.NoNetwork));
        }

        using var timeout = new CancellationTokenSource(Period, time);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(stop, timeout.Token);
        EchoReply reply;
        try
        {
            var (status, roundTrip) = await echo.SendAsync(host, Period, either.Token).WaitAsync(either.Token);
            reply = status == IPStatus.Success ? EchoReply.Of(roundTrip) : EchoReply.Failed(FailureOf(status));
        }
        catch (PingException error) when (!either.IsCancellationRequested)
        {
            reply = EchoReply.Failed(FailureOf(error));
        }
#pragma warning disable CA1031 // The ping outlives whatever one echo throws: the second ran out, it stopped, or it is logged and reads as missing.
        catch (Exception error)
#pragma warning restore CA1031
        {
            reply = EchoReply.Failed(timeout.IsCancellationRequested ? PingFailure.TimedOut : PingFailure.Unreachable);
            if (!either.IsCancellationRequested)
            {
                log.PingFailed(host, error);
            }
        }

        return Said(host, reply);
    }

    // Once a second for hours, so only a change of what it gets back is worth a line.
    private EchoReply Said(string host, EchoReply reply)
    {
        var outcome = reply.Failure?.ToString() ?? "replies";
        if (_said != (host, outcome))
        {
            _said = (host, outcome);
            log.PingChanged(host, outcome);
        }

        return reply;
    }

    private static PingFailure FailureOf(IPStatus status) =>
        status is IPStatus.TimedOut or IPStatus.TtlExpired or IPStatus.TtlReassemblyTimeExceeded
            ? PingFailure.TimedOut
            : PingFailure.Unreachable;

    // The system wraps what resolving the name or sending the echo said; a name is what fails most.
    private static PingFailure FailureOf(PingException error) =>
        (error.InnerException as SocketException)?.SocketErrorCode is not { } code ? PingFailure.Unreachable
        : code is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain ? PingFailure.UnknownHost
        : code is SocketError.NetworkDown or SocketError.NetworkUnreachable ? PingFailure.NoNetwork
        : PingFailure.Unreachable;

    private sealed record Heard(string Host, EchoReply Reply, long At);
}
