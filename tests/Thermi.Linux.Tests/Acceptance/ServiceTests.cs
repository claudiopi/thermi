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
        for client in held: client.connect('/run/thermi/sensors.sock')
        print(sum(1 for client in held if client.makefile().readline()), flush=True)
        sys.stdin.read()
        """;

    // Writes to the socket, reads three lines, and says how much of what it wrote the service still left unread: a stream
    // socket's bytes count against their writer until the reader takes them.
    private const string Writer = """
        import fcntl, socket, struct, termios
        client = socket.socket(socket.AF_UNIX)
        client.connect('/run/thermi/sensors.sock')
        client.sendall(b'thermi\n' * 1024)
        lines = client.makefile()
        for _ in range(3): print(lines.readline(), end='', flush=True)
        print('unread', struct.unpack('i', fcntl.ioctl(client, termios.TIOCOUTQ, b'\0' * 4))[0], flush=True)
        """;

    // A server of another user's on the socket's path, which serves the recorded lines to whoever connects; it says it
    // listens on its first line.
    private const string Impostor = """
        import socket, sys
        server = socket.socket(socket.AF_UNIX)
        server.bind('/run/thermi/sensors.sock')
        server.listen()
        print('listening', flush=True)
        lines = open(sys.argv[1], 'rb').read()
        while True:
            client, _ = server.accept()
            client.sendall(lines)
            client.close()
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
        var seconds = Directory.GetDirectories(root).Length;
        var dump = Path.Combine(session.Home, "dump.jsonl");

        var ran = await session.RunAsync(
            Built.Program("thermi-sensors"), ["--dump", dump, "--seconds", $"{seconds}", "--root", root], cancel: cancel);

        Assert.Equal(0, ran.Exit);
        var recorded = Snapshots(Built.Tree(Source(tree)) + ".json");
        var read = Snapshots(dump);
        Assert.Equal(seconds, read.Count);
        foreach (var (second, (was, now)) in recorded.Zip(read).Index())
        {
            var expected = SensorMap.Read(was, OverlayConfig.First);
            var readings = SensorMap.Read(now, OverlayConfig.First);
            foreach (var metric in Metrics.All.Where(metric => metric.Label is not (MetricLabel.Fps or MetricLabel.Bat)))
            {
                var missing = Missing(tree, metric, second);
                // Rates read as a change since the second before, so the first second has none yet.
                Assert.True(
                    missing || second == 0 || readings[metric.Id].Usable is not null,
                    $"{metric.Id} read nothing in second {second} of {tree}.");
                Assert.Equal(missing ? null : Timeless(metric) ? expected[metric.Id].Usable : readings[metric.Id].Usable, readings[metric.Id].Usable);
            }

            // The chips as Windows' recordings of the same PC map them: the CPU's own sensor, the board's CPU fan input.
            Assert.Equal(
                Missing(tree, Metrics.CpuTemperature, second) ? null : Kernel(root, second, "k10temp", "Tctl"), readings[Metrics.CpuTemperature.Id].Usable);
            Assert.Equal(Missing(tree, Metrics.CpuFan, second) ? null : Kernel(root, second, "nct6798", "fan2"), readings[Metrics.CpuFan.Id].Usable);
            Assert.DoesNotContain(now.Sensors, sensor => sensor.Type == "Temperature" && sensor.Value <= -40);
            VirtualAdaptersAreApart(was, now);
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
        await File.WriteAllTextAsync(Path.Combine(session.Home, "writer.py"), Writer, cancel);
        await File.WriteAllTextAsync(Path.Combine(session.Home, "impostor.py"), Impostor, cancel);
        File.Copy(Built.Tree(SensorFeed.Desktop) + ".json", Path.Combine(session.Home, "lines"));
        await using var pc = await Container.BootAsync(session, Container.Ubuntu2604, session.Home, cancel);
        var install = Container.Installs(Container.Ubuntu2604, $"python3-minimal $(ls {Container.Packages}/thermi-sensors_*_amd64.deb)");
        await pc.MustAsync($"{install} && useradd -m person && useradd -m other", cancel);

        // The last client leaves as its script ends, on the clock systemd stamps a unit's state with.
        var (exit, said, error) = await pc.RunAsync(
            $"set +e; {ClientScript(client)}; ended=$?; python3 -c 'import time; print(time.monotonic())' >&2; exit $ended", cancel);
        var stopped = await pc.MustAsync(
            "pid=$(systemctl show -p MainPID --value thermi-sensors.service); [ \"$pid\" = 0 ] || tail --pid=$pid -f /dev/null; "
                + "systemctl show -p InactiveEnterTimestampMonotonic --value thermi-sensors.service",
            cancel);

        var lines = said.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(client == Client.Impostor ? 3 : 0, exit);
        Assert.Equal(client == Client.Person, said.Contains($"\"{Metrics.CpuTemperature.Id}\"", StringComparison.Ordinal));
        if (client == Client.Writer)
        {
            Assert.All(lines[..3], line => Assert.NotNull(SnapshotJson.Read(line)));
            Assert.True(int.Parse(lines[3].Split(' ')[1], CultureInfo.InvariantCulture) >= 7 * 1024, $"The service read what the client wrote: {lines[3]}");
        }

        Assert.Equal(client == Client.Crowd ? ["1", "8"] : lines, lines);
        var left = double.Parse(error.Trim().Split('\n')[^1], CultureInfo.InvariantCulture);
        var after = (double.Parse(stopped, CultureInfo.InvariantCulture) / 1e6) - left;
        Assert.True(client == Client.Impostor || after is >= 9.5 and < 11, $"The service ended {after} s after the last client left.");
    }

    [Fact(Skip = "Waiting for its code")]
    public async Task TheServiceRunsWithNoRights()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var device = Path.Combine(session.Home, "intel-rapl:0");
        Directory.CreateDirectory(device);
        // A recorded tree escapes a colon in a name, which Windows forbids, as %3A.
        var recorded = Path.Combine(Built.Tree(SensorFeed.Desktop), "0", "sys", "class", "powercap", "intel-rapl%3A0", "energy_uj");
        File.Copy(recorded, Path.Combine(device, "energy_uj"));
        await using var pc = await Container.BootAsync(session, Container.Ubuntu2604, session.Home, cancel);
        await pc.MustAsync(Container.Installs(Container.Ubuntu2604, $"$(ls {Container.Packages}/thermi-sensors_*_amd64.deb)"), cancel);

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
            ["AmbientCapabilities=", "CapabilityBoundingSet=", "User=thermi"],
            unit.Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
        Assert.Equal("root thermi 440", rights.Trim());
    }

    [GeneratedRegex("""RUN\+="(?<command>[^"]+)""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Run();

    // Each row connects to the socket as it says: a person's read; a server another user listens with on the socket's path,
    // which the person's read refuses; a client that writes as it reads; and nine connections of one user, then one of
    // another's. The person runs the thermi-cli the package put on every PATH.
    private static string ClientScript(Client client) => client switch
    {
        Client.Person => "su person -c 'thermi-cli read --schema 1'",
        Client.Impostor => "systemctl stop thermi-sensors.socket thermi-sensors.service && rm -rf /run/thermi && install -d -o other /run/thermi && "
            + $"mkfifo /tmp/listens && su other -s /bin/sh -c 'exec python3 {Container.Work}/impostor.py {Container.Work}/lines'"
            + " > /tmp/listens 2>/dev/null < /dev/null & "
            + "read -r _ < /tmp/listens && su person -c 'thermi-cli read --schema 1'",
        Client.Writer => $"su person -c 'python3 {Container.Work}/writer.py'",
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
    // file gone or that fails to read from the second it did, and every sensor of a virtual machine but the system's own.
    private static bool Missing(Tree tree, Metric metric, int second) => tree switch
    {
        Tree.PlainUser => metric == Metrics.CpuPower,
        Tree.NoNvidiaDriver => metric.Label == MetricLabel.Gpu,
        Tree.Failing => second > 0 && (metric == Metrics.CpuTemperature || metric == Metrics.CpuFan),
        Tree.VirtualMachine => metric.Label is MetricLabel.Gpu || metric == Metrics.CpuTemperature || metric == Metrics.CpuPower
            || metric == Metrics.CpuFan || metric == Metrics.CpuFanDuty,
        Tree.Service => false,
        _ => throw new ArgumentOutOfRangeException(nameof(tree), tree, "Unknown tree."),
    };

    // The virtual adapters, as loopback and a bridge, are named apart from the real ones, as the recording named them.
    private static void VirtualAdaptersAreApart(Snapshot was, Snapshot now)
    {
        var adapters = now.Sensors.Where(sensor => sensor.HardwareType == "Network").Select(SensorMap.DeviceIdOf).Distinct(StringComparer.Ordinal).ToList();
        Assert.Equal(was.VirtualAdapters ?? [], now.VirtualAdapters ?? []);
        Assert.NotEmpty(now.VirtualAdapters ?? []);
        Assert.All(now.VirtualAdapters ?? [], adapter => Assert.Contains(adapter, adapters));
        Assert.True(adapters.Except(now.VirtualAdapters ?? [], StringComparer.Ordinal).Any(), "No adapter was real.");
    }

    // A chip's sensor as its hwmon files hold it in a second of the tree, by the label of its input, or by the input's own
    // name where the chip labels none: degrees from millidegrees, a fan in RPM.
    private static double Kernel(string root, int second, string chip, string sensor)
    {
        var folder = Chip(root, second, chip);
        var input = File.Exists(Path.Combine(folder, $"{sensor}_input"))
            ? $"{sensor}_input"
            : Path.GetFileName(Directory.EnumerateFiles(folder, "*_label").Single(label => File.ReadAllText(label).Trim() == sensor))
                .Replace("_label", "_input", StringComparison.Ordinal);
        var value = double.Parse(File.ReadAllText(Path.Combine(folder, input)).Trim(), CultureInfo.InvariantCulture);
        return input.StartsWith("temp", StringComparison.Ordinal) ? value / 1000 : value;
    }

    private static string Chip(string root, int second, string name) =>
        Directory.EnumerateFiles(Path.Combine(root, $"{second}"), "name", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Trim() == name).Select(file => Path.GetDirectoryName(file)!).First();

    // A copy of the row's recorded tree, broken as the row says: from its second second on, the CPU's temperature is gone,
    // as when its module unloads, and the board's fan fails to read; and the board reads -40 °C, as an unwired input does.
    private static string TreeOf(OwnSession session, Tree tree)
    {
        var copy = Path.Combine(session.Home, "root");
        Built.Copy(Built.Tree(Source(tree)), copy);
        var files = Directory.EnumerateFiles(copy, "*", SearchOption.AllDirectories).ToList();
        if (tree == Tree.NoNvidiaDriver)
        {
            files.Where(file => Path.GetFileName(file).StartsWith("nvml", StringComparison.Ordinal)).ToList().ForEach(File.Delete);
        }
        else if (tree == Tree.Failing)
        {
            foreach (var second in Directory.GetDirectories(copy).Select(folder => int.Parse(Path.GetFileName(folder), CultureInfo.InvariantCulture)))
            {
                var board = Chip(copy, second, "nct6798");
                var unwired = Directory.EnumerateFiles(board, "temp*_input").Order(StringComparer.Ordinal).First();
                File.WriteAllText(unwired, "-40000\n");
                if (second > 0)
                {
                    File.Delete(Path.Combine(Chip(copy, second, "k10temp"), "temp1_input"));
                    File.Delete(Path.Combine(board, "fan2_input"));
                    Directory.CreateDirectory(Path.Combine(board, "fan2_input"));
                }
            }
        }

        return copy;
    }

    // Every place a PC's tree may name it: its host, a person's folder, its adapters' addresses and its drives' serials.
    private static void Named(string tree, string[] names)
    {
        foreach (var second in Directory.EnumerateDirectories(tree))
        {
            Directory.CreateDirectory(Path.Combine(second, "proc", "sys", "kernel"));
            File.WriteAllText(Path.Combine(second, "proc", "sys", "kernel", "hostname"), names[0]);
            File.WriteAllText(Path.Combine(second, "proc", "mounts"), $"/dev/nvme0n1p2 /home/{names[1]} ext4 rw 0 0\n");
            Directory.EnumerateFiles(Path.Combine(second, "sys", "class", "net"), "address", SearchOption.AllDirectories).ToList()
                .ForEach(address => File.WriteAllText(address, names[2]));
            Directory.EnumerateFiles(Path.Combine(second, "sys", "block"), "serial", SearchOption.AllDirectories).ToList()
                .ForEach(serial => File.WriteAllText(serial, names[3]));
        }
    }
}
