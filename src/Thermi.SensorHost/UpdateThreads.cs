namespace Thermi.SensorHost;

/// <summary>
/// The threads the library's devices are updated on, one update at a time, on a thread of the service's own and never the
/// pool's: an update may block in a driver up to the read limit, and the pool's threads serve the pipe. One still running
/// when the next device's is asked, as a board whose bus another program holds, keeps its thread, which ends once it
/// returns, and the next goes to a new one; so a stall ties up one thread, and the second it ends.
/// </summary>
internal sealed class UpdateThreads : IDisposable
{
    private Worker? _worker;

    /// <summary>Runs <paramref name="update"/> on the thread of the updates, or on a new one when it is still busy.</summary>
    public Task Run(Action update)
    {
        if (_worker is not { Idle: true })
        {
            _worker?.Retire();
            _worker = new Worker();
        }

        return _worker.Run(update);
    }

    public void Dispose() => _worker?.Retire();

    private sealed class Worker
    {
        private readonly object _gate = new();
        private (Action Update, TaskCompletionSource Done)? _next;
        private bool _retired;
        private int _idle = 1;

        public Worker()
        {
            new Thread(Loop) { IsBackground = true, Name = "thermi device updates" }.Start();
        }

        public bool Idle => Volatile.Read(ref _idle) == 1;

        public Task Run(Action update)
        {
            // Its awaiter goes on elsewhere, so this thread is free for the next update as soon as this one returns.
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _idle, 0);
            lock (_gate)
            {
                _next = (update, done);
                Monitor.Pulse(_gate);
            }

            return done.Task;
        }

        public void Retire()
        {
            lock (_gate)
            {
                _retired = true;
                Monitor.Pulse(_gate);
            }
        }

        // Idle again before the update's task completes, so whoever awaits it finds this thread free.
        private void Loop()
        {
            while (Next() is { } next)
            {
                var run = new Task(next.Update);
                run.RunSynchronously();
                Volatile.Write(ref _idle, 1);
                next.Done.SetFromTask(run);
            }
        }

        private (Action Update, TaskCompletionSource Done)? Next()
        {
            lock (_gate)
            {
                while (_next is null && !_retired)
                {
                    Monitor.Wait(_gate);
                }

                var next = _next;
                _next = null;
                return next;
            }
        }
    }
}
