using Thermi.Core.Sensors;

namespace Thermi.SensorHost;

/// <summary>How a device's sensors go out in a read.</summary>
internal enum DeviceRead
{
    Updated,

    /// <summary>Its last values, still fresh: not due, or still updating since less than a snapshot's age limit.</summary>
    Held,

    /// <summary>As missing: still updating, and its last values past a snapshot's age limit.</summary>
    Missing,

    /// <summary>Left out: its update failed.</summary>
    Failed,
}

/// <summary>A device to read, by its id and its name: how to update it, and whether its pace has it updated now.</summary>
internal sealed record DeviceUpdate(string Id, string Name, Func<Task> Update, bool Due);

/// <summary>
/// Updates a source's devices one after another, as the library does, waiting for each at most <see cref="Patience"/>: one
/// still updating then, as a board whose bus another program holds, is left to finish and holds back none after it. Until
/// it returns it is not started again, and its last values go out while they are no older than a snapshot may be, then as
/// missing. See docs/adr/0015-a-stalled-device-holds-back-none.md. An update that took longer than its patience is told to
/// <c>slow</c> once it returns, at most once a minute a device; one still running past
/// <see cref="SensorPipeServer.ReadLimit"/> calls <c>hung</c>.
/// </summary>
internal sealed class DeviceUpdates(TimeProvider time, Action<string, TimeSpan> slow, Action hung)
{
    public static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan SaidEvery = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, Running> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _updated = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _said = new(StringComparer.Ordinal);

    /// <summary>Whether an update is still running, which closing the library must not pull from under.</summary>
    public bool Busy => _running.Values.Any(running => !running.Update.IsCompleted);

    public async Task<IReadOnlyDictionary<string, DeviceRead>> UpdateAsync(IReadOnlyList<DeviceUpdate> devices, CancellationToken stop)
    {
        Returned();
        var read = new Dictionary<string, DeviceRead>(StringComparer.Ordinal);
        foreach (var device in devices)
        {
            read[device.Id] = _running.ContainsKey(device.Id) ? Aged(device.Id)
                : device.Due ? await UpdateAsync(device, stop)
                : DeviceRead.Held;
        }

        return read;
    }

    private async Task<DeviceRead> UpdateAsync(DeviceUpdate device, CancellationToken stop)
    {
        var began = time.GetTimestamp();
        var update = device.Update();

        // Drivers fail in many ways (a GPU driver reset, a busy bus): one device's failure only leaves it out.
        await update.WaitAsync(Patience, time, stop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        stop.ThrowIfCancellationRequested();
        if (!update.IsCompleted)
        {
            _running[device.Id] = new Running(device.Name, update, began, time);
            return Aged(device.Id);
        }

        if (!update.IsCompletedSuccessfully)
        {
            // Seen, so it is no unobserved failure.
            _ = update.Exception;
            return DeviceRead.Failed;
        }

        _updated[device.Id] = time.GetTimestamp();
        return DeviceRead.Updated;
    }

    // Updates left running that have returned since: told when slow, and fresh again if they did not fail.
    private void Returned()
    {
        foreach (var (id, running) in _running.ToList())
        {
            if (!running.Update.IsCompleted)
            {
                if (time.GetElapsedTime(running.Began) > SensorPipeServer.ReadLimit)
                {
                    hung();
                }

                continue;
            }

            // Its own stamp may not be in yet, as it is set from the thread the update returned on.
            var ended = running.Ended ?? time.GetTimestamp();
            _running.Remove(id);
            _ = running.Update.Exception;
            if (running.Update.IsCompletedSuccessfully)
            {
                _updated[id] = ended;
            }

            if (!_said.TryGetValue(id, out var said) || time.GetElapsedTime(said) >= SaidEvery)
            {
                _said[id] = ended;
                slow(running.Name, time.GetElapsedTime(running.Began, ended));
            }
        }
    }

    private DeviceRead Aged(string id) =>
        _updated.TryGetValue(id, out var at) && time.GetElapsedTime(at) <= LatestSnapshot.MaxAge ? DeviceRead.Held : DeviceRead.Missing;

    // When the update left running returned, on the clock it began on; set from the thread it returns on.
    private sealed class Running
    {
        private long _ended = -1;

        public Running(string name, Task update, long began, TimeProvider time)
        {
            Name = name;
            Update = update;
            Began = began;
            _ = update.ContinueWith(
                _ => Interlocked.Exchange(ref _ended, time.GetTimestamp()), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        public string Name { get; }

        public Task Update { get; }

        public long Began { get; }

        public long? Ended => Interlocked.Read(ref _ended) is var ended and >= 0 ? ended : null;
    }
}
