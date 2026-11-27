using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Thermi.App.Ping;
using Thermi.Core.Frames;
using Thermi.Core.Pipe;
using Thermi.Core.Ping;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;

namespace Thermi.Windows.Win32;

/// <summary>
/// The sensor feed with what Windows itself says added to each snapshot: how long it has run, the local address of the
/// adapter its default route takes, and the time, as Core's <see cref="SystemSensors"/>, the frames of the app in front,
/// as <see cref="GameSensors"/>, and the ping, as <see cref="PingSensors"/>, which its reader sends while it runs. The
/// address is found again only when the network changes, which it listens to while it runs, so an agent's call, which runs
/// one of its own, lets go of them as it ends; the rest is read at most once a second.
/// </summary>
internal sealed class SystemFeed(ISensorFeed sensors, TimeProvider time, PingReader? ping = null) : ISensorFeed
{
    // Any address beyond the local network: connecting a datagram socket only picks the route, and sends nothing.
    private static readonly IPEndPoint Beyond = new(IPAddress.Parse("192.0.2.1"), 9);

    private static readonly Snapshot NoSensors = new([]);

    private readonly ISensorFeed _sensors = sensors;
    private readonly TimeProvider _time = time;
    private readonly FrontApp _front = new();
    private readonly FrontGame _game = new();
    private readonly Lock _gate = new();
    private uint? _address;
    private bool _addressKnown;
    private (Snapshot Read, long Second, Snapshot With)? _last;

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

    /// <summary>The ping it adds and runs, which follows the settings its process holds; null for none.</summary>
    public PingReader? Ping => ping;

    public async Task RunAsync(CancellationToken stop)
    {
        NetworkChange.NetworkAddressChanged += AddressChanged;
        using var pinging = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var echoes = ping?.RunAsync(pinging.Token) ?? Task.CompletedTask;
        try
        {
            await _sensors.RunAsync(stop);
        }
        finally
        {
            NetworkChange.NetworkAddressChanged -= AddressChanged;
            await pinging.CancelAsync();
            await echoes;
        }
    }

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

            var (front, fills) = _front.Front();
            var game = GameSensors.With(
                SystemSensors.With(read, new WindowsFacts(TimeSpan.FromMilliseconds(Environment.TickCount64), Address(), now)),
                _game.Next(FrameSlots.Read(read), front, fills, _front.Windows));
            var with = ping is null ? game : PingSensors.With(game, ping.Latest);
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
