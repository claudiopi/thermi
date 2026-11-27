using System.Globalization;
using System.Text.RegularExpressions;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// <c>thermi-sensors</c> on Linux (<c>docs/specs/006-linux/spec.md</c>, R3 to R7): what it reads from recorded trees of
/// <c>/sys</c> and <c>/proc</c>, what its recordings hold, its socket, and the rights it runs with.
/// </summary>
public sealed partial class ServiceTests
{
    // Opens as many connections to the socket as asked, says how many were served a line, and holds them until its input ends.
    private const string Clients = """
        import socket, sys
        held = [socket.socket(socket.AF_UNIX) for _ in range(int(sys.argv[1]))]
        for client in held: client.connect('/run/thermi/sensors')
        print(sum(1 for client in held if client.makefile().readline()), flush=True)
        sys.stdin.read()
        """;

    public enum Tree
    {
        Service,
        PlainUser,
        NoNvidiaDriver,
        Failing,
        VirtualMachine,
    }

    public enum Client
    {
        Person,
        Impostor,
        Writer,
        Crowd,
    }

    [Theory]
    [InlineData(Tree.Service, Skip = "Waiting for its code")]
    [InlineData(Tree.PlainUser, Skip = "Waiting for its code")]
    [InlineData(Tree.NoNvidiaDriver, Skip = "Waiting for its code")]
    [InlineData(Tree.Failing, Skip = "Waiting for its code")]
    [InlineData(Tree.VirtualMachine, Skip = "Waiting for its code")]
    public async Task TheServiceReadsWhatTheKernelGives(Tree tree)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var root = TreeOf(session, tree);
        var dump = Path.Combine(session.Home, "dump.jsonl");

        var ran = await session.RunAsync(Built.Program("thermi-sensors"), ["--dump", dump, "--seconds", "3", "--root", root], cancel: cancel);

        Assert.Equal(0, ran.Exit);
        var recorded = Snapshots(Built.Tree(Source(tree)) + ".json");
        var read = Snapshots(dump);
        Assert.Equal(recorded.Count, read.Count);
        foreach (var (was, now) in recorded.Zip(read))
        {
            var expected = SensorMap.Read(was, OverlayConfig.First);
            var readings = SensorMap.Read(now, OverlayConfig.First);
            Assert.All(Metrics.All.Where(metric => metric.Label != MetricLabel.Fps), metric => Assert.Equal(
                Missing(tree, metric) ? null : Timeless(metric) ? expected[metric.Id].Usable : readings[metric.Id].Usable ?? double.NaN,
                readings[metric.Id].Usable));
            Assert.DoesNotContain(now.Sensors, sensor => sensor.Type == "Temperature" && sensor.Value <= -40);
            Assert.Equal(was.VirtualAdapters ?? [], now.VirtualAdapters ?? []);
        }
    }

    [Fact(Skip = "Waiting for its code")]
    public async Task ARecordedTreeReplaysAndNamesNoPc()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var pc = TreeOf(session, Tree.Service);
        string[] names = ["living-room-pc", "claudio", "a8:a1:59:12:34:56", "S6B0NL0T123456"];
        Named(pc, names);
        var tree = Path.Combine(session.Home, "tree");
        var dump = Path.Combine(session.Home, "dump.jsonl");
        var replayed = Path.Combine(session.Home, "replayed.jsonl");

        string[][] runs = [["--root", pc, "--tree", tree], ["--root", pc, "--dump", dump], ["--root", tree, "--dump", replayed]];
        foreach (var args in runs)
        {
            Assert.Equal(0, (await session.RunAsync(Built.Program("thermi-sensors"), [.. args, "--seconds", "3"], cancel: cancel)).Exit);
        }

        Assert.Equal(await File.ReadAllLinesAsync(dump, cancel), await File.ReadAllLinesAsync(replayed, cancel));
        var written = Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories).Append(dump).Select(File.ReadAllText).ToList();
        Assert.All(names.Append(session.Home), name => Assert.DoesNotContain(written, text => text.Contains(name, StringComparison.OrdinalIgnoreCase)));
    }

    // Other users and the service's own one exist only in a container, where systemd starts the service by its socket.
    [Theory]
    [InlineData(Client.Person, Skip = "Waiting for its code")]
    [InlineData(Client.Impostor, Skip = "Waiting for its code")]
    [InlineData(Client.Writer, Skip = "Waiting for its code")]
    [InlineData(Client.Crowd, Skip = "Waiting for its code")]
    public async Task TheSocketServesOnlyTheServicesLines(Client client)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        await File.WriteAllTextAsync(Path.Combine(session.Home, "clients.py"), Clients, cancel);
        File.Copy(Built.Tree(SensorFeed.Desktop) + ".json", Path.Combine(session.Home, "lines"));
        await using var pc = await Container.BootAsync(session, Container.Ubuntu2604, session.Home, cancel);
        var install = Container.Installs(Container.Ubuntu2604, $"python3-minimal socat {Container.Work}/thermi-sensors_*_amd64.deb");
        await pc.MustAsync($"{install} && useradd -m person && useradd -m other", cancel);

        var (exit, said, _) = await pc.RunAsync(ClientScript(client), cancel);
        var left = await pc.MustAsync(
            "cut -d' ' -f1 /proc/uptime; pid=$(systemctl show -p MainPID --value thermi-sensors.service); [ \"$pid\" = 0 ] || tail --pid=$pid -f /dev/null; "
                + "systemctl show -p InactiveEnterTimestampMonotonic --value thermi-sensors.service",
            cancel);

        var lines = said.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(client == Client.Impostor ? 3 : 0, exit);
        Assert.All(client is Client.Writer ? lines : [], line => Assert.NotNull(SnapshotJson.Read(line)));
        Assert.Equal(client == Client.Person, said.Contains($"\"{Metrics.CpuTemperature.Id}\"", StringComparison.Ordinal));
        Assert.Equal(client == Client.Crowd ? ["1", "8"] : lines, lines);
        var times = left.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(time => double.Parse(time, CultureInfo.InvariantCulture)).ToArray();
        var after = (times[1] / 1e6) - times[0];
        Assert.True(client == Client.Impostor || after >= 10, $"The service ended {after} s after the last client left.");
    }

    [Fact(Skip = "Waiting for its code")]
    public async Task TheServiceRunsWithNoRights()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var device = Path.Combine(session.Home, "intel-rapl:0");
        Directory.CreateDirectory(device);
        var recorded = Path.Combine(Built.Tree(SensorFeed.Desktop), "0", "sys", "class", "powercap", "intel-rapl:0", "energy_uj");
        File.Copy(recorded, Path.Combine(device, "energy_uj"));
        await using var pc = await Container.BootAsync(session, Container.Ubuntu2604, session.Home, cancel);
        await pc.MustAsync(Container.Installs(Container.Ubuntu2604, $"{Container.Work}/thermi-sensors_*_amd64.deb"), cancel);

        var security = await pc.MustAsync("systemd-analyze security --offline=true --no-pager thermi-sensors.service", cancel);
        var unit = await pc.MustAsync("systemctl show -p User -p CapabilityBoundingSet -p AmbientCapabilities thermi-sensors.service", cancel);
        // udev runs the rule's commands on a device's folder under /sys; here, on the recorded one's copy, as root.
        var rule = await pc.MustAsync("grep -h powercap /usr/lib/udev/rules.d/*thermi*.rules", cancel);
        var commands = Run().Matches(rule)
            .Select(match => match.Groups["command"].Value.Replace("/sys%p", $"{Container.Work}/intel-rapl:0", StringComparison.Ordinal));
        await pc.MustAsync(string.Join("; ", commands), cancel);
        var rights = await pc.MustAsync($"stat -c '%U %G %a' {Container.Work}/intel-rapl:0/energy_uj", cancel);

        Assert.Matches(@"Overall exposure level for thermi-sensors\.service: \d+\.\d+ (OK|SAFE)", security);
        Assert.Equal(
            ["AmbientCapabilities=", "CapabilityBoundingSet=", "User=thermi-sensors"],
            unit.Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
        Assert.Equal("root thermi-sensors 440", rights.Trim());
    }

    [GeneratedRegex("""RUN\+="(?<command>[^"]+)""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Run();

    // Each row connects to the socket as it says: a person's read; a server of another user's on the socket's path, which
    // the read refuses; a client that writes as it reads; and nine connections of one user, then one of another's.
    private static string ClientScript(Client client) => client switch
    {
        Client.Person => $"su person -c '{Container.Output}/thermi-cli read --schema 1'",
        Client.Impostor => "systemctl stop thermi-sensors.socket thermi-sensors.service && install -d -o other /run/thermi && "
            + $"systemd-run --uid=other --socket-property=ListenStream=/run/thermi/sensors --socket-property=Accept=yes cat {Container.Work}/lines && "
            + $"su person -c '{Container.Output}/thermi-cli read --schema 1'",
        Client.Writer => "su person -c 'yes thermi | socat - UNIX-CONNECT:/run/thermi/sensors | head -n 3'",
        Client.Crowd => $"mkfifo /tmp/go /tmp/said; su person -c 'python3 {Container.Work}/clients.py 9' < /tmp/go > /tmp/said & exec 3>/tmp/go; "
            + $"read -r person < /tmp/said; su other -c 'python3 {Container.Work}/clients.py 1' < /dev/null; echo $person; exec 3>&-; wait",
        _ => throw new ArgumentOutOfRangeException(nameof(client), client, "Unknown client."),
    };

    private static List<Snapshot> Snapshots(string file) => [.. File.ReadLines(file).Select(line => SnapshotJson.Read(line)!)];

    // Clocks and counters of time read the moment they replay at, not the one they were recorded at: they only read.
    private static bool Timeless(Metric metric) => metric != Metrics.Clock && metric != Metrics.Uptime;

    private static string Source(Tree tree) => tree switch
    {
        Tree.PlainUser => SensorFeed.PlainUser,
        Tree.VirtualMachine => SensorFeed.VirtualMachine,
        Tree.Service or Tree.NoNvidiaDriver or Tree.Failing => SensorFeed.Desktop,
        _ => throw new ArgumentOutOfRangeException(nameof(tree), tree, "Unknown tree."),
    };

    // What the kernel does not give reads missing, never 0: the energy counters to a plain user, the GPU with no driver, a
    // file that fails to read or is gone, and every sensor of a virtual machine.
    private static bool Missing(Tree tree, Metric metric) => tree switch
    {
        Tree.PlainUser => metric == Metrics.CpuPower,
        Tree.NoNvidiaDriver => metric.Label == MetricLabel.Gpu,
        Tree.Failing => metric == Metrics.CpuTemperature || metric == Metrics.CpuFan,
        Tree.VirtualMachine => metric.Kind is not (SensorKind.Duration or SensorKind.Address or SensorKind.Time) && metric.Label != MetricLabel.Ram,
        Tree.Service => false,
        _ => throw new ArgumentOutOfRangeException(nameof(tree), tree, "Unknown tree."),
    };

    // A copy of the row's recorded tree, broken as the row says.
    private static string TreeOf(OwnSession session, Tree tree)
    {
        var copy = Path.Combine(session.Home, "root");
        Copy(Built.Tree(Source(tree)), copy);
        var files = Directory.EnumerateFiles(copy, "*", SearchOption.AllDirectories).ToList();
        if (tree == Tree.NoNvidiaDriver)
        {
            files.Where(file => Path.GetFileName(file).StartsWith("nvml", StringComparison.Ordinal)).ToList().ForEach(File.Delete);
        }
        else if (tree == Tree.Failing)
        {
            // The CPU's temperature is gone, as when its module unloads, and the board's fan fails to read.
            Chip(files, "k10temp").ForEach(folder => File.Delete(Path.Combine(folder, "temp1_input")));
            Chip(files, "nct6798").ForEach(folder => File.SetUnixFileMode(Path.Combine(folder, "fan2_input"), UnixFileMode.None));
        }

        return copy;
    }

    private static List<string> Chip(List<string> files, string name) =>
        [.. files.Where(file => Path.GetFileName(file) == "name" && File.ReadAllText(file).Trim() == name).Select(file => Path.GetDirectoryName(file)!)];

    // Every place a PC's tree may name it: its host, a person's folder, its adapters' addresses and its drives' serials.
    private static void Named(string tree, string[] names)
    {
        foreach (var second in Directory.EnumerateDirectories(tree))
        {
            File.WriteAllText(Path.Combine(second, "proc", "sys", "kernel", "hostname"), names[0]);
            File.WriteAllText(Path.Combine(second, "proc", "mounts"), $"/dev/nvme0n1p2 /home/{names[1]} ext4 rw 0 0\n");
            Directory.EnumerateFiles(Path.Combine(second, "sys", "class", "net"), "address", SearchOption.AllDirectories).ToList()
                .ForEach(address => File.WriteAllText(address, names[2]));
            Directory.EnumerateFiles(Path.Combine(second, "sys", "block"), "serial", SearchOption.AllDirectories).ToList()
                .ForEach(serial => File.WriteAllText(serial, names[3]));
        }
    }

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
