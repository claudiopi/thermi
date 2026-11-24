namespace Thermi.Core;

/// <summary>
/// Lets at most one value out per interval, and always the last one: a value goes out at once when the
/// interval since the last has passed, and otherwise waits for it, replaced by any value that comes meanwhile.
/// <c>send</c> runs on the caller's thread when the value goes out at once, and on the timer's when it waited.
/// </summary>
public sealed class Throttle<T> : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Action<T> _send;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly Lock _lock = new();
    private long? _sentAt;
    private bool _waiting;
    private bool _disposed;
    private T? _next;

    public Throttle(TimeSpan interval, Action<T> send, TimeProvider time)
    {
        _interval = interval;
        _send = send;
        _time = time;
        _timer = time.CreateTimer(_ => SendWaiting(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Sends the value now or once the interval has passed; once disposed, with no timer left to wait on, always now.</summary>
    public void Post(T value)
    {
        lock (_lock)
        {
            var wait = !_disposed && _sentAt is { } sentAt ? _interval - _time.GetElapsedTime(sentAt) : TimeSpan.Zero;
            if (_waiting || wait > TimeSpan.Zero)
            {
                _next = value;
                if (!_waiting)
                {
                    _waiting = true;
                    _timer.Change(wait, Timeout.InfiniteTimeSpan);
                }

                return;
            }

            _sentAt = _time.GetTimestamp();
        }

        _send(value);
    }

    /// <summary>A value still waiting goes out now, so the last one is never lost.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }

        _timer.Dispose();
        SendWaiting();
    }

    private void SendWaiting()
    {
        T value;
        lock (_lock)
        {
            if (!_waiting)
            {
                return;
            }

            value = _next!;
            _next = default;
            _waiting = false;
            _sentAt = _time.GetTimestamp();
        }

        _send(value);
    }
}
