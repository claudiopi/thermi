using System.Buffers;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Pipe;
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

    // Values with no whole snapshot before them, or not one per sensor of the last, are no snapshot.
    [Fact]
    public void SeveralLinesInOneReadAllArrive()
    {
        var bytes = Encoding.UTF8.GetBytes("[953]\n")
            .Concat(Line(Fan))
            .Concat(Line(Accented))
            .Concat(Encoding.UTF8.GetBytes("garbage\n[1,2]\n[8]\n"))
            .ToArray();

        var snapshots = new SnapshotLines().Add(bytes);

        Assert.Equal([953, 7, 8], snapshots.Select(snapshot => snapshot.Sensors.Single().Value));
    }

    // A client gets the whole snapshot when it connects and when the sensors change, their values alone in between, and
    // makes the same snapshots of them as of whole ones: a recorded series, then a sensor renamed, reading nothing and NaN.
    [Fact]
    public void EachClientGetsTheWholeSnapshotOnceAndThenOnlyItsValues()
    {
        var series = TestFiles.Series("ryzen-9950x-rtx-5060-ti-cpu-load-fan-software.jsonl");
        var last = series[^1];
        var renamed = last with
        {
            Sensors =
            [
                .. last.Sensors.Select((sensor, at) => at switch
                {
                    0 => sensor with { Name = "Renamed" },
                    1 => sensor with { Value = null },
                    2 => sensor with { Value = double.NaN },
                    _ => sensor,
                }),
            ],
        };
        List<Snapshot> sent = [.. series, renamed, renamed];
        var writer = new SnapshotWriter();
        var clients = new[]
        {
            (From: 0, Lines: new SnapshotLines(), Had: 0, Got: new List<Snapshot>(), Bytes: new List<int>()),
            (From: series.Count / 2, Lines: new SnapshotLines(), Had: 0, Got: [], Bytes: []),
        };

        for (var second = 0; second < sent.Count; second++)
        {
            writer.Next(sent[second], sending: second % 2 == 0);
            for (var at = 0; at < clients.Length; at++)
            {
                if (second >= clients[at].From)
                {
                    var line = writer.LineFor(clients[at].Had);
                    clients[at].Had = writer.Catalog;
                    clients[at].Bytes.Add(line.Length);
                    clients[at].Got.AddRange(clients[at].Lines.Add(line.Span));
                }
            }
        }

        foreach (var (from, _, _, got, bytes) in clients)
        {
            Assert.Equal(sent.Skip(from).Select(snapshot => snapshot.Sensors), got.Select(snapshot => snapshot.Sensors));
            var whole = Line(series[0]).Length;
            Assert.Equal([0, sent.Count - 2 - from], bytes.Select((size, at) => (size, at)).Where(line => line.size * 4 > whole).Select(line => line.at));
        }
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

    // Whatever the wall clock does meanwhile: set back an hour, as by a person or a time sync, frozen numbers would look
    // live for that hour.
    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void AHostThatGoesQuietStopsShowingNumbers(int wallMinutes)
    {
        var clock = new WallShifted(new FakeTimeProvider());
        var latest = new LatestSnapshot(clock);

        latest.Set(Fan);
        clock.Shift = TimeSpan.FromMinutes(wallMinutes);
        clock.Fake.Advance(LatestSnapshot.MaxAge);
        Assert.Same(Fan, latest.Current);

        clock.Fake.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(latest.Current);
    }

    /// <summary>A fake clock whose wall time is moved by <see cref="Shift"/>, while its counter runs on.</summary>
    private sealed class WallShifted(FakeTimeProvider fake) : TimeProvider
    {
        public FakeTimeProvider Fake => fake;

        public TimeSpan Shift { get; set; }

        public override DateTimeOffset GetUtcNow() => fake.GetUtcNow() + Shift;

        public override long GetTimestamp() => fake.GetTimestamp();

        public override long TimestampFrequency => fake.TimestampFrequency;
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
            [
                ServiceState.Running,
                ServiceState.Stale,
                ServiceState.Running,
                ServiceState.Stale,
                ServiceState.Stopped,
                ServiceState.Stopped,
                ServiceState.Running,
                ServiceState.Running,
                ServiceState.Stopped,
            ],
            states);
        Assert.Same(Fan, fresh);
        Assert.Null(latest.Current);
    }

    // A release's service serves the overlay of the release before, so the lines it writes for a recording are this file's,
    // byte for byte, and a change of format replaces it on purpose: a run that differs writes what it got beside it.
    [Fact]
    public void ThePipeKeepsItsLines()
    {
        var series = TestFiles.Series("ryzen-9950x-rtx-5060-ti-cpu-load-fan-software.jsonl").Take(3).ToList();
        var last = series[^1];
        var renamed = last with { Sensors = [last.Sensors[0] with { Name = "Renamed", Value = double.NaN }, .. last.Sensors.Skip(1)] };
        var writer = new SnapshotWriter();
        var had = 0;
        var got = new List<byte>();
        foreach (var snapshot in (Snapshot[])[.. series, renamed, renamed])
        {
            writer.Next(snapshot, sending: false);
            got.AddRange(writer.LineFor(had).ToArray());
            had = writer.Catalog;
        }

        var golden = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "pipe-lines.jsonl"));
        if (!golden.AsSpan().SequenceEqual(got.ToArray()))
        {
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "pipe-lines.got.jsonl"), [.. got]);
        }

        Assert.Equal(golden, got);
    }
}
