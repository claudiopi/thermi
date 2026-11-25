using System.Buffers;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;
using Thermi.Overlay;

namespace Thermi.Windows.Tests.Overlay;

/// <summary>
/// The overlay's reader of the sensor pipe, over a real pipe of the test's own serving recorded snapshots, on a fake clock:
/// what it yields, whose pipe it refuses, how it waits for a host that went away, and what it says of the service meanwhile.
/// Only a pipe the service or an administrator owns is trusted. The pipes here are owned by the test's own user, set
/// explicitly since an elevated test, as on CI, would otherwise give them to the administrators: the real check refuses
/// that user, and the tests that read through trust it by name.
/// </summary>
public sealed class SensorClientTests : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static readonly Snapshot Admin = TestRecordings.Read(TestRecordings.Admin);

    private static readonly Snapshot NoAdmin = TestRecordings.Read(TestRecordings.NoAdmin);

    private readonly string _pipeName = $"thermi-test-{Guid.NewGuid():N}";
    private readonly FakeTimeProvider _clock = new();
    private readonly ListLogger _log = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<Snapshot?> _received = Channel.CreateUnbounded<Snapshot?>();
    private readonly List<NamedPipeServerStream> _hosts = [];
    private readonly SecurityIdentifier _me;
    private Task? _running;

    public SensorClientTests()
    {
        using var me = WindowsIdentity.GetCurrent();
        _me = me.User!;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_running is not null)
        {
            await _running.WaitAsync(Patience, TimeProvider.System, CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        _stop.Dispose();
    }

    // The service runs as LocalSystem; an ordinary user, as the test's own in APipeOwnedByAnOrdinaryUserIsRefused, or no
    // owner at all is never trusted.
    [Theory]
    [InlineData("the service", true)]
    [InlineData("administrators", true)]
    [InlineData("users", false)]
    [InlineData("nobody", false)]
    public void TrustsOnlyAPipeOwnedByTheServiceOrAdministrators(string owner, bool trusted)
    {
        var sid = owner switch
        {
            "the service" => new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            "administrators" => new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            "users" => new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            _ => null,
        };

        Assert.Equal(trusted, SensorClient.IsTrustedOwner(sid));
    }

    // How bytes become snapshots, split reads included, is the pipe's, in PipeTests.
    [Fact]
    public async Task EachRecordedSnapshotComesThrough()
    {
        Start(trustMe: true);
        var host = await HostAsync();

        await SendAsync(host, Line(Admin));
        var first = await NextAsync();
        await SendAsync(host, Line(NoAdmin));
        var second = await NextAsync();

        Assert.Equal(Admin.Sensors, first?.Sensors);
        Assert.Equal(NoAdmin.Sensors, second?.Sensors);
        Assert.Empty(_log.Entries);
    }

    // Anyone may make a pipe of that name while the service restarts: the client hangs up on it unread, and says the
    // service is stopped because another program holds its pipe, without logging what is no failure.
    [Fact]
    public async Task APipeOwnedByAnOrdinaryUserIsRefused()
    {
        var client = Start(trustMe: false);
        var host = await HostAsync();

        var hungUp = await HungUpAsync(host);

        Assert.True(hungUp, "The client read from a pipe it should not trust.");
        Assert.True(await EventuallyAsync(() => client is { State: ServiceState.Stopped, Taken: true }), $"The service was {client.State}.");
        Assert.False(_received.Reader.TryRead(out _));
        Assert.Null(client.Latest);
        Assert.Empty(_log.Entries);
    }

    // Stopped once the host is gone, which the log says; then the client waits 2 s on the clock and reads again from the
    // host that came back.
    // When a host that stays connected counts as stale is the snapshot's, in PipeTests.
    [Fact]
    public async Task WhenTheHostGoesTheClientSaysSoAndReconnectsTwoSecondsLater()
    {
        var client = Start(trustMe: true);
        var states = new List<ServiceState>();
        var host = await HostAsync();
        await SendAsync(host, Line(Admin));
        await NextAsync();

        await host.DisposeAsync();
        Assert.True(await EventuallyAsync(() => client.State == ServiceState.Stopped), $"The service was {client.State}.");
        states.Add(client.State);
        var back = Listen(out var connecting);
        _clock.Advance(TimeSpan.FromSeconds(1.9));
        var early = connecting.IsCompleted;
        _clock.Advance(TimeSpan.FromSeconds(0.1));
        await connecting.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);
        await SendAsync(back, Line(NoAdmin));
        var again = await NextAsync();
        states.Add(client.State);

        Assert.False(early, "The client came back before its 2 s.");
        Assert.Equal(NoAdmin.Sensors, again?.Sensors);
        Assert.Equal([ServiceState.Stopped, ServiceState.Running], states);
        Assert.False(client.Taken);
        Assert.Equal(LogLevel.Warning, Assert.Single(_log.Entries).Level);
    }

    // Windows woke after an hour asleep: the client reads from a new connection at once, without the 2 s wait, and while it
    // connects the value from before the sleep is stale, not the service stopped, since the service ran all along.
    [Fact]
    public async Task OnWakingTheClientReconnectsAtOnceAndNeverSaysTheServiceStopped()
    {
        using var checking = new ManualResetEventSlim();
        using var trust = new ManualResetEventSlim();
        var connections = 0;
        var client = Start(owner =>
        {
            if (Interlocked.Increment(ref connections) > 1)
            {
                checking.Set();
                trust.Wait(Patience, TestContext.Current.CancellationToken);
            }

            return owner == _me;
        });
        var host = await HostAsync();
        await SendAsync(host, Line(Admin));
        await NextAsync();
        var back = Listen(out var connecting);

        _clock.Advance(TimeSpan.FromHours(1));
        client.Resume();
        await connecting.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);
        var reached = checking.Wait(Patience, TestContext.Current.CancellationToken);
        var meanwhile = (client.State, client.Latest);
        trust.Set();
        await SendAsync(back, Line(NoAdmin));
        var again = await NextAsync();

        Assert.True(reached, "The client did not connect again.");
        Assert.Equal((ServiceState.Stale, null), meanwhile);
        Assert.Equal(NoAdmin.Sensors, again?.Sensors);
        Assert.Equal(ServiceState.Running, client.State);
        Assert.Empty(_log.Entries);
    }

    // As the service writes it: one line of JSON a snapshot.
    private static byte[] Line(Snapshot snapshot)
    {
        var line = new ArrayBufferWriter<byte>();
        SnapshotJson.WriteLine(snapshot, line);
        return line.WrittenSpan.ToArray();
    }

    private static async Task SendAsync(NamedPipeServerStream host, byte[] bytes)
    {
        await host.WriteAsync(bytes, TestContext.Current.CancellationToken);
        await host.FlushAsync(TestContext.Current.CancellationToken);
    }

    // True once the client has let the pipe go without reading what was sent.
    private static async Task<bool> HungUpAsync(NamedPipeServerStream host)
    {
        try
        {
            await SendAsync(host, Line(Admin));
            host.WaitForPipeDrain();
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private SensorClient Start(bool trustMe) => Start(trustMe ? owner => owner == _me : null);

    private SensorClient Start(Func<SecurityIdentifier?, bool>? trusted)
    {
        var client = new SensorClient(_clock, _log, new FixedPipe(_pipeName, trusted ?? SensorClient.IsTrustedOwner));
        client.Received += (_, _) => _received.Writer.TryWrite(client.Latest);
        _running = Task.Run(() => client.RunAsync(_stop.Token), CancellationToken.None);
        return client;
    }

    private async Task<NamedPipeServerStream> HostAsync()
    {
        var host = Listen(out var connecting);
        await connecting.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);
        return host;
    }

    // A host's pipe, as the service's has its direction, waiting for the client, owned by the test's user; a second
    // instance waits while the first is still there.
    private NamedPipeServerStream Listen(out Task connecting)
    {
        var security = new PipeSecurity();
        security.SetOwner(_me);
        security.AddAccessRule(new PipeAccessRule(_me, PipeAccessRights.FullControl, AccessControlType.Allow));
        var host = NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.Out, 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
        _hosts.Add(host);
        connecting = host.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private async Task<Snapshot?> NextAsync()
    {
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        giveUp.CancelAfter(Patience);
        return await _received.Reader.ReadAsync(giveUp.Token);
    }

    // What the client's own thread does once the pipe has ended, which raises nothing to wait on: the state itself is
    // watched, and the real clock only bounds a test that hangs.
    private static Task<bool> EventuallyAsync(Func<bool> done) => Task.FromResult(SpinWait.SpinUntil(done, Patience));
}
