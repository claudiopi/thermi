using Thermi.Core.Sensors;
using Thermi.Sensors.Linux.Kernel;

namespace Thermi.Sensors.Linux.Reading;

/// <summary>
/// How often each of the board's chips is read: each second, but one whose read costs the CPU more than
/// <see cref="Costly"/> is read again only once that cost, spread over the seconds since, is <see cref="Share"/> of one
/// core, and its last values are sent between. A Super I/O chip's driver reads every register of it again once its values
/// are a second and a half old, through a bus that may take a tenth of a millisecond for each: 96 ms on an ASUS board's
/// NCT6798D, which, read each second, was most of the service's CPU. Its values were only as fresh as that refresh anyway.
/// The chips found anew start each second again.
/// </summary>
internal sealed class ChipPace
{
    public static readonly TimeSpan Costly = TimeSpan.FromMilliseconds(10);

    public const double Share = 0.01;

    // Each costly chip's last sensors, and when it is read again, in seconds on the kernel's clock that only goes forward.
    private readonly Dictionary<Chip, (double Due, IReadOnlyList<RawSensor> Last)> _held = [];

    /// <summary>
    /// <paramref name="chip"/>'s sensors at <paramref name="at"/>: read now, or a costly chip's last ones until it is due.
    /// A replay with no clock reads each second.
    /// </summary>
    public IReadOnlyList<RawSensor> Read(IKernel kernel, Chip chip, double? at, Func<IReadOnlyList<RawSensor>> read)
    {
        if (at is not { } now)
        {
            return read();
        }

        if (_held.TryGetValue(chip, out var held) && now < held.Due)
        {
            return held.Last;
        }

        var before = kernel.Spent();
        var sensors = read();
        var cost = kernel.Spent() - before;
        if (cost > Costly)
        {
            _held[chip] = (now + (cost.TotalSeconds / Share), sensors);
        }
        else
        {
            _held.Remove(chip);
        }

        return sensors;
    }
}
