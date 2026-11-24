using System.Buffers;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;

namespace Thermi.Tests;

/// <summary>What the overlay makes of the bytes the sensor host sends, however they arrive.</summary>
public sealed class PipeTests
{
    private static readonly Snapshot Fan = new([new RawSensor("/lpc/it8689e/0/fan/0", "SuperIO", "ITE IT8689E", "Fan", "Fan #1", 953)]);
    private static readonly Snapshot Accented = new([new RawSensor("/a", "Cpu", "Ryzen ñandú", "Load", "Carga °", 7)]);

    // As the service writes it.
    private static byte[] Line(Snapshot snapshot)
    {
        var line = new ArrayBufferWriter<byte>();
        SnapshotJson.WriteLine(snapshot, line);
        return line.WrittenSpan.ToArray();
    }

    // Split after its first bytes, inside a character of two bytes, or before its end alone.
    [Theory]
    [InlineData(false, "10")]
    [InlineData(true, "in ñ")]
    [InlineData(false, "before the end")]
    public void ALineSplitAcrossReadsArrivesWhole(bool accented, string at)
    {
        var sent = accented ? Accented : Fan;
        var bytes = Line(sent);
        var split = at switch
        {
            "in ñ" => Array.IndexOf(bytes, (byte)0xC3) + 1,
            "before the end" => bytes.Length - 1,
            _ => 10,
        };
        var lines = new SnapshotLines();

        Assert.Empty(lines.Add(bytes.AsSpan(0, split)));
        var snapshot = Assert.Single(lines.Add(bytes.AsSpan(split)));
        Assert.Equal(sent.Sensors, snapshot.Sensors);
    }

    [Fact]
    public void SeveralLinesInOneReadAllArrive()
    {
        var bytes = Line(Fan).Concat(Line(Accented)).Concat(Encoding.UTF8.GetBytes("garbage\n")).ToArray();

        Assert.Equal(2, new SnapshotLines().Add(bytes).Count);
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

    // R45, R46: readings awaited count as running for as long as a snapshot may take; then the service is stale while its
    // pipe is there, stopped once it is gone, and running again once a snapshot comes.
    [Fact]
    public void TheServiceIsStoppedWithoutItsPipeAndStaleWithoutFreshReadings()
    {
        var clock = new FakeTimeProvider();
        var latest = new LatestSnapshot(clock);
        var states = new List<ServiceState> { latest.State };

        clock.Advance(LatestSnapshot.MaxAge + TimeSpan.FromMilliseconds(1));
        states.Add(latest.State);
        latest.Connected();
        states.Add(latest.State);
        latest.Set(Fan);
        clock.Advance(LatestSnapshot.MaxAge + TimeSpan.FromMilliseconds(1));
        states.Add(latest.State);
        latest.Lost();
        states.Add(latest.State);
        clock.Advance(TimeSpan.FromMinutes(1));
        states.Add(latest.State);
        latest.Connected();
        states.Add(latest.State);
        latest.Set(Fan);
        states.Add(latest.State);
        var fresh = latest.Current;
        latest.Lost();
        states.Add(latest.State);

        Assert.Equal(
            [ServiceState.Running, ServiceState.Stale, ServiceState.Running, ServiceState.Stale, ServiceState.Stopped, ServiceState.Stopped, ServiceState.Running, ServiceState.Running, ServiceState.Stopped],
            states);
        Assert.Same(Fan, fresh);
        Assert.Null(latest.Current);
    }
}
