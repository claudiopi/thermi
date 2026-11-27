using System.Net.NetworkInformation;
using Thermi.Core.Frames;
using Thermi.Core.Pipe;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;

namespace Thermi.Windows.Win32;

/// <summary>
/// The sensor feed with what Windows itself says added to each snapshot: how long it has run, the local address of the
/// adapter its default route takes, and the time, as Core's <see cref="SystemSensors"/>, and the frames of the app in
/// front, as <see cref="GameSensors"/>. The address is found again only when the network changes, which it listens to
/// while it runs, so an agent's call, which runs one of its own, lets go of them as it ends; the rest is read at most
/// once a second.
/// </summary>
internal sealed class SystemFeed(ISensorFeed sensors, TimeProvider time) : ISensorFeed
{
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

    public async Task RunAsync(CancellationToken stop)
    {
        NetworkChange.NetworkAddressChanged += AddressChanged;
        try
        {
            await _sensors.RunAsync(stop);
        }
        finally
        {
            NetworkChange.NetworkAddressChanged -= AddressChanged;
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
            var with = GameSensors.With(
                SystemSensors.With(read, new SystemFacts(TimeSpan.FromMilliseconds(Environment.TickCount64), Address(), now)),
                _game.Next(FrameSlots.Read(read), front, fills, _front.Windows));
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
            _address = SystemSensors.DefaultRouteAddress();
            _addressKnown = true;
        }

        return _address;
    }
}
