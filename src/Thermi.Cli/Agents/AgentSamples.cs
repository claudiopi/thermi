using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Thermi.Core.Pipe;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;

namespace Thermi.Cli.Agents;

/// <summary>
/// The snapshots an agent's command takes from a feed of its own, each as it comes, once a second; the feed's connection
/// lives only while they are taken (<c>docs/adr/0013-agents-cli-and-mcp.md</c>), so a call never holds the pipe after it.
/// </summary>
internal static class AgentSamples
{
    /// <summary>How long a call waits for the first snapshot: the pipe's retry, its answer and a second of reading.</summary>
    public static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(5);

    /// <summary>How long a watch waits for the next before it says the service went quiet: the overlay's freshness.</summary>
    public static readonly TimeSpan NextWait = TimeSpan.FromSeconds(3);

    /// <summary>The first <paramref name="count"/> snapshots, or fewer when the service went quiet first, with how it stands.</summary>
    public static async Task<(IReadOnlyList<Snapshot> Samples, ServiceState State, bool Taken)> TakeAsync(
        ISensorFeed feed,
        int count,
        TimeProvider time,
        CancellationToken stop)
    {
        var samples = new List<Snapshot>(count);
        await foreach (var snapshot in StreamAsync(feed, time, stop))
        {
            if (snapshot is null)
            {
                break;
            }

            samples.Add(snapshot);
            if (samples.Count == count)
            {
                break;
            }
        }

        return (samples, samples.Count > 0 ? ServiceState.Running : feed.State, feed.Taken);
    }

    /// <summary>
    /// Every snapshot as it comes, until <paramref name="stop"/>; a null when none came in time, after which nothing more
    /// does. The connection ends when the enumeration does.
    /// </summary>
    public static async IAsyncEnumerable<Snapshot?> StreamAsync(ISensorFeed feed, TimeProvider time, [EnumeratorCancellation] CancellationToken stop)
    {
        var arrived = Channel.CreateUnbounded<Snapshot>(new UnboundedChannelOptions { SingleReader = true });
        void Received(object? sender, EventArgs e)
        {
            if (feed.Latest is { } snapshot)
            {
                arrived.Writer.TryWrite(snapshot);
            }
        }

        using var reading = CancellationTokenSource.CreateLinkedTokenSource(stop);
        feed.Received += Received;
        var run = feed.RunAsync(reading.Token);
        try
        {
            var wait = FirstWait;
            while (!stop.IsCancellationRequested)
            {
                var next = await NextAsync(arrived.Reader, wait, time, stop);
                yield return next;
                if (next is null)
                {
                    yield break;
                }

                wait = NextWait;
            }
        }
        finally
        {
            feed.Received -= Received;
            await reading.CancelAsync();
            await run.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private static async Task<Snapshot?> NextAsync(ChannelReader<Snapshot> arrived, TimeSpan wait, TimeProvider time, CancellationToken stop)
    {
        using var timeout = new CancellationTokenSource(wait, time);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(stop, timeout.Token);
        try
        {
            return await arrived.ReadAsync(either.Token);
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            return null;
        }
    }
}
