using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.MacOS.Tests.Desktop;

namespace Thermi.MacOS.Tests.Acceptance;

/// <summary>
/// <c>thermi-sensors</c> on macOS (<c>docs/specs/008-macos/spec.md</c>, R5 to R8): what it reads from recordings of what
/// a Mac's IOKit answered, what its recordings hold, its socket, and the agents <c>thermi register</c> loads.
/// </summary>
public sealed partial class ServiceTests
{
    public enum Mac
    {
        AtRest,
        UnderLoad,
        VanishingKeys,
        OtherChip,
        Fanless,
        VirtualMachine,
    }

    public enum Client
    {
        Person,
        OverSsh,
        AnotherUsersServer,
        Writer,
        NamedSocket,
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(Mac.AtRest, Skip = "Waiting for its code")]
    [InlineData(Mac.UnderLoad, Skip = "Waiting for its code")]
    [InlineData(Mac.VanishingKeys, Skip = "Waiting for its code")]
    [InlineData(Mac.OtherChip, Skip = "Waiting for its code")]
    [InlineData(Mac.Fanless, Skip = "Waiting for its code")]
    [InlineData(Mac.VirtualMachine, Skip = "Waiting for its code")]
    public async Task TheServiceReadsWhatTheMacGives(Mac mac)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var recording = await RecordingOfAsync(session, mac, cancel);
        var seconds = Directory.GetDirectories(recording).Length;
        var dump = Path.Combine(session.Temporary, "dump.jsonl");

        var ran = await session.RunAsync(
            Built.Program("thermi-sensors"), ["--replay", recording, "--dump", dump, "--seconds", $"{seconds}"], cancel: cancel);

        Assert.Equal(0, ran.Exit);
        var recorded = Snapshots(Built.Recording(Source(mac)) + ".json");
        var read = Snapshots(dump);
        Assert.Equal(seconds, read.Count);
        foreach (var (second, (was, now)) in recorded.Zip(read).Index())
        {
            var expected = SensorMap.Read(was, OverlayConfig.First);
            var readings = SensorMap.Read(now, OverlayConfig.First);
            foreach (var metric in Metrics.All.Where(metric => metric.Label != MetricLabel.Fps))
            {
                // A value the Mac does not give is missing, never a number: a vanished key, a fan that is not there.
                var value = readings[metric.Id].Usable;
                Assert.Equal(Missing(mac, metric, second) ? null : Timeless(metric) ? expected[metric.Id].Usable : value, value);
            }

            // The CPU's temperature is its cores' hottest, and its power the CPU's energy over the second, as IOReport gives it.
            var cpu = now.Sensors.Where(sensor => sensor.HardwareType == "Cpu").ToList();
            var cores = cpu.Where(sensor => sensor.Type == "Temperature" && sensor.Name.Contains("Core", StringComparison.Ordinal));
            var power = cpu.SingleOrDefault(sensor => sensor.Type == "Power");
            Assert.Equal(Missing(mac, Metrics.CpuTemperature, second) ? null : cores.Max(sensor => sensor.Value), readings[Metrics.CpuTemperature.Id].Usable);
            Assert.Equal(Missing(mac, Metrics.CpuPower, second) ? null : power?.Value, readings[Metrics.CpuPower.Id].Usable);
            // Loopback, tunnels, bridges and Apple's peer-to-peer adapters are set apart, as Windows' virtual ones are.
            Assert.Equal(was.VirtualAdapters ?? [], now.VirtualAdapters ?? []);
            Assert.NotEmpty(now.VirtualAdapters ?? []);
        }
    }

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task AMacRecordingReplaysAndNamesNoMac()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var mac = Raw.Copy(session, SensorFeed.MacMini);
        string[] names = ["claudios-mac-mini", "claudio", "C02ZK1ABCDEF", "a8:a1:59:12:34:56"];
        await Raw.NameTheMacAsync(mac, names, cancel);
        var raw = Path.Combine(session.Temporary, "raw");
        var dump = Path.Combine(session.Temporary, "dump.jsonl");
        var replayed = Path.Combine(session.Temporary, "replayed.jsonl");

        string[][] runs = [["--replay", mac, "--raw", raw], ["--replay", mac, "--dump", dump], ["--replay", raw, "--dump", replayed]];
        foreach (var args in runs)
        {
            Assert.Equal(0, (await session.RunAsync(Built.Program("thermi-sensors"), [.. args, "--seconds", "3"], cancel: cancel)).Exit);
        }

        Assert.Equal(await File.ReadAllLinesAsync(dump, cancel), await File.ReadAllLinesAsync(replayed, cancel));
        var written = Directory.EnumerateFiles(raw, "*", SearchOption.AllDirectories).Append(dump).Select(File.ReadAllText).ToList();
        Assert.All(names.Append(session.Home), name => Assert.DoesNotContain(written, text => text.Contains(name, StringComparison.OrdinalIgnoreCase)));
    }

    // launchd starts the service at its socket's first client, in the person's background session, which an SSH login
    // shares; another user's server stands where the service's socket would be, or where THERMI_SENSORS_SOCKET points.
    [Theory(Timeout = Bound.Test)]
    [InlineData(Client.Person, Skip = "Waiting for its code")]
    [InlineData(Client.OverSsh, Skip = "Waiting for its code")]
    [InlineData(Client.AnotherUsersServer, Skip = "Waiting for its code")]
    [InlineData(Client.Writer, Skip = "Waiting for its code")]
    [InlineData(Client.NamedSocket, Skip = "Waiting for its code")]
    public async Task TheSocketServesOnlyThePersonsService(Client client)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = OwnSession.OnScreen();
        await WriteScriptsAsync(session, cancel);
        var named = Path.Combine(session.Temporary, "other.sock");
        if (client is Client.AnotherUsersServer or Client.NamedSocket)
        {
            await ImpostorAsync(session, client == Client.NamedSocket ? named : session.Socket, cancel);
        }

        if (client != Client.AnotherUsersServer)
        {
            await Launchd.LoadServiceAsync(session, SensorFeed.MacMini, cancel);
        }

        var said = await ConnectAsync(session, client, named, cancel);
        var left = TimeProvider.System.GetTimestamp();
        var (_, user) = await session.DomainsAsync(cancel);
        var service = await Launchd.PrintAsync(session, user, Launchd.ServiceLabel, cancel);
        var ended = service is null ? 0 : await EndedAfterAsync(service, left, cancel);

        // The person reads the service's lines, over SSH too, and refuses a server of another user's wherever it listens.
        var trusted = client is Client.Person or Client.OverSsh or Client.Writer;
        Assert.Equal(trusted ? 0 : 3, said.Exit);
        if (client == Client.Writer)
        {
            var lines = said.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.All(lines[..3], line => Assert.NotNull(SnapshotJson.Read(line)));
            Assert.True(Unread(lines[3]) >= 7 * 1024, $"The service read what the client wrote: {lines[3]}");
        }
        else
        {
            Assert.Equal(trusted, said.Out.Contains($"\"{Metrics.CpuTemperature.Id}\"", StringComparison.Ordinal));
        }

        Assert.True(client == Client.AnotherUsersServer || service is not null, "launchd had no service to start at the socket.");
        Assert.True(!trusted || ended >= 10, $"The service ended {ended} s after the last client left.");
    }

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task TheAgentsAreThePersonsAlone()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = OwnSession.OnScreen();
        var (gui, user) = await session.DomainsAsync(cancel);
        var agents = Path.Combine(session.Home, "Library", "LaunchAgents");
        var settings = Path.Combine(session.Home, "Library", "Application Support", "thermi", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.Copy(Built.Config("v6.json"), settings);

        var registered = await session.RunAsync(Built.Thermi, ["register"], cancel: cancel);
        var service = await Launchd.PrintAsync(session, user, Launchd.ServiceLabel, cancel);
        var overlay = await Launchd.PrintAsync(session, gui, Launchd.OverlayLabel, cancel);
        var unregistered = await session.RunAsync(Built.Thermi, ["unregister"], cancel: cancel);
        var left = Directory.Exists(agents) ? Directory.GetFiles(agents) : [];
        string?[] gone =
            [await Launchd.PrintAsync(session, user, Launchd.ServiceLabel, cancel), await Launchd.PrintAsync(session, gui, Launchd.OverlayLabel, cancel)];
        var off = await session.RunAsync(Built.Thermi, ["register", "--open-at-login", "off"], cancel: cancel);
        var overlayOff = await File.ReadAllTextAsync(Path.Combine(agents, $"{Launchd.OverlayLabel}.plist"), cancel);

        // Both loaded with no prompt, each in its session: the service started by its socket and every 4 hours to check for
        // updates; the overlay kept alive when it fails, and not once it exits with 0, as Exit thermi does.
        Assert.Equal((0, 0, 0), (registered.Exit, unregistered.Exit, off.Exit));
        Assert.NotNull(service);
        Assert.Contains("Background", service, StringComparison.Ordinal);
        Assert.Contains("sensors.sock", service, StringComparison.Ordinal);
        Assert.Contains("run interval = 14400 seconds", service, StringComparison.Ordinal);
        Assert.NotNull(overlay);
        Assert.Contains("Aqua", overlay, StringComparison.Ordinal);
        Assert.Contains("successful exit => false", overlay, StringComparison.Ordinal);
        // Unregister takes both out of launchd and their plists away, and keeps the person's settings.
        Assert.DoesNotContain(gone, printed => printed is not null);
        Assert.Empty(left);
        Assert.Equal(await File.ReadAllTextAsync(Built.Config("v6.json"), cancel), await File.ReadAllTextAsync(settings, cancel));
        // With Open at login off, the overlay's agent stays, so the window turns it on again, but launchd starts it at no login.
        Assert.Contains("<key>RunAtLoad</key>\n  <false/>", overlayOff.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    private static List<Snapshot> Snapshots(string file) => [.. File.ReadLines(file).Select(line => SnapshotJson.Read(line)!)];

    // Clocks and counters of time read the moment they replay at, not the one they were recorded at.
    private static bool Timeless(Metric metric) => metric != Metrics.Clock && metric != Metrics.Uptime;
}
