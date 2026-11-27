using System.Globalization;
using Thermi.Core.Sensors;

namespace Thermi.Core.Frames;

/// <summary>
/// The sensors of a device of thermi's own, the game, that every reader adds to each snapshot as it adds the PC's
/// (<see cref="SystemSensors"/>): the FPS, frame time and lows of the app in front, found among the frame slots by its
/// process or as the parent of the one that presents for it, as a browser's. They read nothing while nothing in front
/// presents, not a number while the service cannot read frames, and minus infinity while its host has no rights to, so the
/// window can say which; the lows read not a number too from a service of an earlier version, which sends none.
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

    private const string Device = "/game/0";

    /// <summary>The snapshot with its frame slots turned into the game's sensors, for <paramref name="front"/>, the game's process, if any.</summary>
    public static Snapshot With(Snapshot snapshot, int? front)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rates = FrameSlots.Read(snapshot);
        var rate = front is { } process && rates is not null ? RateOf(rates, process) : null;
        var none = rates is not null ? (double?)null : FrameSlots.NeedRights(snapshot) ? NoRights : double.NaN;
        var noLows = rate is null ? none : double.NaN;
        return snapshot with
        {
            Sensors =
            [
                .. snapshot.Sensors.Where(sensor => !FrameSlots.Is(sensor)),
                Sensor(SensorKind.FrameRate, 0, Fps, rate?.Fps ?? none),
                Sensor(SensorKind.FrameTime, 0, FrameTime, rate?.FrameTime ?? none),
                Sensor(SensorKind.FrameRate, 1, OnePercentLow, rate?.OnePercentLow ?? noLows),
                Sensor(SensorKind.FrameRate, 2, PointOnePercentLow, rate?.PointOnePercentLow ?? noLows),
            ],
        };
    }

    public static bool Is(RawSensor sensor) => sensor.HardwareType == HardwareType;

    /// <summary>The frames of a process, its own or else of one it started, as a browser's, or null when neither presents.</summary>
    public static FrameRate? RateOf(IReadOnlyList<FrameRate> rates, int process) =>
        rates.Where(each => each.Process == process).Concat(rates.Where(each => each.Parent == process)).Cast<FrameRate?>().FirstOrDefault();

    private static RawSensor Sensor(SensorKind kind, int index, string name, double? value) => new(
        string.Create(CultureInfo.InvariantCulture, $"{Device}/{kind.ToString().ToLowerInvariant()}/{index}"),
        HardwareType,
        HardwareType,
        kind.ToString(),
        name,
        value);
}
