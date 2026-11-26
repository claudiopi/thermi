using System.IO.Pipes;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Sensors;
using Windows.Win32;

namespace Thermi.Windows.Tests.Sensors;

/// <summary>A test's client of the sensor host's pipe, which steps the host's fake clock while it waits.</summary>
internal static class PipeReading
{
    // Each client's own reader, which keeps the sensors' names of the whole snapshot its values lines follow.
    private static readonly ConditionalWeakTable<NamedPipeClientStream, SnapshotLines> Readers = new();

    /// <summary>
    /// Ticks the clock until a whole snapshot arrives; the server may not have registered the client yet. Time moves only
    /// while nothing is on its way to this client, so a write the server has begun never times out however long the PC
    /// keeps the reader waiting, and only while <paramref name="ready"/>, when given, says the host's work allows it.
    /// </summary>
    public static async Task<Snapshot> NextSnapshotAsync(NamedPipeClientStream client, FakeTimeProvider clock, TimeSpan patience, Func<bool>? ready = null)
    {
        var lines = Readers.GetOrCreateValue(client);
        var buffer = new byte[64 * 1024];
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        giveUp.CancelAfter(patience);
        var partial = false;
        while (true)
        {
            if (!partial && !Holds(client) && ready?.Invoke() != false)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(giveUp.Token);
            wait.CancelAfter(TimeSpan.FromMilliseconds(100));
            try
            {
                var read = await client.ReadAsync(buffer, wait.Token);
                Assert.NotEqual(0, read);
                partial = buffer[read - 1] != (byte)'\n';
                if (lines.Add(buffer.AsSpan(0, read)) is [var snapshot, ..])
                {
                    return snapshot;
                }
            }
            catch (OperationCanceledException) when (!giveUp.IsCancellationRequested)
            {
                // Nothing yet: tick again, if nothing is on its way.
            }
        }
    }

    // Bytes in the pipe for this client, a write the server has begun included.
    private static bool Holds(NamedPipeClientStream client) =>
        TestPInvoke.PeekNamedPipe(client.SafePipeHandle, default, out _, out var available, out _) && available > 0;
}
