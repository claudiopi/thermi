using Thermi.Core.Sensors;

namespace Thermi.Core.Frames;

/// <summary>
/// The sensors of a device of thermi's own, the game, that every reader adds to each snapshot as it adds the PC's
/// (<see cref="SystemSensors"/>): the FPS and frame time of the app in front, found among the frame slots by its process
/// or as the parent of the one that presents for it, as a browser's. They read nothing while nothing in front presents,
/// and not a number while the service cannot read frames, so the window can say which.
/// </summary>
public static class GameSensors
{
    public const string HardwareType = "Game";

    public const string Fps = "FPS";

    public const string FrameTime = "Frame time";

    private const string Device = "/game/0";

    /// <summary>The snapshot with its frame slots turned into the game's sensors, for <paramref name="front"/>, the process in front, if any.</summary>
    public static Snapshot With(Snapshot snapshot, int? front)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rates = FrameSlots.Read(snapshot);
        var rate = front is { } process && rates is not null
            ? rates.Where(each => each.Process == process).Concat(rates.Where(each => each.Parent == process)).Cast<FrameRate?>().FirstOrDefault()
            : null;
        var none = rates is null ? double.NaN : (double?)null;
        return snapshot with
        {
            Sensors =
            [
                .. snapshot.Sensors.Where(sensor => !FrameSlots.Is(sensor)),
                Sensor(SensorKind.FrameRate, Fps, rate?.Fps ?? none),
                Sensor(SensorKind.FrameTime, FrameTime, rate?.FrameTime ?? none),
            ],
        };
    }

    public static bool Is(RawSensor sensor) => sensor.HardwareType == HardwareType;

    private static RawSensor Sensor(SensorKind kind, string name, double? value) =>
        new($"{Device}/{kind.ToString().ToLowerInvariant()}/0", HardwareType, HardwareType, kind.ToString(), name, value);
}
