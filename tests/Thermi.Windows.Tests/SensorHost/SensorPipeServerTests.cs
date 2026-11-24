extern alias sensors;

using System.IO.Pipes;
using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.SensorHost;
using Thermi.Core.Sensors;

namespace Thermi.Windows.Tests.SensorHost;

/// <summary>
/// The sensor host over real pipes, with a fake clock: the ways clients come, go and misbehave, and the
/// ways the host itself fails. A failure must end the server, never leave it running and silent.
/// </summary>
public sealed class SensorPipeServerTests : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _pipeName = $"thermi-test-{Guid.NewGuid():N}";
    private readonly FakeTimeProvider _clock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<NamedPipeClientStream> _clients = [];
    private SensorPipeServer? _server;
    private Task? _running;

    private static Snapshot Small { get; } = new([new RawSensor("/ram/load/0", "Memory", "Total Memory", "Load", "Memory", 45)]);

    // Bigger than the pipe's buffer, so a client that does not read blocks the write at once.
    private static Snapshot Big { get; } = new(
        [.. Enumerable.Range(0, 1500).Select(i => new RawSensor($"/amdcpu/0/load/{i}", "Cpu", "AMD Ryzen 9 9950X", "Load", $"Core #{i}", i))]);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_running is not null)
        {
            await _running.WaitAsync(Patience, TimeProvider.System, CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        _clients.ForEach(client => client.Dispose());
        _server?.Dispose();
        _stop.Dispose();
    }

    [Fact]
    public async Task EveryConnectedClientGetsEachSnapshot()
    {
        _ = Start(() => Small);
        var first = await ConnectAsync();
        var second = await ConnectAsync();

        Assert.Equal(Small.Sensors, (await NextSnapshotAsync(first)).Sensors);
        Assert.Equal(Small.Sensors, (await NextSnapshotAsync(second)).Sensors);
    }

    [Fact]
    public async Task AClientCanComeBackAfterLeaving()
    {
        _ = Start(() => Small);
        var leaving = await ConnectAsync();
        await NextSnapshotAsync(leaving);
        await leaving.DisposeAsync();

        var back = await ConnectAsync();

        Assert.NotNull(await NextSnapshotAsync(back));
    }

    [Fact]
    public async Task AClientThatStopsReadingIsDroppedWithoutHoldingBackTheOthers()
    {
        _ = Start(() => Big);
        var stuck = await ConnectAsync();
        var reader = await ConnectAsync();

        for (var i = 0; i < 4; i++)
        {
            await NextSnapshotAsync(reader);
        }

        Assert.True(await EndsAsync(stuck), "The client that stopped reading should have been dropped.");
    }

    [Fact]
    public async Task PastTheLimitNewClientsWaitForAFreeSlot()
    {
        _ = Start(() => Small);
        var connected = new List<NamedPipeClientStream>();
        for (var i = 0; i < SensorPipeServer.MaxClients; i++)
        {
            connected.Add(await ConnectAsync());
        }

        await Assert.ThrowsAsync<TimeoutException>(() => ConnectAsync(TimeSpan.FromMilliseconds(300)));

        await connected[0].DisposeAsync();
        var late = await ConnectWhenFreeAsync();

        Assert.NotNull(await NextSnapshotAsync(late));
        Assert.False(_running!.IsCompleted, "The server should still be running.");
    }

    [Fact]
    public async Task ASensorReadThatFailsEndsTheServerWithTheError()
    {
        var running = Start(() => throw new InvalidOperationException("The GPU driver reset."));
        await ConnectAsync();
        var token = TestContext.Current.CancellationToken;

        for (var tick = 0; tick < 100 && !running.IsCompleted; tick++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(TimeSpan.FromMilliseconds(50), TimeProvider.System, token);
        }

        Assert.True(running.IsFaulted, "The server should have failed.");
        var error = Assert.IsType<InvalidOperationException>(running.Exception.InnerException);
        Assert.Equal("The GPU driver reset.", error.Message);
    }

    [Fact]
    public async Task APipeSomeoneElseCreatedFirstIsNeverJoined()
    {
        // Same direction and instance count as the real host, so only "first instance" can refuse it.
        await using var squatter = new NamedPipeServerStream(_pipeName, PipeDirection.Out, SensorPipeServer.MaxClients);

        var running = Start(() => Small);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => running.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StoppingEndsTheServerAndHangsUpOnClients()
    {
        var running = Start(() => Small);
        var client = await ConnectAsync();
        await NextSnapshotAsync(client);

        await _stop.CancelAsync();

        await running.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.True(await EndsAsync(client));
    }

    private Task Start(Func<Snapshot> read)
    {
        _server = new SensorPipeServer(read, _clock, _pipeName);
        _running = Task.Run(() => _server.RunAsync(_stop.Token), CancellationToken.None);
        return _running;
    }

    private async Task<NamedPipeClientStream> ConnectAsync(TimeSpan? timeout = null)
    {
        var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.In, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(timeout ?? Patience, TestContext.Current.CancellationToken);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }

        _clients.Add(client);
        return client;
    }

    // A slot frees once the server notices the client is gone, which it does on its next write.
    private async Task<NamedPipeClientStream> ConnectWhenFreeAsync()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            try
            {
                return await ConnectAsync(TimeSpan.FromMilliseconds(200));
            }
            catch (TimeoutException)
            {
                // Not yet.
            }
        }

        throw new TimeoutException("No slot freed up.");
    }

    private Task<Snapshot> NextSnapshotAsync(NamedPipeClientStream client) => PipeReading.NextSnapshotAsync(client, _clock, Patience);

    // True once the server has hung up: whatever was buffered is read, then the pipe ends.
    private async Task<bool> EndsAsync(NamedPipeClientStream client)
    {
        var buffer = new byte[64 * 1024];
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        giveUp.CancelAfter(Patience);
        while (!giveUp.IsCancellationRequested)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(giveUp.Token);
            wait.CancelAfter(TimeSpan.FromMilliseconds(100));
            try
            {
                if (await client.ReadAsync(buffer, wait.Token) == 0)
                {
                    return true;
                }
            }
            catch (OperationCanceledException) when (!giveUp.IsCancellationRequested)
            {
                // Still open: tick again.
            }
            catch (IOException)
            {
                return true;
            }
        }

        return false;
    }
}
