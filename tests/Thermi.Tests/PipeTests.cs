using System.Text;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core;

namespace Thermi.Tests;

/// <summary>What the overlay makes of the bytes the sensor host sends, however they arrive.</summary>
public sealed class PipeTests
{
    private static readonly Snapshot Fan = new([new RawSensor("/lpc/it8689e/0/fan/0", "SuperIO", "ITE IT8689E", "Fan", "Fan #1", 953)]);
    private static readonly Snapshot Accented = new([new RawSensor("/a", "Cpu", "Ryzen ñandú", "Load", "Carga °", 7)]);

    private static byte[] Line(Snapshot snapshot) => Encoding.UTF8.GetBytes(SnapshotJson.Write(snapshot) + "\n");

    [Fact]
    public void ALineSplitAcrossReadsArrivesWhole()
    {
        var bytes = Line(Fan);
        var lines = new SnapshotLines();

        Assert.Empty(lines.Add(bytes.AsSpan(0, 10)));
        var snapshot = Assert.Single(lines.Add(bytes.AsSpan(10)));
        Assert.Equal(Fan.Sensors, snapshot.Sensors);
    }

    [Fact]
    public void ACharacterSplitAcrossReadsSurvives()
    {
        var bytes = Line(Accented);
        var split = Array.IndexOf(bytes, (byte)0xC3) + 1;
        var lines = new SnapshotLines();

        Assert.Empty(lines.Add(bytes.AsSpan(0, split)));
        Assert.Equal("Ryzen ñandú", Assert.Single(lines.Add(bytes.AsSpan(split))).Sensors[0].Hardware);
    }

    [Fact]
    public void SeveralLinesInOneReadAllArrive()
    {
        var bytes = Line(Fan).Concat(Line(Accented)).Concat(Encoding.UTF8.GetBytes("garbage\n")).ToArray();

        Assert.Equal(2, new SnapshotLines().Add(bytes).Count);
    }

    [Fact]
    public void ALineWithoutItsEndWaits()
    {
        var bytes = Line(Fan);

        Assert.Empty(new SnapshotLines().Add(bytes.AsSpan(0, bytes.Length - 1)));
    }

    [Fact]
    public void AnEndlessLineMeansTheOtherEndIsNotASensorHost()
    {
        var lines = new SnapshotLines();
        var chunk = Encoding.UTF8.GetBytes(new string('x', 64 * 1024));

        Assert.Throws<InvalidDataException>(() =>
        {
            for (var i = 0; i < (SnapshotJson.MaxLineLength / chunk.Length) + 2; i++)
            {
                lines.Add(chunk);
            }
        });
    }

    [Fact]
    public void AHostThatGoesQuietStopsShowingNumbers()
    {
        var clock = new FakeTimeProvider();
        var latest = new LatestSnapshot(clock);

        latest.Set(Fan);
        clock.Advance(LatestSnapshot.MaxAge);
        Assert.Same(Fan, latest.Current);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(latest.Current);
    }

    [Fact]
    public void ADroppedConnectionClearsTheSnapshotAtOnce()
    {
        var latest = new LatestSnapshot(new FakeTimeProvider());

        latest.Set(Fan);
        latest.Clear();

        Assert.Null(latest.Current);
    }

    [Theory]
    [InlineData("{\"sensors\":[{\"hardwareType\":\"SuperIO\",\"hardware\":\"x\",\"type\":\"Fan\",\"name\":\"CPU Fan\",\"value\":1}]}")]
    [InlineData("{\"sensors\":[{\"id\":\"/a\",\"hardwareType\":\"SuperIO\",\"hardware\":\"x\",\"type\":\"Fan\",\"value\":1}]}")]
    public void ASensorWithMissingFieldsIsNotASnapshot(string line)
    {
        Assert.Null(SnapshotJson.Read(line));
    }

    [Fact]
    public void AFanSourceThatIsNotAFanHasNoDutyMeter()
    {
        var snapshot = new Snapshot([new RawSensor("/lpc/it8689e/0/control/0", "SuperIO", "ITE IT8689E", "Control", "Fan #1", 42)]);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal) { [Metrics.CpuFan.Id] = "/lpc/it8689e/0/control/0" };

        Assert.Null(SensorMap.Read(snapshot, sources)[Metrics.CpuFanDuty.Id].Value);
    }
}
