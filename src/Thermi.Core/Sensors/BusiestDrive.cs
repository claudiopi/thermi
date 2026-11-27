namespace Thermi.Core.Sensors;

/// <summary>
/// The drive the disk metrics follow: the one whose total activity, averaged over the last minute, is highest, chosen
/// again at most once a minute, so a burst on another drive never moves the pill. It is fed each fresh snapshot once,
/// and keeps a minute of them at most.
/// </summary>
public sealed class BusiestDrive(TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Queue<(long At, Dictionary<string, double> Activity)> _seen = new();
    private long _chosenAt;

    /// <summary>The device id of the drive followed now, as <see cref="SensorMap"/> takes it; null without drives.</summary>
    public string? Current { get; private set; }

    /// <summary>
    /// Takes a fresh snapshot, and gives the drive followed from now: the busiest of the last minute once a minute has passed
    /// since the last choice, or at once when the one followed is gone.
    /// </summary>
    public string? Add(Snapshot snapshot)
    {
        var now = time.GetTimestamp();
        var activity = SensorMap.DriveActivity(snapshot);
        _seen.Enqueue((now, activity));
        while (time.GetElapsedTime(_seen.Peek().At, now) > Window)
        {
            _seen.Dequeue();
        }

        if (Current is null || !activity.ContainsKey(Current) || time.GetElapsedTime(_chosenAt, now) >= Window)
        {
            Current = Busiest(activity.Keys);
            _chosenAt = now;
        }

        return Current;
    }

    // A drive's average leaves out the snapshots it was not in; the first of equals in the service's order wins.
    private string? Busiest(IEnumerable<string> drives) => drives
        .Select(drive => (Drive: drive, Average: _seen.Where(seen => seen.Activity.ContainsKey(drive)).Average(seen => seen.Activity[drive])))
        .OrderByDescending(drive => drive.Average)
        .Select(drive => drive.Drive)
        .FirstOrDefault();
}

/// <summary>The network adapter the network metrics follow, among a snapshot's.</summary>
internal static class BusiestAdapter
{
    // The adapter that has moved the most data, as the library counts it: a physical one while any moves data, so a VPN
    // or a virtual switch, which carries the same traffic again, never wins; any one when none does.
    // In one pass, without grouping, since it runs with every pick: of equal ones, the first listed wins.
    public static string? Of(Snapshot snapshot)
    {
        var moved = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var sensor in snapshot.Sensors)
        {
            if (sensor is { HardwareType: "Network", Type: nameof(SensorKind.Data) })
            {
                var id = SensorMap.DeviceIdOf(sensor);
                moved[id] = moved.GetValueOrDefault(id) + (sensor.Value is { } value && double.IsFinite(value) ? value : 0);
            }
        }

        var virtuals = snapshot.VirtualAdapters ?? [];
        var physicalMoves = moved.Any(adapter => adapter.Value > 0 && !virtuals.Contains(adapter.Key, StringComparer.Ordinal));
        string? busiest = null;
        var most = double.NegativeInfinity;
        foreach (var (id, data) in moved)
        {
            if (data > most && !(physicalMoves && virtuals.Contains(id, StringComparer.Ordinal)))
            {
                (busiest, most) = (id, data);
            }
        }

        return busiest;
    }
}
