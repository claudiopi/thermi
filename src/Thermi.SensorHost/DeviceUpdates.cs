using Thermi.Core.Sensors;

namespace Thermi.SensorHost;

/// <summary>How a device's sensors go out in a read.</summary>
internal enum DeviceRead
{
    Updated,

    /// <summary>Its last values, still fresh: not due, or late for less than a snapshot's age limit.</summary>
    Held,

    /// <summary>As missing: late for longer than a snapshot's age limit, or still updating for the first time.</summary>
    Missing,

    /// <summary>Left out: its update failed, and it never read.</summary>
    Failed,
}

/// <summary>A device to read, by its id and its name: how to update it, and whether its pace has it updated now.</summary>
internal sealed record DeviceUpdate(string Id, string Name, Func<Task> Update, bool Due);

/// <summary>
/// Updates a source's devices one after another, as the library does, waiting for each at most its patience: one still
/// updating then, as a board whose bus another program holds, is left to finish and holds back none after it, even when
/// the read is stopped. Until it returns it is not started again. A device is late from when an update of it came due and
/// did not return within the patience, or failed, until one returns well; its last values go out while it is late by no
/// more than a snapshot may be old, then as missing, so a drive read every few seconds keeps them between its reads and
/// for as long after a read that failed. See docs/adr/0015-a-stalled-device-holds-back-none.md. An update that took
/// longer than its patience, or failed, is told to <c>slow</c> once it returns, with its failure, at most once a minute a
/// device; one still running past <see cref="SensorPipeServer.ReadLimit"/> calls <c>hung</c>.
/// </summary>
internal sealed class DeviceUpdates(TimeProvider time, Action<string, TimeSpan, Exception?> slow, Action hung, TimeSpan patience)
{
    /// <summary>How long the service waits for each device; a dump waits for every one, however long.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan SaidEvery = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, Running> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _updated = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _late = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _said = new(StringComparer.Ordinal);

    /// <summary>Whether an update is still running, which closing the library or scanning it again must not pull from under.</summary>
    public bool Busy => _running.Values.Any(running => !running.Update.IsCompleted);

    public async Task<IReadOnlyDictionary<string, DeviceRead>> UpdateAsync(IReadOnlyList<DeviceUpdate> devices, CancellationToken stop)
    {
        Returned();
        var read = new Dictionary<string, DeviceRead>(StringComparer.Ordinal);
        foreach (var device in devices)
        {
            stop.ThrowIfCancellationRequested();
            read[device.Id] = !_running.ContainsKey(device.Id) && device.Due ? await UpdateAsync(device, stop) : Kept(device.Id);
        }

        return read;
    }

    private async Task<DeviceRead> UpdateAsync(DeviceUpdate device, CancellationToken stop)
    {
        var began = time.GetTimestamp();
        var update = device.Update();

        // Drivers fail in many ways (a GPU driver reset, a busy bus): one device's failure only holds its own values back.
        await update.WaitAsync(patience, time, stop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!update.IsCompleted)
        {
            _running[device.Id] = new Running(device.Name, update, began, time);
            _late.TryAdd(device.Id, began);
            return Kept(device.Id);
        }

        var ended = time.GetTimestamp();
        // One that never read, as a device the library lists but cannot read, is left out every second without a word.
        if (!update.IsCompletedSuccessfully)
        {
            if (!_updated.ContainsKey(device.Id))
            {
                _ = update.Exception;
                return DeviceRead.Failed;
            }

            _late.TryAdd(device.Id, began);
            Say(device.Id, device.Name, time.GetElapsedTime(began, ended), update.Exception, ended);
            return Kept(device.Id);
        }

        Read(device.Id, ended);
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
            if (running.Update.IsCompletedSuccessfully)
            {
                Read(id, ended);
            }

            Say(id, running.Name, time.GetElapsedTime(running.Began, ended), running.Update.Exception, ended);
        }
    }

    private void Read(string id, long ended)
    {
        _updated[id] = ended;
        _late.Remove(id);
    }

    // Reading the failure observes it, so it is no unobserved task exception; a task's own wraps the update's.
    private void Say(string id, string name, TimeSpan took, AggregateException? failed, long ended)
    {
        if (!_said.TryGetValue(id, out var said) || time.GetElapsedTime(said, ended) >= SaidEvery)
        {
            _said[id] = ended;
            slow(name, took, failed?.InnerException ?? failed);
        }
    }

    // What a device not updated now sends: its last values while it is not late, or late by no more than a snapshot may be old.
    private DeviceRead Kept(string id) =>
        !_updated.ContainsKey(id) ? (_running.ContainsKey(id) ? DeviceRead.Missing : DeviceRead.Failed)
        : !_late.TryGetValue(id, out var late) || time.GetElapsedTime(late) <= LatestSnapshot.MaxAge ? DeviceRead.Held
        : DeviceRead.Missing;

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
