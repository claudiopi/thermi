using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Thermi.App.Logging;
using Thermi.Core.Ping;
using Thermi.Core.Settings;

namespace Thermi.App.Ping;

/// <summary>
/// The ping of a process that shows readings: one echo a second, waited for a second at most, to the host the settings it
/// follows name while an item of one of their overlays reads the ping, and none otherwise (<c>docs/adr/0020-ping.md</c>).
/// Each such process of the user's sends its own; the sensor service never pings.
/// </summary>
internal sealed class PingReader(IEcho echo, TimeProvider time, ILogger log)
{
    /// <summary>How often an echo goes out, which is also how long one is waited for.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(1);

    private volatile Func<ThermiConfig?> _settings = () => null;
    private volatile EchoReply? _latest;
    private (string Host, string Outcome)? _said;

    /// <summary>The last echo's reply, or null while the settings ask for no ping or before the first comes back.</summary>
    public EchoReply? Latest => _latest;

    /// <summary>Follows the settings <paramref name="settings"/> gives, read before each echo; with null, none goes out.</summary>
    public void Follow(Func<ThermiConfig?>? settings) => _settings = settings ?? (() => null);

    public async Task RunAsync(CancellationToken stop)
    {
        try
        {
            while (true)
            {
                var started = time.GetTimestamp();
                var host = PingSensors.HostOf(_settings());
                _latest = host is null ? null : await EchoAsync(host, stop);

                var left = Period - time.GetElapsedTime(started);
                await Task.Delay(left > TimeSpan.Zero ? left : TimeSpan.Zero, time, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Its process stopped reading, which is how it ends.
        }
    }

    private async Task<EchoReply> EchoAsync(string host, CancellationToken stop)
    {
        var reply = EchoReply.Failed(PingFailure.NoNetwork);
        if (echo.NetworkUp())
        {
            try
            {
                var (status, roundTrip) = await echo.SendAsync(host, Period, stop);
                reply = status == IPStatus.Success ? EchoReply.Of(roundTrip) : EchoReply.Failed(FailureOf(status));
            }
            catch (PingException error) when (!stop.IsCancellationRequested)
            {
                reply = EchoReply.Failed(FailureOf(error));
            }
        }

        // Once a second for hours, so only a change of what it gets back is worth a line.
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
}
