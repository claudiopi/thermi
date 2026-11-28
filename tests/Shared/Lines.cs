using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Thermi.Tests.Shared;

/// <summary>
/// What a program writes on one of its streams, read as it comes, so a pipe that fills never stops the program: each line
/// kept, for a test to read in order, or to wait for the next that matches, armed before what makes it. Each wait is
/// bounded (<see cref="Bound"/>), and fails saying what it waited for and the program's last lines.
/// </summary>
internal sealed class Lines
{
    // As many of the last lines as say what a program was doing when a wait ran out.
    private const int Shown = 20;

    private readonly Channel<string> _unread = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly List<string> _said = [];
    private readonly List<Waiter> _waiting = [];
    private readonly Lock _lock = new();
    private readonly Task _reading;
    private readonly string _program;
    private readonly Lines? _errors;
    private bool _ended;

    /// <summary>
    /// The lines of <paramref name="program"/>'s <paramref name="stream"/>; <paramref name="errors"/> are its standard
    /// error's, when this is another stream.
    /// </summary>
    public Lines(StreamReader stream, string program, Lines? errors = null)
    {
        _program = program;
        _errors = errors;
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
    public async Task<string?> ReadLineAsync(CancellationToken cancel)
    {
        using var late = new CancellationTokenSource(Bound.Line, TimeProvider.System);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancel, late.Token);
        try
        {
            return await _unread.Reader.WaitToReadAsync(bounded.Token).ConfigureAwait(false) && _unread.Reader.TryRead(out var line) ? line : null;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw Late($"wrote no line in {Bound.Line.TotalSeconds:0} s");
        }
    }

    /// <summary>
    /// The <paramref name="count"/>th line that matches of those written from this call on: what came before never counts,
    /// so a test arms it before what it waits for. It fails once the stream ends first.
    /// </summary>
    public Task<string> NextAsync(
        Func<string, bool> match, CancellationToken cancel, int count = 1, [CallerArgumentExpression(nameof(match))] string what = "") =>
        ArmAsync(match, count, [], what, cancel);

    /// <summary>As the other <c>NextAsync</c>, for a line a failure names as <paramref name="what"/>.</summary>
    public Task<string> NextAsync(Func<string, bool> match, string what, CancellationToken cancel, int count = 1) => ArmAsync(match, count, [], what, cancel);

    /// <summary>
    /// The first line that matches of all it wrote, those before this call too: what a program says once, as it starts, cannot
    /// pass before the test asks.
    /// </summary>
    public Task<string> FirstAsync(Func<string, bool> match, CancellationToken cancel, [CallerArgumentExpression(nameof(match))] string what = "") =>
        ArmAsync(match, 1, _said, what, cancel);

    /// <summary>As the other <c>FirstAsync</c>, for a line a failure names as <paramref name="what"/>.</summary>
    public Task<string> FirstAsync(Func<string, bool> match, string what, CancellationToken cancel) => ArmAsync(match, 1, _said, what, cancel);

    public async Task<string> ToEndAsync(CancellationToken cancel)
    {
        try
        {
            await _reading.WaitAsync(Bound.End, TimeProvider.System, cancel).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw Late($"did not end in {Bound.End.TotalSeconds:0} s");
        }

        return Text;
    }

    /// <summary>Why a wait for the program ran out: what it waited for, and the last lines the program wrote.</summary>
    public TimeoutException Late(string said) => new(_errors is null
        ? $"{_program} {said}. Its last lines on stderr:\n{Last()}"
        : $"{_program} {said}. Its last lines on stdout:\n{Last()}\nand on stderr:\n{_errors.Last()}");

    // A run of the same event, as the paint a trace says each second, is told as its first and last lines, so what came
    // before the run shows too.
    private string Last()
    {
        var told = new List<string>();
        lock (_lock)
        {
            var at = _said.Count - 1;
            while (at >= 0 && told.Count < Shown)
            {
                var from = at;
                while (from > 0 && Said(_said[from - 1]) == Said(_said[at]))
                {
                    from--;
                }

                told.Add(at - from < 2 ? _said[at] : $"{_said[from]}\n... {at - from - 1} more like it\n{_said[at]}");
                at = (at - from < 2 ? at : from) - 1;
            }
        }

        told.Reverse();
        return string.Join('\n', told);
    }

    // What a line says, without the time a trace's line starts with.
    private static string Said(string line) => Traced.Of(line) is { } traced ? $"{traced.Event} {traced.About}" : line;

    // What came before counts only when the test asks for it, and is read under the same lock as the lines that come.
    private Task<string> ArmAsync(Func<string, bool> match, int count, List<string> past, string what, CancellationToken cancel)
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
                return Task.FromException<string>(new InvalidOperationException($"{_program} ended before it wrote a line matching {what}."));
            }

            _waiting.Add(waiter);
        }

        return WaitAsync(waiter, what, cancel);
    }

    private async Task<string> WaitAsync(Waiter waiter, string what, CancellationToken cancel)
    {
        try
        {
            return await waiter.Done.Task.WaitAsync(Bound.Line, TimeProvider.System, cancel).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw Late($"wrote no line matching {what} in {Bound.Line.TotalSeconds:0} s");
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
        left.ForEach(waiter => waiter.Done.TrySetException(new InvalidOperationException($"{_program} ended before it wrote the line waited for:\n{Text}")));
    }

    private sealed class Waiter(Func<string, bool> match, int count)
    {
        private int _left = count;

        public TaskCompletionSource<string> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Saw(string line) => match(line) && --_left == 0;
    }
}
