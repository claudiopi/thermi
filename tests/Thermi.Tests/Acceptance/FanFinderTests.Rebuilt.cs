using Thermi.Core.Sensors;

namespace Thermi.Tests.Acceptance;

// The fan finder's series rebuilt from runs on the owner's PC, a second at a time.
public sealed partial class FanFinderTests
{
    /// <summary>
    /// QA round 5, run 2 on the owner's PC, rebuilt from the window's list a second at a time: the CPU fan rose from 545 to
    /// 739 RPM, 36 % but 194 RPM, and the finder, which then asked for 200 RPM, said no fan rose.
    /// </summary>
    private static List<Snapshot> RisingAThird
    {
        get
        {
            double[] fan =
            [
                545, 545, 544, 546, 545, 546, 545, 543, 543, 544, 553, 601, 618, 624, 628, 628, 631, 630, 627, 631, 628, 630, 628, 638, 694, 722,
                732, 734, 736, 734, 734, 734, 736, 735, 735, 735, 739, 738, 739, 739, 739, 735, 736, 728, 664, 594, 563, 553, 550, 546, 545, 545,
            ];
            double[] load = [2, 3, 3, 1, 2, 88, .. Enumerable.Repeat(100.0, 29), 13, 3, 3, 2, 2, 2, 4, 5, 4, 4, 4, 6, 8, 3, 2, 1, 2];
            return Derived(Derived([.. Enumerable.Repeat(Desktop, fan.Length)], BoardFan, (_, at) => fan[at]), CpuTotal, (_, at) => load[at]);
        }
    }

    /// <summary>
    /// QA round 6, run 1 on the owner's PC, rebuilt from the window's list a second at a time: the CPU fan was still up from
    /// an earlier load, rising to 748 RPM before this one, held there through it, and fell to 545 RPM 25 s after it, while
    /// the finder, which then took only a rise, said no fan sped up.
    /// </summary>
    private static List<Snapshot> HeldFromALoad
    {
        get
        {
            double[] fan =
            [
                683, 728, 742, 746, 748, 748, 749, 749, 750, 749, 748, 748, 749, 750, 749, 748, 748, 747, 748, 747, 750, 749, 750, 748, 748, 748,
                748, 749, 748, 748, 748, 748, 748, 748, 750, 752, 751, 750, 748, 748, 749, 751, 748, 748, 750, 750, 753, 752, 749, 750, 749, 752,
                752, 753, 751, 749, 752, 748, 702, 620, 573, 555, 549, 547, 546, 548, 547, 548, 546, 547, 546, 545, 547, 546, 544,
            ];
            double[] load =
            [
                9, 4, 7, 7, 6, 85, .. Enumerable.Repeat(100.0, 28), 12, 7, 4, 4, 8, 8, 7, 10, 7, 9, 7, 7, 10, 8, 10, 15, 9, 7, 3, 2, 1, 3, 2, 2, 2,
                3, 2, 3, 1, 4, 2, 2, 3, 1, 2, 2, 4, 4, 0, 1, 3,
            ];
            return Derived(Derived([.. Enumerable.Repeat(Desktop, fan.Length)], BoardFan, (_, at) => fan[at]), CpuTotal, (_, at) => load[at]);
        }
    }

    /// <summary>
    /// QA round 7 on the owner's PC, rebuilt from the window's charts: a game held the CPU at 85 to 91 °C and its power at
    /// 142 W, its limit, so the load cooled it a little, and the CPU fan fell from 1,520 to 1,234 RPM through it and rose
    /// only 44 RPM after it: nothing the finder could pick, but not for want of a fan that answers. As a contrast, the same
    /// fan with the CPU starting from 45 °C and 40 W, which the load heats as it would an idle one.
    /// </summary>
    private static List<Snapshot> AlreadyHot(bool heated)
    {
        double[] fan =
        [
            1520, 1518, 1521, 1519, 1520, 1480, 1430, 1390, 1350, 1320, 1300, 1285, 1270, 1260, 1252, 1246, 1240, 1238, 1236, 1235,
            .. Enumerable.Repeat(1234.0, 15), 1240, 1252, 1265, 1272, 1278, .. Enumerable.Repeat(1278.0, Watched - 5),
        ];
        double[] load = [35, 38, 33, 36, 34, 88, .. Enumerable.Repeat(100.0, 29), .. Enumerable.Repeat(35.0, Watched)];
        double[] temperature = [.. load.Select((each, at) => heated && each < 100 ? 45 : at % 3 == 0 ? 91 : 86)];
        double[] power = [.. load.Select(each => heated && each < 100 ? 40 : 142.0)];
        var series = Derived(Derived([.. Enumerable.Repeat(Desktop, fan.Length)], BoardFan, (_, at) => fan[at]), CpuTotal, (_, at) => load[at]);
        return Derived(Derived(series, CpuTemperature, (_, at) => temperature[at]), CpuPower, (_, at) => power[at]);
    }
}
