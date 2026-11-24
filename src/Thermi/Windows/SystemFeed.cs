using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;
using Thermi.Overlay;

namespace Thermi.Windows;

/// <summary>
/// The sensor feed with what Windows itself says added to each snapshot: how long it has run, the local address of the
/// adapter its default route takes, and the time, as Core's <see cref="SystemSensors"/>. The address is found again only
/// when the network changes; the rest is read at most once a second. It lives as long as its process, so it never lets
/// go of Windows' network changes.
/// </summary>
internal sealed class SystemFeed : ISensorFeed
{
    // Any address beyond the local network: connecting a datagram socket only picks the route, and sends nothing.
    private static readonly IPEndPoint Beyond = new(IPAddress.Parse("192.0.2.1"), 9);

    private static readonly Snapshot NoSensors = new([]);

    private readonly ISensorFeed _sensors;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private uint? _address;
    private bool _addressKnown;
    private (Snapshot Read, long Second, Snapshot With)? _last;

    public SystemFeed(ISensorFeed sensors, TimeProvider time)
    {
        _sensors = sensors;
        _time = time;
        NetworkChange.NetworkAddressChanged += AddressChanged;
    }

    public event EventHandler? Received
    {
        add => _sensors.Received += value;
        remove => _sensors.Received -= value;
    }

    public Snapshot? Latest => _sensors.Latest is { } read ? With(read) : null;

    /// <summary>
    /// What the overlay shows: the latest snapshot, or while the service sends none, one of no sensors but what Windows
    /// itself says, so the clock, the uptime and the address never wait on the service.
    /// </summary>
    public Snapshot Shown => With(_sensors.Latest ?? NoSensors);

    public ServiceState State => _sensors.State;

    public bool Taken => _sensors.Taken;

    public Task RunAsync(CancellationToken stop) => _sensors.RunAsync(stop);

    private Snapshot With(Snapshot read)
    {
        var now = _time.GetLocalNow();
        var second = now.ToUnixTimeSeconds();
        lock (_gate)
        {
            if (_last is { } last && ReferenceEquals(last.Read, read) && last.Second == second)
            {
                return last.With;
            }

            var with = SystemSensors.With(read, new WindowsFacts(TimeSpan.FromMilliseconds(Environment.TickCount64), Address(), now));
            _last = (read, second, with);
            return with;
        }
    }

    private void AddressChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            _addressKnown = false;
        }
    }

    private uint? Address()
    {
        if (!_addressKnown)
        {
            _address = DefaultRouteAddress();
            _addressKnown = true;
        }

        return _address;
    }

    private static uint? DefaultRouteAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(Beyond);
            return socket.LocalEndPoint is IPEndPoint { Address: var local } && !IPAddress.IsLoopback(local)
                ? SystemSensors.AddressOf(local.GetAddressBytes())
                : null;
        }
        catch (SocketException)
        {
            // No route out: no network.
            return null;
        }
    }
}
