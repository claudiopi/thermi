using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Thermi.Core.Sensors;

namespace Thermi.MacOS.Tests.Acceptance;

/// <summary>What A1 replays, and the clients and the server of another user's that A3 runs.</summary>
public sealed partial class ServiceTests
{
    // Writes to the socket, reads three lines, and says how much of what it wrote the service still left unread: a stream
    // socket's bytes count against their writer until the reader takes them, which macOS's SO_NWRITE tells.
    private const string Writer = """
        import socket, sys
        client = socket.socket(socket.AF_UNIX)
        client.connect(sys.argv[1])
        client.sendall(b'thermi\n' * 1024)
        lines = client.makefile()
        for _ in range(3): print(lines.readline(), end='', flush=True)
        print('unread', client.getsockopt(socket.SOL_SOCKET, 0x1024), flush=True)
        """;

    // A server of another user's on a socket's path, which serves recorded lines to whoever connects, as anyone may; it
    // says it listens on its first line.
    private const string Impostor = """
        import os, socket, sys
        server = socket.socket(socket.AF_UNIX)
        server.bind(sys.argv[1])
        os.chmod(sys.argv[1], 0o666)
        server.listen()
        print('listening', flush=True)
        lines = open(sys.argv[2], 'rb').read()
        while True:
            client, _ = server.accept()
            try:
                client.sendall(lines)
            except OSError:
                pass  # A client that refuses hangs up before the lines are all sent.
            client.close()
        """;

    private static Task WriteScriptsAsync(OwnSession session, CancellationToken cancel) => Task.WhenAll(
        File.WriteAllTextAsync(Path.Combine(session.Temporary, "writer.py"), Writer, cancel),
        File.WriteAllTextAsync(Path.Combine(session.Temporary, "impostor.py"), Impostor, cancel));

    // Another user, nobody, which every Mac has, serves at the path, in a folder anyone may add to and only its owner empty.
    private static async Task ImpostorAsync(OwnSession session, string path, CancellationToken cancel)
    {
        var folder = Path.GetDirectoryName(path)!;
        File.SetUnixFileMode(folder, File.GetUnixFileMode(folder) | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute | UnixFileMode.StickyBit);
        var lines = Built.Recording(SensorFeed.MacMini) + ".json";
        var server = session.Start("sudo", ["-n", "-u", "nobody", "python3", Path.Combine(session.Temporary, "impostor.py"), path, lines]);
        await server.ReadyAsync(cancel);
    }

    // Each row reads as it says: thermi-cli as the person, in their desktop's shell or over SSH, where their background
    // session is the one launchd serves too; a client that writes as it reads; and thermi-cli told another socket.
    private static Task<Ran> ConnectAsync(OwnSession session, Client client, string named, CancellationToken cancel) => client switch
    {
        Client.Person or Client.AnotherUsersServer => session.RunAsync("thermi-cli", ["read", "--schema", "1"], cancel: cancel),
        Client.OverSsh => session.RunAsync(
            "ssh",
            ["-o", "BatchMode=yes", "localhost", "env", $"THERMI_SENSORS_SOCKET={session.Socket}", Built.Program("thermi-cli"), "read", "--schema", "1"],
            cancel: cancel),
        Client.Writer => session.RunAsync("python3", [Path.Combine(session.Temporary, "writer.py"), session.Socket], cancel: cancel),
        Client.NamedSocket => session.RunAsync(
            "thermi-cli", ["read", "--schema", "1"], new Dictionary<string, string> { ["THERMI_SENSORS_SOCKET"] = named }, cancel),
        _ => throw new ArgumentOutOfRangeException(nameof(client), client, "Unknown client."),
    };

    // How long the service ran after the last client left: launchd's print names its process while it runs.
    private static async Task<double> EndedAfterAsync(string printed, long left, CancellationToken cancel)
    {
        if (LaunchdPid().Match(printed) is not { Success: true } running)
        {
            return 0;
        }

        using var service = Process.GetProcessById(int.Parse(running.Groups["pid"].Value, CultureInfo.InvariantCulture));
        await service.WaitForExitAsync(cancel).WaitAsync(Bound.End, TimeProvider.System, cancel);
        return TimeProvider.System.GetElapsedTime(left).TotalSeconds;
    }

    private static int Unread(string line) => int.Parse(line.Split(' ')[1], CultureInfo.InvariantCulture);

    // The recording each row replays: the Mac mini as it was, loaded, with its cores' keys gone after the first second or
    // with no fan, an M1's keys, or the virtual machine.
    private static async Task<string> RecordingOfAsync(OwnSession session, Mac mac, CancellationToken cancel)
    {
        var copy = Raw.Copy(session, Source(mac));
        if (mac == Mac.VanishingKeys)
        {
            await Raw.VanishAsync(copy, key => key.StartsWith("Tp", StringComparison.Ordinal) || key.StartsWith("Te", StringComparison.Ordinal), 1, cancel);
        }

        if (mac == Mac.Fanless)
        {
            await Raw.VanishAsync(copy, key => key.StartsWith('F'), 0, cancel);
        }

        return copy;
    }

    private static string Source(Mac mac) => mac switch
    {
        Mac.AtRest or Mac.VanishingKeys or Mac.Fanless => SensorFeed.MacMini,
        Mac.UnderLoad => SensorFeed.MacMiniLoaded,
        Mac.OtherChip => SensorFeed.MadeM1,
        Mac.VirtualMachine => SensorFeed.VirtualMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(mac), mac, "Unknown Mac."),
    };

    // What each Mac lacks in a second: only the MacBook has a battery; a virtual machine has no SMC, HID sensors or energy.
    private static bool Missing(Mac mac, Metric metric, int second) => (metric.Label == MetricLabel.Bat && mac != Mac.OtherChip) || mac switch
    {
        Mac.VanishingKeys => second > 0 && metric == Metrics.CpuTemperature,
        Mac.Fanless => metric == Metrics.CpuFan || metric == Metrics.CpuFanDuty,
        Mac.VirtualMachine => metric.Label == MetricLabel.Gpu || metric == Metrics.CpuTemperature || metric == Metrics.CpuPower
            || metric == Metrics.CpuFan || metric == Metrics.CpuFanDuty,
        Mac.AtRest or Mac.UnderLoad or Mac.OtherChip => false,
        _ => throw new ArgumentOutOfRangeException(nameof(mac), mac, "Unknown Mac."),
    };

    [GeneratedRegex(@"^\s*pid = (?<pid>\d+)$", RegexOptions.Multiline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LaunchdPid();
}
