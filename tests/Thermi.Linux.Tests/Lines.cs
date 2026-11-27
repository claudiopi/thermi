using System.Threading.Channels;

namespace Thermi.Linux.Tests;

/// <summary>
/// What a program writes on one of its streams, read as it comes, so a pipe that fills never stops the program: each line
/// kept, for a test to read in order, or to wait for the next that matches, armed before what makes it.
/// </summary>
internal sealed class Lines
{
    private readonly Channel<string> _unread = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly List<string> _said = [];
    private readonly List<Waiter> _waiting = [];
    private readonly Lock _lock = new();
    private readonly Task _reading;
    private bool _ended;

    public Lines(StreamReader stream)
    {
        _reading = ReadAsync(stream);
    }

    public string Text
    {
        get
        {
            lock (_lock)
            {
                return string.Join('\n', _said);
            }
        }
    }

    /// <summary>The next line not read yet, in order, each once; null once the stream ended and every line was read.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken cancel) =>
        await _unread.Reader.WaitToReadAsync(cancel).ConfigureAwait(false) && _unread.Reader.TryRead(out var line) ? line : null;

    /// <summary>
    /// The <paramref name="count"/>th line that matches of those written from this call on: what came before never counts,
    /// so a test arms it before what it waits for. It fails once the stream ends first.
    /// </summary>
    public Task<string> NextAsync(Func<string, bool> match, CancellationToken cancel, int count = 1) => ArmAsync(match, count, [], cancel);

    /// <summary>
    /// The first line that matches of all it wrote, those before this call too: what a program says once, as it starts, cannot
    /// pass before the test asks.
    /// </summary>
    public Task<string> FirstAsync(Func<string, bool> match, CancellationToken cancel) => ArmAsync(match, 1, _said, cancel);

    public async Task<string> ToEndAsync(CancellationToken cancel)
    {
        await _reading.WaitAsync(cancel).ConfigureAwait(false);
        return Text;
    }

    // What came before counts only when the test asks for it, and is read under the same lock as the lines that come.
    private Task<string> ArmAsync(Func<string, bool> match, int count, List<string> past, CancellationToken cancel)
    {
        var waiter = new Waiter(match, count);
        lock (_lock)
        {
            if (past.Find(waiter.Saw) is { } said)
            {
                return Task.FromResult(said);
            }

            if (_ended)
            {
                return Task.FromException<string>(new InvalidOperationException("The program ended before it wrote the line waited for."));
            }

            _waiting.Add(waiter);
        }

        return WaitAsync(waiter, cancel);
    }

    private async Task<string> WaitAsync(Waiter waiter, CancellationToken cancel)
    {
        try
        {
            return await waiter.Done.Task.WaitAsync(cancel).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                _waiting.Remove(waiter);
            }
        }
    }

    private async Task ReadAsync(StreamReader stream)
    {
        await Task.Yield();
        while (await stream.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            List<Waiter> met;
            lock (_lock)
            {
                _said.Add(line);
                met = [.. _waiting.Where(waiter => waiter.Saw(line))];
                _waiting.RemoveAll(met.Contains);
            }

            _unread.Writer.TryWrite(line);
            met.ForEach(waiter => waiter.Done.TrySetResult(line));
        }

        List<Waiter> left;
        lock (_lock)
        {
            _ended = true;
            left = [.. _waiting];
            _waiting.Clear();
        }

        _unread.Writer.Complete();
        left.ForEach(waiter => waiter.Done.TrySetException(new InvalidOperationException($"The program ended before it wrote the line waited for:\n{Text}")));
    }

    private sealed class Waiter(Func<string, bool> match, int count)
    {
        private int _left = count;

        public TaskCompletionSource<string> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Saw(string line) => match(line) && --_left == 0;
    }
}
