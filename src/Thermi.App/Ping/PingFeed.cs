using Thermi.Core.Pipe;
using Thermi.Core.Ping;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;

namespace Thermi.App.Ping;

/// <summary>
/// A feed with the ping added to each snapshot, whose reader runs while the feed does. Its first snapshot waits for the
/// first echo's end, a second at most, while an item reads the ping, so a call that reads once says what the echo did,
/// never that it waits.
/// </summary>
internal sealed class PingFeed(ISensorFeed inner, PingReader reader) : ISensorFeed
{
    private readonly Lock _gate = new();
    private bool _answered;
    private bool _held;
    private volatile Made? _made;

    public event EventHandler? Received;

    public Snapshot? Latest => inner.Latest is { } read ? With(read) : null;

    public Snapshot Shown => With(inner.Shown);

    public ServiceState State => inner.State;

    public bool Taken => inner.Taken;

    /// <summary>Follows the settings <paramref name="settings"/> gives, as <see cref="PingReader.Follow"/> does.</summary>
    public void Follow(Func<ThermiConfig?>? settings) => reader.Follow(settings);

    public async Task RunAsync(CancellationToken stop)
    {
        using var pinging = CancellationTokenSource.CreateLinkedTokenSource(stop);
        inner.Received += Arrived;
        reader.Answered += Answered;
        var echoes = reader.RunAsync(pinging.Token);
        try
        {
            await inner.RunAsync(stop);
        }
        finally
        {
            inner.Received -= Arrived;
            reader.Answered -= Answered;
            await pinging.CancelAsync();
            await echoes;
        }
    }

    private void Arrived(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            _held = !_answered && reader.Latest is { Failure: PingFailure.Waiting };
            if (_held)
            {
                return;
            }
        }

        Received?.Invoke(this, e);
    }

    private void Answered(object? sender, EventArgs e)
    {
        bool held;
        lock (_gate)
        {
            (_answered, held, _held) = (true, _held, false);
        }

        // With no fresh snapshot of the service's, the ping is the one live reading, which the overlay records each second.
        if (held || inner.Latest is null)
        {
            Received?.Invoke(this, EventArgs.Empty);
        }
    }

    // The same snapshot for the same reading and reply, as a reader that keeps what it made of a snapshot tells them by
    // reference, and making one copies every sensor.
    private Snapshot With(Snapshot read)
    {
        var reply = reader.Latest;
        if (_made is { } made && ReferenceEquals(made.Read, read) && Equals(made.Reply, reply))
        {
            return made.With;
        }

        var with = PingSensors.With(read, reply);
        _made = new Made(read, reply, with);
        return with;
    }

    private sealed record Made(Snapshot Read, EchoReply? Reply, Snapshot With);
}
