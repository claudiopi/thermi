using System.Net.NetworkInformation;

namespace Thermi.App.Ping;

/// <summary>One ICMP echo as <see cref="PingReader"/> sends it, which tests replace.</summary>
internal interface IEcho
{
    /// <summary>Whether any network is up, so an echo can leave the PC at all.</summary>
    bool NetworkUp();

    /// <summary>
    /// The echo's status and round trip in milliseconds, within <paramref name="timeout"/>; a name that resolves to no
    /// address throws <see cref="PingException"/>, as the system's ping does.
    /// </summary>
    Task<(IPStatus Status, long RoundTrip)> SendAsync(string host, TimeSpan timeout, CancellationToken stop);
}

/// <summary>The system's own ping, which needs no administrator rights on Windows or on Linux.</summary>
internal sealed class IcmpEcho : IEcho
{
    public bool NetworkUp() => NetworkInterface.GetIsNetworkAvailable();

    public async Task<(IPStatus Status, long RoundTrip)> SendAsync(string host, TimeSpan timeout, CancellationToken stop)
    {
        using var ping = new System.Net.NetworkInformation.Ping();
        var reply = await ping.SendPingAsync(host, timeout, cancellationToken: stop);
        return (reply.Status, reply.RoundtripTime);
    }
}
