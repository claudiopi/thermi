using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Frames;

/// <summary>
/// The sensors of a device of thermi's own, the game, that every reader adds to each snapshot as it adds the PC's
/// (<see cref="SystemSensors"/>): the FPS, frame time and lows of the app in front, found among the frame slots by its
/// process or as the parent of the one that presents for it, as a browser's. They read nothing while nothing in front
/// presents, not a number while the service cannot read frames, and minus infinity while its host has no rights to, so the
/// window can say which; the lows read plus infinity from a service of an earlier version, which sends none.
/// </summary>
public static class GameSensors
{
    public const string HardwareType = "Game";

    public const string Fps = "FPS";

    public const string FrameTime = "Frame time";

    public const string OnePercentLow = "1% low";

    public const string PointOnePercentLow = "0.1% low";

    /// <summary>What the game's sensors read from a host with no rights to trace presents: no number, and not NaN either.</summary>
    public const double NoRights = double.NegativeInfinity;

    /// <summary>What the lows read from a service of an earlier version, which sends FPS but no lows: no number, nor any other.</summary>
    public const double NoLows = double.PositiveInfinity;

    public const string OnePercentLowId = "/game/0/framerate/1";

    public const string PointOnePercentLowId = "/game/0/framerate/2";

    private const string FpsId = "/game/0/framerate/0";

    private const string FrameTimeId = "/game/0/frametime/0";

    /// <summary>The snapshot with its frame slots turned into the game's sensors, for <paramref name="front"/>, the game's process, if any.</summary>
    public static Snapshot With(Snapshot snapshot, int? front)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rates = FrameSlots.Read(snapshot);
        var rate = front is { } process && rates is not null ? RateOf(rates, process) : null;
        var none = rates is not null ? (double?)null : FrameSlots.NeedRights(snapshot) ? NoRights : double.NaN;
        return snapshot with
        {
            Sensors =
            [
                .. snapshot.Sensors.Where(sensor => !FrameSlots.Is(sensor)),
                Sensor(FpsId, SensorKind.FrameRate, Fps, rate?.Fps ?? none),
                Sensor(FrameTimeId, SensorKind.FrameTime, FrameTime, rate?.FrameTime ?? none),
                Sensor(OnePercentLowId, SensorKind.FrameRate, OnePercentLow, rate?.OnePercentLow ?? none),
                Sensor(PointOnePercentLowId, SensorKind.FrameRate, PointOnePercentLow, rate?.PointOnePercentLow ?? none),
            ],
        };
    }

    public static bool Is(RawSensor sensor) => sensor.HardwareType == HardwareType;

    /// <summary>Which low an item reads, by its metric or its sensor, as the tag that tells it from FPS; null for any other item.</summary>
    public static OverlayText? LowOf(ItemConfig item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var read = item.Sensor ?? item.Metric ?? item.Auto;
        return read == OnePercentLowId || read == Metrics.OnePercentLow.Id ? OverlayText.OnePercentLowTag
            : read == PointOnePercentLowId || read == Metrics.PointOnePercentLow.Id ? OverlayText.PointOnePercentLowTag
            : null;
    }

    /// <summary>The frames of a process, its own or else of one it started, as a browser's, or null when neither presents.</summary>
    public static FrameRate? RateOf(IReadOnlyList<FrameRate> rates, int process) =>
        rates.Where(each => each.Process == process).Concat(rates.Where(each => each.Parent == process)).Cast<FrameRate?>().FirstOrDefault();

    private static RawSensor Sensor(string id, SensorKind kind, string name, double? value) =>
        new(id, HardwareType, HardwareType, kind.ToString(), name, value);
}
