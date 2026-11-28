using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Thermi.Core.Sensors;

namespace Thermi.MacOS.Tests.Acceptance;

/// <summary>What A1 replays, and the clients that A3 runs.</summary>
public sealed partial class ServiceTests
{
    // Writes to the socket until it takes no more, reads three lines, and says how much of what it wrote the service still
    // left unread: on macOS a Unix socket's bytes wait in their reader's buffer, which frees room only as the reader takes
    // them, so what the writer cannot write again is what was left.
    private const string Writer = """
        import socket, sys
        client = socket.socket(socket.AF_UNIX)
        client.connect(sys.argv[1])
        def fill():
            sent = 0
            client.setblocking(False)
            try:
                while True: sent += client.send(b'thermi\n' * 128)
            except BlockingIOError:
                pass
            client.setblocking(True)
            return sent
        written = fill()
        lines = client.makefile()
        for _ in range(3): print(lines.readline(), end='', flush=True)
        print('unread', written - fill(), flush=True)
        """;

    // Each row reads as it says: thermi-cli as the person, in their desktop's shell or over SSH, where their background
    // session is the one launchd serves too; a client that writes as it reads; and thermi-cli told another socket.
    private static Task<Ran> ConnectAsync(OwnSession session, Client client, string impostor, CancellationToken cancel) => client switch
    {
        Client.Person or Client.AnotherUsersServer => session.RunAsync("thermi-cli", ["read", "--schema", "1"], cancel: cancel),
        Client.OverSsh => session.RunAsync(
            "ssh",
            [
                "-o", "BatchMode=yes", "localhost",
                $"export {string.Join(' ', session.Variables.Select(variable => $"{variable.Key}='{variable.Value}'"))}; "
                    + $"'{Built.Program("thermi-cli")}' read --schema 1",
            ],
            cancel: cancel),
        Client.Writer => session.RunAsync("python3", [Path.Combine(session.Temporary, "writer.py"), session.Socket], cancel: cancel),
        Client.NamedSocket => session.RunAsync(
            "thermi-cli", ["read", "--schema", "1"], new Dictionary<string, string> { ["THERMI_SENSORS_SOCKET"] = impostor }, cancel),
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

    // The recording each row replays: the Mac mini as it was, loaded, with its cores' keys gone after the first second and
    // its fan's failing to read in the second alone, or with no fan, an M1's keys, or the virtual machine.
    private static async Task<string> RecordingOfAsync(OwnSession session, Mac mac, CancellationToken cancel)
    {
        var copy = Raw.Copy(session, Source(mac));
        if (mac == Mac.VanishingKeys)
        {
            await Raw.VanishAsync(copy, key => key.StartsWith("Tp", StringComparison.Ordinal) || key.StartsWith("Te", StringComparison.Ordinal), 1, cancel);
            await Raw.FailAsync(copy, "F0Ac", 1, cancel);
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
        Mac.VanishingKeys => (second > 0 && metric == Metrics.CpuTemperature) || (second == 1 && (metric == Metrics.CpuFan || metric == Metrics.CpuFanDuty)),
        Mac.Fanless => metric == Metrics.CpuFan || metric == Metrics.CpuFanDuty,
        Mac.VirtualMachine => metric.Label == MetricLabel.Gpu || metric == Metrics.CpuTemperature || metric == Metrics.CpuPower
            || metric == Metrics.CpuFan || metric == Metrics.CpuFanDuty,
        Mac.AtRest or Mac.UnderLoad or Mac.OtherChip => false,
        _ => throw new ArgumentOutOfRangeException(nameof(mac), mac, "Unknown Mac."),
    };

    [GeneratedRegex(@"^\s*pid = (?<pid>\d+)$", RegexOptions.Multiline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LaunchdPid();
}
